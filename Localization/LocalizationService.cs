using System.Globalization;
using System.Resources;

namespace HotCPU.Localization
{
    internal static class LocalizationService
    {
        private static readonly ResourceManager ResourceManager = new("HotCPU.Resources.Strings", typeof(LocalizationService).Assembly);
        private static CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

        public static CultureInfo CurrentCulture => _currentCulture;

        public static void SetCulture(CultureInfo culture)
        {
            _currentCulture = culture ?? CultureInfo.CurrentUICulture;
        }

        public static string GetString(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return ResourceManager.GetString(key, _currentCulture) ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            var format = GetString(key);
            return string.Format(_currentCulture, format, args);
        }
    }
}
