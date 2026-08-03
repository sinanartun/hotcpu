using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using System.Drawing.Drawing2D;

namespace HotCPU
{
    /// <summary>
    /// A borderless "inspector" panel that doubles as a rich tooltip for the
    /// tray icon. The previous version used a cursor-proximity hide-timer
    /// that would close the panel the moment the user tried to mouse onto
    /// it; this version:
    ///   * Anchors above/below the tray icon, not the cursor.
    ///   * Uses MouseLeave + a grace timer so incidental pointer jitter
    ///     never closes it.
    ///   * Supports scrolling when content outgrows the screen.
    ///   * Supports explicit close via ESC, a close button, or a second
    ///     click on the tray icon.
    /// </summary>
    internal class HoverInfoForm : Form
    {
        private TemperatureReading? _currentReading;

        // Grace timer: counts up from 0 while cursor is OUTSIDE the form.
        // If it ever re-enters, the counter resets. Close at >= DismissAfterTicks.
        private readonly System.Windows.Forms.Timer _dismissTimer;
        private int _ticksOutside;
        private bool _pinned;
        private Rectangle _anchorRect;

        // Tunables
        private const int DismissTickInterval = 50;   // ms
        private const int DismissAfterTicks = 12;     // -> 600ms grace window
        private const int AnchorGapPx = 4;            // vertical space between tray and form
        private const int MaxHeightPercent = 80;      // cap form height at this % of workarea
        private const int ContentWidth = 560;

        private readonly Font _fontBold;
        private readonly Font _fontNormal;
        private readonly Font _fontEmoji;

        // Inner canvas holds the drawn content. An AutoScroll container means
        // tall readings scroll inside the form instead of being clipped at
        // screen edges (which the previous version did).
        private readonly Panel _scrollHost;
        private readonly ContentCanvas _canvas;
        private readonly Button _closeButton;

        public HoverInfoForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Padding = new Padding(1);
            DoubleBuffered = true;
            KeyPreview = true;
            Size = new Size(ContentWidth + 2, 120);

            _fontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
            _fontNormal = new Font("Segoe UI", 9f, FontStyle.Regular);
            _fontEmoji = new Font("Segoe UI Emoji", 9f);

            // Close chip (top-right) so users always have an obvious way out.
            _closeButton = new Button
            {
                Text = "✕",
                FlatStyle = FlatStyle.Flat,
                Size = new Size(22, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(ContentWidth - 22, 2),
                TabStop = false,
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                FlatAppearance = { BorderSize = 0 },
            };
            _closeButton.Click += (_, _) => HideWindow();

            _scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(24, 24, 24),
                Padding = new Padding(0, 26, 0, 4), // leave room for close button
            };
            _canvas = new ContentCanvas(this)
            {
                Location = new Point(0, 0),
                Width = ContentWidth - SystemInformation.VerticalScrollBarWidth,
                BackColor = Color.FromArgb(24, 24, 24),
            };
            _scrollHost.Controls.Add(_canvas);
            Controls.Add(_scrollHost);
            Controls.Add(_closeButton);
            _closeButton.BringToFront();

            _dismissTimer = new System.Windows.Forms.Timer { Interval = DismissTickInterval };
            _dismissTimer.Tick += DismissTimer_Tick;

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) HideWindow();
            };

            // Re-enter events reset the grace timer.
            _scrollHost.MouseEnter += (_, _) => _ticksOutside = 0;
            _canvas.MouseEnter     += (_, _) => _ticksOutside = 0;
            _closeButton.MouseEnter += (_, _) => _ticksOutside = 0;
        }

        private void ApplyTheme()
        {
            if (_currentReading?.Settings == null) return;
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_currentReading.Settings);
            BackColor = Helpers.ThemeHelper.GetBackgroundColor(isDark);
            _scrollHost.BackColor = Helpers.ThemeHelper.GetHeaderColor(isDark);
            _canvas.BackColor = Helpers.ThemeHelper.GetHeaderColor(isDark);
            _closeButton.ForeColor = Helpers.ThemeHelper.GetTextColor(isDark);
        }

        public void UpdateData(TemperatureReading reading)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    // BeginInvoke avoids deadlocking a background poll thread that
                    // is waiting on the UI while the UI waits on a lock/service.
                    BeginInvoke(() => UpdateData(reading));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            if (IsDisposed) return;

            try
            {
                _currentReading = reading;
                ApplyTheme();
                ResizeContent();
                _canvas.Invalidate();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void ResizeContent()
        {
            if (_currentReading == null) return;
            int contentHeight = MeasureContentHeight(_currentReading);
            _canvas.Height = contentHeight;

            var screen = Screen.FromControl(this);
            int maxH = (int)(screen.WorkingArea.Height * MaxHeightPercent / 100d);
            int desired = Math.Min(contentHeight + 32 /* padding + close row */, maxH);
            // Only resize width once; keep height flexible.
            Width = ContentWidth + 2;
            Height = desired;
        }

        public void UpdateText(string text) { /* kept for compatibility */ }

        /// <summary>
        /// Show the inspector anchored to the given tray icon bounds. The caller
        /// passes the icon rectangle so the form can position itself with a
        /// stable anchor and draw a short connector gap - moving the cursor
        /// toward the form no longer instantly dismisses it.
        /// </summary>
        public void ShowAnchored(Rectangle iconBounds, bool pin)
        {
            _pinned = pin;
            _anchorRect = iconBounds;

            if (_currentReading != null) ResizeContent();

            var screen = Screen.FromPoint(new Point(iconBounds.X, iconBounds.Y));
            int x = iconBounds.X + iconBounds.Width / 2 - Width / 2;
            int y = iconBounds.Top - Height - AnchorGapPx;

            // If there isn't room above, drop below the icon.
            if (y < screen.WorkingArea.Top)
                y = iconBounds.Bottom + AnchorGapPx;

            // Clamp horizontally within the working area.
            if (x + Width > screen.WorkingArea.Right) x = screen.WorkingArea.Right - Width - 4;
            if (x < screen.WorkingArea.Left) x = screen.WorkingArea.Left + 4;
            if (y + Height > screen.WorkingArea.Bottom) y = screen.WorkingArea.Bottom - Height - 4;
            if (y < screen.WorkingArea.Top) y = screen.WorkingArea.Top + 4;

            Location = new Point(x, y);
            _ticksOutside = 0;

            if (!Visible) Show();
            BringToFront();
            if (!_pinned) _dismissTimer.Start();
            else _dismissTimer.Stop();
        }

        /// <summary>
        /// Backwards-compatible cursor-anchor entry point. Prefer
        /// <see cref="ShowAnchored"/> which knows the tray icon rect.
        /// </summary>
        public void ShowAtCursor()
        {
            var cursor = Cursor.Position;
            // Treat the cursor point as a 1x1 anchor; position will be
            // recalculated by ShowAnchored.
            ShowAnchored(new Rectangle(cursor.X - 8, cursor.Y - 8, 16, 16), pin: false);
        }

        /// <summary>
        /// Called by the tray icon on a left-click. If the form is already
        /// visible, hide it; otherwise show it pinned so hover tracking
        /// doesn't close it out from under the user.
        /// </summary>
        public void Toggle(Rectangle iconBounds)
        {
            DebugLog.Info("Hover", $"Toggle Visible={Visible} bounds={iconBounds}");
            if (Visible)
            {
                HideWindow();
            }
            else
            {
                ShowAnchored(iconBounds, pin: true);
            }
        }

        public void HideWindow()
        {
            DebugLog.Info("Hover", "HideWindow");
            _dismissTimer.Stop();
            _pinned = false;
            if (Visible) Hide();
        }

        private void DismissTimer_Tick(object? sender, EventArgs e)
        {
            if (_pinned) return;

            var cursor = Cursor.Position;
            // Allow cursor anywhere in the form plus a short corridor back to
            // the anchor icon. That corridor is the union of the form rect
            // and the anchor rect, extended by a generous tolerance.
            var corridor = Rectangle.Union(Bounds, _anchorRect);
            corridor.Inflate(6, 6);

            if (corridor.Contains(cursor))
            {
                _ticksOutside = 0;
            }
            else
            {
                _ticksOutside++;
                if (_ticksOutside >= DismissAfterTicks)
                {
                    HideWindow();
                }
            }
        }

        // === Content rendering ==============================================

        private int MeasureContentHeight(TemperatureReading reading)
        {
            int height = 10;
            const int rowHeight = 24;
            const int headerHeight = 22;

            if (reading.CpuStatus == CpuSensorStatus.DriverMissing)
                height += 18 + 18 + 6;

            foreach (var hw in reading.AllTemps.Where(h => h.Sensors.Any()))
            {
                var visibleSensors = GetSortedSensors(hw.Sensors);
                if (visibleSensors.Count == 0) continue;
                height += headerHeight;
                height += visibleSensors.Count * rowHeight;
                height += 5;
            }

            return Math.Max(height + 10, 80);
        }

        internal void RenderContent(Graphics g, Rectangle clip)
        {
            if (_currentReading?.Settings == null) return;

            bool isDark = Helpers.ThemeHelper.IsDarkMode(_currentReading.Settings);
            Color headerColor = Helpers.ThemeHelper.GetHeaderColor(isDark);
            Color textColor = Helpers.ThemeHelper.GetTextColor(isDark);
            Color dimTextColor = Helpers.ThemeHelper.GetDimTextColor(isDark);
            Color gridColor = Helpers.ThemeHelper.GetBorderColor(isDark);

            g.Clear(headerColor);

            float y = 10;
            float xName = 10;
            float chartWidth = 220;
            float xChart = _canvas.Width - chartWidth - 16;
            float rowHeight = 24;

            using var brushText = new SolidBrush(textColor);
            using var brushDim = new SolidBrush(dimTextColor);
            using var penGrid = new Pen(gridColor, 1f);

            if (_currentReading.CpuStatus == CpuSensorStatus.DriverMissing)
            {
                using var warnBrush = new SolidBrush(Color.FromArgb(0xFF, 0xA5, 0x00));
                const string line1 = "⚠  CPU temperature unavailable";
                const string line2 = "Install PawnIO to enable Ryzen / Intel sensors (right-click tray → Install PawnIO...)";
                g.DrawString(line1, _fontBold, warnBrush, xName, y);
                y += 18;
                g.DrawString(line2, _fontNormal, brushDim, xName, y);
                y += 18;
                g.DrawLine(penGrid, xName, y, _canvas.Width - 10, y);
                y += 6;
            }

            foreach (var hw in _currentReading.AllTemps.Where(h => h.Sensors.Any()))
            {
                var visibleSensors = GetSortedSensors(hw.Sensors);
                if (visibleSensors.Count == 0) continue;

                g.DrawString(hw.Icon, _fontEmoji, Brushes.Orange, xName, y);
                var iconSize = g.MeasureString(hw.Icon, _fontEmoji);
                float textX = xName + iconSize.Width - 5;
                if (textX < xName + 18) textX = xName + 18;

                g.DrawString(hw.Name, _fontBold, Brushes.Orange, textX, y);
                g.DrawLine(penGrid, xName, y + 18, _canvas.Width - 10, y + 18);
                y += 22;

                foreach (var sensor in visibleSensors)
                {
                    string name = sensor.Name;
                    if (name.Length > 28) name = name.Substring(0, 25) + "...";
                    g.DrawString(name, _fontNormal, brushDim, xName, y);

                    string finalValStr = FormatValue(sensor.Value, sensor.Unit);
                    var sizeVal = g.MeasureString(finalValStr, _fontBold);
                    float valDrawX = xChart - 5 - sizeVal.Width;

                    g.DrawString(finalValStr, _fontBold, brushText, valDrawX, y);

                    // Chart style per unit (kept in parity with the prior build).
                    Color chartColor = Color.DeepSkyBlue;
                    float? fixedMin = null;
                    float? fixedMax = null;
                    bool fillArea = false;
                    float lineThickness = 1.5f;

                    if (sensor.Unit == "°C")
                    {
                        chartColor = Color.OrangeRed;
                        lineThickness = 2.5f;
                        fixedMin = 30f;
                        fixedMax = 100f;
                    }
                    else if (sensor.Unit == "%")
                    {
                        chartColor = Color.Lime;
                        fillArea = true;
                        fixedMin = 0f;
                        fixedMax = 100f;
                    }
                    else if (sensor.Unit == "W")
                    {
                        chartColor = Color.Yellow;
                        fixedMin = 0f;
                        float tdp = _currentReading.Settings.CpuTdp > 0 ? _currentReading.Settings.CpuTdp : 125f;
                        fixedMax = tdp * 1.2f;
                    }
                    else if (sensor.Unit == "MHz")
                    {
                        chartColor = Color.DeepSkyBlue;
                        fixedMin = _currentReading.Settings.CpuBaseClock;
                        fixedMax = _currentReading.Settings.CpuBoostClock;
                        if (fixedMin >= fixedMax) { fixedMin = 0; fixedMax = 5000; }
                    }
                    else
                    {
                        chartColor = _currentReading.Settings.GetWarmColorValue();
                    }

                    DrawSparkline(g, xChart, y, chartWidth, rowHeight - 2, sensor.History, sensor.Temperature, chartColor, fixedMin, fixedMax, fillArea, lineThickness);

                    y += rowHeight;
                }
                y += 5;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                // WS_EX_TOOLWINDOW keeps the form out of Alt-Tab and the taskbar;
                // WS_EX_NOACTIVATE prevents stealing focus from whatever the
                // user is working on.
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fontBold?.Dispose();
                _fontNormal?.Dispose();
                _fontEmoji?.Dispose();
                _dismissTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        // === Sensor sorting / filtering (unchanged from previous build) =====

        private bool IsVisible(SensorTemp s)
        {
            if (_currentReading?.Settings?.HiddenSensorIds == null) return true;
            if (_currentReading.Settings.HiddenSensorIds.Contains(s.Identifier)) return false;
            return !IsSensorHiddenByOptimization(s);
        }

        private static bool IsSensorHiddenByOptimization(SensorTemp s)
        {
            if (!s.Name.Contains("Core #")) return false;
            if (s.Name.Contains("SMU", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Name.Contains("VID", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private List<SensorTemp> GetSortedSensors(List<SensorTemp> sensors)
        {
            return sensors
                .Where(s => IsVisible(s))
                .OrderBy(s => GetSensorBlockPriority(s))
                .ThenBy(s => HotCPU.Helpers.StringHelper.ExtractNumber(s.Name))
                .ThenBy(s => s.Name)
                .ToList();
        }

        private static int GetSensorBlockPriority(SensorTemp s)
        {
            if (s.Name.Equals("Total CPU Usage", StringComparison.OrdinalIgnoreCase)) return 0;
            if (s.Name.Equals("Core Max", StringComparison.OrdinalIgnoreCase)) return 1;
            if (s.Name.Contains("Package Power", StringComparison.OrdinalIgnoreCase)) return 2;
            if (s.Name.Contains("DRAM Power", StringComparison.OrdinalIgnoreCase)) return 3;
            if (s.Unit == "%" && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 10;
            if (s.Name.Contains("Clock", StringComparison.OrdinalIgnoreCase) && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 20;
            return 99;
        }

        private void DrawSparkline(Graphics g, float x, float y, float w, float h, float[] history, float current, Color color, float? fixedMin = null, float? fixedMax = null, bool fillArea = false, float lineThickness = 1.5f)
        {
            if (history == null || history.Length < 2) return;

            bool isDark = _currentReading?.Settings != null && Helpers.ThemeHelper.IsDarkMode(_currentReading.Settings);
            Color gridColor = isDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
            Color chartBg = isDark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(245, 245, 245);

            using (var bgBrush = new SolidBrush(chartBg))
                g.FillRectangle(bgBrush, x, y, w, h);

            using (var penGrid = new Pen(gridColor, 1f) { DashStyle = DashStyle.Dot })
            {
                g.DrawLine(penGrid, x, y + h / 2, x + w, y + h / 2);
                float stepW = w / 4;
                for (int i = 1; i < 4; i++)
                    g.DrawLine(penGrid, x + i * stepW, y, x + i * stepW, y + h);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            float min, max;
            if (fixedMin.HasValue && fixedMax.HasValue)
            {
                min = fixedMin.Value; max = fixedMax.Value;
            }
            else
            {
                min = history.Min(); max = history.Max();
                if (max - min < 5) { float mid = (min + max) / 2; min = mid - 5; if (min < 0) min = 0; max = mid + 5; }
            }

            // Guard against zero-range (identical fixedMin/Max or flat history)
            // which would produce NaN coordinates and flaky GDI+ draws.
            if (float.IsNaN(min) || float.IsNaN(max) || float.IsInfinity(min) || float.IsInfinity(max))
            {
                min = 0; max = 1;
            }
            if (max - min < 0.001f)
            {
                max = min + 1f;
            }

            int maxCapacity = TemperatureService.MAX_HISTORY;
            float range = max - min;
            float stepX = w / (Math.Max(maxCapacity, 2) - 1);
            int missingPoints = Math.Max(0, maxCapacity - history.Length);
            float startX = x + (missingPoints * stepX);

            var points = new List<PointF>();
            for (int i = 0; i < history.Length; i++)
            {
                float val = history[i];
                if (float.IsNaN(val) || float.IsInfinity(val)) continue;
                float px = startX + (i * stepX);
                float normalized = (val - min) / range;
                if (normalized < 0) normalized = 0;
                if (normalized > 1) normalized = 1;
                float py = y + h - (normalized * h);
                points.Add(new PointF(px, py));
            }

            if (points.Count < 2)
            {
                if (points.Count == 1)
                {
                    using var dotBrush = new SolidBrush(color);
                    g.FillEllipse(dotBrush, points[0].X - 2, points[0].Y - 2, 5, 5);
                }
                return;
            }

            var pointsArray = points.ToArray();

            using (var path = new GraphicsPath())
            {
                path.AddLines(pointsArray);
                float axisY = y + h;
                path.AddLine(pointsArray[^1].X, axisY, pointsArray[0].X, axisY);
                path.CloseFigure();

                if (fillArea)
                {
                    using var brush = new SolidBrush(color);
                    g.FillPath(brush, path);
                }
                else
                {
                    using var brush = new LinearGradientBrush(
                        new RectangleF(x, y, w, h),
                        Color.FromArgb(100, color),
                        Color.FromArgb(10, color),
                        90f);
                    g.FillPath(brush, path);
                }
            }

            using (var pen = new Pen(color, lineThickness))
                g.DrawLines(pen, pointsArray);

            var last = pointsArray[^1];
            using (var dotBrush = new SolidBrush(color))
                g.FillEllipse(dotBrush, last.X - 2, last.Y - 2, 5, 5);

            g.SmoothingMode = SmoothingMode.Default;
        }

        private static string FormatValue(float value, string unit)
        {
            if (unit == "KB/s")
            {
                if (value > 1024 * 1024) return $"{value / (1024 * 1024):F1} GB/s";
                if (value > 1024) return $"{value / 1024:F1} MB/s";
                return $"{value:F0} KB/s";
            }
            if (unit == "MB")
            {
                if (value > 1024) return $"{value / 1024:F1} GB";
                return $"{value:F0} MB";
            }
            if (unit.Trim().Equals("MHz", StringComparison.OrdinalIgnoreCase))
                return $"{value:F0} MHz";

            if (unit == "°C" || unit == "%" || unit == "RPM")
                return $"{Math.Round(value)}{unit}";

            return $"{value:F1} {unit}";
        }

        // === Inner rendering control =======================================

        /// <summary>
        /// A child control that draws the full sensor tree into a tall
        /// bitmap-sized canvas. Wrapping the drawing in its own control lets
        /// the surrounding ScrollablePanel scroll the content vertically -
        /// drawing directly on the form (as the old implementation did) made
        /// everything below the working-area cutoff invisible and drove the
        /// "cursor leaves window" misbehavior.
        /// </summary>
        private sealed class ContentCanvas : Control
        {
            private readonly HoverInfoForm _owner;

            public ContentCanvas(HoverInfoForm owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                _owner.RenderContent(e.Graphics, e.ClipRectangle);
            }
        }
    }
}
