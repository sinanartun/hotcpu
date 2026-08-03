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
        private bool _driverWarningShown;
        private ToolStripMenuItem? _driverStatusItem;

        public TrayIconManager(TemperatureService temperatureService, LoggerService loggerService, AppSettings settings, Action exitAction)
        {
            _temperatureService = temperatureService;
            _loggerService = loggerService;
            _settings = settings;
            _exitAction = exitAction;

            DebugLog.Info("Tray", $"TrayIconManager ctor; log path = {DebugLog.LogPath}");

            _contextMenu = CreateContextMenu();

            _temperatureService.TemperatureChanged += OnTemperatureChanged;
            
            // Initial update
            UpdateTrayIcons();
        }

        private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
        {
            DebugLog.Info("Tray", $"MouseClick button={e.Button} cursor=({Cursor.Position.X},{Cursor.Position.Y})");
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var reading = _temperatureService.CurrentReading;
            if (reading != null)
            {
                _hoverForm.UpdateData(reading);
                var cursor = Cursor.Position;
                var iconRect = new Rectangle(cursor.X - 12, cursor.Y - 12, 24, 24);
                _hoverForm.Toggle(iconRect);
            }
        }

        private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
        {
            DebugLog.Info("Tray", "DoubleClick");
            ShowSettings();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            // When the context menu is about to show, hide the pinned hover
            // panel so it can't overlap the menu and swallow clicks.
            menu.Opening += (_, _) =>
            {
                DebugLog.Info("Menu", "Opening - hiding hover panel");
                try { _hoverForm.HideWindow(); } catch (Exception ex) { DebugLog.Error("Menu", "hover hide failed", ex); }
            };
            menu.Opened += (_, _) => DebugLog.Info("Menu", "Opened");
            menu.Closed += (_, e) => DebugLog.Info("Menu", $"Closed reason={e.CloseReason}");
            menu.ItemClicked += (_, e) => DebugLog.Info("Menu", $"ItemClicked raw: {e.ClickedItem?.Text}");

            _driverStatusItem = new ToolStripMenuItem("Install PawnIO driver (for CPU temperature)...")
            {
                Visible = false,
            };
            _driverStatusItem.Click += (s, e) =>
            {
                DebugLog.Info("Menu", "Install PawnIO clicked");
                PromptInstallPawnIo();
            };

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) =>
            {
                DebugLog.Info("Menu", "Settings clicked");
                ShowSettings();
            };

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                DebugLog.Info("Menu", "Exit clicked - invoking _exitAction");
                try
                {
                    _exitAction();
                    DebugLog.Info("Menu", "_exitAction returned");
                }
                catch (Exception ex)
                {
                    DebugLog.Error("Menu", "_exitAction threw", ex);
                }
            };

            menu.Items.Add(_driverStatusItem);
            menu.Items.Add(new ToolStripSeparator { Visible = false, Name = "DriverSeparator" });
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        private static void OpenPawnIoReleases()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = CpuDriverHelper.PawnIoReleasesUrl,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Best-effort: if the shell cannot open a browser, there is
                // nothing useful to show the user here.
            }
        }

        private bool _driverInstallInProgress;

        private async void PromptInstallPawnIo()
        {
            if (_driverInstallInProgress) return;

            // If PawnIO is already installed, don't redownload the setup.
            // Instead, re-open LibreHardwareMonitor so the driver that's
            // sitting right there gets picked up. The common cause of hitting
            // this path is: HotCPU started before PawnIO was installed, so
            // LHM never saw it. A Reload() after install is all we need.
            if (CpuDriverHelper.IsPawnIoInstalled())
            {
                MessageBox.Show(
                    "PawnIO is already installed on this system. HotCPU will reconnect to it now.\n\n" +
                    "If the CPU temperature still doesn't appear, wait a moment and open this menu again - the driver service may need a few seconds to settle after a fresh install.",
                    "PawnIO already installed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                try { _temperatureService.Reload(); } catch { /* best-effort refresh */ }
                _driverWarningShown = false;
                return;
            }

            var choice = MessageBox.Show(
                "HotCPU will download the official PawnIO driver (PawnIO_setup.exe, ~3 MB) from github.com/namazso/PawnIO.Setup and install it silently.\n\n" +
                "The setup is signed by the PawnIO author. Windows will ask you once to approve the driver installation - no wizard windows to click through.\n\n" +
                "Continue?",
                "Install PawnIO driver",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);

            if (choice == DialogResult.Cancel) return;

            if (choice == DialogResult.No)
            {
                // Offer the manual link as a fallback so users can audit
                // the binary themselves before installing.
                OpenPawnIoReleases();
                return;
            }

            _driverInstallInProgress = true;
            if (_driverStatusItem != null)
            {
                _driverStatusItem.Enabled = false;
                _driverStatusItem.Text = "Downloading PawnIO driver...";
            }

            // Two-phase progress indicator: flip to "Installing..." once the
            // download finishes so the user knows something is still
            // happening during the silent install.
            var progress = new Progress<double>(pct =>
            {
                if (_driverStatusItem == null) return;
                if (pct >= 1.0)
                {
                    _driverStatusItem.Text = "Installing PawnIO driver...";
                }
            });

            PawnIoInstallResult result;
            try
            {
                // Silent install: pass /S to NSIS so the setup wizard never
                // appears. The UAC prompt for driver installation still
                // surfaces because it's enforced by Windows, not the installer.
                result = await PawnIoInstaller.DownloadAndRunAsync(silent: true, progress);
            }
            finally
            {
                _driverInstallInProgress = false;
                if (_driverStatusItem != null)
                {
                    _driverStatusItem.Enabled = true;
                    _driverStatusItem.Text = "Install PawnIO driver (for CPU temperature)...";
                }
            }

            switch (result.Outcome)
            {
                case PawnIoInstallOutcome.Installed:
                    // Re-open LHM so the freshly-registered driver is picked
                    // up immediately. Without this, the tray keeps showing
                    // dashes until the user restarts the app.
                    try { _temperatureService.Reload(); } catch { /* best-effort refresh */ }

                    MessageBox.Show(
                        "PawnIO is installed. HotCPU will start reading your CPU temperature on the next refresh. " +
                        "Restart HotCPU if the tray still shows dashes.",
                        "HotCPU",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    // Reset the one-shot warning so if the user uninstalls
                    // later we can prompt again.
                    _driverWarningShown = false;
                    break;

                case PawnIoInstallOutcome.UserCancelled:
                    // User cancelled the UAC prompt or closed the wizard - no-op.
                    break;

                case PawnIoInstallOutcome.HashMismatch:
                    MessageBox.Show(
                        "The downloaded PawnIO installer did not match the expected hash and was discarded. " +
                        "Please install it manually from the official page.",
                        "HotCPU",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    OpenPawnIoReleases();
                    break;

                case PawnIoInstallOutcome.DownloadFailed:
                case PawnIoInstallOutcome.NotAvailable:
                    MessageBox.Show(
                        $"Could not download the PawnIO installer.{(result.Detail != null ? $"\nReason: {result.Detail}" : string.Empty)}\n\n" +
                        "You can install it manually from the official page.",
                        "HotCPU",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    OpenPawnIoReleases();
                    break;

                case PawnIoInstallOutcome.InstallerFailed:
                    // Surface the exit code and any captured detail so users
                    // (and we) can tell the difference between "rejected
                    // because already installed", "access denied" and a real
                    // failure. PawnIO.Setup 2.2.0 uses DOS error codes, so
                    // looking up the exit code in `net helpmsg <code>` gives
                    // a human-readable explanation.
                    var exitInfo = result.ExitCode.HasValue
                        ? $"Exit code: {result.ExitCode.Value} (0x{result.ExitCode.Value:X})"
                        : "No exit code reported";
                    MessageBox.Show(
                        $"The PawnIO installer reported an error.\n\n{exitInfo}" +
                        (result.Detail != null ? $"\n{result.Detail}" : string.Empty) +
                        "\n\nYou can retry or install it manually from the official page.",
                        "HotCPU",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
            }
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
                    // BeginInvoke: never block the temperature poller on the UI
                    // thread (avoids deadlocks with Reload/service locks).
                    _contextMenu.BeginInvoke(() => OnTemperatureChanged(reading));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            try
            {
                UpdateTrayIcons();
            }
            catch (Exception ex)
            {
                DebugLog.Error("Tray", "UpdateTrayIcons failed", ex);
            }
        }

        private void UpdateTrayIcons()
        {
            if (_disposed) return;
            var reading = _temperatureService.CurrentReading;
            if (reading == null) return;

            // Reveal / hide the PawnIO shortcut in the context menu.
            UpdateDriverStatusMenu(reading);

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
                    if (!reading.HasCpuTemperature)
                    {
                        // CPU sensor is unavailable - don't pretend there's a
                        // value. Suppress threshold coloring and show dashes.
                        suppressColorChange = true;
                    }
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
                        if (id == "DEFAULT_CPU" && !reading.HasCpuTemperature)
                        {
                            // No real CPU reading - render dashes instead of
                            // whatever the fallback happened to latch onto.
                            text = "--";
                        }
                        else if (isThroughputSensor && selectedSensor != null)
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

                    // NotifyIcon.Text is limited to 63 characters on older
                    // Windows versions; the Windows 10/11 API accepts more but
                    // we keep the message short to stay compatible.
                    string hoverText = id == "DEFAULT_CPU"
                        ? TruncateTooltip(reading.TooltipText)
                        : "HotCPU";
                    try { notifyIcon.Text = hoverText; } catch { }
                }
                catch { }
            }

            // Link HoverForm update if visible
            if (_hoverForm.Visible)
                 _hoverForm.UpdateData(reading);
        }

        private static string TruncateTooltip(string text)
        {
            // NotifyIcon.Text has a historical 63-character limit. Keep within
            // bounds and append an ellipsis when we truncate.
            const int Limit = 63;
            if (string.IsNullOrEmpty(text) || text.Length <= Limit) return text ?? string.Empty;
            return text.Substring(0, Limit - 1) + "…";
        }

        private void UpdateDriverStatusMenu(TemperatureReading reading)
        {
            bool needsDriver = reading.CpuStatus == CpuSensorStatus.DriverMissing;

            if (_driverStatusItem != null)
            {
                _driverStatusItem.Visible = needsDriver;
            }

            // Toggle the separator that sits right after the status item.
            if (_contextMenu.Items["DriverSeparator"] is ToolStripSeparator sep)
            {
                sep.Visible = needsDriver;
            }

            if (needsDriver && !_driverWarningShown)
            {
                _driverWarningShown = true;
                ShowDriverMissingBalloon();
            }
        }

        private void ShowDriverMissingBalloon()
        {
            // Use the first visible tray icon to host the balloon. There will
            // always be at least one by the time this runs because
            // UpdateTrayIcons creates DEFAULT_CPU on the first call.
            NotifyIcon? host = null;
            foreach (var ni in _notifyIcons.Values)
            {
                if (ni.Visible) { host = ni; break; }
            }
            if (host == null) return;

            try
            {
                host.BalloonTipTitle = "CPU sensor unavailable";
                host.BalloonTipText =
                    "HotCPU couldn't read your CPU temperature. Install the free PawnIO driver to enable Ryzen/Intel readings.\n" +
                    "Right-click the tray icon to open the download page.";
                host.BalloonTipIcon = ToolTipIcon.Warning;
                host.ShowBalloonTip(8000);
            }
            catch
            {
                // Showing a balloon is a nicety, never fatal.
            }
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
