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

                string content = "";
                string format = _settings.LogFormat.ToUpperInvariant();

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

                    // Note: This simple CSV impl assumes keys don't change often. 
                    // If keys change (user changes sensors), the CSV columns might mismatch until manual fix or file delete.
                    // For now, sufficient.
                     var values = entry.Values.Select(v => 
                        v is float f ? f.ToString("F1") : v.ToString());
                    
                    content = string.Join(",", values) + Environment.NewLine;
                    File.AppendAllText(_settings.LogPath, content);
                }
                else // TXT
                {
                    content = $"[{entry["Timestamp"]}] ";
                    foreach (var kvp in entry)
                    {
                        if (kvp.Key == "Timestamp") continue;
                        if (kvp.Value is float f)
                            content += $"{kvp.Key}: {f:F1}°C, ";
                        else
                            content += $"{kvp.Key}: {kvp.Value}, ";
                    }
                    content = content.TrimEnd(',', ' ') + Environment.NewLine;
                    File.AppendAllText(_settings.LogPath, content);
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
