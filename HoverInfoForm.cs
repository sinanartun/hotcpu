using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

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
            BackColor = Color.FromArgb(32, 32, 32);
            Padding = new Padding(1);
            DoubleBuffered = true;
            
            _fontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
            _fontNormal = new Font("Segoe UI", 9f, FontStyle.Regular);
            _fontEmoji = new Font("Segoe UI Emoji", 9f);

            _monitorTimer = new System.Windows.Forms.Timer { Interval = 50 }; // Ultra fast check
            _monitorTimer.Tick += MonitorTimer_Tick;

            Paint += OnPaint;
        }

        public void UpdateData(TemperatureReading reading)
        {
            _currentReading = reading;
            Size = MeasureSize();
            Invalidate();
            if (Visible) UpdatePosition(Cursor.Position);
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
                var visibleSensors = hw.Sensors.Where(s => IsVisible(s)).ToList();
                if (!visibleSensors.Any()) continue;

                height += headerHeight;
                height += (visibleSensors.Count * rowHeight);
                height += 5;

                var sizeIcon = g.MeasureString(hw.Icon, _fontEmoji);
                var sizeName = g.MeasureString(hw.Name, _fontBold);
                var totalWidth = sizeIcon.Width + sizeName.Width;
                
                if (totalWidth + 40 > maxWidth) maxWidth = totalWidth + 40;
            }

            return new Size(480, (int)height + 10);
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(40, 40, 40));
            
            if (_currentReading == null) return;

            float y = 10;

            float xName = 10;
            float xValue = 260;
            float xChart = 320;
            float chartWidth = 140;
            float rowHeight = 24;

            var brushText = Brushes.White;
            var brushDim = Brushes.LightGray;
            var penChart = new Pen(Color.LimeGreen, 1.5f);
            var penGrid = new Pen(Color.FromArgb(60, 60, 60), 1f);

            foreach (var hw in _currentReading.AllTemps.Where(h => h.Sensors.Any()))
            {
                var visibleSensors = hw.Sensors
                    .Where(s => IsVisible(s))
                    .OrderBy(s => !s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(s => HotCPU.Helpers.StringHelper.ExtractNumber(s.Name))
                    .ThenBy(s => s.Name)
                    .ToList();

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

                    string val = $"{sensor.RoundedTemp}°C";
                    g.DrawString(val, _fontBold, brushText, xValue, y);

                    DrawSparkline(g, xChart, y, chartWidth, rowHeight - 2, sensor.History, sensor.Temperature);

                    y += rowHeight;
                }
                y += 5;
            }
        }
        
        private bool IsVisible(SensorTemp s)
        {
             if (_currentReading?.Settings?.HiddenSensorIds == null) return true;
             return !_currentReading.Settings.HiddenSensorIds.Contains(s.Identifier);
        }

        private void DrawSparkline(Graphics g, float x, float y, float w, float h, float[] history, float current)
        {
            g.FillRectangle(new SolidBrush(Color.FromArgb(30,30,30)), x, y, w, h);

            if (history == null || history.Length < 2) return;

            float min = history.Min();
            float max = history.Max();
            if (max - min < 10) 
            {
                float mid = (min + max) / 2;
                min = mid - 5;
                max = mid + 5;
            }

            var points = new PointF[history.Length];
            float stepX = w / (history.Length - 1);
            
            for (int i = 0; i < history.Length; i++)
            {
                float val = history[i];
                if (val < min) val = min;
                if (val > max) val = max;

                float px = x + (i * stepX);
                float py = y + h - ((val - min) / (max - min) * h);
                points[i] = new PointF(px, py);
            }

            var color = current switch
            {
               < 60 => Color.DeepSkyBlue,
               < 80 => Color.Orange,
               _ => Color.Red
            };
            using var pen = new Pen(color, 1.5f);
            g.DrawLines(pen, points);
            var last = points.Last();
            g.FillEllipse(new SolidBrush(color), last.X - 2, last.Y - 2, 4, 4);
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
    }
}
