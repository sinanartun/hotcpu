Write-Host "--- Probing WMI Thermal Classes ---"

function Probe-Class($namespace, $class) {
    Write-Host "Checking $namespace : $class ..." -NoNewline
    try {
        $items = Get-CimInstance -Namespace $namespace -ClassName $class -ErrorAction Stop | Select-Object *
        if ($items) {
            Write-Host " FOUND!" -ForegroundColor Green
            $items | Format-List
        }
        else {
            Write-Host " EMPTY." -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host " FAILED/ACCESS DENIED." -ForegroundColor Red
        Write-Host $_.Exception.Message
    }
    Write-Host "-------------------------------------"
}

Probe-Class "root/cimv2" "Win32_PerfFormattedData_Counters_ThermalZoneInformation"
Probe-Class "root/cimv2" "Win32_TemperatureProbe"
Probe-Class "root/wmi" "MSAcpi_ThermalZoneTemperature"
Probe-Class "root/cimv2" "Win32_PerfRawData_Counters_ThermalZoneInformation"

Write-Host "Checking Performance Counters directly..."
try {
    $counters = Get-Counter "\Thermal Zone Information(*)\Temperature" -ErrorAction Stop
    if ($counters.CounterSamples) {
        Write-Host " FOUND!" -ForegroundColor Green
        $counters.CounterSamples | Format-List Path, CookedValue
    }
    else {
        Write-Host " EMPTY." -ForegroundColor Yellow
    }
}
catch {
    Write-Host " FAILED." -ForegroundColor Red
    Write-Host $_.Exception.Message
}
