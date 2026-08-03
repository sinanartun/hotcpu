using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HotCPU
{
    public class SplashForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(256, 256);
            ShowInTaskbar = false;

            try
            {
                string imagePath = Path.Combine(AppContext.BaseDirectory, "Images", "AppIcon.png");
                if (File.Exists(imagePath))
                {
                    BackgroundImage = Image.FromFile(imagePath);
                    BackgroundImageLayout = ImageLayout.Zoom;
                }
            }
            catch
            {
                // Missing/corrupt image must never prevent startup.
            }

            // Magenta transparency key for simple PNG chrome.
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, _) =>
            {
                try { _timer.Stop(); } catch { }
                try { Close(); } catch { }
            };
            _timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Stop(); } catch { }
                try { _timer.Dispose(); } catch { }
                try
                {
                    BackgroundImage?.Dispose();
                    BackgroundImage = null;
                }
                catch { }
            }
            base.Dispose(disposing);
        }
    }
}
