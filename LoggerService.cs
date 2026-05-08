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
        private volatile bool _disposed;
        private readonly object _timerLock = new();

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
            lock (_timerLock)
            {
                if (_disposed) return;
                _timer.Stop();
                if (_settings.LogEnabled && _settings.LogIntervalSeconds > 0)
                {
                    _timer.Interval = _settings.LogIntervalSeconds * 1000;
                    _timer.Start();
                }
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                if (_disposed) return;
                if (!_settings.LogEnabled) return;

                var reading = _tempService.CurrentReading;
                var entry = BuildLogEntry(reading, _settings);
                if (entry == null) return;

                WriteLogEntry(entry, _settings);
            }
            catch
            {
                // Ignore logging errors to prevent crash
            }
        }

        /// <summary>
        /// Build the log entry dictionary from a reading. Returns null when there is nothing
        /// to log (no tracked sensors, or no matching sensors in this reading).
        /// Exposed internal for testability.
        /// </summary>
        internal static Dictionary<string, object>? BuildLogEntry(TemperatureReading? reading, AppSettings settings)
        {
            if (reading == null) return null;
            var tracked = settings.LogSensorIds ?? new List<string>();
            if (tracked.Count == 0) return null;

            var trackedSet = new HashSet<string>(tracked, StringComparer.Ordinal);

            var allSensors = reading.AllTemps
                .SelectMany(h => h.Sensors)
                .Where(s => trackedSet.Contains(s.Identifier))
                .ToList();

            if (allSensors.Count == 0) return null;

            var timestamp = DateTime.Now;
            // Preserve insertion order for stable CSV schema.
            var logEntry = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "Timestamp", timestamp.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            // Individual sensors — key by Name+Unit but de-dupe when two sensors
            // happen to share the same simplified name (e.g. two "Core (°C)").
            foreach (var sensor in allSensors)
            {
                string baseKey = $"{sensor.Name} ({sensor.Unit})";
                string key = baseKey;
                int suffix = 2;
                while (logEntry.ContainsKey(key))
                {
                    key = $"{baseKey} #{suffix++}";
                }
                logEntry[key] = sensor.Value;
            }

            if (settings.LogAverage)
                logEntry["Average"] = allSensors.Average(s => s.Value);
            if (settings.LogMin)
                logEntry["Min"] = allSensors.Min(s => s.Value);
            if (settings.LogMax)
                logEntry["Max"] = allSensors.Max(s => s.Value);

            return logEntry;
        }

        /// <summary>
        /// Normalize arbitrary user / file input into one of CSV, JSON, TXT.
        /// </summary>
        internal static string NormalizeLogFormat(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "CSV";
            var trimmed = raw.Trim().ToUpperInvariant();
            return trimmed is "CSV" or "JSON" or "TXT" ? trimmed : "CSV";
        }

        /// <summary>
        /// Format a value for CSV output. Uses invariant culture so decimal points
        /// don't become commas on European locales (which would break CSV).
        /// </summary>
        internal static string FormatCsvValue(object? value)
        {
            string? s = value switch
            {
                null => string.Empty,
                float f => f.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                double d => d.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                IFormattable fmt => fmt.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            s ??= string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            {
                s = $"\"{s.Replace("\"", "\"\"")}\"";
            }
            return s;
        }

        internal static void WriteLogEntry(Dictionary<string, object> entry, AppSettings settings)
        {
            if (entry == null || entry.Count == 0) return;
            if (string.IsNullOrEmpty(settings.LogPath)) return;

            try
            {
                var dir = Path.GetDirectoryName(settings.LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string format = NormalizeLogFormat(settings.LogFormat);
                var sb = new System.Text.StringBuilder();

                if (format == "JSON")
                {
                    var content = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false }) + Environment.NewLine;
                    File.AppendAllText(settings.LogPath, content);
                }
                else if (format == "CSV")
                {
                    var keys = entry.Keys.ToList();
                    string currentHeader = string.Join(",", keys);

                    bool fileExists = File.Exists(settings.LogPath);
                    if (fileExists)
                    {
                        // Schema integrity: if the header on disk no longer matches the
                        // current column set, rotate the old file aside so we start fresh.
                        string? existingHeader = null;
                        try { existingHeader = File.ReadLines(settings.LogPath).FirstOrDefault(); }
                        catch { /* treat as missing */ }

                        if (existingHeader != currentHeader)
                        {
                            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            string backupPath = $"{settings.LogPath}.{ts}.bak";
                            try
                            {
                                File.Move(settings.LogPath, backupPath);
                                fileExists = false;
                            }
                            catch { /* If move fails, we might append garbage, but we tried */ }
                        }
                    }

                    if (!fileExists)
                    {
                        File.AppendAllText(settings.LogPath, currentHeader + Environment.NewLine);
                    }

                    foreach (var value in entry.Values)
                    {
                        sb.Append(FormatCsvValue(value)).Append(',');
                    }

                    if (sb.Length > 0) sb.Length--; // Remove last comma
                    sb.AppendLine();
                    File.AppendAllText(settings.LogPath, sb.ToString());
                }
                else // TXT
                {
                    sb.Append('[').Append(entry["Timestamp"]).Append("] ");
                    foreach (var kvp in entry)
                    {
                        if (kvp.Key == "Timestamp") continue;
                        if (kvp.Value is float f)
                            sb.Append(kvp.Key).Append(": ").Append(f.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(", ");
                        else
                            sb.Append(kvp.Key).Append(": ").Append(kvp.Value).Append(", ");
                    }
                    if (sb.Length >= 2 && sb[sb.Length - 2] == ',')
                        sb.Length -= 2; // Trim last ", "

                    sb.AppendLine();
                    File.AppendAllText(settings.LogPath, sb.ToString());
                }
            }
            catch { }
        }

        public void Dispose()
        {
            lock (_timerLock)
            {
                if (_disposed) return;
                _disposed = true;
                try { _timer.Stop(); } catch { }
                try { _timer.Dispose(); } catch { }
            }
        }
    }
}
