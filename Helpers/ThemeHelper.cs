using System;
using System.Drawing;
using Microsoft.Win32;

namespace HotCPU.Helpers
{
    internal static class ThemeHelper
    {
        public static bool IsDarkMode(AppSettings settings)
        {
            var mode = (settings.ThemeMode ?? "Auto").Trim().ToLowerInvariant();
            if (mode == "dark") return true;
            if (mode == "light") return false;

            // Auto mode: Detect from Windows
            return !GetSystemIsLightTheme();
        }

        private static bool GetSystemIsLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int i) return i != 0;
                }
            }
            catch { }
            return false; // Default to dark if detection fails
        }

        // Standard Theme Colors
        public static Color GetBackgroundColor(bool isDark) =>
            isDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);

        public static Color GetSurfaceColor(bool isDark) =>
            isDark ? Color.FromArgb(45, 45, 45) : Color.White;

        public static Color GetHeaderColor(bool isDark) =>
            isDark ? Color.FromArgb(20, 20, 20) : Color.FromArgb(230, 230, 230);

        public static Color GetTextColor(bool isDark) =>
            isDark ? Color.White : Color.Black;

        public static Color GetDimTextColor(bool isDark) =>
            isDark ? Color.LightGray : Color.FromArgb(100, 100, 100);

        public static Color GetBorderColor(bool isDark) =>
            isDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);

        public static Color GetSelectionColor(bool isDark) =>
            isDark ? Color.FromArgb(100, 100, 100) : Color.FromArgb(210, 210, 210);

        public static Color GetNavBackgroundColor(bool isDark) =>
            isDark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(235, 235, 235);
    }
}
