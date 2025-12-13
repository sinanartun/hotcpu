using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HotCPU
{
    internal static class TrayIconGenerator
    {
        public static Icon CreateIcon(int temperature, TemperatureLevel level, AppSettings settings, Image? backgroundImage = null)
        {
            using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);

            // Enable antialiasing
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            g.Clear(Color.Transparent);

            // Draw background image if provided
            if (backgroundImage != null)
            {
                g.DrawImage(backgroundImage, new Rectangle(0, 0, 16, 16));
                // Overlay removed as per user request
            }

            // Display text based on settings
            // Display text based on settings
            string text = temperature.ToString();
            
            // Font size from settings, adjusted for digit count
            float fontSize = text.Length switch
            {
                1 => settings.FontSize + 2,
                2 => settings.FontSize,
                3 => settings.FontSize - 2,
                _ => settings.FontSize - 3
            };

            using var font = GetFont(fontSize);

            // Adjust vertical position - larger fonts need to be moved up slightly
            int yOffset = settings.FontSize >= 14 ? -1 : 0;
            var rect = new Rectangle(0, yOffset, bmp.Width, bmp.Height);
            var flags = TextFormatFlags.NoPadding |
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoClipping;

            // Get color based on settings
            var textColor = GetTextColor(temperature, level, settings);
            
            // If we have a background image (fire), white text is usually best
            if (backgroundImage != null) 
            {
                textColor = Color.White;

                // Add Drop Shadow (Dark Red) for readability on fire
                var shadowRect = new Rectangle(1, yOffset + 1, bmp.Width, bmp.Height);
                TextRenderer.DrawText(g, text, font, shadowRect, Color.DarkRed, flags);
            }

            TextRenderer.DrawText(g, text, font, rect, textColor, flags);

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static readonly Dictionary<float, Font> _fontCache = new();

        private static Font GetFont(float sizePx)
        {
            if (_fontCache.TryGetValue(sizePx, out var cachedFont))
                return cachedFont;

            Font font;
            try 
            { 
                font = new Font("Segoe UI Variable Text", sizePx, FontStyle.Regular, GraphicsUnit.Pixel); 
            }
            catch 
            {
                try 
                { 
                    font = new Font("Segoe UI Variable Display", sizePx, FontStyle.Regular, GraphicsUnit.Pixel); 
                }
                catch 
                {
                    font = new Font("Segoe UI", sizePx, FontStyle.Regular, GraphicsUnit.Pixel);
                }
            }

            _fontCache[sizePx] = font;
            return font;
        }

        private static Color GetTextColor(int temperature, TemperatureLevel level, AppSettings settings)
        {
            if (!settings.UseGradientColors)
                return Color.White;

            // Get colors for interpolation
            return level switch
            {
                TemperatureLevel.Cool => settings.GetCoolColorValue(),
                TemperatureLevel.Warm => InterpolateColor(
                    settings.GetCoolColorValue(), 
                    settings.GetWarmColorValue(),
                    temperature, settings.WarmThreshold - 10, settings.WarmThreshold),
                TemperatureLevel.Hot => InterpolateColor(
                    settings.GetWarmColorValue(), 
                    settings.GetHotColorValue(),
                    temperature, settings.WarmThreshold, settings.HotThreshold),
                TemperatureLevel.Critical => settings.GetCriticalColorValue(),
                _ => Color.White
            };
        }

        private static Color InterpolateColor(Color from, Color to, int value, int min, int max)
        {
            if (value <= min) return from;
            if (value >= max) return to;
            
            float t = (float)(value - min) / (max - min);
            return Color.FromArgb(
                255,
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
