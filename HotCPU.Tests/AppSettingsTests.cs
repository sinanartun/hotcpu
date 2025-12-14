using System.Drawing;
using Xunit;

namespace HotCPU.Tests
{
    public class AppSettingsTests
    {
        [Fact]
        public void DefaultValues_ShouldBeCorrect()
        {
            var settings = new AppSettings();
            
            Assert.Equal(1000, settings.RefreshIntervalMs);
            Assert.Equal(60, settings.WarmThreshold);
            Assert.Equal(80, settings.HotThreshold);
            Assert.Equal(90, settings.CriticalThreshold);
            Assert.Equal(14, settings.FontSize);
        }

        [Fact]
        public void ColorConversion_ShouldWork()
        {
            var settings = new AppSettings();
            var red = Color.Red;
            
            settings.SetCriticalColor(red);
            var result = settings.GetCriticalColorValue();
            
            Assert.Equal(red.ToArgb(), result.ToArgb());
        }

        [Fact]
        public void HiddenSensors_ShouldBeEmptyByDefault()
        {
            var settings = new AppSettings();
            Assert.Empty(settings.HiddenSensorIds);
            Assert.Empty(settings.TraySensorIds);
        }

        [Fact]
        public void TraySensorIds_ShouldPersist()
        {
            var settings = new AppSettings();
            settings.TraySensorIds.Add("sensor_1");
            settings.TraySensorIds.Add("sensor_2");

            Assert.Contains("sensor_1", settings.TraySensorIds);
            Assert.Contains("sensor_2", settings.TraySensorIds);
            Assert.Equal(2, settings.TraySensorIds.Count);
        }
    }
}
