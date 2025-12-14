using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HotCPU.Helpers
{
    public static class StringHelper
    {
        public static int ExtractNumber(string name)
        {
            if (string.IsNullOrEmpty(name)) return 999;
            var numStr = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(numStr, out var num) ? num : 999;
        }

        public static string SimplifyHardwareName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            // 1. Remove Trademarks (TM, R, etc)
            // Regex handles: (TM), (R), (C), and symbols
            name = Regex.Replace(name, @"\s*[\(\[]?(TM|R|C|®|™|©)[\)\]]?", "", RegexOptions.IgnoreCase);

            // 2. Remove "Processor" suffix
            name = Regex.Replace(name, @"\s+Processor", "", RegexOptions.IgnoreCase);

            // 3. Remove "x-Core" / "x-Thread" specifics (e.g., "16-Core", "32-Thread")
            name = Regex.Replace(name, @"\s+\d+-(Core|Thread|Way)", "", RegexOptions.IgnoreCase);

            // 4. Remove Clock Speeds (e.g., " @ 3.50GHz", "3.5GHz")
            name = Regex.Replace(name, @"\s*@?\s*\d+(\.\d+)?\s*GHz", "", RegexOptions.IgnoreCase);

            // 5. Remove "Generic" prefixes if they are just clutter (like "Disk Drive")
            // But be careful not to remove "Disk" if that's the only name.
            name = Regex.Replace(name, @"\s+Disk Device", "", RegexOptions.IgnoreCase);

            // 6. Generic Cleanup
            // Collapse multiple spaces
            name = Regex.Replace(name, @"\s+", " ").Trim();

            // Storage Title Case (e.g. "KINGSTON..." -> "Kingston...")
            // Apply if name is mostly uppercase (more than 70%)
            int upperCount = name.Count(char.IsUpper);
            if (name.Length > 0 && (double)upperCount / name.Length > 0.7)
            {
                name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
            }

            return name.Trim();
        }

        public static string CleanSensorName(string name, string hardwareName)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Sensor";

            // 1. Remove parent hardware name prefix
            // Escape special chars in hardware name for Regex safety
            string hwPattern = Regex.Escape(hardwareName);
            name = Regex.Replace(name, $"^{hwPattern}\\s*[-_]?\\s*", "", RegexOptions.IgnoreCase);

            // 2. Remove common noisy prefixes
            name = Regex.Replace(name, @"^(CPU|GPU|Memory|Disk)\s+", "", RegexOptions.IgnoreCase);

            // 3. Map specific technical terms to user-friendly ones
            if (name.Equals("Temperature", StringComparison.OrdinalIgnoreCase)) return "Core";
            if (name.Equals("Package", StringComparison.OrdinalIgnoreCase)) return "Package"; // Keep Package
            if (name.Equals("Tctl/Tdie", StringComparison.OrdinalIgnoreCase)) return "Core (Tctl/Tdie)"; // Specific AMD
            
            // "GPU Core" -> "Core" (redundant since it's under GPU section)
            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) return "Core";

            // If we reduced it to nothing, fallback
            if (string.IsNullOrWhiteSpace(name)) return "Core";
            
            // Clean up any remaining double spaces or edge - 
            name = name.Trim().TrimStart('-').Trim();

            return name;
        }
    }
}
