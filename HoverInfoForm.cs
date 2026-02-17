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
    /// A custom borderless form that acts as a rich tooltip.
    /// </summary>
    internal class HoverInfoForm : Form
    {
        private TemperatureReading? _currentReading;
        private readonly System.Windows.Forms.Timer _monitorTimer;
        private Point _lastShowLocation;
        private readonly Font _fontBold;
        private readonly Font _fontNormal;
        private readonly Font _fontEmoji;

        public HoverInfoForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Padding = new Padding(1);
            DoubleBuffered = true;

            _fontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
            _fontNormal = new Font("Segoe UI", 9f, FontStyle.Regular);
            _fontEmoji = new Font("Segoe UI Emoji", 9f);

            _monitorTimer = new System.Windows.Forms.Timer { Interval = 50 }; // Ultra fast check
            _monitorTimer.Tick += MonitorTimer_Tick;

            Paint += OnPaint;
        }

        private void ApplyTheme()
        {
            if (_currentReading?.Settings == null) return;
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_currentReading.Settings);
            BackColor = Helpers.ThemeHelper.GetBackgroundColor(isDark);
        }

        public void UpdateData(TemperatureReading reading)
        {
            if (InvokeRequired)
            {
                Invoke(() => UpdateData(reading));
                return;
            }

            _currentReading = reading;
            ApplyTheme();
            Size = MeasureSize();
            Invalidate();
            if (Visible && !Bounds.Contains(Cursor.Position)) UpdatePosition(Cursor.Position);
        }

        // Keep compatibility
        public void UpdateText(string text) { }

        public void ShowAtCursor()
        {
            var cursor = Cursor.Position;
            // Always update the "valid" location while the external controller (TryIcon) reports valid hover
            _lastShowLocation = cursor;

            if (!Visible)
            {
                UpdatePosition(cursor);
                Show();
                _monitorTimer.Start();
            }
            if (!_monitorTimer.Enabled) _monitorTimer.Start();
        }

        private Size MeasureSize()
        {
            if (_currentReading == null) return new Size(100, 50);

            using var g = CreateGraphics();
            float maxWidth = 200; 
            float height = 10;
            
            float rowHeight = 24;
            float headerHeight = 22;

            foreach (var hw in _currentReading.AllTemps.Where(h => h.Sensors.Any()))
            {
                var visibleSensors = GetSortedSensors(hw.Sensors);
                if (!visibleSensors.Any()) continue;

                height += headerHeight;
                height += (visibleSensors.Count * rowHeight);
                height += 5;

                var sizeIcon = g.MeasureString(hw.Icon, _fontEmoji);
                var sizeName = g.MeasureString(hw.Name, _fontBold);
                var totalWidth = sizeIcon.Width + sizeName.Width;
                
                if (totalWidth + 40 > maxWidth) maxWidth = totalWidth + 40;
            }

            return new Size(550, (int)height + 10);
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_currentReading?.Settings == null) return;

            bool isDark = Helpers.ThemeHelper.IsDarkMode(_currentReading.Settings);
            Color bgColor = Helpers.ThemeHelper.GetBackgroundColor(isDark);
            Color headerColor = Helpers.ThemeHelper.GetHeaderColor(isDark);
            Color textColor = Helpers.ThemeHelper.GetTextColor(isDark);
            Color dimTextColor = Helpers.ThemeHelper.GetDimTextColor(isDark);
            Color gridColor = Helpers.ThemeHelper.GetBorderColor(isDark);

            g.Clear(headerColor);

            float y = 10;
            float xName = 10;
            // Align charts to the right side
            float chartWidth = 220;
            float xChart = Width - chartWidth - 10; // Right align with 10px padding
            float xValue = xChart - 10; // Value ends before chart
            
            float rowHeight = 24;

            using var brushText = new SolidBrush(textColor);
            using var brushDim = new SolidBrush(dimTextColor);
            using var penGrid = new Pen(gridColor, 1f);

            foreach (var hw in _currentReading.AllTemps.Where(h => h.Sensors.Any()))
            {
                var visibleSensors = GetSortedSensors(hw.Sensors);
                if (!visibleSensors.Any()) continue;

                g.DrawString(hw.Icon, _fontEmoji, Brushes.Orange, xName, y);
                
                var iconSize = g.MeasureString(hw.Icon, _fontEmoji);
                float textX = xName + iconSize.Width - 5; 
                if (textX < xName + 18) textX = xName + 18; 

                g.DrawString(hw.Name, _fontBold, Brushes.Orange, textX, y);
                g.DrawLine(penGrid, xName, y + 18, Width - 10, y + 18);
                y += 22;

                foreach (var sensor in visibleSensors)
                {
                    string name = sensor.Name;
                    if (name.Length > 28) name = name.Substring(0, 25) + "...";
                    g.DrawString(name, _fontNormal, brushDim, xName, y);

                    // string val = $"{sensor.RoundedTemp}°C";
                    // Human Readable Formatting
                    string finalValStr = FormatValue(sensor.Value, sensor.Unit);
                    
                    // Right Align Value to left of Chart
                    var sizeVal = g.MeasureString(finalValStr, _fontBold);
                    float valDrawX = xChart - 5 - sizeVal.Width; // 5px padding from chart
                    
                    g.DrawString(finalValStr, _fontBold, brushText, valDrawX, y);

                    // Style Definitions
                    Color chartColor = Color.DeepSkyBlue;
                    float? fixedMin = null;
                    float? fixedMax = null;
                    bool fillArea = false;
                    float lineThickness = 1.5f;

                    if (sensor.Unit == "°C")
                    {
                         // Style: Orange (#FF4500), Thick Line, Fixed Scale 30-100
                         chartColor = Color.OrangeRed; 
                         lineThickness = 2.5f;
                         fixedMin = 30f;
                         fixedMax = 100f;
                    }
                    else if (sensor.Unit == "%")
                    {
                        // Style: Green (#00FF00), Fill Area, Fixed Scale 0-100
                        chartColor = Color.Lime;
                        fillArea = true;
                        fixedMin = 0f;
                        fixedMax = 100f;
                    }
                    else if (sensor.Unit == "W")
                    {
                        // Style: Yellow (#FFFF00), Thin Line, Fixed Scale 0 - [TDP + 20%]
                        chartColor = Color.Yellow;
                        lineThickness = 1.5f;
                        fixedMin = 0f;
                        // Default TDP 125W if not set, plus 20%
                        float tdp = _currentReading.Settings.CpuTdp > 0 ? _currentReading.Settings.CpuTdp : 125f;
                        fixedMax = tdp * 1.2f;
                    }
                    else if (sensor.Unit == "MHz")
                    {
                        // Style: Blue (#00BFFF), Thin Line, Fixed Scale Base - Boost
                        chartColor = Color.DeepSkyBlue;
                        lineThickness = 1.5f;
                        fixedMin = _currentReading.Settings.CpuBaseClock;
                        fixedMax = _currentReading.Settings.CpuBoostClock;
                        
                        // Safety: if base >= boost, default to auto-scale behavior or safe fallback
                        if (fixedMin >= fixedMax) 
                        {
                            fixedMin = 0;
                            fixedMax = 5000;
                        }
                    }
                    else
                    {
                         // Default / Fallback
                         chartColor = _currentReading.Settings.GetWarmColorValue(); 
                         if (_currentReading.Settings.UseGradientColors)
                         {
                             // Use original gradient logic for other sensors? Or just simplified.
                             // Keeping it simple for now as requested styles cover the main ones.
                             // If "Other", use simple auto-scale.
                         }
                    }

                    DrawSparkline(g, xChart, y, chartWidth, rowHeight - 2, sensor.History, sensor.Temperature, chartColor, fixedMin, fixedMax, fillArea, lineThickness);

                    y += rowHeight;
                }
                y += 5;
            }
        }
        
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            
            // Check Benchmark Button Click - REMOVED
        }

        private bool IsVisible(SensorTemp s)
        {
             if (_currentReading?.Settings?.HiddenSensorIds == null) return true;
             
             // Check User Preference
             if (_currentReading.Settings.HiddenSensorIds.Contains(s.Identifier)) return false;

             // Check Optimization Rules
             return !IsSensorHiddenByOptimization(s);
        }

        private bool IsSensorHiddenByOptimization(SensorTemp s)
        {
            // Target Entity: Any row matching the RegEx pattern Core #\d+
            // We use a compiled regex or simple string checking. Given the frequency (Paint), simple string checks are faster if sufficient.
            // But requirement said "RegEx pattern Core #\d+".
            // "Core #1", "Core #10", etc.
            
            // Optimization: first check if it contains "Core #" to avoid Regex overhead on everything
            if (!s.Name.Contains("Core #")) return false;

            // Optional: strict Regex check if needed, but string check above is likely enough context
            // if (!Regex.IsMatch(s.Name, @"Core #\d+")) return false; 

            // IF row contains SMU (System Management Unit power) -> ACTION: Hide.
            if (s.Name.Contains("SMU", StringComparison.OrdinalIgnoreCase)) return true;

            // IF row contains VID (Voltage ID request) -> ACTION: Hide.
            if (s.Name.Contains("VID", StringComparison.OrdinalIgnoreCase)) return true;

            // IF row contains Effective (Effective Clock) -> ACTION: Hide (Unless diagnosing clock stretching).
            if (s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase)) return true;

            // IF row contains Usage -> ACTION: Keep
            // (Implicitly kept if it doesn't match above)
            
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

        private int GetSensorBlockPriority(SensorTemp s)
        {
            // Block A: Global Health (Top Priority)
            // 1. Total CPU Usage
            // 2. Core Max (Temperature)
            // 3. CPU Package Power
            // 4. DRAM Power
            
            if (s.Name.Equals("Total CPU Usage", StringComparison.OrdinalIgnoreCase)) return 0;
            if (s.Name.Equals("Core Max", StringComparison.OrdinalIgnoreCase)) return 1;
            if (s.Name.Contains("Package Power", StringComparison.OrdinalIgnoreCase)) return 2;
            if (s.Name.Contains("DRAM Power", StringComparison.OrdinalIgnoreCase)) return 3;

            // Block B: Load Balancing (Usage) - Grouped Contiguously
            // Assumes "Core #X Usage" or similar pattern where Unit is %
            if (s.Unit == "%" && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 10;

            // Block C: Frequency Floor/Ceiling
            // Group Core #0 Clock through Core #N Clock together below Usage
            if (s.Name.Contains("Clock", StringComparison.OrdinalIgnoreCase) && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 20;

            // Block D: Everything else
            return 99;
        }

        private void DrawSparkline(Graphics g, float x, float y, float w, float h, float[] history, float current, Color color, float? fixedMin = null, float? fixedMax = null, bool fillArea = false, float lineThickness = 1.5f)
        {
            if (history == null || history.Length < 2) return;

            bool isDark = _currentReading?.Settings != null && Helpers.ThemeHelper.IsDarkMode(_currentReading.Settings);
            
            // Background (Subtle frame or transparent)
            Color gridColor = isDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
            Color chartBg = isDark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(245, 245, 245);
            
            using (var bgBrush = new SolidBrush(chartBg))
                g.FillRectangle(bgBrush, x, y, w, h);
            
            // Grid Lines (Vertical & Horizontal)
            using (var penGrid = new Pen(gridColor, 1f) { DashStyle = DashStyle.Dot })
            {
                // Horizontal (Mid)
                g.DrawLine(penGrid, x, y + h / 2, x + w, y + h / 2);
                
                // Vertical (split into 4 sections)
                float stepW = w / 4;
                for (int i=1; i<4; i++)
                    g.DrawLine(penGrid, x + i * stepW, y, x + i * stepW, y + h);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            float min, max;

            if (fixedMin.HasValue && fixedMax.HasValue)
            {
                min = fixedMin.Value;
                max = fixedMax.Value;
            }
            else
            {
                // Auto-scale logic
                min = history.Min();
                max = history.Max();
                // Force some range to prevent flatline weirdness
                if (max - min < 5) 
                {
                    float mid = (min + max) / 2;
                    min = mid - 5;
                    if (min < 0) min = 0;
                    max = mid + 5;
                }
            }

            // Fixed scale logic:
            // The width 'w' represents the full capacity (TemperatureService.MAX_HISTORY).
            // Data should fill from the right.
            
            int maxCapacity = TemperatureService.MAX_HISTORY;
            // Ensure we don't divide by zero if maxCapacity is 1 (unlikely)
            float stepX = w / (Math.Max(maxCapacity, 2) - 1); 

            // Calculate starting X offset so the latest data point lines up with the right edge
            // If we have N points, we are missing (Capacity - N) points on the left.
            // X start = x + (Capacity - Count) * stepX
            int missingPoints = maxCapacity - history.Length;
            if (missingPoints < 0) missingPoints = 0;
            
            float startX = x + (missingPoints * stepX);

            var points = new List<PointF>();  // Use List for flexibility
            
            for (int i = 0; i < history.Length; i++)
            {
                float val = history[i];
                float px = startX + (i * stepX);
                
                // Invert Y because screen starts at top
                // Clamp normalization for fixed scales
                float normalized = (val - min) / (max - min);
                if (normalized < 0) normalized = 0;
                if (normalized > 1) normalized = 1;
                
                float py = y + h - (normalized * h);
                points.Add(new PointF(px, py));
            }

            if (points.Count < 2) 
            {
                // Draw a single dot if we have 1 point
                if (points.Count == 1)
                {
                     using var dotBrush = new SolidBrush(color);
                     g.FillEllipse(dotBrush, points[0].X - 2, points[0].Y - 2, 5, 5);
                }
                return;
            }

            var pointsArray = points.ToArray();

            // Fill Path (Line down to axis)
            using (var path = new GraphicsPath())
            {
                path.AddLines(pointsArray);
                
                // Close the shape: Down to axis at last point X, then back to first point X at axis
                float axisY = y + h;
                path.AddLine(pointsArray.Last().X, axisY, pointsArray.First().X, axisY);
                path.CloseFigure();

                if (fillArea)
                {
                    // "Solid wall" style
                    // Using Alpha=255 might obscure grid lines. 
                    // But requirement says "Fill for Usage... solid wall of color".
                    // Let's use 255.
                    using (var brush = new SolidBrush(color))
                    {
                        g.FillPath(brush, path);
                    }
                }
                else
                {
                    // Gradient from Color to Transparent
                    using (var brush = new LinearGradientBrush(
                        new RectangleF(x, y, w, h), 
                        Color.FromArgb(100, color), 
                        Color.FromArgb(10, color),
                        90f))
                    {
                        g.FillPath(brush, path);
                    }
                }
            }

            // Stroke Line (Top)
            // If FillArea is true, do we draw keyline? Usually yes, to define the edge cleanly.
            using (var pen = new Pen(color, lineThickness))
            {
                g.DrawLines(pen, pointsArray);
            }
            
            // End Dot
            var last = pointsArray.Last();
            using (var dotBrush = new SolidBrush(color))
            {
                g.FillEllipse(dotBrush, last.X - 2, last.Y - 2, 5, 5);
            }
            
            g.SmoothingMode = SmoothingMode.Default;
        }

        private void UpdatePosition(Point cursor)
        {
            var screen = Screen.FromPoint(cursor);
            int x = cursor.X - (Width / 2);
            int y = cursor.Y - Height - 10;

            if (x + Width > screen.WorkingArea.Right) x = screen.WorkingArea.Right - Width - 5;
            if (x < screen.WorkingArea.Left) x = screen.WorkingArea.Left + 5;

            // Smart vertical positioning
            if (y < screen.WorkingArea.Top) y = cursor.Y + 20; 
            if (y + Height > screen.WorkingArea.Bottom)
                y = screen.WorkingArea.Bottom - Height - 5;

            Location = new Point(x, y);
        }

        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            var cursor = Cursor.Position;
            var formRect = Bounds;
            
            // Ultra Strict: Minimal inflation
            formRect.Inflate(2, 2); 
            
            // If we moved away from the show point by more than 5px (tiny tremor allowance), close it.
            var distanceToIcon = Math.Sqrt(Math.Pow(cursor.X - _lastShowLocation.X, 2) + Math.Pow(cursor.Y - _lastShowLocation.Y, 2));

            if (!formRect.Contains(cursor) && distanceToIcon > 5)
            {
                Hide();
                _monitorTimer.Stop();
            }
        }
        
        protected override bool ShowWithoutActivation => true;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fontBold?.Dispose();
                _fontNormal?.Dispose();
                _fontEmoji?.Dispose();
                _monitorTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
        private string FormatValue(float value, string unit)
        {
            // Auto-scale specific units
            if (unit == "KB/s")
            {
                if (value > 1024 * 1024) return $"{value / (1024 * 1024):F1} GB/s";
                if (value > 1024) return $"{value / 1024:F1} MB/s";
                return $"{value:F0} KB/s"; // No decimal for KB if small
            }
            if (unit == "MB")
            {
                if (value > 1024) return $"{value / 1024:F1} GB";
                return $"{value:F0} MB";
            }
            if (unit.Trim().Equals("MHz", StringComparison.OrdinalIgnoreCase))
            {
                // User requested integer only for MHz.
                // explicitly disable GHz scaling to comply with "show only integer".
                return $"{value:F0} MHz";
            }

            // Standard Formatting
            if (unit == "°C" || unit == "%" || unit == "RPM")
                return $"{Math.Round(value)}{unit}"; // Tight spacing, integer

            // Default
            return $"{value:F1} {unit}";
        }
    }
}
