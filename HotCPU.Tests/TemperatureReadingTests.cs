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

        // === Regression tests for the NaN handling fix ===

        [Fact]
        public void Level_WithNaN_ReturnsCoolInsteadOfCritical()
        {
            // Before the fix, NaN comparisons are always false, so the switch fell
            // through every `<` case and silently classified as Critical, turning
            // the tray icon red with no real hot sensor.
            var reading = new TemperatureReading(float.NaN, "CPU", new AppSettings(), new List<HardwareTemps>());
            Assert.Equal(TemperatureLevel.Cool, reading.Level);
        }

        [Fact]
        public void Level_WithInfinity_DoesNotReturnCritical()
        {
            var reading = new TemperatureReading(float.PositiveInfinity, "CPU", new AppSettings(), new List<HardwareTemps>());
            Assert.Equal(TemperatureLevel.Cool, reading.Level);
        }

        [Fact]
        public void Level_WithZeroTemperature_ReturnsCool()
        {
            // 0 usually means "no reading yet" — should not be Warm/Hot/Critical.
            var reading = new TemperatureReading(0f, "Initializing...", new AppSettings(), new List<HardwareTemps>());
            Assert.Equal(TemperatureLevel.Cool, reading.Level);
        }

        [Fact]
        public void CoreTemps_OnlyIncludesTemperatureSensors()
        {
            // Regression: CPU hardware can expose Voltage/Power/Load sensors too.
            // We only want Temperature ones in CoreTemps.
            var cpu = new HardwareTemps("CPU", "🔲", "Cpu");
            cpu.Sensors.Add(new SensorTemp("Package", 65f, "Temperature", "°C", new float[0], "cpu/package"));
            cpu.Sensors.Add(new SensorTemp("VCore", 1.25f, "Voltage", "V", new float[0], "cpu/voltage/0"));
            cpu.Sensors.Add(new SensorTemp("Core #0 Load", 33f, "Load", "%", new float[0], "cpu/load/0"));

            var reading = new TemperatureReading(65f, "CPU", new AppSettings(), new List<HardwareTemps> { cpu });

            Assert.Single(reading.CoreTemps);
            Assert.Equal("Package", reading.CoreTemps[0].Name);
        }

        [Fact]
        public void TemperatureReading_WithNullSettings_DoesNotThrow()
        {
            var reading = new TemperatureReading(50f, "CPU", null, new List<HardwareTemps>());
            // Should apply default thresholds and not NRE.
            var _ = reading.Level;
            var __ = reading.TooltipText;
            var ___ = reading.DetailedText;
        }
    }
}
