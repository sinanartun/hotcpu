using System;
using System.Globalization;
using System.Threading;
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

            const string mutexName = "HotCPU_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

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

            // Global Exception Handling
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => HandleException(e.Exception, "UI Thread");
            AppDomain.CurrentDomain.UnhandledException += (s, e) => HandleException(e.ExceptionObject as Exception, "AppDomain");

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new SplashForm());
                Application.Run(new TrayApplicationContext(settings));
            }
            finally
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
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
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal_error.log");
                string message = $"[{DateTime.Now}] [{source}] Critical Error: {ex}\n\n";
                File.AppendAllText(path, message);
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
            _temperatureService = new TemperatureService(_settings);
            _loggerService = new LoggerService(_temperatureService, _settings);
            _trayIconManager = new TrayIconManager(_temperatureService, _loggerService, _settings, ExitApplication);
            _temperatureService.Start();
        }

        private void ExitApplication()
        {
            _trayIconManager.Dispose();
            _loggerService.Dispose();
            _temperatureService.Dispose();
            Application.Exit();
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
