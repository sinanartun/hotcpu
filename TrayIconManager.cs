using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
        private TemperatureLevel _lastLevel = TemperatureLevel.Cool;

        // Animation
        private readonly Image? _animationImage;
        private readonly System.Windows.Forms.Timer _animationTimer;
        private int _currentFrame = 0;
        private int _frameCount = 0;
        private bool _isAnimating = false;

        private readonly HoverInfoForm _hoverForm = new HoverInfoForm();

        public TrayIconManager(TemperatureService temperatureService, LoggerService loggerService, AppSettings settings, Action exitAction)
        {
            _temperatureService = temperatureService;
            _loggerService = loggerService;
            _settings = settings;
            _exitAction = exitAction;

            _contextMenu = CreateContextMenu();

            // Load animation gif (logic unchanged)
            try
            {
                string gifPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tenor.gif");
                if (File.Exists(gifPath))
                {
                    _animationImage = Image.FromFile(gifPath);
                    if (ImageAnimator.CanAnimate(_animationImage))
                        _frameCount = _animationImage.GetFrameCount(FrameDimension.Time);
                }
                else
                {
                     string devPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "tenor.gif");
                     if (File.Exists(devPath))
                     {
                         _animationImage = Image.FromFile(devPath);
                        if (ImageAnimator.CanAnimate(_animationImage))
                            _frameCount = _animationImage.GetFrameCount(FrameDimension.Time);
                     }
                }
            }
            catch { }

            // Setup timer
            _animationTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _animationTimer.Tick += OnAnimationTick;

            _temperatureService.TemperatureChanged += OnTemperatureChanged;
            
            // Initial update
            UpdateTrayIcons();
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            if (_animationImage == null || !_isAnimating) return;
            
            _currentFrame = (_currentFrame + 1) % _frameCount;
            _animationImage.SelectActiveFrame(FrameDimension.Time, _currentFrame);

            UpdateTrayIcons(_animationImage);
        }

        private void OnNotifyIconMouseMove(object? sender, MouseEventArgs e)
        {
            var reading = _temperatureService.CurrentReading;
            if (reading != null)
            {
                _hoverForm.UpdateData(reading);
                _hoverForm.ShowAtCursor();
            }
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

        private void ShowSettings()
        {
            var hardware = new List<HardwareTemps>();
            if (_temperatureService.CurrentReading != null)
            {
                hardware = _temperatureService.CurrentReading.AllTemps;
            }

            var form = new SettingsForm(_settings, OnSettingsChanged, hardware, _temperatureService);
            form.ShowDialog();
        }

        private void OnSettingsChanged()
        {
            _temperatureService.UpdateInterval(_settings.RefreshIntervalMs);
            _loggerService.UpdateSettings();
            UpdateTrayIcons();
        }

        private void OnTemperatureChanged(TemperatureReading reading)
        {
            if (_contextMenu.InvokeRequired)
            {
                _contextMenu.Invoke(() => OnTemperatureChanged(reading));
                return;
            }

            // Simple animation logic: trigger if ANY sensor is critical?
            // Or just stick to the main Level for now to keep it simple.
            bool critical = reading.Level == TemperatureLevel.Critical;
            _lastLevel = reading.Level;

            if (critical && _animationImage != null && _frameCount > 1)
            {
                if (!_isAnimating)
                {
                    _isAnimating = true;
                    _animationTimer.Start();
                }
            }
            else
            {
                if (_isAnimating)
                {
                    _isAnimating = false;
                    _animationTimer.Stop();
                }
                UpdateTrayIcons();
            }
        }

        private void UpdateTrayIcons(Image? bgImage = null)
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

            // 1. Remove icons no longer needed
            var toRemove = _notifyIcons.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                if (_notifyIcons.TryGetValue(key, out var icon))
                {
                    icon.Visible = false;
                    icon.Dispose();
                    _notifyIcons.Remove(key);
                }
            }

            // 2. Add/Update icons
            var allSensors = reading.AllTemps.SelectMany(h => h.Sensors).ToDictionary(s => s.Identifier);

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
                    newIcon.DoubleClick += (s, e) => ShowSettings();
                    newIcon.MouseMove += OnNotifyIconMouseMove;
                    _notifyIcons[id] = newIcon;
                }

                var notifyIcon = _notifyIcons[id];
                
                // Calculate value for this icon
                float temp = 0;
                TemperatureLevel level = TemperatureLevel.Cool;

                if (id == "DEFAULT_CPU")
                {
                    temp = reading.Temperature;
                    level = reading.Level;
                }
                else if (allSensors.TryGetValue(id, out var sensor))
                {
                    temp = sensor.Temperature;
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
                    notifyIcon.Icon = TrayIconGenerator.CreateIcon(
                        (int)Math.Round(temp), 
                        level, 
                        _settings, 
                        bgImage);
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
            
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _animationImage?.Dispose();

            _temperatureService.TemperatureChanged -= OnTemperatureChanged;
            
            // Dispose all icons
            foreach (var icon in _notifyIcons.Values)
            {
                icon.Visible = false;
                icon.Dispose();
            }
            _notifyIcons.Clear();
            
            _contextMenu.Dispose();
            _hoverForm.Dispose();
        }
    }

}
