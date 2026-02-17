# Project Structure

```
HotCPU/
├── Program.cs                  # Entry point, single-instance mutex, TrayApplicationContext
├── AppSettings.cs              # Settings model with JSON persistence to %AppData%/HotCPU/
├── TemperatureService.cs       # Core sensor polling (LibreHardwareMonitor, NvAPI, WMI, PerfCounters)
│                                 Also defines data records: SensorTemp, HardwareTemps, TemperatureReading, TemperatureLevel
├── TrayIconManager.cs          # System tray NotifyIcon lifecycle, multi-sensor icon support
├── TrayIconGenerator.cs        # Dynamic icon rendering (GDI+, multi-size ICO, DPI-aware)
├── HoverInfoForm.cs            # Borderless tooltip form with sparkline charts (GDI+ custom paint)
├── SettingsForm.cs             # Settings dialog (WinForms)
├── SplashForm.cs               # 1-second splash screen on startup
├── LoggerService.cs            # Timed sensor data logging (CSV/JSON/TXT) with schema rotation
├── StartupManager.cs           # Start-with-Windows (MSIX StartupTask or Registry)
├── BenchmarkManager.cs         # Launches companion Rust benchmark executable
├── AssemblyInfo.cs             # InternalsVisibleTo for test project
│
├── Helpers/
│   ├── StringHelper.cs         # Hardware/sensor name cleanup (trademark removal, title casing)
│   └── ThemeHelper.cs          # Light/dark theme detection and color palette
│
├── Localization/
│   └── LocalizationService.cs  # ResourceManager wrapper for i18n
│
├── Resources/
│   ├── Strings.resx            # Default (English) strings
│   └── Strings.{locale}.resx  # Translated strings (12 languages)
│
├── Images/                     # App icons, store logos, screenshots
├── Properties/                 # Publish profiles
│
├── HotCPU.Tests/               # xUnit test project
│   ├── AppSettingsTests.cs
│   ├── StringHelperTests.cs
│   └── TemperatureReadingTests.cs
│
├── HotCPU.Package/             # MSIX/WAP packaging project for Microsoft Store
│
├── benchmark/                  # Rust companion benchmark tool (egui)
│   └── src/
│       ├── main.rs
│       ├── ranking.rs
│       ├── renderer.rs
│       └── scene.rs
│
├── website/                    # Static product website (Vite)
│
├── HotCPU.sln                 # Solution file
├── HotCPU.csproj              # Main project file
├── global.json                # .NET SDK version pin
├── publish.ps1                # Standalone publish script
└── publish_store.ps1          # MSIX Store publish script
```

## Architecture Notes
- No dependency injection container — services are wired manually in `TrayApplicationContext`
- `TemperatureService` owns the polling timer and raises `TemperatureChanged` events
- `TrayIconManager` subscribes to temperature events and manages one or more `NotifyIcon` instances
- Data model uses C# records: `SensorTemp` → `HardwareTemps` → `TemperatureReading`
- All UI rendering (tray icons, hover tooltip) is custom GDI+ — no third-party UI framework
- Settings are serialized as JSON via `System.Text.Json` to `%AppData%/HotCPU/settings.json`
- Internal types are exposed to tests via `[InternalsVisibleTo("HotCPU.Tests")]`
