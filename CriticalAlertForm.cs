using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace HotCPU
{
    public class CriticalAlertForm : Form
    {
        private Image? _bgImage;
        private System.Windows.Forms.Timer? _closeTimer;
        private readonly double _temperature;

        public CriticalAlertForm(double temperature)
        {
            _temperature = temperature;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(200, 120); // Reduced height
            
            // Calc bottom right
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var workingArea = screen.WorkingArea;
            Location = new Point(workingArea.Right - Width - 10, workingArea.Bottom - Height - 10);
            
            TopMost = true;
            ShowInTaskbar = false;
            
            // Enable double buffering
            SetStyle(ControlStyles.DoubleBuffer | 
                     ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();

            // Load GIF
            try
            {
                string gifPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tenor.gif");
                // Dev fallback
                if (!File.Exists(gifPath))
                     gifPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "tenor.gif");

                if (File.Exists(gifPath))
                {
                    _bgImage = Image.FromFile(gifPath);
                    if (ImageAnimator.CanAnimate(_bgImage))
                    {
                        ImageAnimator.Animate(_bgImage, OnFrameChanged);
                    }
                }
            }
            catch { }

            // Auto-close timer (3 seconds)
            _closeTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _closeTimer.Tick += (s, e) => Close();
            _closeTimer.Start();
        }

        private void OnFrameChanged(object? sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            try { Invalidate(); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_bgImage != null)
            {
                ImageAnimator.UpdateFrames(_bgImage);
                e.Graphics.DrawImage(_bgImage, new Rectangle(0, 0, Width, Height));
                
                // Draw decorative border
                e.Graphics.DrawRectangle(Pens.Red, 0, 0, Width - 1, Height - 1);
                
                // Draw Text
                string title = "CRITICAL TEMP!";
                string val = $"{_temperature}°C";
                
                using var fontTitle = new Font("Segoe UI", 12, FontStyle.Bold);
                using var fontVal = new Font("Segoe UI", 24, FontStyle.Bold);
                
                // Measure Title
                var sizeTitle = e.Graphics.MeasureString(title, fontTitle);
                var xTitle = (Width - sizeTitle.Width) / 2;
                var yTitle = 20f;
                
                // Measure Value
                var sizeVal = e.Graphics.MeasureString(val, fontVal);
                var xVal = (Width - sizeVal.Width) / 2;
                var yVal = yTitle + sizeTitle.Height + 5;
                
                // Shadow
                e.Graphics.DrawString(title, fontTitle, Brushes.Black, xTitle + 1, yTitle + 1);
                e.Graphics.DrawString(val, fontVal, Brushes.Black, xVal + 2, yVal + 2);
                
                // Text
                e.Graphics.DrawString(title, fontTitle, Brushes.White, xTitle, yTitle);
                e.Graphics.DrawString(val, fontVal, Brushes.White, xVal, yVal);
            }
            else
            {
                e.Graphics.Clear(Color.Red);
                e.Graphics.DrawString($"CRITICAL! {_temperature}°C", SystemFonts.DefaultFont, Brushes.White, 10, 10);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_bgImage != null)
            {
                ImageAnimator.StopAnimate(_bgImage, OnFrameChanged);
                _bgImage.Dispose();
            }
            _closeTimer?.Stop();
            _closeTimer?.Dispose();
        }
    }
}
