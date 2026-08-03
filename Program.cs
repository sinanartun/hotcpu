using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HotCPU.Localization;

namespace HotCPU
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main(string[] args)
        {
            // Check for sensor report argument
            if (args != null && args.Length > 0 && args.Contains("--report-sensors"))
            {
                GenerateSensorReport();
                return;
            }

            // Local\ keeps the single-instance mutex per user session. A bare
            // name can collide across sessions / elevation boundaries and throw
            // on some multi-user / Terminal Server machines.
            const string mutexName = @"Local\HotCPU_SingleInstance_Mutex";
            bool createdNew;
            try
            {
                _mutex = new Mutex(true, mutexName, out createdNew);
            }
            catch (Exception ex)
            {
                // Mutex creation failure must not prevent the app from starting.
                HandleException(ex, "Mutex");
                createdNew = true;
                _mutex = null;
            }

            if (!createdNew)
            {
                MessageBox.Show(
                    "HotCPU is already running.\nCheck your system tray.",
                    "HotCPU",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            AppSettings settings = AppSettings.Load();
            ApplyCulture(settings);

            // Global Exception Handling — managed exceptions only. Native AVs
            // from LibreHardwareMonitor / NvAPI still tear down the process,
            // which is why those libraries are also hardened separately.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => HandleException(e.Exception, "UI Thread");
            AppDomain.CurrentDomain.UnhandledException += (s, e) => HandleException(e.ExceptionObject as Exception, "AppDomain");
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                HandleException(e.Exception, "Task");
                e.SetObserved();
            };

            try
            {
                ApplicationConfiguration.Initialize();
                try
                {
                    Application.Run(new SplashForm());
                }
                catch (Exception ex)
                {
                    // Splash is cosmetic; never block the tray app from starting.
                    HandleException(ex, "Splash");
                }

                Application.Run(new TrayApplicationContext(settings));
            }
            catch (Exception ex)
            {
                HandleException(ex, "Main");
                try
                {
                    MessageBox.Show(
                        "HotCPU failed to start. Details were written to the log in %LOCALAPPDATA%\\HotCPU\\fatal_error.log",
                        "HotCPU",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { /* ignore */ }
            }
            finally
            {
                try
                {
                    if (_mutex != null)
                    {
                        try { _mutex.ReleaseMutex(); } catch { /* abandoned / not owned */ }
                        _mutex.Dispose();
                    }
                }
                catch { /* ignore */ }
            }
        }

        private static void GenerateSensorReport()
        {
            try 
            {
                var settings = AppSettings.Load();
                using var tempService = new TemperatureService(settings);
                // Force open momentarily to detect hardware
                var computerField = typeof(TemperatureService)
                    .GetField("_computer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (computerField != null)
                {
                    var computer = (LibreHardwareMonitor.Hardware.Computer)computerField.GetValue(tempService)!;
                    computer.Open();
                }

                string report = tempService.GetFullHardwareReport();
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensor_report.txt");
                File.WriteAllText(path, report);
                
                // Show a dialog confirming generation
                // MessageBox.Show($"Report generated at:\n{path}", "HotCPU Sensor Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                // MessageBox.Show($"Failed to generate report: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensor_error.txt"), ex.ToString());
            }
        }

        private static void ApplyCulture(AppSettings settings)
        {
            var fallback = CultureInfo.CurrentUICulture;
            var cultureName = string.IsNullOrWhiteSpace(settings.Language)
                ? fallback.Name
                : settings.Language;

            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                LocalizationService.SetCulture(culture);
            }
            catch (CultureNotFoundException)
            {
                LocalizationService.SetCulture(fallback);
            }
        }

        private static void HandleException(Exception? ex, string source)
        {
            if (ex == null) return;
            try
            {
                // MSIX install directories are read-only for Store packages, so
                // BaseDirectory writes always fail there. Prefer LocalAppData.
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HotCPU");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "fatal_error.log");
                string message = $"[{DateTime.Now}] [{source}] Critical Error: {ex}\n\n";
                File.AppendAllText(path, message);
                DebugLog.Error("Fatal", $"{source}: {ex.Message}", ex);
            }
            catch 
            {
                // Last resort: failsafe
            }
        }
    }

    /// <summary>
    /// Application context that manages the tray icon without a main window.
    /// </summary>
    internal class TrayApplicationContext : ApplicationContext
    {
        private readonly AppSettings _settings;
        private readonly TemperatureService _temperatureService;
        private readonly LoggerService _loggerService;
        private readonly TrayIconManager _trayIconManager;

        public TrayApplicationContext(AppSettings settings)
        {
            _settings = settings;
            try
            {
                _temperatureService = new TemperatureService(_settings);
                _loggerService = new LoggerService(_temperatureService, _settings);
                _trayIconManager = new TrayIconManager(_temperatureService, _loggerService, _settings, ExitApplication);
                _temperatureService.Start();
            }
            catch (Exception ex)
            {
                DebugLog.Error("App", "TrayApplicationContext init failed", ex);
                // Re-throw so Program.Main can show a user-visible error and log it.
                // Leaving a half-constructed context running would only crash later.
                throw;
            }
        }

        private void ExitApplication()
        {
            DebugLog.Info("App", "ExitApplication: disposing services and calling Application.Exit");
            try { _trayIconManager.Dispose(); } catch (Exception ex) { DebugLog.Error("App", "tray dispose failed", ex); }
            try { _loggerService.Dispose(); } catch (Exception ex) { DebugLog.Error("App", "logger dispose failed", ex); }
            try { _temperatureService.Dispose(); } catch (Exception ex) { DebugLog.Error("App", "temp service dispose failed", ex); }
            Application.Exit();
            DebugLog.Info("App", "Application.Exit returned");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIconManager?.Dispose();
                _loggerService?.Dispose();
                _temperatureService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
