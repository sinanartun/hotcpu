using System.Collections.Generic;
using Xunit;

namespace HotCPU.Tests
{
    /// <summary>
    /// Tests for the CPU-sensor status surfaced on <see cref="TemperatureReading"/>.
    /// These cover the user-visible behavior for the "CPU temperature unavailable"
    /// case that used to silently surface a non-CPU sensor as the CPU temperature.
    /// </summary>
    public class CpuDriverStatusTests
    {
        [Fact]
        public void HasCpuTemperature_TrueForAvailablePositiveTemp()
        {
            var reading = new TemperatureReading(50f, "CPU", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.Available);
            Assert.True(reading.HasCpuTemperature);
        }

        [Fact]
        public void HasCpuTemperature_FalseWhenDriverMissing()
        {
            var reading = new TemperatureReading(0f, "CPU", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.DriverMissing);
            Assert.False(reading.HasCpuTemperature);
        }

        [Fact]
        public void HasCpuTemperature_FalseWhenCpuNotDetected()
        {
            var reading = new TemperatureReading(0f, "CPU", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.NotDetected);
            Assert.False(reading.HasCpuTemperature);
        }

        [Fact]
        public void DisplayText_ShowsDashesWhenCpuUnavailable()
        {
            var reading = new TemperatureReading(0f, "CPU", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.DriverMissing);
            Assert.Equal("--", reading.DisplayText);
        }

        [Fact]
        public void DisplayText_ShowsNumberWhenAvailable()
        {
            var reading = new TemperatureReading(63.8f, "Ryzen 9", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.Available);
            Assert.Equal("64", reading.DisplayText);
        }

        [Fact]
        public void TooltipText_MentionsPawnIoWhenDriverMissing()
        {
            var reading = new TemperatureReading(0f, "Ryzen 9 9950X3D", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.DriverMissing);
            // Should NOT pretend there is a temperature value.
            Assert.DoesNotContain("0°C", reading.TooltipText);
            Assert.Contains("PawnIO", reading.TooltipText);
        }

        [Fact]
        public void TooltipText_StatesCpuNotDetected()
        {
            var reading = new TemperatureReading(0f, "CPU", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.NotDetected);
            Assert.Contains("not detected", reading.TooltipText);
        }

        [Fact]
        public void Level_StaysCoolWhenCpuUnavailable_RegardlessOfTemperatureField()
        {
            // Defensive: even if the numeric field somehow contains garbage
            // (e.g. from a stale reading), status-driven fallback keeps the
            // tray icon from turning red.
            var reading = new TemperatureReading(99f, "CPU", new AppSettings(), new List<HardwareTemps>(), CpuSensorStatus.DriverMissing);
            Assert.Equal(TemperatureLevel.Cool, reading.Level);
        }

        [Fact]
        public void PawnIoReleasesUrl_UsesOfficialGithubSource()
        {
            // Guard against typos in the link that opens from the tray menu.
            Assert.StartsWith("https://github.com/", CpuDriverHelper.PawnIoReleasesUrl);
            Assert.Contains("PawnIO", CpuDriverHelper.PawnIoReleasesUrl);
        }
    }
}
