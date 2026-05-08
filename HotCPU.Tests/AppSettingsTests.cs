using System.Drawing;
using System.IO;
using System.Text.Json;
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
            Assert.Equal(55, settings.WarmThreshold);
            Assert.Equal(65, settings.HotThreshold);
            Assert.Equal(78, settings.CriticalThreshold);
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

        // === Sanitize() tests: these protect against corrupt/hand-edited settings files. ===

        [Fact]
        public void Sanitize_RepairsThresholdOrdering()
        {
            // User-edited file could have Warm >= Hot >= Critical; Level calculation would skip levels.
            var settings = new AppSettings
            {
                WarmThreshold = 90,
                HotThreshold = 80,
                CriticalThreshold = 70
            };

            settings.Sanitize();

            Assert.True(settings.WarmThreshold < settings.HotThreshold);
            Assert.True(settings.HotThreshold < settings.CriticalThreshold);
        }

        [Fact]
        public void Sanitize_ClampsRefreshInterval()
        {
            var settings = new AppSettings { RefreshIntervalMs = 0 };
            settings.Sanitize();
            Assert.True(settings.RefreshIntervalMs >= 250);

            settings.RefreshIntervalMs = 500_000;
            settings.Sanitize();
            Assert.True(settings.RefreshIntervalMs <= 60_000);
        }

        [Fact]
        public void Sanitize_ClampsLogInterval()
        {
            var settings = new AppSettings { LogIntervalSeconds = 0 };
            settings.Sanitize();
            Assert.True(settings.LogIntervalSeconds >= 1);

            settings.LogIntervalSeconds = -5;
            settings.Sanitize();
            Assert.True(settings.LogIntervalSeconds >= 1);
        }

        [Fact]
        public void Sanitize_NormalizesLogFormat()
        {
            var settings = new AppSettings { LogFormat = "  csv " };
            settings.Sanitize();
            Assert.Equal("CSV", settings.LogFormat);

            settings.LogFormat = "json";
            settings.Sanitize();
            Assert.Equal("JSON", settings.LogFormat);

            settings.LogFormat = "garbage";
            settings.Sanitize();
            Assert.Equal("CSV", settings.LogFormat);
        }

        [Fact]
        public void Sanitize_ReplacesNullCollections()
        {
            var settings = new AppSettings();
            // Simulate collections dropped by a hand-edited JSON file.
            settings.HiddenSensorIds = null!;
            settings.TraySensorIds = null!;
            settings.LogSensorIds = null!;

            settings.Sanitize();

            Assert.NotNull(settings.HiddenSensorIds);
            Assert.NotNull(settings.TraySensorIds);
            Assert.NotNull(settings.LogSensorIds);
        }

        [Fact]
        public void Sanitize_ReplacesEmptyThemeAndFont()
        {
            var settings = new AppSettings
            {
                ThemeMode = "   ",
                TrayFontFamily = "",
            };
            settings.Sanitize();

            Assert.False(string.IsNullOrWhiteSpace(settings.ThemeMode));
            Assert.False(string.IsNullOrWhiteSpace(settings.TrayFontFamily));
        }

        [Fact]
        public void LoadFromPath_MissingFile_ReturnsDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_test_missing_{System.Guid.NewGuid():N}.json");
            try
            {
                var settings = AppSettings.Load(path);
                Assert.Equal(new AppSettings().RefreshIntervalMs, settings.RefreshIntervalMs);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LoadFromPath_CorruptJson_ReturnsSanitizedDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_test_corrupt_{System.Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "{ this is not valid json ");
                var settings = AppSettings.Load(path);
                Assert.NotNull(settings);
                // Defaults apply and relationships hold.
                Assert.True(settings.WarmThreshold < settings.HotThreshold);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LoadFromPath_NullCollectionsInJson_AreReplaced()
        {
            // Regression: when a user hand-edits their file and sets a list to null,
            // downstream code used to NRE when calling .Contains()/.Add().
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_test_null_lists_{System.Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, @"{ ""HiddenSensorIds"": null, ""TraySensorIds"": null, ""LogSensorIds"": null }");
                var settings = AppSettings.Load(path);

                Assert.NotNull(settings.HiddenSensorIds);
                Assert.NotNull(settings.TraySensorIds);
                Assert.NotNull(settings.LogSensorIds);
                // Safe to mutate after load.
                settings.HiddenSensorIds.Add("x");
                settings.TraySensorIds.Add("y");
                settings.LogSensorIds.Add("z");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void SaveAndLoad_RoundTrip_PreservesValues()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_test_roundtrip_{System.Guid.NewGuid():N}.json");
            try
            {
                var original = new AppSettings
                {
                    RefreshIntervalMs = 2000,
                    WarmThreshold = 50,
                    HotThreshold = 70,
                    CriticalThreshold = 85,
                    Language = "fr-FR",
                };
                original.TraySensorIds.Add("cpu-package");
                original.HiddenSensorIds.Add("/nic/0/load/0");
                original.Save(path);

                var loaded = AppSettings.Load(path);

                Assert.Equal(2000, loaded.RefreshIntervalMs);
                Assert.Equal(50, loaded.WarmThreshold);
                Assert.Equal(70, loaded.HotThreshold);
                Assert.Equal(85, loaded.CriticalThreshold);
                Assert.Equal("fr-FR", loaded.Language);
                Assert.Contains("cpu-package", loaded.TraySensorIds);
                Assert.Contains("/nic/0/load/0", loaded.HiddenSensorIds);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                var tmp = path + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Fact]
        public void Save_UsesAtomicReplace_DoesNotLeaveTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_test_atomic_{System.Guid.NewGuid():N}.json");
            var tmp = path + ".tmp";
            try
            {
                var s = new AppSettings();
                s.Save(path);
                Assert.True(File.Exists(path));
                Assert.False(File.Exists(tmp));

                // Second save (file exists branch).
                s.Save(path);
                Assert.True(File.Exists(path));
                Assert.False(File.Exists(tmp));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
    }
}
