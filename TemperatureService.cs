using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Timers;
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace HotCPU
{
    internal sealed class TemperatureService : IDisposable
    {
        private readonly Computer _computer;
        private readonly System.Timers.Timer _timer;
        private readonly PerformanceCounterCategory? _thermalCategory;
        private readonly AppSettings _settings;
        private bool _disposed;
        private bool _isNvApiInitialized;
        private string _cachedCpuName = "CPU";
        // WMI Circuit Breakers - disabled by default to prevent "Not supported" exceptions
        // and ensure Windows Store compliance (Non-Admin). 
        // We now rely on Performance Counters for motherboard thermal zones.
        private bool _wmiThermalFailed = true;
        private bool _wmiStorageFailed = true;
        private bool _wmiCimFailed = true;

        public event Action<TemperatureReading>? TemperatureChanged;
        public TemperatureReading CurrentReading { get; private set; } = new(0, "Initializing...", null, new List<HardwareTemps>());

        public TemperatureService(AppSettings settings)
        {
            _settings = settings;
            
            // Only enable hardware types that provide temperature readings
            // Network, Memory, PSU, Battery, Controller typically don't provide useful temps
            _computer = new Computer 
            { 
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                // Disabled to reduce memory footprint:
                IsNetworkEnabled = true,
                IsControllerEnabled = true,
                IsMemoryEnabled = true,
                IsPsuEnabled = true,
                IsBatteryEnabled = true
            };

            // Initialize Performance Counters
            try
            {
                if (PerformanceCounterCategory.Exists("Thermal Zone Information"))
                {
                    _thermalCategory = new PerformanceCounterCategory("Thermal Zone Information");
                }
            }
            catch { /* Ignore permission errors */ }

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
            
            _cachedCpuName = GetCpuNameFromWmi();

            _timer = new System.Timers.Timer(_settings.RefreshIntervalMs);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = false; // Prevent reentrancy
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
        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_disposed) return;
            try 
            {
                UpdateTemperature();
            }
            finally
            {
                // Only restart if not disposed
                if (!_disposed)
                {
                    try { _timer.Start(); } catch { }
                }
            }
        }

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
                    
                    // Debug: Log ALL hardware detected before filtering
                    System.Diagnostics.Debug.WriteLine($"[HotCPU LHM] Hardware: {hardware.Name}, Type: {hardware.HardwareType}, RawSensors: {hardware.Sensors.Length}, SubHW: {hardware.SubHardware.Length}");
                    
                    var hwTemps = new HardwareTemps(
                        HotCPU.Helpers.StringHelper.SimplifyHardwareName(hardware.Name),
                        GetHardwareTypeIcon(hardware.HardwareType),
                        hardware.HardwareType.ToString());

                    // Get ALL sensors from this hardware
                    CollectAllSensors(hardware, hwTemps);

                    // Check sub-hardware recursively
                    CollectSubHardware(hardware, hwTemps);
                    
                    // Debug: Log after filtering
                    System.Diagnostics.Debug.WriteLine($"[HotCPU LHM] -> After filter: {hwTemps.Sensors.Count} sensors");

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

                // === Performance Counters (Non-Admin Fallback for Motherboard) ===
                if (_thermalCategory != null)
                {
                    try
                    {
                        var perfTemps = GetPerformanceCounterTemperatures();
                        if (perfTemps.Sensors.Any())
                            allHardwareTemps.Add(perfTemps);
                    }
                    catch { }
                }

                // === WMI Temperature Sensors (Backup Source for Thermal Zones) ===
                if (!_wmiThermalFailed)
                {
                    var (wmiTemps, failed) = GetWmiTemperatures();
                    if (failed)
                        _wmiThermalFailed = true;
                    else if (wmiTemps.Sensors.Any())
                        allHardwareTemps.Add(wmiTemps);
                }

                // === WMI Storage Temperatures (Backup for Disks) ===
                if (!_wmiStorageFailed)
                {
                    var (diskTemps, failed) = GetStorageTemperaturesFromWmi();
                    if (failed)
                        _wmiStorageFailed = true;
                    else if (diskTemps.Sensors.Any())
                        allHardwareTemps.Add(diskTemps);
                }

                // === CIMv2 Thermal Zone Information (Standard User Friendly) ===
                if (!_wmiCimFailed)
                {
                    var (cimTemps, failed) = GetCimTemperatures();
                    if (failed)
                        _wmiCimFailed = true;
                    else if (cimTemps.Sensors.Any())
                        allHardwareTemps.Add(cimTemps);
                }

                // Check if we have any real CPU
                bool hasCpu = allHardwareTemps.Any(h => h.Type == "Cpu" && h.Sensors.Any());

                // Fallback: If no CPU temp found, use the MAX of any available TEMPERATURE sensor
                if (!hasCpu || mainCpuTemp == null || mainCpuTemp <= 0)
                {
                    var maxSensor = allHardwareTemps
                        .SelectMany(h => h.Sensors)
                        .Where(s => s.Type == "Temperature" && s.Value > 0 && s.Value < 200) // Only valid temps
                        .OrderByDescending(s => s.Temperature)
                        .FirstOrDefault();
                    
                    if (maxSensor != null)
                    {
                        mainCpuTemp = maxSensor.Temperature;
                        cpuName = HotCPU.Helpers.StringHelper.SimplifyHardwareName(_cachedCpuName);

                        // Create a simulated CPU hardware so it shows up in the UI/Settings
                        var simCpu = new HardwareTemps(cpuName, "🔲", "Cpu");
                        var simId = "Simulated_CPU_Max";
                        UpdateHistory(simId, maxSensor.Temperature);
                        
                        simCpu.Sensors.Add(new SensorTemp(
                            "Core", 
                            maxSensor.Temperature,  // Use Temperature, not raw Value
                            "Temperature",          // Correct type
                            "°C",
                            GetHistory(simId), 
                            simId));
                        
                        // Insert at the top so it looks like a primary CPU
                        allHardwareTemps.Insert(0, simCpu);
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
                        var name = HotCPU.Helpers.StringHelper.SimplifyHardwareName(gpu.FullName);
                        // Thermal Sensors
                        foreach (var sensor in gpu.ThermalInformation.ThermalSensors)
                        {
                            var temp = (float)sensor.CurrentTemperature;
                            // Sanity check
                            if (temp <= 0 || temp >= 200) continue;

                            var rawSensName = sensor.Target.ToString();
                            // "GPU", "Memory", "PowerSupply", "Board"
                            
                            var sensorName = HotCPU.Helpers.StringHelper.CleanSensorName(rawSensName, name);

                            var id = $"NvAPI_{name}_{sensor.Target}";
                            UpdateHistory(id, temp);

                            nvTemps.Sensors.Add(new SensorTemp(sensorName, temp, "Temperature", "°C", GetHistory(id), id));
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
        private readonly Dictionary<string, DateTime> _sensorLastSeen = new();
        private const int MAX_HISTORY = 30;
        private const int STALE_SENSOR_THRESHOLD_MINUTES = 5;
        private DateTime _lastCleanup = DateTime.UtcNow;

        private void UpdateHistory(string identifier, float temperature)
        {
            _sensorLastSeen[identifier] = DateTime.UtcNow;
            
            if (!_sensorHistory.ContainsKey(identifier))
                _sensorHistory[identifier] = new Queue<float>();

            var queue = _sensorHistory[identifier];
            queue.Enqueue(temperature);
            
            if (queue.Count > MAX_HISTORY)
                queue.Dequeue();
            
            // Periodic cleanup of stale sensors (every minute)
            if ((DateTime.UtcNow - _lastCleanup).TotalMinutes >= 1)
            {
                CleanupStaleSensors();
                _lastCleanup = DateTime.UtcNow;
            }
        }

        private void CleanupStaleSensors()
        {
            var now = DateTime.UtcNow;
            var staleIds = _sensorLastSeen
                .Where(kvp => (now - kvp.Value).TotalMinutes > STALE_SENSOR_THRESHOLD_MINUTES)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var id in staleIds)
            {
                _sensorHistory.Remove(id);
                _sensorLastSeen.Remove(id);
            }
        }

        private float[] GetHistory(string identifier)
        {
            return _sensorHistory.ContainsKey(identifier) 
                ? _sensorHistory[identifier].ToArray() 
                : Array.Empty<float>();
        }

        /// <summary>
        /// Logs weird/invalid sensor data to Debug output for troubleshooting.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private void LogWeirdSensor(string reason, string sensorName, string? sensorId, SensorType sensorType, float value, string? hardwareName = null)
        {
            var hw = hardwareName != null ? $" [{hardwareName}]" : "";
            var msg = $"[HotCPU SENSOR]{hw} {reason}: {sensorName} (Type: {sensorType}, Value: {value}, ID: {sensorId ?? "N/A"})";
            System.Diagnostics.Debug.WriteLine(msg);
        }

        private void CollectAllSensors(IHardware hardware, HardwareTemps hwTemps)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value.HasValue)
                {
                    var val = sensor.Value.Value;
                    var id = sensor.Identifier.ToString();
                    
                    // Check for weird temperature readings
                    if (sensor.SensorType == SensorType.Temperature)
                    {
                        if (val <= 0)
                        {
                            LogWeirdSensor("SKIPPED (temp <= 0)", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                            continue;
                        }
                        if (val >= 200)
                        {
                            LogWeirdSensor("SKIPPED (temp >= 200)", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                            continue;
                        }
                    }
                    
                    // Check for weird voltage readings (should be 0-20V typically)
                    if (sensor.SensorType == SensorType.Voltage && (val < -1 || val > 50))
                    {
                        LogWeirdSensor("WEIRD VOLTAGE", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                    }
                    
                    // Check for weird fan readings (should be 0-10000 RPM typically)
                    if (sensor.SensorType == SensorType.Fan && val > 20000)
                    {
                        LogWeirdSensor("WEIRD FAN RPM", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                    }
                    
                    // Check for weird power readings (should be 0-2000W typically)
                    if (sensor.SensorType == SensorType.Power && (val < 0 || val > 5000))
                    {
                        LogWeirdSensor("WEIRD POWER", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                    }
                    
                    // Check for weird clock readings (should be 0-20000 MHz - GDDR7 runs at ~15000+ MHz)
                    if (sensor.SensorType == SensorType.Clock && (val < 0 || val > 20000))
                    {
                        LogWeirdSensor("WEIRD CLOCK", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                    }
                    
                    // Check for weird load readings (should be 0-100%)
                    if (sensor.SensorType == SensorType.Load && (val < 0 || val > 100))
                    {
                        LogWeirdSensor("WEIRD LOAD %", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                    }

                    UpdateHistory(id, val);

                    var simplifiedName = HotCPU.Helpers.StringHelper.CleanSensorName(sensor.Name, hwTemps.Name);
                    var type = sensor.SensorType.ToString();
                    var unit = GetUnitForSensorType(sensor.SensorType);

                    // Safety Check: If unit is Celsius but value is garbage (driver bug), skip it
                    if (unit == "°C" && (val <= 0 || val >= 200)) 
                    {
                        LogWeirdSensor("HIDDEN (bad temp in °C unit)", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                        continue;
                    }
                    
                    // Final sanity check for extreme values - only for sensor types with known bounds
                    // (Throughput can be 100s of MB/s, Data can be 100s of GB, etc.)
                    bool isExtreme = sensor.SensorType switch
                    {
                        SensorType.Temperature => val > 500 || val < -50,  // -50 to 500°C
                        SensorType.Voltage => val > 100 || val < -50,      // -50 to 100V
                        SensorType.Power => val > 10000 || val < -100,     // -100 to 10000W
                        SensorType.Fan => val > 50000,                      // 0 to 50000 RPM
                        SensorType.Load => val > 200 || val < -10,          // -10 to 200%
                        SensorType.Clock => val > 50000 || val < -100,      // -100 to 50000 MHz
                        _ => false  // Don't block other types (Throughput, Data, etc.)
                    };
                    
                    if (isExtreme)
                    {
                        LogWeirdSensor("BLOCKED (extreme value)", sensor.Name, id, sensor.SensorType, val, hardware.Name);
                        continue;
                    }

                    hwTemps.Sensors.Add(new SensorTemp(
                        simplifiedName, 
                        val,
                        type,
                        unit,
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
                    if (sensor.Value.HasValue)
                    {
                        var val = sensor.Value.Value;
                        var id = sensor.Identifier.ToString();
                        var hwName = $"{hardware.Name}/{subHardware.Name}";
                        
                        // Check for weird temperature readings
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            if (val <= 0)
                            {
                                LogWeirdSensor("SKIPPED (temp <= 0)", sensor.Name, id, sensor.SensorType, val, hwName);
                                continue;
                            }
                            if (val >= 200)
                            {
                                LogWeirdSensor("SKIPPED (temp >= 200)", sensor.Name, id, sensor.SensorType, val, hwName);
                                continue;
                            }
                        }
                        
                        // Check for weird voltage readings
                        if (sensor.SensorType == SensorType.Voltage && (val < -1 || val > 50))
                        {
                            LogWeirdSensor("WEIRD VOLTAGE", sensor.Name, id, sensor.SensorType, val, hwName);
                        }
                        
                        // Check for weird fan readings
                        if (sensor.SensorType == SensorType.Fan && val > 20000)
                        {
                            LogWeirdSensor("WEIRD FAN RPM", sensor.Name, id, sensor.SensorType, val, hwName);
                        }
                        
                        // Check for weird power readings
                        if (sensor.SensorType == SensorType.Power && (val < 0 || val > 5000))
                        {
                            LogWeirdSensor("WEIRD POWER", sensor.Name, id, sensor.SensorType, val, hwName);
                        }
                        
                        // Check for weird clock readings (should be 0-20000 MHz - GDDR7 runs at ~15000+ MHz)
                        if (sensor.SensorType == SensorType.Clock && (val < 0 || val > 20000))
                        {
                            LogWeirdSensor("WEIRD CLOCK", sensor.Name, id, sensor.SensorType, val, hwName);
                        }
                        
                        // Check for weird load readings
                        if (sensor.SensorType == SensorType.Load && (val < 0 || val > 100))
                        {
                            LogWeirdSensor("WEIRD LOAD %", sensor.Name, id, sensor.SensorType, val, hwName);
                        }

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
                            
                        
                        // Clean sensor name relative to SUBhardware name
                        if (name.Contains("-"))
                        {
                            var parts = name.Split('-');
                            if (parts.Length > 1) name = parts[1].Trim();
                        }
                        
                        var simplifiedSensor = HotCPU.Helpers.StringHelper.CleanSensorName(name, subHardware.Name);

                        UpdateHistory(id, val);
                        
                        var type = sensor.SensorType.ToString();
                        var unit = GetUnitForSensorType(sensor.SensorType);

                        // Safety Check: If unit is Celsius but value is garbage (driver bug), skip it
                        if (unit == "°C" && (val <= 0 || val >= 200)) 
                        {
                            LogWeirdSensor("HIDDEN (bad temp in °C unit)", sensor.Name, id, sensor.SensorType, val, hwName);
                            continue;
                        }
                        
                        // Final sanity check for extreme values - only for sensor types with known bounds
                        bool isExtreme = sensor.SensorType switch
                        {
                            SensorType.Temperature => val > 500 || val < -50,
                            SensorType.Voltage => val > 100 || val < -50,
                            SensorType.Power => val > 10000 || val < -100,
                            SensorType.Fan => val > 50000,
                            SensorType.Load => val > 200 || val < -10,
                            SensorType.Clock => val > 50000 || val < -100,
                            _ => false
                        };
                        
                        if (isExtreme)
                        {
                            LogWeirdSensor("BLOCKED (extreme value)", sensor.Name, id, sensor.SensorType, val, hwName);
                            continue;
                        }

                        hwTemps.Sensors.Add(new SensorTemp(
                            simplifiedSensor, 
                            val,
                            type,
                            unit,
                            GetHistory(id),
                            id));
                    }
                }

                CollectSubHardware(subHardware, hwTemps);
            }
        }

        /// <summary>
        /// Tests if a WMI class is available and queryable without throwing exceptions.
        /// This specifically tests the MoveNext() call which is where "Not supported" errors occur.
        /// </summary>
        private bool IsWmiClassAvailable(string wmiNamespace, string className)
        {
            try
            {
                // Use WHERE FALSE to check class validity without triggering expensive/unsupported enumeration
                using var searcher = new ManagementObjectSearcher(
                    wmiNamespace,
                    $"SELECT * FROM {className} WHERE FALSE");
                using var collection = searcher.Get();
                using var enumerator = collection.GetEnumerator();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private (HardwareTemps temps, bool failed) GetWmiTemperatures()
        {
            var wmiTemps = new HardwareTemps("Motherboard / ACPI", "🌡️", "WMI_ACPI");

            // Check if the WMI class is available before trying to query it
            if (!IsWmiClassAvailable(@"root\WMI", "MSAcpi_ThermalZoneTemperature"))
            {
                return (wmiTemps, failed: true);
            }

            try
            {
                // MSAcpi_ThermalZoneTemperature (Legacy Thermal Zones)
                using var searcher = new ManagementObjectSearcher(
                    @"root\WMI",
                    "SELECT * FROM MSAcpi_ThermalZoneTemperature");

                using var collection = searcher.Get();
                
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
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

                            wmiTemps.Sensors.Add(new SensorTemp(name, tempCelsius, "Temperature", "°C", GetHistory(id), id));
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Any error during enumeration - signal failure
                return (wmiTemps, failed: true);
            }

            return (wmiTemps, failed: false);
        }

        private (HardwareTemps temps, bool failed) GetStorageTemperaturesFromWmi()
        {
            var diskTemps = new HardwareTemps("Storage (WMI)", "💾", "WMI_Storage");

            try
            {
                // MSFT_PhysicalDisk (Modern Windows Storage API)
                // Requires Windows 8/10/11
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT * FROM MSFT_PhysicalDisk");

                using var collection = searcher.Get();
                
                // Wrap enumeration in try-catch because MoveNext() can throw ManagementException
                try
                {
                    foreach (ManagementObject obj in collection)
                    {
                        using (obj)
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
                                    
                                    var simpleDisk = HotCPU.Helpers.StringHelper.SimplifyHardwareName(name);
                                    
                                    diskTemps.Sensors.Add(new SensorTemp(simpleDisk, tempCelsius, "Temperature", "°C", GetHistory(id), id));
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (ManagementException)
                {
                    // WMI class not supported - signal failure
                    return (diskTemps, failed: true);
                }
            }
            catch
            {
                // Other errors - signal failure
                return (diskTemps, failed: true);
            }

            return (diskTemps, failed: false);
        }

        private (HardwareTemps temps, bool failed) GetCimTemperatures()
        {
            var cimTemps = new HardwareTemps("Motherboard / ACPI (CIM)", "🌡️", "WMI_CIM");

            try
            {
                // Win32_PerfFormattedData_Counters_ThermalZoneInformation
                // Accessible to standard users usually
                using var searcher = new ManagementObjectSearcher(
                    @"root\CIMv2",
                    "SELECT Name, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation WHERE Temperature > 0");

                using var collection = searcher.Get();
                
                // Wrap enumeration in try-catch because MoveNext() can throw ManagementException
                try
                {
                    foreach (ManagementObject obj in collection)
                    {
                        using (obj)
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

                                cimTemps.Sensors.Add(new SensorTemp(name, tempCelsius, "Temperature", "°C", GetHistory(id), id));
                            }
                            catch { }
                        }
                    }
                }
                catch (ManagementException)
                {
                    // WMI class not supported - signal failure
                    return (cimTemps, failed: true);
                }
            }
            catch
            {
                // Other errors - signal failure
                return (cimTemps, failed: true);
            }

            return (cimTemps, failed: false);
        }


        private string GetCpuNameFromWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\CIMv2", "SELECT Name FROM Win32_Processor");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        var name = obj["Name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(name)) 
                            return name.Trim();
                    }
                }
            }
            catch { }
            return "CPU (System Estimate)";
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

        public string GetFullHardwareReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== HotCPU Hardware Report ===");
            sb.AppendLine($"Generated: {DateTime.Now}");
            sb.AppendLine();

            sb.AppendLine("=== LibreHardwareMonitor Sensors ===");
            foreach (var hardware in _computer.Hardware)
            {
                AppendHardwareSensorsRecursive(hardware, sb, depth: 0);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void AppendHardwareSensorsRecursive(IHardware hardware, System.Text.StringBuilder sb, int depth)
        {
            hardware.Update();
            var indent = new string(' ', depth * 2);
            var sensorIndent = new string(' ', (depth + 1) * 2);
            var label = depth == 0 ? "Hardware" : "SubHardware";
            var typeSuffix = depth == 0 ? $" (Type: {hardware.HardwareType})" : "";

            sb.AppendLine($"{indent}- {label}: {hardware.Name}{typeSuffix}");

            foreach (var sensor in hardware.Sensors)
            {
                sb.AppendLine($"{sensorIndent}- Sensor: {sensor.Name} | Type: {sensor.SensorType} | Value: {sensor.Value}");
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                AppendHardwareSensorsRecursive(subHardware, sb, depth + 1);
            }
        }

        private string GetUnitForSensorType(SensorType type) => type switch
        {
            SensorType.Temperature => "°C",
            SensorType.Voltage => "V",
            SensorType.Current => "A",
            SensorType.Power => "W",
            SensorType.Clock => "MHz",
            SensorType.Load => "%",
            SensorType.Fan => "RPM",
            SensorType.Flow => "L/h",
            SensorType.Control => "%",
            SensorType.Level => "%",
            SensorType.Factor => "x",
            SensorType.Data => "GB",
            SensorType.SmallData => "MB",
            SensorType.Throughput => "KB/s",
            SensorType.TimeSpan => "s",
            SensorType.Energy => "mWh",
            _ => ""
        };

        private HardwareTemps GetPerformanceCounterTemperatures()
        {
            var temps = new HardwareTemps("Thermal Zones (Perf)", "🌡️", "ThermalZone");

            try
            {
                if (_thermalCategory == null) return temps;

                var instanceNames = _thermalCategory.GetInstanceNames();
                System.Diagnostics.Debug.WriteLine($"[HotCPU PERF] Thermal Zone instances found: {instanceNames.Length}");
                
                foreach (var instance in instanceNames)
                {
                    try
                    {
                        // Some systems report duplicates or invalid names, handle gracefully
                        using var counter = new PerformanceCounter("Thermal Zone Information", "High Precision Temperature", instance);
                        counter.NextValue(); // First read is always 0
                        var rawValue = counter.NextValue();
                        
                        // "High Precision Temperature" is typically in 1/10ths of Kelvin.
                        // Standard: 273.15 K = 0 C. So 3010 = 301.0 K = 27.85 C.
                        
                        if (rawValue > 0)
                        {
                            float celsius = rawValue;

                            // Heuristic to determine unit
                            if (rawValue > 2000) // Likely 1/10 K (e.g. 3010)
                            {
                                celsius = (rawValue / 10.0f) - 273.15f;
                            }
                            else if (rawValue > 200) // Likely Kelvin (e.g. 301)
                            {
                                celsius = rawValue - 273.15f;
                            }
                            // Else assume Celsius

                            if (celsius > -50 && celsius < 200)
                            {
                                System.Diagnostics.Debug.WriteLine($"[HotCPU PERF] Found Valid Temp: {instance} = {celsius}°C (Raw: {rawValue})");
                                var id = $"Perf_TZ_{instance}";
                                UpdateHistory(id, celsius);
                                temps.Sensors.Add(new SensorTemp(instance, celsius, "Temperature", "°C", GetHistory(id), id));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HotCPU PERF] Error reading instance {instance}: {ex.Message}");
                    }
                }
            }
            catch { }

            return temps;
        }

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
    internal record SensorTemp(string Name, float Value, string Type, string Unit, float[] History, string Identifier = "")
    {
        public int RoundedValue => (int)Math.Round(Value);
        // Compat property, prefer Value
        public float Temperature => Value; 
    }

    internal record HardwareTemps(string Name, string Icon, string Type)
    {
        public List<SensorTemp> Sensors { get; } = new();
        public float? MaxTemp => Sensors
            .Where(s => s.Type == "Temperature")
            .OrderByDescending(s => s.Value)
            .Select(s => (float?)s.Value)
            .FirstOrDefault();
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
                        // Use correct unit and formatting
                        string val = sensor.Unit == "°C" || sensor.Unit == "%" || sensor.Unit == "RPM" 
                             ? sensor.RoundedValue.ToString() 
                             : sensor.Value.ToString("F1");

                        lines.Add($"  {sensor.Name}: {val}{sensor.Unit}");
                    }
                }

                return lines.Any() ? string.Join("\n", lines) : "No temperature sensors found.";
            }
        }

        public List<CoreTemp> CoreTemps => AllTemps
            .Where(h => h.Type == "Cpu")
            .SelectMany(h => h.Sensors
                .Where(s => s.Type == "Temperature")
                .Select(s => new CoreTemp(s.Name, s.Value)))
            .ToList();
    }

    internal record CoreTemp(string Name, float Temperature)
    {
        public int RoundedTemp => (int)Math.Round(Temperature);
    }

    public enum TemperatureLevel { Cool, Warm, Hot, Critical }
}
