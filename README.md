# HotCPU

<p align="center">
  <img src="Images/AppIcon.png" alt="HotCPU and CPU Temperature Monitor" width="128" height="128" />
</p>

**HotCPU** is a lightweight, system-tray-based utility designed to monitor and display real-time temperature data for your computer's hardware. Built with .NET 8 and Windows Forms, it provides a non-intrusive way to keep track of your system's thermal performance.

## 🚀 Features

*   **System Tray Integration**: Runs quietly in the background, accessible via the system tray.
*   **Comprehensive Monitoring**: Tracks temperatures for:
    *   **CPU** (Package, Cores, CCDs)
    *   **GPU** (NVIDIA via NvAPI & others via LibreHardwareMonitor)
    *   **Motherboard** & VRMs
    *   **Storage** (NVMe, SSD, HDD)
    *   **Network**, **Memory**, **PSU**, and **Batteries**
*   **Real-time Alerts**: Tray icon color changes based on customizable temperature thresholds (Cool, Warm, Hot, Critical).
*   **Detailed Insights**: Hover over or click the tray icon for a detailed summary of all sensors.
*   **Modern Tech Stack**: Utilizes [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) and [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) for accurate hardware readings.

## 🛠️ Getting Started

### Prerequisites

*   Windows 10/11
*   .NET 8 Runtime (if running the portable version)

### Installation

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/sinanartun/hotcpu.git
    cd hotcpu
    ```

2.  **Build the project:**
    You can use Visual Studio 2022 or the .NET CLI:
    ```bash
    dotnet build -c Release
    ```

3.  **Run:**
    Locate the executable in `bin/Release/net8.0-windows10.0.19041.0/win-x64/` and run `hotcpu.exe`.

> **Note:** Administrator privileges may be required to access certain hardware sensors.

## ⚙️ Configuration

HotCPU allows you to customize temperature thresholds and refresh intervals. These settings are managed via the settings dialog (accessible from the tray context menu).

## 🧩 Technologies Used

*   **Language**: C# 12
*   **Framework**: .NET 8.0 (Windows Forms)
*   **Hardware Access**:
    *   `LibreHardwareMonitorLib`
    *   `NvAPIWrapper.Net`
    *   WMI (Windows Management Instrumentation)

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

---

<p align="center">
  Made with ❤️ by Sinan Artun
</p>
