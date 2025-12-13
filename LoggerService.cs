using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Timers;

namespace HotCPU
{
    internal class LoggerService : IDisposable
    {
        private readonly TemperatureService _tempService;
        private readonly AppSettings _settings;
        private readonly System.Timers.Timer _timer;
        private bool _disposed;

        public LoggerService(TemperatureService tempService, AppSettings settings)
        {
            _tempService = tempService;
            _settings = settings;
            _timer = new System.Timers.Timer();
            _timer.Elapsed += OnTimerElapsed;
            UpdateSettings();
        }

        public void UpdateSettings()
        {
            _timer.Stop();
            if (_settings.LogEnabled && _settings.LogIntervalSeconds > 0)
            {
                _timer.Interval = _settings.LogIntervalSeconds * 1000;
                _timer.Start();
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                if (!_settings.LogEnabled) return;

                var reading = _tempService.CurrentReading;
                if (reading == null) return;

                // Flatten all sensors
                var allSensors = reading.AllTemps
                    .SelectMany(h => h.Sensors)
                    .Where(s => _settings.LogSensorIds.Contains(s.Identifier))
                    .ToList();

                if (!allSensors.Any()) return; // Nothing to log

                var timestamp = DateTime.Now;
                var logEntry = new Dictionary<string, object>
                {
                    { "Timestamp", timestamp.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                // Individual sensors
                foreach (var sensor in allSensors)
                {
                    logEntry[sensor.Name] = sensor.Temperature;
                }

                // Stats
                if (_settings.LogAverage)
                    logEntry["Average"] = allSensors.Average(s => s.Temperature);
                if (_settings.LogMin)
                    logEntry["Min"] = allSensors.Min(s => s.Temperature);
                if (_settings.LogMax)
                    logEntry["Max"] = allSensors.Max(s => s.Temperature);

                WriteLog(logEntry);
            }
            catch
            {
                // Ignore logging errors to prevent crash
            }
        }

        private void WriteLog(Dictionary<string, object> entry)
        {
            try
            {
                var dir = Path.GetDirectoryName(_settings.LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string content;
                string format = _settings.LogFormat.ToUpperInvariant();
                var sb = new System.Text.StringBuilder();

                if (format == "JSON")
                {
                    content = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false }) + Environment.NewLine;
                    File.AppendAllText(_settings.LogPath, content);
                }
                else if (format == "CSV")
                {
                    // Check if file exists to write header
                    bool fileExists = File.Exists(_settings.LogPath);
                    
                    if (!fileExists)
                    {
                        var keys = entry.Keys.ToList();
                        File.AppendAllText(_settings.LogPath, string.Join(",", keys) + Environment.NewLine);
                    }

                     foreach (var value in entry.Values)
                     {
                         var s = value is float f ? f.ToString("F1") : value.ToString();
                         if (s != null && (s.Contains(",") || s.Contains("\"")))
                         {
                             // Simple escape: double quotes around, double up internal quotes
                             s = $"\"{s.Replace("\"", "\"\"")}\"";
                         }
                         sb.Append(s).Append(',');
                     }
                    
                    if (sb.Length > 0) sb.Length--; // Remove last comma
                    sb.AppendLine();
                    File.AppendAllText(_settings.LogPath, sb.ToString());
                }
                else // TXT
                {
                    sb.Append('[').Append(entry["Timestamp"]).Append("] ");
                    foreach (var kvp in entry)
                    {
                        if (kvp.Key == "Timestamp") continue;
                        if (kvp.Value is float f)
                            sb.Append(kvp.Key).Append(": ").Append(f.ToString("F1")).Append("°C, ");
                        else
                            sb.Append(kvp.Key).Append(": ").Append(kvp.Value).Append(", ");
                    }
                    if (sb.Length >= 2 && sb[sb.Length - 2] == ',')
                        sb.Length -= 2; // Trim last ", "
                    
                    sb.AppendLine();
                    File.AppendAllText(_settings.LogPath, sb.ToString());
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
