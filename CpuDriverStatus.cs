using System;
using System.IO;
using Microsoft.Win32;

namespace HotCPU
{
    /// <summary>
    /// Availability state for the low-level CPU temperature source (PawnIO driver)
    /// used by LibreHardwareMonitor to read Ryzen MSR/SMN registers without admin.
    /// </summary>
    internal enum CpuSensorStatus
    {
        /// <summary>A valid CPU temperature reading is being produced.</summary>
        Available,

        /// <summary>
        /// LibreHardwareMonitor detected a CPU, but every temperature sensor it
        /// exposed was 0/invalid. This almost always means the PawnIO kernel
        /// driver is not installed or not running.
        /// </summary>
        DriverMissing,

        /// <summary>No CPU hardware was detected by LibreHardwareMonitor at all.</summary>
        NotDetected,
    }

    /// <summary>
    /// PawnIO driver detection. LibreHardwareMonitor 0.9.5 uses PawnIO to read
    /// AMD Zen temperature / power registers in a non-admin process. The NuGet
    /// package does NOT install the driver - it must be deployed separately.
    /// </summary>
    internal static class CpuDriverHelper
    {
        /// <summary>Official PawnIO download page.</summary>
        public const string PawnIoReleasesUrl = "https://github.com/namazso/PawnIO/releases";

        /// <summary>
        /// Returns true when the PawnIO kernel service is registered on this
        /// system. We check the same registry locations LibreHardwareMonitor
        /// itself uses, so that "installed" from HotCPU's perspective always
        /// matches LHM's internal <c>PawnIo.IsInstalled</c> check.
        /// Falls back to the Service Control Manager key and a well-known
        /// file-system path when the Uninstall hive is unreachable.
        /// </summary>
        public static bool IsPawnIoInstalled()
        {
            // 1. LHM v0.9.5 PawnIo.cs static ctor reads DisplayVersion from
            //    HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO
            //    (and falls back to the Registry64 view of the same path).
            //    This is the authoritative "is PawnIO installed?" signal.
            if (HasDisplayVersion(RegistryView.Default)) return true;
            if (HasDisplayVersion(RegistryView.Registry64)) return true;
            if (HasDisplayVersion(RegistryView.Registry32)) return true;

            // 2. SCM service key - a registered (even stopped) service still
            //    means the driver files are present.
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\PawnIO",
                    writable: false);
                if (key != null) return true;
            }
            catch { /* registry access denied */ }

            // 3. Raw filesystem fallback for sandboxed environments.
            try
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(programFiles))
                {
                    var sysPath = Path.Combine(programFiles, "PawnIO", "PawnIO.sys");
                    if (File.Exists(sysPath)) return true;
                }
            }
            catch { /* sandbox / access denied */ }

            return false;
        }

        private static bool HasDisplayVersion(RegistryView view)
        {
            try
            {
                using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                var ver = key?.GetValue("DisplayVersion") as string;
                return !string.IsNullOrWhiteSpace(ver);
            }
            catch
            {
                return false;
            }
        }
    }
}
