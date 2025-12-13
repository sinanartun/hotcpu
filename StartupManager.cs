using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;
// These are available because of net8.0-windows10.0.19041.0
using Windows.ApplicationModel;

namespace HotCPU
{
    public static class StartupManager
    {
        // Must match the ID in Package.appxmanifest <StartupTask Id="...">
        private const string StartupTaskId = "HotCPUStartup";

        public static bool IsPackage
        {
            get
            {
                try
                {
                    var p = Package.Current;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static async Task<bool> IsStartupEnabledAsync()
        {
            if (IsPackage)
            {
                try
                {
                    var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                    return task.State == Windows.ApplicationModel.StartupTaskState.Enabled;
                }
                catch
                {
                    // If ID calls fail or manifest entry missing
                    return false;
                }
            }
            else
            {
                return GetRegistryState();
            }
        }

        public static async Task SetStartupEnabledAsync(bool enable)
        {
            if (IsPackage)
            {
                try
                {
                    var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                    if (enable)
                    {
                        if (task.State != Windows.ApplicationModel.StartupTaskState.Enabled)
                        {
                            await task.RequestEnableAsync();
                        }
                    }
                    else
                    {
                        task.Disable();
                    }
                }
                catch { }
            }
            else
            {
                SetRegistryState(enable);
            }
        }

        private static bool GetRegistryState()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("HotCPU") != null;
            }
            catch
            {
                return false;
            }
        }

        private static void SetRegistryState(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        // Fallback mainly for weird hosting scenarios, unlikely for WinForms
                        exePath = Path.Combine(AppContext.BaseDirectory, "HotCPU.exe");
                    }
                    
                    key.SetValue("HotCPU", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("HotCPU", false);
                }
            }
            catch { }
        }
    }
}
