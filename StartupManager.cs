using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
// These are available because of net8.0-windows10.0.19041.0
using Windows.ApplicationModel;

namespace HotCPU
{
    /// <summary>
    /// Result of attempting to change the "Start with Windows" setting. Lets the UI explain
    /// why a request did not take effect (the StartupTask API can silently refuse to enable
    /// when the user disabled it in Task Manager or IT policy blocks it).
    /// </summary>
    public enum StartupChangeResult
    {
        /// <summary>Requested state applied successfully.</summary>
        Success,
        /// <summary>User must enable the task manually in Task Manager &gt; Startup apps.</summary>
        DisabledByUser,
        /// <summary>Group Policy / MDM blocks enabling auto-start.</summary>
        DisabledByPolicy,
        /// <summary>Manifest entry missing, or an unexpected platform error occurred.</summary>
        Failed
    }

    public static class StartupManager
    {
        // Must match the TaskId in <desktop:StartupTask TaskId="..."/> in Package.appxmanifest.
        internal const string StartupTaskId = "HotCPUStartup";

        // Cache the result since it never changes during runtime
        private static bool? _isPackage;

        public static bool IsPackage
        {
            get
            {
                if (_isPackage.HasValue)
                    return _isPackage.Value;

                _isPackage = CheckIsPackage();
                return _isPackage.Value;
            }
        }

        private static bool CheckIsPackage()
        {
            try
            {
                // APPMODEL_ERROR_NO_PACKAGE (15700) = not running as packaged app.
                // We pass a zero-length buffer; we only care about the error code.
                int length = 0;
                int result = GetCurrentPackageFullName(ref length, null);
                return result != 15700;
            }
            catch (DllNotFoundException)
            {
                // Pre-Windows 8 or unusual host — treat as unpackaged.
                return false;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder? packageFullName);

        public static async Task<bool> IsStartupEnabledAsync()
        {
            if (IsPackage)
            {
                try
                {
                    var task = await StartupTask.GetAsync(StartupTaskId);
                    return task.State == StartupTaskState.Enabled ||
                           task.State == StartupTaskState.EnabledByPolicy;
                }
                catch
                {
                    // Manifest missing the StartupTask declaration, or API unavailable.
                    return false;
                }
            }

            return GetRegistryState();
        }

        /// <summary>
        /// Legacy boolean API kept for compatibility. Prefer <see cref="TrySetStartupEnabledAsync"/>
        /// in new code so the caller can react to <see cref="StartupChangeResult"/>.
        /// </summary>
        public static async Task SetStartupEnabledAsync(bool enable)
        {
            await TrySetStartupEnabledAsync(enable).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply the requested state and report what actually happened. This is the API
        /// Settings should call so it can notify the user when Windows refuses to enable
        /// autostart (task disabled by user, disabled by policy, or manifest missing).
        /// </summary>
        public static async Task<StartupChangeResult> TrySetStartupEnabledAsync(bool enable)
        {
            if (IsPackage)
            {
                StartupTask task;
                try
                {
                    task = await StartupTask.GetAsync(StartupTaskId);
                }
                catch
                {
                    return StartupChangeResult.Failed;
                }

                if (!enable)
                {
                    try
                    {
                        if (task.State == StartupTaskState.Enabled ||
                            task.State == StartupTaskState.EnabledByPolicy)
                        {
                            task.Disable();
                        }
                        return StartupChangeResult.Success;
                    }
                    catch
                    {
                        return StartupChangeResult.Failed;
                    }
                }

                // enable == true
                return task.State switch
                {
                    StartupTaskState.Enabled => StartupChangeResult.Success,
                    StartupTaskState.EnabledByPolicy => StartupChangeResult.Success,
                    StartupTaskState.DisabledByUser => StartupChangeResult.DisabledByUser,
                    StartupTaskState.DisabledByPolicy => StartupChangeResult.DisabledByPolicy,
                    StartupTaskState.Disabled => await RequestEnableAsync(task).ConfigureAwait(false),
                    _ => StartupChangeResult.Failed
                };
            }

            // Unpackaged path — Registry Run key.
            try
            {
                SetRegistryState(enable);
                return GetRegistryState() == enable
                    ? StartupChangeResult.Success
                    : StartupChangeResult.Failed;
            }
            catch
            {
                return StartupChangeResult.Failed;
            }
        }

        private static async Task<StartupChangeResult> RequestEnableAsync(StartupTask task)
        {
            try
            {
                var newState = await task.RequestEnableAsync();
                return newState switch
                {
                    StartupTaskState.Enabled => StartupChangeResult.Success,
                    StartupTaskState.EnabledByPolicy => StartupChangeResult.Success,
                    StartupTaskState.DisabledByUser => StartupChangeResult.DisabledByUser,
                    StartupTaskState.DisabledByPolicy => StartupChangeResult.DisabledByPolicy,
                    _ => StartupChangeResult.Failed
                };
            }
            catch
            {
                return StartupChangeResult.Failed;
            }
        }

        private static bool GetRegistryState()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false);
                return key?.GetValue("HotCPU") != null;
            }
            catch
            {
                return false;
            }
        }

        private static void SetRegistryState(bool enable)
        {
            // CreateSubKey ensures the Run key exists even on freshly provisioned profiles
            // where OpenSubKey would return null.
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    // Fallback mainly for weird hosting scenarios, unlikely for WinForms.
                    exePath = Path.Combine(AppContext.BaseDirectory, "HotCPU.exe");
                }

                // Always quote so a path containing spaces ("C:\Program Files\...") parses
                // correctly when Windows executes the Run entry.
                key.SetValue("HotCPU", $"\"{exePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue("HotCPU", throwOnMissingValue: false);
            }
        }
    }
}
