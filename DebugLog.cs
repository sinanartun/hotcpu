using System;
using System.IO;
using System.Threading;

namespace HotCPU
{
    /// <summary>
    /// Lightweight rotating file logger for diagnosing UI-thread issues
    /// (menu clicks, tray behaviour, install flow). Writes to
    /// <c>%LOCALAPPDATA%\HotCPU\hotcpu-debug.log</c> so it survives app
    /// restarts and doesn't need admin to read.
    ///
    /// Thread-safe: a single mutex serializes writes across the app domain.
    /// Intentionally minimal - this is a throwaway diagnostic aid, not
    /// structured telemetry.
    /// </summary>
    internal static class DebugLog
    {
        private static readonly object _lock = new();
        private static readonly Lazy<string> _logPath = new(ResolveLogPath);

        public static string LogPath => _logPath.Value;

        public static void Info(string category, string message) => Write("INFO", category, message);
        public static void Warn(string category, string message) => Write("WARN", category, message);
        public static void Error(string category, string message, Exception? ex = null)
        {
            if (ex != null)
                Write("ERROR", category, $"{message} :: {ex.GetType().Name}: {ex.Message}");
            else
                Write("ERROR", category, message);
        }

        private static string ResolveLogPath()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HotCPU");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "hotcpu-debug.log");
            }
            catch
            {
                // Fall back to temp if %LOCALAPPDATA% is unreadable.
                return Path.Combine(Path.GetTempPath(), "hotcpu-debug.log");
            }
        }

        private static void Write(string level, string category, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-5}] [t{Environment.CurrentManagedThreadId:D3}] [{category}] {message}";

            // Mirror to the VS output window for live tailing during debugging.
            System.Diagnostics.Debug.WriteLine(line);

            lock (_lock)
            {
                try
                {
                    // Cap log size at ~1 MB so it doesn't grow without bound.
                    var path = _logPath.Value;
                    if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                    {
                        var rotated = path + ".1";
                        try { File.Delete(rotated); } catch { }
                        try { File.Move(path, rotated); } catch { }
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch
                {
                    // Logging must never crash the app.
                }
            }
        }
    }
}
