using Xunit;
using HotCPU.Helpers;

namespace HotCPU.Tests
{
    public class StringHelperTests
    {
        [Theory]
        [InlineData("AMD Ryzen 9 5950X (TM) Processor", "AMD Ryzen 9 5950X")]
        [InlineData("Intel(R) Core(TM) i9-14900K", "Intel Core i9-14900K")]
        [InlineData("AMD Radeon RX 7900 XTX", "AMD Radeon RX 7900 XTX")]
        [InlineData("NVIDIA GeForce RTX 4090", "NVIDIA GeForce RTX 4090")]
        [InlineData("Kingston SFYR2S2T0", "Kingston SFYR2S2T0")] // Title casing check if applicable, or just clean
        [InlineData("Generic   Hardware   Name  ", "Generic Hardware Name")]
        [InlineData("Hardware (R) with brackets", "Hardware with brackets")]
        public void SimplifyHardwareName_ShouldCleanNamesCorrectly(string input, string expected)
        {
            var result = StringHelper.SimplifyHardwareName(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SimplifyHardwareName_ShouldNotRemoveLettersFromWords()
        {
            // Specific regression test for the "Ryzen" / "Core" bug
            Assert.Equal("AMD Ryzen", StringHelper.SimplifyHardwareName("AMD Ryzen"));
            Assert.Equal("Core", StringHelper.SimplifyHardwareName("Core"));
        }

        // === ExtractNumber tests ===
        // Regression: the previous implementation concatenated ALL digits, so
        // "Fan 1 Speed 3600" returned 13600 (or threw on overflow) which broke
        // the "sort cores by index" behavior in DetailedText.

        [Theory]
        [InlineData(null, 999)]
        [InlineData("", 999)]
        [InlineData("Core", 999)]
        [InlineData("Core #0", 0)]
        [InlineData("Core #7", 7)]
        [InlineData("CPU Core #12", 12)]
        [InlineData("Fan 1 Speed 3600", 1)]        // Must return FIRST number only.
        [InlineData("Sensor-42-foo-99", 42)]
        public void ExtractNumber_ReturnsFirstRunOfDigits(string? input, int expected)
        {
            Assert.Equal(expected, StringHelper.ExtractNumber(input!));
        }

        [Fact]
        public void ExtractNumber_DoesNotOverflowOnHugeDigitStrings()
        {
            // A long digit run used to crash int.TryParse silently-fallback; we
            // now bound the match to avoid surprising behavior.
            var huge = new string('9', 40);
            var result = StringHelper.ExtractNumber(huge);
            // Should either be 999 (sentinel) or a successfully parsed int. Must not throw.
            Assert.True(result == 999 || result > 0);
        }

        [Fact]
        public void ExtractNumber_WorksForSortingCoreNames()
        {
            // Real ordering use case: cores 1,2,10 should not sort 1,10,2.
            var names = new[] { "Core #10", "Core #2", "Core #1" };
            System.Array.Sort(names, (a, b) => StringHelper.ExtractNumber(a).CompareTo(StringHelper.ExtractNumber(b)));
            Assert.Equal(new[] { "Core #1", "Core #2", "Core #10" }, names);
        }
    }
}
