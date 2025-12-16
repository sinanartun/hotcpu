using System;
using System.IO;
using System.Text.Json;

namespace HotCPU
{
    /// <summary>
    /// Application settings with JSON persistence.
    /// </summary>
    internal class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HotCPU", "settings.json");
        
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        // Settings properties
        public int RefreshIntervalMs { get; set; } = 1000;
        public int WarmThreshold { get; set; } = 60;
        public int HotThreshold { get; set; } = 80;
        public int CriticalThreshold { get; set; } = 90;
        public bool StartWithWindows { get; set; } = false;
        public int FontSize { get; set; } = 14;
        public bool ShowTrayIconTemperature { get; set; } = true;
        public List<string> HiddenSensorIds { get; set; } = new();
        public List<string> TraySensorIds { get; set; } = new();
        public string? Language { get; set; }

        // Logging settings
        public bool LogEnabled { get; set; } = false;
        public string LogPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HotCPU_Log.csv");
        public int LogIntervalSeconds { get; set; } = 5;
        public string LogFormat { get; set; } = "CSV"; // CSV, JSON, TXT
        public List<string> LogSensorIds { get; set; } = new();
        public bool LogAverage { get; set; } = false;
        public bool LogMin { get; set; } = false;
        public bool LogMax { get; set; } = false;
        
        // Color settings (stored as ARGB integers for JSON serialization)
        public int CoolColor { get; set; } = unchecked((int)0xFFFFFFFF);  // White
        public int WarmColor { get; set; } = unchecked((int)0xFFFFA500);  // Orange
        public int HotColor { get; set; } = unchecked((int)0xFFFF4500);   // Red-Orange
        public int CriticalColor { get; set; } = unchecked((int)0xFFFF0000); // Red
        public bool UseGradientColors { get; set; } = false;  // Default to white only

        public System.Drawing.Color GetCoolColorValue() => System.Drawing.Color.FromArgb(CoolColor);
        public System.Drawing.Color GetWarmColorValue() => System.Drawing.Color.FromArgb(WarmColor);
        public System.Drawing.Color GetHotColorValue() => System.Drawing.Color.FromArgb(HotColor);
        public System.Drawing.Color GetCriticalColorValue() => System.Drawing.Color.FromArgb(CriticalColor);
        
        public void SetCoolColor(System.Drawing.Color c) => CoolColor = c.ToArgb();
        public void SetWarmColor(System.Drawing.Color c) => WarmColor = c.ToArgb();
        public void SetHotColor(System.Drawing.Color c) => HotColor = c.ToArgb();
        public void SetCriticalColor(System.Drawing.Color c) => CriticalColor = c.ToArgb();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
