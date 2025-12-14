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
    }
}
