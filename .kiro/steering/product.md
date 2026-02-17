# Product: HotCPU

HotCPU is a lightweight Windows system tray application that monitors and displays real-time hardware temperatures and sensor data. It runs in the background with a customizable tray icon showing live CPU temperature, and provides a rich hover tooltip with sparkline charts for all detected sensors.

## Core Capabilities
- Real-time monitoring of CPU, GPU, motherboard, storage, network, memory, PSU, and battery sensors
- System tray icon with dynamic temperature display and color-coded thresholds (Cool → Warm → Hot → Critical)
- Hover tooltip with per-sensor sparkline graphs and organized hardware groupings
- Multi-sensor tray icon support (users can pin multiple sensors to the tray)
- Sensor data logging to CSV, JSON, or TXT files
- Settings UI for thresholds, colors, fonts, theme (light/dark/auto), sensor visibility, and logging
- Localization support (12+ languages via .resx resource files)
- Start-with-Windows support (both MSIX packaged and unpackaged registry-based)
- Single-instance enforcement via named mutex
- Companion Rust-based benchmark tool with egui GUI

## Target Platform
- Windows 10/11 only
- Distributed via Microsoft Store (MSIX) and as standalone executable
