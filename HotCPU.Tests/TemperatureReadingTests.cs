using System.Collections.Generic;
using Xunit;

namespace HotCPU.Tests
{
    public class TemperatureReadingTests
    {
        [Theory]
        [InlineData(40, TemperatureLevel.Cool)]
        [InlineData(65, TemperatureLevel.Warm)]
        [InlineData(85, TemperatureLevel.Hot)]
        [InlineData(95, TemperatureLevel.Critical)]
        public void TemperatureLevel_ShouldCalculatedCorrectly(float temp, TemperatureLevel expectedLevel)
        {
            var settings = new AppSettings
            {
                WarmThreshold = 60,
                HotThreshold = 80,
                CriticalThreshold = 90
            };

            var reading = new TemperatureReading(temp, "CPU", settings, new List<HardwareTemps>());

            Assert.Equal(expectedLevel, reading.Level);
        }

        [Fact]
        public void DisplayText_ShouldBeRounded()
        {
            var reading = new TemperatureReading(45.6f, "CPU", new AppSettings(), new List<HardwareTemps>());
            
            Assert.Equal("46", reading.DisplayText);
            Assert.Equal(46, reading.RoundedTemperature);
        }

        [Fact]
        public void TooltipText_ShouldIncludeCpuNameAndTemp()
        {
            var reading = new TemperatureReading(50f, "Ryzen 9", new AppSettings(), new List<HardwareTemps>());
            
            Assert.Contains("Ryzen 9", reading.TooltipText);
            Assert.Contains("50°C", reading.TooltipText);
        }
    }
}
