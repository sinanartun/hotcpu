using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HotCPU
{
    public class SplashForm : Form
    {
        public SplashForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(256, 256);
            this.ShowInTaskbar = false;
            
            string imagePath = Path.Combine(AppContext.BaseDirectory, "Images", "AppIcon.png");
            if (File.Exists(imagePath))
            {
                this.BackgroundImage = Image.FromFile(imagePath);
                this.BackgroundImageLayout = ImageLayout.Zoom;
            }
            
            // Set transparency key to support simple transparency if the PNG has a distinct background
            // or if we want the form to be shaped like the image.
            // Using Magenta as a common "magic pink" for transparency.
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 3000; // 3 seconds
            timer.Tick += (s, e) => 
            {
                timer.Stop();
                this.Close();
            };
            timer.Start();
        }
    }
}
