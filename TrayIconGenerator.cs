using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HotCPU
{
    internal static class TrayIconGenerator
    {
        public static Icon CreateIcon(string text, float value, TemperatureLevel level, AppSettings settings, bool forceDefaultTextColor = false, bool renderBadge = true)
        {
            var sizes = GetPreferredIconSizes();
            var bitmaps = new List<Bitmap>(sizes.Count);
            try
            {
                foreach (var size in sizes)
                {
                    bitmaps.Add(RenderIconBitmap(size, text, value, level, settings, forceDefaultTextColor, renderBadge));
                }
                return CreateIconFromBitmaps(bitmaps);
            }
            finally
            {
                foreach (var bmp in bitmaps)
                    bmp.Dispose();
            }
        }

        private static readonly Dictionary<(float, string, FontStyle), Font> _fontCache = new();

        private static Font GetFont(float sizePx, string fontFamily, FontStyle style)
        {
            var key = (sizePx, fontFamily, style);
            if (_fontCache.TryGetValue(key, out var cachedFont))
                return cachedFont;

            Font font;
            try 
            { 
                font = new Font(fontFamily, sizePx, style, GraphicsUnit.Pixel); 
            }
            catch 
            {
                // Fallback sequence
                try { font = new Font("Segoe UI Variable Text", sizePx, style, GraphicsUnit.Pixel); }
                catch 
                {
                    try { font = new Font("Segoe UI", sizePx, style, GraphicsUnit.Pixel); }
                    catch { font = new Font(FontFamily.GenericSansSerif, sizePx, style, GraphicsUnit.Pixel); }
                }
            }

            _fontCache[key] = font;
            return font;
        }

        private static Color GetTextColor(float value, TemperatureLevel level, AppSettings settings, bool forceDefaultTextColor)
        {
            if (forceDefaultTextColor || !settings.UseGradientColors)
                return GetDefaultTextColor(settings);

            // Get colors for interpolation
            return level switch
            {
                TemperatureLevel.Cool => settings.GetCoolColorValue(),
                TemperatureLevel.Warm => InterpolateColor(
                    settings.GetCoolColorValue(), 
                    settings.GetWarmColorValue(),
                    value, settings.WarmThreshold - 10, settings.WarmThreshold),
                TemperatureLevel.Hot => InterpolateColor(
                    settings.GetWarmColorValue(), 
                    settings.GetHotColorValue(),
                    value, settings.WarmThreshold, settings.HotThreshold),
                TemperatureLevel.Critical => settings.GetCriticalColorValue(),
                _ => Color.White
            };
        }

        internal static Color GetDefaultTextColor(AppSettings settings)
        {
            bool isDark = Helpers.ThemeHelper.IsDarkMode(settings);
            return isDark ? settings.GetDarkTextColorValue() : settings.GetLightTextColorValue();
        }

        private static Color InterpolateColor(Color from, Color to, float value, int min, int max)
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

        private static Bitmap RenderIconBitmap(int iconSize, string text, float value, TemperatureLevel level, AppSettings settings, bool forceDefaultTextColor, bool renderBadge)
        {
            float scale = iconSize / 16f;
            var bmp = new Bitmap(iconSize, iconSize, PixelFormat.Format32bppArgb);
            TrySetBitmapDpi(bmp);

            using var g = Graphics.FromImage(bmp);

            // Enable antialiasing
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            g.Clear(Color.Transparent);

            // Font size from settings, adjusted for digit count (slightly larger for tray)
            string line1 = text;
            string line2 = string.Empty;
            bool isMultiLine = false;
            if (!string.IsNullOrEmpty(text) && (text.Contains('\n') || text.Contains('\r')))
            {
                var parts = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                line1 = parts.Length > 0 ? parts[0] : string.Empty;
                line2 = parts.Length > 1 ? parts[1] : string.Empty;
                isMultiLine = !string.IsNullOrWhiteSpace(line2);
                if (!isMultiLine)
                    text = line1;
            }

            int lengthForSizing = line1?.Length ?? 0;
            float baseSize = lengthForSizing switch
            {
                <= 2 => settings.FontSize + 8, // Slightly reduced from +10 to prevent clipping
                3 => settings.FontSize + 5,
                4 => settings.FontSize + 3,
                _ => settings.FontSize + 1
            };
            if (!string.IsNullOrEmpty(line1) && (line1.Contains('↑') || line1.Contains('↓')))
                baseSize -= 1;
            if (isMultiLine)
                baseSize -= 1;
            if (!renderBadge)
                baseSize += 3;

            float fontSize = baseSize * scale;
            var font = GetFont(fontSize, settings.TrayFontFamily, (FontStyle)settings.TrayFontStyle);

            // Adjust vertical position - center it better
            int yOffset = 0; 
            // settings.FontSize >= 14 ? -1 : 0; // Removed aggressive offset
            yOffset = (int)Math.Round(yOffset * scale);
            var flags = TextFormatFlags.NoPadding |
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.NoClipping |
                        TextFormatFlags.SingleLine;

            // Get color based on settings
            var textColor = GetTextColor(value, level, settings, forceDefaultTextColor);

            if (!string.IsNullOrWhiteSpace(text))
            {
                // Badge mode: draw opaque rounded rectangle so ClearType can kick in.
                var badgeBack = Color.Transparent; // Fully transparent as requested
                var badgeBorder = Color.Transparent;
                var paddingX = 0; // Reset to 0 to prevent clipping
                var paddingY = 0;

                var measureFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                Size textSize = Size.Empty;
                Size unitSize = Size.Empty;
                int lineGap = 0;
                float finalUnitFontSize = 0f;

                if (isMultiLine)
                {
                    float unitScale = 0.55f;
                    float fitValueSize = fontSize;
                    float fitUnitSize = Math.Max(5f, fitValueSize * unitScale);
                    var valueFont = GetFont(fitValueSize, settings.TrayFontFamily, (FontStyle)settings.TrayFontStyle);
                    var unitFont = GetFont(fitUnitSize, settings.TrayFontFamily, (FontStyle)settings.TrayFontStyle);
                    lineGap = Math.Max(0, (int)Math.Round(scale * 0.25f));

                    textSize = TextRenderer.MeasureText(g, line1, valueFont, new Size(int.MaxValue, int.MaxValue), measureFlags);
                    unitSize = TextRenderer.MeasureText(g, line2, unitFont, new Size(int.MaxValue, int.MaxValue), measureFlags);

                    int guard = 12;
                    // Slightly reduce available width to ensure no clipping
                    int available = Math.Max(1, iconSize - 1); 
                    while ((Math.Max(textSize.Width, unitSize.Width) + (paddingX * 2) > available
                            || (textSize.Height + unitSize.Height + lineGap + (paddingY * 2) > available))
                           && guard-- > 0)
                    {
                        fitValueSize -= Math.Max(1f, scale);
                        if (fitValueSize < 6f) break;
                        fitUnitSize = Math.Max(5f, fitValueSize * unitScale);
                        valueFont = GetFont(fitValueSize, settings.TrayFontFamily, (FontStyle)settings.TrayFontStyle);
                        unitFont = GetFont(fitUnitSize, settings.TrayFontFamily, (FontStyle)settings.TrayFontStyle);
                        textSize = TextRenderer.MeasureText(g, line1, valueFont, new Size(int.MaxValue, int.MaxValue), measureFlags);
                        unitSize = TextRenderer.MeasureText(g, line2, unitFont, new Size(int.MaxValue, int.MaxValue), measureFlags);
                    }

                    font = valueFont;
                    finalUnitFontSize = unitFont.Size;
                }
                else
                {
                    textSize = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue), measureFlags);

                    // If it doesn't fit, scale down a bit.
                    float fitFontSize = fontSize;
                    int guard = 12;
                    int available = Math.Max(1, iconSize - 1); // Safety margin
                    while ((textSize.Width + (paddingX * 2) > available || textSize.Height + (paddingY * 2) > available) && guard-- > 0)
                    {
                        fitFontSize -= Math.Max(0.5f, scale * 0.5f);

                        if (fitFontSize < 6f) break;
                        font = GetFont(fitFontSize, settings.TrayFontFamily, (FontStyle)settings.TrayFontStyle);
                        textSize = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue), measureFlags);
                    }
                }

                var badgeRect = new Rectangle(0, 0, iconSize, iconSize);
                if (renderBadge)
                {
                    int radius = 0; // full-size square badge
                    using (var path = CreateRoundedRectPath(badgeRect, radius))
                    using (var brush = new SolidBrush(badgeBack))
                    {
                        g.FillPath(brush, path);
                        using var pen = new Pen(badgeBorder, Math.Max(1f, scale * 0.75f));
                        g.DrawPath(pen, path);
                    }
                }

                var textRect = Rectangle.Inflate(badgeRect, -paddingX, -paddingY);
                if (yOffset != 0)
                    textRect = new Rectangle(textRect.X, textRect.Y + yOffset, textRect.Width, textRect.Height);

                if (isMultiLine)
                {
                    float unitScale = 0.55f;
                    float unitFontSize = finalUnitFontSize > 0f ? finalUnitFontSize : Math.Max(5f, font.Size * unitScale);
                    var unitFont = GetFont(unitFontSize, settings.TrayFontFamily, (FontStyle)settings.TrayFontStyle);
                    int totalHeight = textSize.Height + unitSize.Height + lineGap;
                    int startY = textRect.Y + (textRect.Height - totalHeight) / 2;
                    var valueRect = new Rectangle(textRect.X, startY, textRect.Width, textSize.Height);
                    var unitRect = new Rectangle(textRect.X, startY + textSize.Height + lineGap, textRect.Width, unitSize.Height);
                    if (renderBadge)
                    {
                        TextRenderer.DrawText(g, line1, font, valueRect, textColor, badgeBack, flags);
                        TextRenderer.DrawText(g, line2, unitFont, unitRect, textColor, badgeBack, flags);
                    }
                    else
                    {
                        TextRenderer.DrawText(g, line1, font, valueRect, textColor, flags);
                        TextRenderer.DrawText(g, line2, unitFont, unitRect, textColor, flags);
                    }
                }
                else
                {
                    var drawFlags = flags | TextFormatFlags.VerticalCenter;
                    if (renderBadge)
                        TextRenderer.DrawText(g, text, font, textRect, textColor, badgeBack, drawFlags);
                    else
                        TextRenderer.DrawText(g, text, font, textRect, textColor, drawFlags);
                }
            }

            return bmp;
        }

        private static Icon CreateIconFromBitmaps(List<Bitmap> bitmaps)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((short)0); // reserved
            bw.Write((short)1); // type: icon
            bw.Write((short)bitmaps.Count);

            int headerSize = 6 + (16 * bitmaps.Count);
            int offset = headerSize;

            var imageData = new List<byte[]>(bitmaps.Count);
            foreach (var bmp in bitmaps)
            {
                using var imgStream = new MemoryStream();
                bmp.Save(imgStream, ImageFormat.Png);
                var data = imgStream.ToArray();
                imageData.Add(data);
            }

            for (int i = 0; i < bitmaps.Count; i++)
            {
                var bmp = bitmaps[i];
                int width = bmp.Width;
                int height = bmp.Height;
                var data = imageData[i];

                bw.Write((byte)(width >= 256 ? 0 : width));
                bw.Write((byte)(height >= 256 ? 0 : height));
                bw.Write((byte)0); // color count
                bw.Write((byte)0); // reserved
                bw.Write((short)1); // planes
                bw.Write((short)32); // bitcount
                bw.Write(data.Length);
                bw.Write(offset);

                offset += data.Length;
            }

            for (int i = 0; i < imageData.Count; i++)
            {
                bw.Write(imageData[i]);
            }

            bw.Flush();
            ms.Position = 0;
            using var icon = new Icon(ms);
            return (Icon)icon.Clone();
        }

        private static List<int> GetPreferredIconSizes()
        {
            int systemSize = GetTrayIconSizePx();
            var sizes = new HashSet<int> { 16, 20, 24, 28, 32, 36, 40, 48, 64, systemSize };
            var ordered = sizes.Where(s => s > 0 && s <= 64).OrderBy(s => s).ToList();
            return ordered.Count > 0 ? ordered : new List<int> { 16 };
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;
        private const uint CLR_INVALID = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static int GetTrayIconSizePx()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                int cx = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
                int cy = GetSystemMetricsForDpi(SM_CYSMICON, dpi);
                int size = Math.Max(cx, cy);
                if (size > 0) return size;
            }
            catch { }

            try
            {
                int size = Math.Max(GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON));
                if (size > 0) return size;
            }
            catch { }

            return 16;
        }

        private static void TrySetBitmapDpi(Bitmap bmp)
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 96 && dpi <= 768)
                {
                    bmp.SetResolution(dpi, dpi);
                }
            }
            catch { }
        }

        private static readonly TimeSpan TaskbarColorCacheDuration = TimeSpan.FromSeconds(5);
        private static Color _cachedTaskbarColor = Color.Empty;
        private static DateTime _cachedTaskbarColorAtUtc = DateTime.MinValue;

        internal static Color GetTaskbarBackgroundColor()
        {
            var now = DateTime.UtcNow;
            if (!_cachedTaskbarColor.IsEmpty && (now - _cachedTaskbarColorAtUtc) < TaskbarColorCacheDuration)
                return _cachedTaskbarColor;

            if (TryGetTaskbarColor(out var color))
            {
                _cachedTaskbarColor = color;
                _cachedTaskbarColorAtUtc = now;
                return color;
            }

            return Color.FromArgb(255, 26, 26, 26);
        }

        private static bool TryGetTaskbarColor(out Color color)
        {
            color = default;
            if (!TryGetTaskbarRect(out var rect))
                return false;

            var points = GetTaskbarSamplePoints(rect);
            if (points.Count == 0)
                return false;

            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = GetDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero) return false;

                var samples = new List<Color>(points.Count);
                foreach (var p in points)
                {
                    uint pixel = GetPixel(hdc, p.X, p.Y);
                    if (pixel == CLR_INVALID) continue;
                    byte r = (byte)(pixel & 0xFF);
                    byte g = (byte)((pixel >> 8) & 0xFF);
                    byte b = (byte)((pixel >> 16) & 0xFF);
                    samples.Add(Color.FromArgb(255, r, g, b));
                }

                if (samples.Count == 0) return false;
                color = MedianColor(samples);
                return true;
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                    ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        private static List<Point> GetTaskbarSamplePoints(Rectangle rect)
        {
            var points = new List<Point>(10);
            bool horizontal = rect.Width >= rect.Height;
            int length = horizontal ? rect.Width : rect.Height;
            int thickness = horizontal ? rect.Height : rect.Width;

            if (length <= 0 || thickness <= 0) return points;

            double[] tPositions = { 0.15, 0.35, 0.50, 0.65, 0.85 };
            double[] tOffsets = { 0.30, 0.70 };

            foreach (var t in tPositions)
            {
                foreach (var o in tOffsets)
                {
                    int x, y;
                    if (horizontal)
                    {
                        x = rect.Left + (int)Math.Round(length * t);
                        y = rect.Top + (int)Math.Round(thickness * o);
                    }
                    else
                    {
                        x = rect.Left + (int)Math.Round(thickness * o);
                        y = rect.Top + (int)Math.Round(length * t);
                    }

                    x = Math.Clamp(x, rect.Left + 1, rect.Right - 2);
                    y = Math.Clamp(y, rect.Top + 1, rect.Bottom - 2);
                    points.Add(new Point(x, y));
                }
            }

            return points;
        }

        private static bool TryGetTaskbarRect(out Rectangle rect)
        {
            rect = Rectangle.Empty;
            IntPtr hWnd = FindWindow("Shell_TrayWnd", null);
            if (hWnd == IntPtr.Zero) return false;
            if (!GetWindowRect(hWnd, out var rc)) return false;
            rect = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
            return !rect.IsEmpty && rect.Width > 0 && rect.Height > 0;
        }

        private static Color MedianColor(List<Color> samples)
        {
            int count = samples.Count;
            var rs = new int[count];
            var gs = new int[count];
            var bs = new int[count];
            for (int i = 0; i < count; i++)
            {
                var c = samples[i];
                rs[i] = c.R;
                gs[i] = c.G;
                bs[i] = c.B;
            }
            Array.Sort(rs);
            Array.Sort(gs);
            Array.Sort(bs);
            int mid = count / 2;
            return Color.FromArgb(255, rs[mid], gs[mid], bs[mid]);
        }

        private static Color AdjustColor(Color color, int delta)
        {
            int r = Math.Clamp(color.R + delta, 0, 255);
            int g = Math.Clamp(color.G + delta, 0, 255);
            int b = Math.Clamp(color.B + delta, 0, 255);
            return Color.FromArgb(255, r, g, b);
        }

        private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            var arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);

            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            arc.X = rect.X;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}
