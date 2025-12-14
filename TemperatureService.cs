using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Timers;
using LibreHardwareMonitor.Hardware;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace HotCPU
{
    internal sealed class TemperatureService : IDisposable
    {
        private readonly Computer _computer;
        private readonly System.Timers.Timer _timer;
        private readonly AppSettings _settings;
        private bool _disposed;
        private bool _isNvApiInitialized;

        public event Action<TemperatureReading>? TemperatureChanged;
        public TemperatureReading CurrentReading { get; private set; } = new(0, "Initializing...", null, new List<HardwareTemps>());

        public TemperatureService(AppSettings settings)
        {
            _settings = settings;
            
            // Enable ALL hardware monitoring
            _computer = new Computer 
            { 
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsControllerEnabled = true,
                IsMemoryEnabled = true,
                IsPsuEnabled = true,
                IsBatteryEnabled = true
            };

            // Try Initialize NvAPI
            try
            {
                NVIDIA.Initialize();
                _isNvApiInitialized = true;
            }
            catch 
            {
                // Likely no NVIDIA card or driver issues
                _isNvApiInitialized = false;
            }
            
            _timer = new System.Timers.Timer(_settings.RefreshIntervalMs);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            try
            {
                _computer.Open();
                UpdateTemperature();
                _timer.Start();
            }
            catch (Exception ex)
            {
                CurrentReading = new TemperatureReading(0, $"Error: {ex.Message}", _settings, new List<HardwareTemps>());
                TemperatureChanged?.Invoke(CurrentReading);
            }
        }

        public void Stop() => _timer.Stop();
        public void UpdateInterval(int intervalMs) => _timer.Interval = intervalMs;
        private void OnTimerElapsed(object? sender, ElapsedEventArgs e) => UpdateTemperature();

        private void UpdateTemperature()
        {
            try
            {
                float? mainCpuTemp = null;
                string cpuName = "CPU";
                var allHardwareTemps = new List<HardwareTemps>();

                // === LibreHardwareMonitor (Primary Source) ===
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    
                    var hwTemps = new HardwareTemps(
                        hardware.Name,
                        GetHardwareTypeIcon(hardware.HardwareType),
                        hardware.HardwareType.ToString());

                    // Get ALL sensors from this hardware
                    CollectAllSensors(hardware, hwTemps);

                    // Check sub-hardware recursively
                    CollectSubHardware(hardware, hwTemps);

                    if (hwTemps.Sensors.Any())
                        allHardwareTemps.Add(hwTemps);

                    // Track main CPU temp for icon
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        cpuName = hardware.Name;
                        mainCpuTemp = GetMainCpuTemp(hwTemps.Sensors);
                    }
                }

                // === NvAPI (NVIDIA GPU Source) ===
                if (_isNvApiInitialized)
                {
                    try
                    {
                        var nvidiaTemps = GetNvidiaTemperatures();
                        if (nvidiaTemps.Sensors.Any())
                            allHardwareTemps.Add(nvidiaTemps);
                    }
                    catch { }
                }

                // === WMI Temperature Sensors (Backup Source for Thermal Zones) ===
                try
                {
                    var wmiTemps = GetWmiTemperatures();
                    if (wmiTemps.Sensors.Any())
                        allHardwareTemps.Add(wmiTemps);
                }
                catch { }

                // === WMI Storage Temperatures (Backup for Disks) ===
                try
                {
                    var diskTemps = GetStorageTemperaturesFromWmi();
                    if (diskTemps.Sensors.Any())
                        allHardwareTemps.Add(diskTemps);
                }
                catch { }

                // === CIMv2 Thermal Zone Information (Standard User Friendly) ===
                try
                {
                    var cimTemps = GetCimTemperatures();
                    if (cimTemps.Sensors.Any())
                        allHardwareTemps.Add(cimTemps);
                }
                catch { }

                // Fallback: If no CPU temp found, use the MAX of any available sensor
                if (mainCpuTemp == null || mainCpuTemp <= 0)
                {
                    var maxSensor = allHardwareTemps
                        .SelectMany(h => h.Sensors)
                        .OrderByDescending(s => s.Temperature)
                        .FirstOrDefault();
                    
                    if (maxSensor != null)
                    {
                        mainCpuTemp = maxSensor.Temperature;
                        // Find which hardware this sensor belongs to
                        var parentHw = allHardwareTemps.FirstOrDefault(h => h.Sensors.Contains(maxSensor));
                        cpuName = parentHw?.Name ?? "System";
                    }
                }

                CurrentReading = new TemperatureReading(
                    mainCpuTemp ?? 0,
                    cpuName,
                    _settings,
                    allHardwareTemps);

                TemperatureChanged?.Invoke(CurrentReading);
            }
            catch (Exception ex)
            {
                CurrentReading = new TemperatureReading(0, $"Error: {ex.Message}", _settings, new List<HardwareTemps>());
                TemperatureChanged?.Invoke(CurrentReading);
            }
        }



        private HardwareTemps GetNvidiaTemperatures()
        {
            var nvTemps = new HardwareTemps("NVIDIA GPU (NvAPI)", "🎮", "GpuNvidia");

            try
            {
                // if (!NVIDIA.IsAvailable) return nvTemps;

                foreach (var gpu in PhysicalGPU.GetPhysicalGPUs())
                {
                    try
                    {
                        var name = gpu.FullName;
                        // Thermal Sensors
                        foreach (var sensor in gpu.ThermalInformation.ThermalSensors)
                        {
                            var temp = (float)sensor.CurrentTemperature;
                            // Sanity check
                            if (temp <= 0 || temp >= 200) continue;

                            var sensorName = $"{name} - {sensor.Target}";
                            
                            // Map targets to readable names
                            // if (sensor.Target == ThermalSettingsTarget.GPU) sensorName = name;
                            // if (sensor.Target == ThermalSettingsTarget.Memory) sensorName = $"{name} Memory";
                            // if (sensor.Target == ThermalSettingsTarget.PowerSupply) sensorName = $"{name} VRM";
                            // if (sensor.Target == ThermalSettingsTarget.Board) sensorName = $"{name} PCB";

                            var id = $"NvAPI_{name}_{sensor.Target}";
                            UpdateHistory(id, temp);

                            nvTemps.Sensors.Add(new SensorTemp(sensorName, temp, GetHistory(id), id));
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return nvTemps;
        }

        // History tracking
        private readonly Dictionary<string, Queue<float>> _sensorHistory = new();
        private const int MAX_HISTORY = 30;

        private void UpdateHistory(string identifier, float temperature)
        {
            if (!_sensorHistory.ContainsKey(identifier))
                _sensorHistory[identifier] = new Queue<float>();

            var queue = _sensorHistory[identifier];
            queue.Enqueue(temperature);
            
            if (queue.Count > MAX_HISTORY)
                queue.Dequeue();
        }

        private float[] GetHistory(string identifier)
        {
            return _sensorHistory.ContainsKey(identifier) 
                ? _sensorHistory[identifier].ToArray() 
                : Array.Empty<float>();
        }

        private void CollectAllSensors(IHardware hardware, HardwareTemps hwTemps)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                {
                    // Filter out invalid readings (255 is a common error value, or 0/negative)
                    if (sensor.Value.Value <= 0 || sensor.Value.Value >= 200) continue;

                    var id = sensor.Identifier.ToString();
                    UpdateHistory(id, sensor.Value.Value);

                    hwTemps.Sensors.Add(new SensorTemp(
                        sensor.Name, 
                        sensor.Value.Value,
                        GetHistory(id),
                        id));
                }
            }
        }

        private void CollectSubHardware(IHardware hardware, HardwareTemps hwTemps)
        {
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Update();
                
                foreach (var sensor in subHardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                    {
                        // Filter out invalid readings
                        if (sensor.Value.Value <= 0 || sensor.Value.Value >= 200) continue;

                        var name = sensor.Name;

                        // If the subhardware name is NOT in the sensor name, check if we need to prefix/combine
                        // But for Nuvoton/ITE chips, usually the sensor name alone is cleaner (e.g. "CPU", "System")
                        if (subHardware.Name != hardware.Name)
                        {
                            // Heuristic: If subhardware is a controller (Nuvoton, ITE, etc), 
                            // we usually just want the sensor name itself if it's unique enough.
                            // Otherwise we might prefix it.
                            
                            bool isController = subHardware.Name.Contains("Nuvoton", StringComparison.OrdinalIgnoreCase) ||
                                              subHardware.Name.Contains("ITE", StringComparison.OrdinalIgnoreCase) ||
                                              subHardware.Name.Contains("NCT", StringComparison.OrdinalIgnoreCase);

                            if (!isController)
                            {
                                name = $"{subHardware.Name} - {sensor.Name}";
                            }
                        }
                            
                        var id = sensor.Identifier.ToString();
                        UpdateHistory(id, sensor.Value.Value);

                        hwTemps.Sensors.Add(new SensorTemp(
                            name, 
                            sensor.Value.Value,
                            GetHistory(id),
                            id));
                    }
                }

                CollectSubHardware(subHardware, hwTemps);
            }
        }

        private HardwareTemps GetWmiTemperatures()
        {
            var wmiTemps = new HardwareTemps("Motherboard / ACPI", "🌡️", "WMI_ACPI");

            try
            {
                // MSAcpi_ThermalZoneTemperature (Legacy Thermal Zones)
                using var searcher = new ManagementObjectSearcher(
                    @"root\WMI",
                    "SELECT * FROM MSAcpi_ThermalZoneTemperature");

                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        var tempKelvin = Convert.ToDouble(obj["CurrentTemperature"]) / 10.0;
                        var tempCelsius = (float)(tempKelvin - 273.15);
                        
                        // Ignore invalid readings (absolute zero or absurdly high)
                        if (tempCelsius < -50 || tempCelsius > 200) continue;

                        var name = obj["InstanceName"]?.ToString() ?? "Thermal Zone";
                        if (name.Contains("\\"))
                            name = name.Split('\\').Last();
                        
                        var id = $"WMI_Process_{name}";
                        UpdateHistory(id, tempCelsius);

                        wmiTemps.Sensors.Add(new SensorTemp(name, tempCelsius, GetHistory(id), id));
                    }
                    catch { }
                }
            }
            catch { }

            return wmiTemps;
        }

        private HardwareTemps GetStorageTemperaturesFromWmi()
        {
            var diskTemps = new HardwareTemps("Storage (WMI)", "💾", "WMI_Storage");

            try
            {
                // MSFT_PhysicalDisk (Modern Windows Storage API)
                // Requires Windows 8/10/11
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT FriendlyName, Temperature FROM MSFT_PhysicalDisk WHERE Temperature > 0");

                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        var name = obj["FriendlyName"]?.ToString() ?? "Unknown Disk";
                        var tempObj = obj["Temperature"]; // Usually in Celsius already for this API
                        
                        if (tempObj != null)
                        {
                            var tempCelsius = Convert.ToSingle(tempObj);
                            var id = $"WMI_Disk_{name}";
                            UpdateHistory(id, tempCelsius);
                            diskTemps.Sensors.Add(new SensorTemp(name, tempCelsius, GetHistory(id), id));
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return diskTemps;
        }

        private HardwareTemps GetCimTemperatures()
        {
            var cimTemps = new HardwareTemps("Motherboard / ACPI (CIM)", "🌡️", "WMI_CIM");

            try
            {
                // Win32_PerfFormattedData_Counters_ThermalZoneInformation
                // Accessible to standard users usually
                using var searcher = new ManagementObjectSearcher(
                    @"root\CIMv2",
                    "SELECT Name, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation WHERE Temperature > 0");

                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        var tempKelvin = Convert.ToDouble(obj["Temperature"]);
                        // Some implementations use raw Kelvin, others might be already Celsius or scaled
                        // Standard WMI Thermal Zone is tenths of Kelvin usually, but PerfCounters can be different.
                        // However, Win32_PerfFormattedData_Counters_ThermalZoneInformation often mirrors MSAcpi_ThermalZoneTemperature.
                        // Let's assume K for safety if > 200, otherwise C.
                        
                        float tempCelsius = (float)tempKelvin; 
                        
                        // If it's huge, it's likely Kelvin
                        if (tempCelsius > 200)
                            tempCelsius = (float)(tempKelvin - 273.15);
                            
                        // Sanity check
                        if (tempCelsius < -50 || tempCelsius > 200) continue;

                        var name = obj["Name"]?.ToString() ?? "Thermal Zone";
                        
                        var id = $"WMI_CIM_{name}";
                        UpdateHistory(id, tempCelsius);

                        cimTemps.Sensors.Add(new SensorTemp(name, tempCelsius, GetHistory(id), id));
                    }
                    catch { }
                }
            }
            catch { }

            return cimTemps;
        }

        private float? GetMainCpuTemp(List<SensorTemp> sensors)
        {
            // Priority order for main CPU temperature
            var priorities = new[] { "Package", "Tctl", "Tdie", "CPU", "Core (Tctl", "CCD" };
            
            foreach (var priority in priorities)
            {
                var match = sensors.FirstOrDefault(s => 
                    s.Name.Contains(priority, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Temperature;
            }

            // Fallback to max of any core temps
            var cores = sensors.Where(s => 
                s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("CCD", StringComparison.OrdinalIgnoreCase)).ToList();
            if (cores.Any()) return cores.Max(s => s.Temperature);

            return sensors.FirstOrDefault()?.Temperature;
        }

        private string GetHardwareTypeIcon(HardwareType type) => type switch
        {
            HardwareType.Cpu => "🔲",
            HardwareType.GpuNvidia => "🎮",
            HardwareType.GpuAmd => "🎮",
            HardwareType.GpuIntel => "🎮",
            HardwareType.Motherboard => "🖥️",
            HardwareType.Storage => "💾",
            HardwareType.Network => "🌐",
            HardwareType.Cooler => "❄️",
            HardwareType.Memory => "📊",
            HardwareType.Psu => "⚡",
            HardwareType.Battery => "🔋",
            _ => "📟"
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            if (_isNvApiInitialized)
            {
                try { NVIDIA.Unload(); } catch { }
            }
            
            _timer.Stop();
            _timer.Dispose();
            _computer.Close();
        }
    }

    // Data classes
    internal record SensorTemp(string Name, float Temperature, float[] History, string Identifier = "")
    {
        public int RoundedTemp => (int)Math.Round(Temperature);
    }

    internal record HardwareTemps(string Name, string Icon, string Type)
    {
        public List<SensorTemp> Sensors { get; } = new();
        public float? MaxTemp => Sensors.Any() ? Sensors.Max(s => s.Temperature) : null;
    }

    internal record TemperatureReading(float Temperature, string CpuName, AppSettings? Settings, List<HardwareTemps> AllTemps)
    {
        public int RoundedTemperature => (int)Math.Round(Temperature);

        public TemperatureLevel Level
        {
            get
            {
                var s = Settings ?? new AppSettings();
                return Temperature switch
                {
                    var t when t < s.WarmThreshold => TemperatureLevel.Cool,
                    var t when t < s.HotThreshold => TemperatureLevel.Warm,
                    var t when t < s.CriticalThreshold => TemperatureLevel.Hot,
                    _ => TemperatureLevel.Critical
                };
            }
        }

        public string DisplayText => RoundedTemperature.ToString();

        public string TooltipText
        {
            get
            {
                var parts = new List<string> { $"{CpuName}: {RoundedTemperature}°C" };
                
                var gpu = AllTemps.FirstOrDefault(h => h.Type.Contains("Gpu"));
                if (gpu?.MaxTemp != null)
                    parts.Add($"GPU: {(int)gpu.MaxTemp}°C");

                return string.Join(" | ", parts);
            }
        }

        public string DetailedText
        {
            get
            {
                var lines = new List<string>();
                
                foreach (var hw in AllTemps.Where(h => h.Sensors.Any()))
                {
                    lines.Add($"\n{hw.Icon} {hw.Name}");
                    lines.Add(new string('─', 35));
                    
                    // Sort sensors: cores first (by number), then others
                    var sortedSensors = hw.Sensors
                        .OrderBy(s => !s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(s => HotCPU.Helpers.StringHelper.ExtractNumber(s.Name))
                        .ThenBy(s => s.Name);

                    foreach (var sensor in sortedSensors)
                    {
                        lines.Add($"  {sensor.Name}: {sensor.RoundedTemp}°C");
                    }
                }

                return lines.Any() ? string.Join("\n", lines) : "No temperature sensors found.";
            }
        }

        public List<CoreTemp> CoreTemps => AllTemps
            .Where(h => h.Type == "Cpu")
            .SelectMany(h => h.Sensors.Select(s => new CoreTemp(s.Name, s.Temperature)))
            .ToList();
    }

    internal record CoreTemp(string Name, float Temperature)
    {
        public int RoundedTemp => (int)Math.Round(Temperature);
    }

    internal enum TemperatureLevel { Cool, Warm, Hot, Critical }
}
