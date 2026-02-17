# Tech Stack

## Main Application
- Language: C# 12
- Framework: .NET 8.0 targeting `net8.0-windows10.0.19041.0`
- UI: Windows Forms (WinForms)
- SDK version: 8.0.416 (with `rollForward: latestFeature`)
- Nullable reference types: enabled
- Implicit usings: enabled

## Key Libraries
- `LibreHardwareMonitorLib` 0.9.5 — primary hardware sensor access
- `NvAPIWrapper.Net` 0.8.1.101 — NVIDIA GPU temperature via NvAPI
- `Svg` 3.4.7 — SVG icon rendering
- `Microsoft.Msix.Utils` 2.1.1 — MSIX packaging utilities
- WMI / Performance Counters — fallback sensor sources (non-admin compatible)

## Testing
- Framework: xunit 2.5.3
- Runner: xunit.runner.visualstudio 2.5.3
- Coverage: coverlet.collector 6.0.0
- Test SDK: Microsoft.NET.Test.Sdk 17.8.0
- Test project: `HotCPU.Tests/` with `[InternalsVisibleTo]` for internal type access

## Benchmark Tool
- Language: Rust (edition 2021)
- GUI: egui/eframe 0.26.2
- Located in `benchmark/` directory, produces `hotcpu-benchmark.exe`

## Website
- Static site in `website/` using Vite 7.x
- Plain HTML/JS (no framework)

## Packaging
- MSIX packaging via `HotCPU.Package/` (WAP project)
- Code-signed with `HotCPU_Key.pfx`
- Store association configured in `Package.StoreAssociation.xml`

## Common Commands

### Build
```
dotnet build -c Release
```

### Run Tests
```
dotnet test HotCPU.Tests
```

### Publish (standalone exe)
```
.\publish.ps1
# Output: bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\HotCPU.exe
```

### Publish (MSIX for Store)
```
.\publish_store.ps1
# Output: bin\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\
```

### Build Benchmark (Rust)
```
cargo build --release
# Run from: benchmark/
```

### Website Dev
```
npm run dev
# Run from: website/
```
