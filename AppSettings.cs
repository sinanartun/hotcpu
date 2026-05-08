using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HotCPU
{
    /// <summary>
    /// Application settings with JSON persistence.
    /// </summary>
    internal class AppSettings
    {
        private static readonly string DefaultSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HotCPU", "settings.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        // Settings properties
        public int RefreshIntervalMs { get; set; } = 1000;
        public int WarmThreshold { get; set; } = 55;
        public int HotThreshold { get; set; } = 65;
        public int CriticalThreshold { get; set; } = 78;
        public bool StartWithWindows { get; set; } = false;
        public int FontSize { get; set; } = 14;
        public bool ShowTrayIconTemperature { get; set; } = true;
        public string TrayFontFamily { get; set; } = "Segoe UI"; // Default font
        public int TrayFontStyle { get; set; } = 0; // 0=Regular, 1=Bold, 2=Italic, etc.
        public string ThemeMode { get; set; } = "Auto"; // Auto, Light, Dark
        public int LightTextColor { get; set; } = unchecked((int)0xFF000000); // Black
        public int DarkTextColor { get; set; } = unchecked((int)0xFFFFFFFF); // White
        public List<string> HiddenSensorIds { get; set; } = new();
        public List<string> TraySensorIds { get; set; } = new();
        public string? Language { get; set; }

        // Logging settings
        public bool LogEnabled { get; set; } = false;
        public string LogPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "hotcpu", "HotCPU_Log.csv");
        public int LogIntervalSeconds { get; set; } = 5;
        public string LogFormat { get; set; } = "CSV"; // CSV, JSON, TXT
        public List<string> LogSensorIds { get; set; } = new();
        public bool LogAverage { get; set; } = false;
        public bool LogMin { get; set; } = false;
        public bool LogMax { get; set; } = false;

        // Color settings (stored as ARGB integers for JSON serialization)
        public int CoolColor { get; set; } = unchecked((int)0xFF00FFFF);  // Cyan
        public int WarmColor { get; set; } = unchecked((int)0xFF00FF00);  // Green
        public int HotColor { get; set; } = unchecked((int)0xFFFFA500);   // Orange
        public int CriticalColor { get; set; } = unchecked((int)0xFFFF0000); // Red

        public bool UseGradientColors { get; set; } = true;  // Default to gradients enabled

        // Graph Scaling Settings
        public int CpuTdp { get; set; } = 125; // Watts
        public int CpuBaseClock { get; set; } = 3500; // MHz
        public int CpuBoostClock { get; set; } = 5500; // MHz

        public System.Drawing.Color GetCoolColorValue() => System.Drawing.Color.FromArgb(CoolColor);
        public System.Drawing.Color GetWarmColorValue() => System.Drawing.Color.FromArgb(WarmColor);
        public System.Drawing.Color GetHotColorValue() => System.Drawing.Color.FromArgb(HotColor);
        public System.Drawing.Color GetCriticalColorValue() => System.Drawing.Color.FromArgb(CriticalColor);
        public System.Drawing.Color GetLightTextColorValue() => System.Drawing.Color.FromArgb(LightTextColor);
        public System.Drawing.Color GetDarkTextColorValue() => System.Drawing.Color.FromArgb(DarkTextColor);

        public void SetCoolColor(System.Drawing.Color c) => CoolColor = c.ToArgb();
        public void SetWarmColor(System.Drawing.Color c) => WarmColor = c.ToArgb();
        public void SetHotColor(System.Drawing.Color c) => HotColor = c.ToArgb();
        public void SetCriticalColor(System.Drawing.Color c) => CriticalColor = c.ToArgb();
        public void SetLightTextColor(System.Drawing.Color c) => LightTextColor = c.ToArgb();
        public void SetDarkTextColor(System.Drawing.Color c) => DarkTextColor = c.ToArgb();

        public static AppSettings Load() => Load(DefaultSettingsPath);

        /// <summary>
        /// Load settings from the specified path. Exposed for testing and for future
        /// multi-profile scenarios. Invalid or corrupt files yield default settings.
        /// </summary>
        internal static AppSettings Load(string path)
        {
            AppSettings settings = new();
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                        if (loaded != null) settings = loaded;
                    }
                }
            }
            catch
            {
                // Fall through to defaults — never throw on settings load.
                settings = new AppSettings();
            }

            settings.Sanitize();
            return settings;
        }

        public void Save() => Save(DefaultSettingsPath);

        /// <summary>
        /// Atomic save: write to a temp file, then replace the destination.
        /// Prevents corruption when the process is terminated mid-write.
        /// </summary>
        internal void Save(string path)
        {
            try
            {
                Sanitize();

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, _jsonOptions);

                // Write to sibling temp file first, then atomically replace.
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(path))
                {
                    // File.Replace is atomic on NTFS; falls back to copy+delete otherwise.
                    try
                    {
                        File.Replace(tempPath, path, destinationBackupFileName: null);
                    }
                    catch
                    {
                        File.Copy(tempPath, path, overwrite: true);
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch
            {
                // Saving settings must never crash the app.
            }
        }

        /// <summary>
        /// Repair invalid or out-of-range values and replace any nullified
        /// collections that came from external (possibly hand-edited) JSON.
        /// </summary>
        internal void Sanitize()
        {
            // Null collections can appear when JSON explicitly stores "null" or when
            // a future migration drops a property. Default to empty so callers can
            // safely iterate/mutate.
            HiddenSensorIds ??= new List<string>();
            TraySensorIds ??= new List<string>();
            LogSensorIds ??= new List<string>();

            // Strings that feed into formatting / platform APIs must not be null.
            if (string.IsNullOrWhiteSpace(TrayFontFamily)) TrayFontFamily = "Segoe UI";
            if (string.IsNullOrWhiteSpace(ThemeMode)) ThemeMode = "Auto";

            if (string.IsNullOrWhiteSpace(LogFormat))
            {
                LogFormat = "CSV";
            }
            else
            {
                var trimmed = LogFormat.Trim().ToUpperInvariant();
                LogFormat = (trimmed == "CSV" || trimmed == "JSON" || trimmed == "TXT") ? trimmed : "CSV";
            }

            if (string.IsNullOrWhiteSpace(LogPath))
            {
                LogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "hotcpu", "HotCPU_Log.csv");
            }

            // Clamp numeric settings. A tampered file should never cause a
            // divide-by-zero, a 0ms timer, or a disabled UI.
            if (RefreshIntervalMs < 250) RefreshIntervalMs = 250;
            if (RefreshIntervalMs > 60_000) RefreshIntervalMs = 60_000;

            if (LogIntervalSeconds < 1) LogIntervalSeconds = 1;
            if (LogIntervalSeconds > 86_400) LogIntervalSeconds = 86_400;

            if (FontSize < 6) FontSize = 6;
            if (FontSize > 72) FontSize = 72;

            // Thresholds must be strictly Warm < Hot < Critical, otherwise
            // TemperatureReading.Level skips levels and tray colors flicker.
            if (WarmThreshold >= HotThreshold) HotThreshold = WarmThreshold + 1;
            if (HotThreshold >= CriticalThreshold) CriticalThreshold = HotThreshold + 1;
        }
    }
}
