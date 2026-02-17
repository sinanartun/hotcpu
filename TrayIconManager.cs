using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HotCPU
{
    /// <summary>
    /// Manages the system tray icon, context menu, and temperature display.
    /// </summary>
    internal sealed class TrayIconManager : IDisposable
    {
        // Dictionary of SensorID -> NotifyIcon
        private readonly Dictionary<string, NotifyIcon> _notifyIcons = new();
        
        private readonly TemperatureService _temperatureService;
        private readonly LoggerService _loggerService;
        private readonly AppSettings _settings;
        private readonly Action _exitAction;
        private readonly ContextMenuStrip _contextMenu;
        private bool _disposed;

        private readonly HoverInfoForm _hoverForm = new HoverInfoForm();

        public TrayIconManager(TemperatureService temperatureService, LoggerService loggerService, AppSettings settings, Action exitAction)
        {
            _temperatureService = temperatureService;
            _loggerService = loggerService;
            _settings = settings;
            _exitAction = exitAction;

            _contextMenu = CreateContextMenu();

            _temperatureService.TemperatureChanged += OnTemperatureChanged;
            
            // Initial update
            UpdateTrayIcons();
        }

        private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var reading = _temperatureService.CurrentReading;
            if (reading != null)
            {
                _hoverForm.UpdateData(reading);
                _hoverForm.ShowAtCursor();
            }
        }

        private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
        {
            ShowSettings();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            
            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => ShowSettings();

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => _exitAction();

            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        private SettingsForm? _currentSettingsForm;

        private void ShowSettings()
        {
            if (_currentSettingsForm != null && !_currentSettingsForm.IsDisposed)
            {
                _currentSettingsForm.BringToFront();
                _currentSettingsForm.Activate();
                return;
            }

            var hardware = new List<HardwareTemps>();
            if (_temperatureService.CurrentReading != null)
            {
                hardware = _temperatureService.CurrentReading.AllTemps;
            }
 
            _currentSettingsForm = new SettingsForm(_settings, OnSettingsChanged, hardware, _temperatureService);
            _currentSettingsForm.FormClosed += (s, e) => _currentSettingsForm = null;
            _currentSettingsForm.Show();
        }

        private void OnSettingsChanged()
        {
            _temperatureService.UpdateInterval(_settings.RefreshIntervalMs);
            _loggerService.UpdateSettings();
            UpdateTrayIcons();
        }

        private void OnTemperatureChanged(TemperatureReading reading)
        {
            if (_disposed || _contextMenu.IsDisposed) return;

            if (_contextMenu.InvokeRequired)
            {
                try 
                {
                    _contextMenu.Invoke(() => OnTemperatureChanged(reading));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            UpdateTrayIcons();
        }

        private void UpdateTrayIcons()
        {
            if (_disposed) return;
            var reading = _temperatureService.CurrentReading;
            if (reading == null) return;

            var activeIds = new HashSet<string>(_settings.TraySensorIds);
            
            // Fallback: If no sensors selected, use default behavior (Main CPU)
            if (activeIds.Count == 0)
            {
                // We fake an ID for the default view
                activeIds.Add("DEFAULT_CPU"); 
            }

            // 1. Build sensor map
            var allSensors = reading.AllTemps
                .SelectMany(h => h.Sensors)
                .GroupBy(s => s.Identifier)
                .ToDictionary(g => g.Key, g => g.First());

            var sensorHardwareInfo = reading.AllTemps
                .SelectMany(h => h.Sensors.Select(s => new { s.Identifier, h.Type, h.Name }))
                .GroupBy(x => x.Identifier)
                .ToDictionary(g => g.Key, g => g.First());

            // 2. Remove icons no longer needed
            var toRemove = _notifyIcons.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                if (_notifyIcons.TryGetValue(key, out var icon))
                {
                    icon.DoubleClick -= OnNotifyIconDoubleClick;
                    icon.MouseClick -= OnNotifyIconMouseClick;
                    icon.Visible = false;
                    icon.Icon?.Dispose();
                    icon.Dispose();
                    _notifyIcons.Remove(key);
                }
            }

            // 3. Add/Update icons
            foreach (var id in activeIds)
            {
                if (!_notifyIcons.ContainsKey(id))
                {
                    // Create new icon
                    var newIcon = new NotifyIcon
                    {
                        Visible = true,
                        ContextMenuStrip = _contextMenu,
                        Text = "HotCPU"
                    };
                    newIcon.DoubleClick += OnNotifyIconDoubleClick;
                    newIcon.MouseClick += OnNotifyIconMouseClick;
                    _notifyIcons[id] = newIcon;
                }

                var notifyIcon = _notifyIcons[id];
                
                // Calculate value for this icon
                float temp = 0;
                TemperatureLevel level = TemperatureLevel.Cool;
                bool suppressColorChange = false;
                bool renderBadge = true;
                string prefix = "";
                SensorTemp? selectedSensor = null;
                bool isUploadSensor = false;
                bool isThroughputSensor = false;
                bool isNetworkHardware = false;
                string? hardwareName = null;

                if (id == "DEFAULT_CPU")
                {
                    temp = reading.Temperature;
                    level = reading.Level;
                }
                else if (allSensors.TryGetValue(id, out var sensor))
                {
                    selectedSensor = sensor;
                    temp = sensor.Temperature;
                    isUploadSensor = IsUploadSensor(sensor);
                    isThroughputSensor = IsThroughputSensor(sensor);
                    if (sensorHardwareInfo.TryGetValue(id, out var hwInfo))
                    {
                        hardwareName = hwInfo.Name;
                        isNetworkHardware = string.Equals(hwInfo.Type, "Network", StringComparison.OrdinalIgnoreCase);
                    }
                    if (!isNetworkHardware && LooksLikeNetworkText(hardwareName))
                        isNetworkHardware = true;
                    if (!isNetworkHardware && LooksLikeNetworkText(sensor.Name))
                        isNetworkHardware = true;
                    if (isUploadSensor)
                    {
                        prefix = "↑";
                        suppressColorChange = true;
                    }
                    if (isNetworkHardware || isThroughputSensor)
                        renderBadge = false;
                    // Determine level for THIS sensor individually based on global thresholds?
                    // Yes, usage global settings for consistency.
                    if (temp >= _settings.CriticalThreshold) level = TemperatureLevel.Critical;
                    else if (temp >= _settings.HotThreshold) level = TemperatureLevel.Hot;
                    else if (temp >= _settings.WarmThreshold) level = TemperatureLevel.Warm;
                }
                else
                {
                    // Sensor not found (disconnected?)
                    // Show 0 or ?
                    temp = 0;
                }
                
                // Draw
                try
                {
                    var oldIcon = notifyIcon.Icon;
                    
                    string text = "";
                    if (_settings.ShowTrayIconTemperature)
                    {
                        if (isThroughputSensor && selectedSensor != null)
                        {
                            text = FormatThroughputInline(selectedSensor);
                        }
                        else
                        {
                            // Smart formatting for 16x16 icon
                            if (temp > -10 && temp < 10) 
                                text = temp.ToString("F1"); // "1.2", "-5.5"
                            else if (temp >= 1000 && temp < 10000) 
                                text = (temp / 1000f).ToString("F1") + "k"; // "1.5k"
                            else if (temp >= 10000)
                                text = (temp / 1000f).ToString("F0") + "k"; // "15k"
                            else
                                text = Math.Round(temp).ToString(); // "50", "-15", "999"
                        }
                    }

                    if (!string.IsNullOrEmpty(prefix))
                        text = prefix + text;

                    notifyIcon.Icon = TrayIconGenerator.CreateIcon(
                        text,
                        temp, 
                        level, 
                        _settings,
                        suppressColorChange,
                        renderBadge);
                    oldIcon?.Dispose();
                }
                catch { }
            }

            // Link HoverForm update if visible
            if (_hoverForm.Visible)
                 _hoverForm.UpdateData(reading);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _temperatureService.TemperatureChanged -= OnTemperatureChanged;
            
            // Dispose all icons - unsubscribe events first
            foreach (var icon in _notifyIcons.Values)
            {
                icon.DoubleClick -= OnNotifyIconDoubleClick;
                icon.MouseClick -= OnNotifyIconMouseClick;
                icon.Visible = false;
                icon.Icon?.Dispose();
                icon.Dispose();
            }
            _notifyIcons.Clear();
            
            _contextMenu.Dispose();
            _hoverForm.Dispose();
        }

        private static bool IsUploadSensor(SensorTemp sensor)
        {
            if (sensor == null) return false;
            var name = sensor.Name ?? "";
            return name.IndexOf("upload", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("uploaded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsThroughputSensor(SensorTemp sensor)
        {
            if (sensor == null) return false;
            var unit = sensor.Unit ?? string.Empty;
            return sensor.Type.Equals("Throughput", StringComparison.OrdinalIgnoreCase)
                   || unit.Contains("/s", StringComparison.OrdinalIgnoreCase)
                   || unit.Contains("bps", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeNetworkText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("ethernet", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("wifi", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("vethernet", StringComparison.OrdinalIgnoreCase) >= 0;
        }


        private static string FormatCompactValue(SensorTemp sensor)
        {
            float value = sensor.Value;
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            if (value < 0) value = 0;

            string unit = sensor.Unit ?? string.Empty;
            bool isThroughput = sensor.Type.Equals("Throughput", StringComparison.OrdinalIgnoreCase)
                                || unit.Contains("/s", StringComparison.OrdinalIgnoreCase)
                                || unit.Contains("bps", StringComparison.OrdinalIgnoreCase);

            float v = value;
            string suffix = "";

            if (isThroughput)
            {
                if (unit.Contains("KB/s", StringComparison.OrdinalIgnoreCase))
                {
                    if (v >= 1024f * 1024f) { v /= 1024f * 1024f; suffix = "G"; }
                    else if (v >= 1024f) { v /= 1024f; suffix = "M"; }
                    else { suffix = "K"; }
                }
                else if (unit.Contains("MB/s", StringComparison.OrdinalIgnoreCase))
                {
                    if (v >= 1024f) { v /= 1024f; suffix = "G"; }
                    else { suffix = "M"; }
                }
                else if (unit.Contains("GB/s", StringComparison.OrdinalIgnoreCase))
                {
                    suffix = "G";
                }
                else
                {
                    if (v >= 1_000_000f) { v /= 1_000_000f; suffix = "M"; }
                    else if (v >= 1_000f) { v /= 1_000f; suffix = "K"; }
                }
            }
            else if (v >= 1000f)
            {
                v /= 1000f;
                suffix = "k";
            }

            string num;
            if (v >= 100f) num = Math.Round(v).ToString();
            else if (v >= 10f) num = Math.Round(v).ToString();
            else if (v >= 1f) num = v.ToString(v >= 9.95f ? "F0" : "F1");
            else num = "0";

            if (num.EndsWith(".0", StringComparison.Ordinal))
                num = num.Substring(0, num.Length - 2);

            string text = num + suffix;

            if (text.Length > 3)
            {
                num = ((int)Math.Round(v)).ToString();
                if (num.Length > 2) num = num.Substring(0, 2);
                text = num + suffix;
            }

            if (text.Length > 3)
                text = text.Substring(0, 3);

            return text;
        }

        private static string FormatUploadTwoLine(SensorTemp sensor)
        {
            float value = sensor.Value;
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
            if (value < 0) value = 0;

            string unit = sensor.Unit ?? string.Empty;
            bool isThroughput = sensor.Type.Equals("Throughput", StringComparison.OrdinalIgnoreCase)
                                || unit.Contains("/s", StringComparison.OrdinalIgnoreCase)
                                || unit.Contains("bps", StringComparison.OrdinalIgnoreCase);

            if (!isThroughput)
            {
                return FormatCompactValue(sensor);
            }

            string unitLine = ResolveThroughputUnit(unit);
            if (string.IsNullOrEmpty(unitLine))
            {
                return FormatCompactValue(sensor);
            }

            float v = value;
            if (unitLine.Equals("B/s", StringComparison.OrdinalIgnoreCase))
            {
                if (v >= 1_000_000_000f) { v /= 1_000_000_000f; unitLine = "GB/s"; }
                else if (v >= 1_000_000f) { v /= 1_000_000f; unitLine = "MB/s"; }
                else if (v >= 1_000f) { v /= 1_000f; unitLine = "KB/s"; }
            }
            else if (unitLine.Equals("KB/s", StringComparison.OrdinalIgnoreCase))
            {
                if (v >= 1_000_000f) { v /= 1_000_000f; unitLine = "GB/s"; }
                else if (v >= 1_000f) { v /= 1_000f; unitLine = "MB/s"; }
            }
            else if (unitLine.Equals("MB/s", StringComparison.OrdinalIgnoreCase))
            {
                if (v >= 1_000f) { v /= 1_000f; unitLine = "GB/s"; }
            }

            int iv = (int)Math.Round(v);
            if (iv < 0) iv = 0;
            if (iv > 999) iv = 999;
            string valueLine = iv.ToString("D2");

            return valueLine + "\n" + unitLine;
        }

        private static string FormatThroughputInline(SensorTemp sensor)
        {
            float value = sensor.Value;
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
            if (value < 0) value = 0;

            string unit = sensor.Unit ?? string.Empty;
            string unitLine = ResolveThroughputUnit(unit);
            if (string.IsNullOrEmpty(unitLine))
                return FormatCompactValue(sensor);

            // Convert to bytes per second using decimal units for display consistency.
            double bytesPerSecond = unitLine.ToUpperInvariant() switch
            {
                "GB/S" => value * 1_000_000_000d,
                "MB/S" => value * 1_000_000d,
                "KB/S" => value * 1_000d,
                "B/S" => value,
                "BPS" => value / 8d,
                _ => value * 1_000d
            };

            if (bytesPerSecond < 0) bytesPerSecond = 0;

            double displayValue;
            string suffix;
            if (bytesPerSecond >= 1_000_000_000d)
            {
                displayValue = bytesPerSecond / 1_000_000_000d;
                suffix = "G";
            }
            else if (bytesPerSecond >= 1_000_000d)
            {
                displayValue = bytesPerSecond / 1_000_000d;
                suffix = "M";
            }
            else
            {
                displayValue = bytesPerSecond / 1_000d;
                suffix = "K";
            }

            int iv = (int)Math.Round(displayValue);
            if (iv < 0) iv = 0;
            if (iv > 99) iv = 99;
            string valueLine = iv.ToString("D2");

            return valueLine + suffix;
        }

        private static string ResolveThroughputUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return string.Empty;
            if (unit.Contains("GB/s", StringComparison.OrdinalIgnoreCase)) return "GB/s";
            if (unit.Contains("MB/s", StringComparison.OrdinalIgnoreCase)) return "MB/s";
            if (unit.Contains("KB/s", StringComparison.OrdinalIgnoreCase)) return "KB/s";
            if (unit.Contains("B/s", StringComparison.OrdinalIgnoreCase)) return "B/s";
            if (unit.Contains("bps", StringComparison.OrdinalIgnoreCase)) return "bps";
            return string.Empty;
        }
    }

}
