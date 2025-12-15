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
        static void Main()
        {
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
