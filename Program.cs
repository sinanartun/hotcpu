using System;
using System.Threading;
using System.Windows.Forms;

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

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new TrayApplicationContext());
            }
            finally
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
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

        public TrayApplicationContext()
        {
            _settings = AppSettings.Load();
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