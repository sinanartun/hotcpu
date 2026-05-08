using System.Collections.Generic;
using Xunit;

namespace HotCPU.Tests
{
    public class TemperatureServiceTests
    {
        private static SensorTemp Temp(string name, float value, string id = "") =>
            new(name, value, "Temperature", "°C", System.Array.Empty<float>(), id);

        private static SensorTemp NonTemp(string name, float value, string type, string unit, string id = "") =>
            new(name, value, type, unit, System.Array.Empty<float>(), id);

        [Fact]
        public void GetMainCpuTemp_PrefersPackage()
        {
            var sensors = new List<SensorTemp>
            {
                Temp("Core #0", 55f),
                Temp("Core #1", 58f),
                Temp("Package", 65f),
            };

            Assert.Equal(65f, TemperatureService.GetMainCpuTemp(sensors));
        }

        [Fact]
        public void GetMainCpuTemp_FallsBackToTctlThenTdie()
        {
            var sensors = new List<SensorTemp>
            {
                Temp("Core #0", 40f),
                Temp("Tctl", 52f),
                Temp("Tdie", 50f),
            };

            Assert.Equal(52f, TemperatureService.GetMainCpuTemp(sensors));
        }

        [Fact]
        public void GetMainCpuTemp_FallsBackToMaxCore()
        {
            var sensors = new List<SensorTemp>
            {
                Temp("Core #0", 40f),
                Temp("Core #1", 70f),
                Temp("Core #2", 50f),
            };

            Assert.Equal(70f, TemperatureService.GetMainCpuTemp(sensors));
        }

        [Fact]
        public void GetMainCpuTemp_IgnoresNonTemperatureSensors()
        {
            // Regression: the old fallback `sensors.FirstOrDefault()?.Temperature`
            // would happily return a Voltage / Power / Clock / Load value and
            // present it as a CPU temperature. Icon would show e.g. "1" when
            // VCore was 1.25V.
            var sensors = new List<SensorTemp>
            {
                NonTemp("VCore", 1.25f, "Voltage", "V"),
                NonTemp("CPU Load", 33f, "Load", "%"),
                NonTemp("Clock", 4500f, "Clock", "MHz"),
            };

            Assert.Null(TemperatureService.GetMainCpuTemp(sensors));
        }

        [Fact]
        public void GetMainCpuTemp_EmptyList_ReturnsNull()
        {
            Assert.Null(TemperatureService.GetMainCpuTemp(new List<SensorTemp>()));
        }

        [Fact]
        public void GetMainCpuTemp_OnlyNonCoreTemps_ReturnsFirst()
        {
            // No Package/Tctl/Tdie/CPU/Core hit — should return a temperature,
            // never a non-temperature sensor.
            var sensors = new List<SensorTemp>
            {
                NonTemp("VRM Load", 40f, "Load", "%"),
                Temp("SoC", 48f),
            };

            Assert.Equal(48f, TemperatureService.GetMainCpuTemp(sensors));
        }
    }
}
