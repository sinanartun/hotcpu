using System;
using System.Linq;

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
    }
}
