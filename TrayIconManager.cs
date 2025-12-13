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
        private readonly NotifyIcon _notifyIcon;
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

            _notifyIcon = new NotifyIcon
            {
                Icon = TrayIconGenerator.CreateIcon(0, TemperatureLevel.Cool, _settings),
                Visible = true,
                Text = "", // Standard tooltip disabled in favor of HoverForm
                ContextMenuStrip = _contextMenu
            };

            // Load animation gif
            try
            {
                string gifPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tenor.gif");
                if (File.Exists(gifPath))
                {
                    _animationImage = Image.FromFile(gifPath);
                    if (ImageAnimator.CanAnimate(_animationImage))
                    {
                        _frameCount = _animationImage.GetFrameCount(FrameDimension.Time);
                    }
                }
                else
                {
                    // Fallback try project root for dev
                     string devPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "tenor.gif");
                     if (File.Exists(devPath))
                     {
                         _animationImage = Image.FromFile(devPath);
                        if (ImageAnimator.CanAnimate(_animationImage))
                        {
                            _frameCount = _animationImage.GetFrameCount(FrameDimension.Time);
                        }
                     }
                }
            }
            catch { }

            // Setup timer
            _animationTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _animationTimer.Tick += OnAnimationTick;

            _temperatureService.TemperatureChanged += OnTemperatureChanged;
            _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
            _notifyIcon.MouseMove += OnNotifyIconMouseMove;
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            if (_animationImage == null || !_isAnimating) return;
            
            // Advance frame
            _currentFrame = (_currentFrame + 1) % _frameCount;
            _animationImage.SelectActiveFrame(FrameDimension.Time, _currentFrame);

            // Redraw icon with current frame
            // We need current reading
            var reading = _temperatureService.CurrentReading;
            if (reading != null)
            {
                 UpdateIcon(reading, _animationImage);
            }
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
            // Use system default renderer/colors for clean look
            
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
            var sensors = new List<SensorTemp>();
            if (_temperatureService.CurrentReading != null)
            {
                foreach (var hw in _temperatureService.CurrentReading.AllTemps)
                {
                    sensors.AddRange(hw.Sensors);
                }
            }

            var form = new SettingsForm(_settings, OnSettingsChanged, sensors, _temperatureService);
            form.ShowDialog();
        }

        private void OnSettingsChanged()
        {
            _temperatureService.UpdateInterval(_settings.RefreshIntervalMs);
            _loggerService.UpdateSettings();
            // Refresh the icon immediately
            UpdateIcon(_temperatureService.CurrentReading);
        }

        private void OnTemperatureChanged(TemperatureReading reading)
        {
            // Marshall to UI thread strictly
            if (_contextMenu.InvokeRequired)
            {
                _contextMenu.Invoke(() => OnTemperatureChanged(reading));
                return;
            }

            // Check if we need to start/stop animation
            bool critical = reading.Level == TemperatureLevel.Critical;

            // Trigger Popup Alert only on transition TO Critical
            if (critical && _lastLevel != TemperatureLevel.Critical)
            {
                // Notification logic removed
            }
            _lastLevel = reading.Level;

            if (critical && _animationImage != null && _frameCount > 1)
            {
                if (!_isAnimating)
                {
                    _isAnimating = true;
                    _animationTimer.Start();
                }
                // The timer tick will handle updates now
            }
            else
            {
                if (_isAnimating)
                {
                    _isAnimating = false;
                    _animationTimer.Stop();
                }
                
                UpdateIcon(reading);
            }
        }

        private void UpdateIcon(TemperatureReading reading, Image? bgImage = null)
        {
            if (_disposed) return;

            try
            {
                var oldIcon = _notifyIcon.Icon;
                _notifyIcon.Icon = TrayIconGenerator.CreateIcon(
                    reading.RoundedTemperature, 
                    reading.Level, 
                    _settings,
                    bgImage);
                
                // Update hover form text if it's visible
                if (_hoverForm.Visible)
                     _hoverForm.UpdateData(reading);
                    
                oldIcon?.Dispose();
            }
            catch { }
        }

        private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
        {
            ShowSettings();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _animationImage?.Dispose();

            _temperatureService.TemperatureChanged -= OnTemperatureChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _hoverForm.Dispose();
        }
    }

}
