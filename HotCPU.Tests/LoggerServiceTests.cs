using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Xunit;

namespace HotCPU.Tests
{
    public class LoggerServiceTests
    {
        [Theory]
        [InlineData(null, "CSV")]
        [InlineData("", "CSV")]
        [InlineData("   ", "CSV")]
        [InlineData("csv", "CSV")]
        [InlineData("  json ", "JSON")]
        [InlineData("TXT", "TXT")]
        [InlineData("garbage", "CSV")]
        public void NormalizeLogFormat_ReturnsValidFormat(string? input, string expected)
        {
            Assert.Equal(expected, LoggerService.NormalizeLogFormat(input));
        }

        [Fact]
        public void FormatCsvValue_UsesInvariantCulture()
        {
            // Regression: on a locale like fr-FR, a float.ToString("F1") produces "45,5"
            // which contains the CSV delimiter and would corrupt the row.
            var prevCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                var formatted = LoggerService.FormatCsvValue(45.5f);
                // Must be "45.5" (invariant) and therefore not quoted as a comma-containing field.
                Assert.Equal("45.5", formatted);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prevCulture;
            }
        }

        [Fact]
        public void FormatCsvValue_EscapesCommasAndQuotes()
        {
            Assert.Equal("\"a,b\"", LoggerService.FormatCsvValue("a,b"));
            Assert.Equal("\"a \"\"b\"\"\"", LoggerService.FormatCsvValue("a \"b\""));
            Assert.Equal("\"line1\nline2\"", LoggerService.FormatCsvValue("line1\nline2"));
        }

        [Fact]
        public void FormatCsvValue_NullBecomesEmpty()
        {
            Assert.Equal(string.Empty, LoggerService.FormatCsvValue(null));
        }

        [Fact]
        public void BuildLogEntry_NoTrackedSensors_ReturnsNull()
        {
            var settings = new AppSettings(); // LogSensorIds is empty
            var reading = new TemperatureReading(50f, "CPU", settings, new List<HardwareTemps>());

            Assert.Null(LoggerService.BuildLogEntry(reading, settings));
        }

        [Fact]
        public void BuildLogEntry_NullReading_ReturnsNull()
        {
            var settings = new AppSettings();
            settings.LogSensorIds.Add("cpu/0");
            Assert.Null(LoggerService.BuildLogEntry(null, settings));
        }

        [Fact]
        public void BuildLogEntry_TracksOnlyRequestedSensors()
        {
            var cpu = new HardwareTemps("CPU", "🔲", "Cpu");
            cpu.Sensors.Add(new SensorTemp("Package", 65f, "Temperature", "°C", new float[0], "cpu/package"));
            cpu.Sensors.Add(new SensorTemp("Core #0", 60f, "Temperature", "°C", new float[0], "cpu/core/0"));

            var settings = new AppSettings();
            settings.LogSensorIds.Add("cpu/package");

            var reading = new TemperatureReading(65f, "CPU", settings, new List<HardwareTemps> { cpu });
            var entry = LoggerService.BuildLogEntry(reading, settings);

            Assert.NotNull(entry);
            Assert.True(entry!.ContainsKey("Timestamp"));
            Assert.Contains(entry.Keys, k => k.StartsWith("Package"));
            Assert.DoesNotContain(entry.Keys, k => k.StartsWith("Core #0"));
        }

        [Fact]
        public void BuildLogEntry_DuplicateNameUnit_DoesNotOverwrite()
        {
            // Regression: LoggerService used to key by "Name (Unit)", so two tracked
            // sensors with the same simplified name would silently overwrite each
            // other and one column would be missing from the CSV.
            var hw = new HardwareTemps("CPU", "🔲", "Cpu");
            hw.Sensors.Add(new SensorTemp("Core", 60f, "Temperature", "°C", new float[0], "cpu/core/0"));
            hw.Sensors.Add(new SensorTemp("Core", 70f, "Temperature", "°C", new float[0], "cpu/core/1"));

            var settings = new AppSettings();
            settings.LogSensorIds.Add("cpu/core/0");
            settings.LogSensorIds.Add("cpu/core/1");

            var reading = new TemperatureReading(70f, "CPU", settings, new List<HardwareTemps> { hw });
            var entry = LoggerService.BuildLogEntry(reading, settings);

            Assert.NotNull(entry);
            // Timestamp + two sensor columns — no stat columns.
            Assert.Equal(3, entry!.Count);
            Assert.Contains("Core (°C)", entry.Keys);
            Assert.Contains("Core (°C) #2", entry.Keys);
            Assert.Equal(60f, entry["Core (°C)"]);
            Assert.Equal(70f, entry["Core (°C) #2"]);
        }

        [Fact]
        public void BuildLogEntry_AddsRequestedStats()
        {
            var hw = new HardwareTemps("CPU", "🔲", "Cpu");
            hw.Sensors.Add(new SensorTemp("A", 10f, "Temperature", "°C", new float[0], "a"));
            hw.Sensors.Add(new SensorTemp("B", 20f, "Temperature", "°C", new float[0], "b"));
            hw.Sensors.Add(new SensorTemp("C", 30f, "Temperature", "°C", new float[0], "c"));

            var settings = new AppSettings
            {
                LogAverage = true,
                LogMin = true,
                LogMax = true,
            };
            settings.LogSensorIds.AddRange(new[] { "a", "b", "c" });

            var reading = new TemperatureReading(30f, "CPU", settings, new List<HardwareTemps> { hw });
            var entry = LoggerService.BuildLogEntry(reading, settings);

            Assert.NotNull(entry);
            Assert.Equal(20f, (float)entry!["Average"], 3);
            Assert.Equal(10f, (float)entry["Min"], 3);
            Assert.Equal(30f, (float)entry["Max"], 3);
        }

        [Fact]
        public void WriteLogEntry_WritesCsvHeaderAndRow()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_log_{System.Guid.NewGuid():N}.csv");
            try
            {
                var settings = new AppSettings
                {
                    LogPath = path,
                    LogFormat = "CSV",
                };
                var entry = new Dictionary<string, object>
                {
                    { "Timestamp", "2026-05-08 10:00:00" },
                    { "Package (°C)", 65.5f },
                };

                LoggerService.WriteLogEntry(entry, settings);
                LoggerService.WriteLogEntry(entry, settings); // second row

                var lines = File.ReadAllLines(path);
                Assert.Equal(3, lines.Length);
                Assert.Equal("Timestamp,Package (°C)", lines[0]);
                Assert.Equal("2026-05-08 10:00:00,65.5", lines[1]);
                Assert.Equal("2026-05-08 10:00:00,65.5", lines[2]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                foreach (var bak in Directory.GetFiles(Path.GetDirectoryName(path)!,
                             Path.GetFileName(path) + ".*.bak"))
                {
                    try { File.Delete(bak); } catch { }
                }
            }
        }

        [Fact]
        public void WriteLogEntry_SchemaChange_RotatesExistingFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_log_rotate_{System.Guid.NewGuid():N}.csv");
            try
            {
                var settings = new AppSettings
                {
                    LogPath = path,
                    LogFormat = "CSV",
                };

                LoggerService.WriteLogEntry(new Dictionary<string, object>
                {
                    { "Timestamp", "2026-05-08 10:00:00" },
                    { "Package (°C)", 65.5f },
                }, settings);

                // Second call uses a different schema: should rotate the first file.
                LoggerService.WriteLogEntry(new Dictionary<string, object>
                {
                    { "Timestamp", "2026-05-08 10:00:05" },
                    { "Package (°C)", 66.0f },
                    { "Core #0 (°C)", 60.0f },
                }, settings);

                var lines = File.ReadAllLines(path);
                // New file starts with the new header.
                Assert.Equal("Timestamp,Package (°C),Core #0 (°C)", lines[0]);

                // Old file was rotated to <path>.<timestamp>.bak
                var dir = Path.GetDirectoryName(path)!;
                var backups = Directory.GetFiles(dir, Path.GetFileName(path) + ".*.bak");
                Assert.NotEmpty(backups);
                foreach (var bak in backups) File.Delete(bak);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                var dir = Path.GetDirectoryName(path)!;
                foreach (var bak in Directory.GetFiles(dir, Path.GetFileName(path) + ".*.bak"))
                {
                    try { File.Delete(bak); } catch { }
                }
            }
        }

        [Fact]
        public void WriteLogEntry_JsonFormat_AppendsOneLinePerEntry()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotcpu_log_{System.Guid.NewGuid():N}.json");
            try
            {
                var settings = new AppSettings { LogPath = path, LogFormat = "JSON" };
                var entry = new Dictionary<string, object>
                {
                    { "Timestamp", "2026-05-08 10:00:00" },
                    { "Package (°C)", 65.5f },
                };
                LoggerService.WriteLogEntry(entry, settings);
                LoggerService.WriteLogEntry(entry, settings);

                var lines = File.ReadAllLines(path);
                Assert.Equal(2, lines.Length);
                Assert.StartsWith("{", lines[0]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void WriteLogEntry_EmptyPath_DoesNotThrow()
        {
            var settings = new AppSettings { LogPath = "" };
            var entry = new Dictionary<string, object> { { "Timestamp", "x" } };
            // Must silently no-op rather than throw.
            LoggerService.WriteLogEntry(entry, settings);
        }
    }
}
