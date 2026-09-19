<div align="center">
  <img src="www/logo.png" alt="PrintHop Logo" height="120" />
  <h1>PrintHop</h1>
  <p><strong>A sleek, modern, and minimalistic mobile-to-PC local network printing system</strong><br/>Designed for seamless printing from any local device without cloud dependencies.</p>

  ![.NET](https://img.shields.io/badge/.NET-C%23-512BD4?logo=dotnet&logoColor=white)
  ![VanillaJS](https://img.shields.io/badge/Vanilla_JS-ES6-F7DF1E?logo=javascript&logoColor=black)
  ![Windows](https://img.shields.io/badge/Windows-7%2B-0078D6?logo=windows&logoColor=white)
  ![License](https://img.shields.io/badge/License-MIT-green)
  [![GitHub](https://img.shields.io/badge/GitHub-Vignesh--72%2FPrintHop-181717?logo=github)](https://github.com/Vignesh-72/PrintHop)

  <br/>
</div>

---

## 📥 Downloads & Releases

Packaged installers and release builds are hosted on GitHub Releases:

👉 **[Download PrintHop on GitHub](https://github.com/Vignesh-72/PrintHop/releases)**

---

## 📖 Overview

**PrintHop** is a lightweight, zero-configuration local network printing solution. It allows users to send print jobs (such as PDFs or images) from any mobile device or secondary PC directly to the host PC's connected printers — completely offline, over the local Wi-Fi network.

The application is built to be extremely lightweight, utilizing a custom-built HTTP server in C# that serves a responsive, modern web interface. It eliminates the need for complex driver installations on client devices; users simply scan a QR code and print.

---

## ✨ Features

### 🖨️ Local Network Printing
- **Seamless execution** — send print requests from any device on your local network to the host PC.
- **Zero driver installation** — client devices interact via a pure web interface.

### 📱 QR Code Connectivity
- **Instant access** — easily connect mobile devices by scanning a dynamically generated QR code directly from the host PC's web dashboard.

### 🔍 Printer Separation & Organization
- **Smart routing** — clearly distinguishes between the PC's local system printers and dynamically discovered network printers in the UI.

### 🛡️ Activity & Forensic Logging
- **Real-time monitoring** — tracks all incoming print requests and responses in real-time.
- **Access control** — allows administrators to view logs, identify sending devices (IP/User-Agent), and optionally block unwanted devices from printing.

### ⚡ Zero Configuration
- **One-click installation** — a bundled PowerShell installer sets up the application and automatically configures Windows Firewall to allow local network traffic on port `4222`.

---

## 🔎 Application Capabilities & Audit

PrintHop has been fully audited to ensure broad compatibility and robust local network performance, specifically engineering support for legacy and low-spec systems:

- **Legacy OS Support (Windows 7+)**: The backend avoids modern, heavy dependencies (like IIS or ASP.NET Core) in favor of a custom, low-level HTTP implementation (`HttpServer.cs`). This ensures it runs gracefully on Windows 7 systems without requiring complex .NET Runtime updates.
- **Native Print Spooler Integration**: Interfaces directly with the Windows Print Spooler API (`PrintService.cs`), meaning any printer (USB, Network, or Virtual) installed on the host PC is immediately available to PrintHop without additional configuration.
- **Client Agnostic Frontend**: The web interface strictly uses HTML5, Vanilla CSS, and ES6 JavaScript. It guarantees compatibility across Android, iOS, Linux, and macOS clients, requiring only a modern browser (Chrome, Safari, Firefox).
- **Device Identity & Trust Management**: Automatically parses incoming requests to extract IP addresses and User-Agent strings. It maintains a persistent state of connected devices (`whitelist.json`), allowing the host administrator to explicitly block or allow printing privileges per device.
- **Asynchronous Job Handling**: Print jobs and web requests are processed asynchronously to prevent blocking the main thread, ensuring the host PC remains responsive even during high-traffic network printing.
- **Automated Network Configuration**: The included `setup_firewall.bat` utilizes standard Windows commands (`netsh`) to automatically punch holes for TCP Port 4222 in both modern Windows Defender and legacy Windows Firewall, ensuring immediate local network visibility.

---

## 🏗️ Tech Stack

| Layer | Technology |
|---|---|
| Backend | C# (.NET) |
| Web Server | Custom `HttpServer.cs` |
| Print Spooler API | Native Windows Printing API |
| Frontend | HTML5, Vanilla CSS, Vanilla JS |
| UI Design | Custom Responsive CSS Grid / Flexbox |

---

## 🖥️ System Requirements

| Component | Minimum | Recommended |
|---|---|---|
| **OS** | Windows 7 | Windows 10/11 |
| **CPU** | 1.0 GHz Dual-Core | 1.6 GHz+ |
| **RAM** | 1 GB | 2 GB |
| **Network**| Local Wi-Fi / LAN connection | Local Wi-Fi / LAN connection |

---

## 🚀 Development Setup

### Prerequisites
- **.NET SDK** (for compiling C# source)
- **Git**

### Install & Run

```bash
# Clone the repository
git clone https://github.com/Vignesh-72/PrintHop.git
cd PrintHop

# Build the executable
dotnet build -c Release

# Run the server
# Ensure the compiled PrintHop.exe is placed next to the www/ directory
./PrintHop.exe
```

---

## 📂 Project Structure

```
PrintHop/
├── PrintHop/
│   ├── Models/               # Data structures (ActivityLogs, DeviceInfo, etc.)
│   ├── Services/             # Core logic (HttpServer.cs, PrintService.cs, etc.)
│   ├── Program.cs            # Entry point
│   ├── TrayAppContext.cs     # System Tray icon management
│   └── www/                  # Frontend Web Assets (HTML, CSS, JS, logo)
├── install.ps1               # Automated installation script
├── setup_firewall.bat        # Windows Firewall configuration script
└── README.md
```

---

## 🧠 Architecture & Performance

### Custom HTTP Server
Instead of relying on heavy frameworks like ASP.NET, PrintHop implements a lightweight, low-overhead HTTP listener (`HttpServer.cs`). This ensures minimal memory footprint and fast response times on older hardware.

### State & Logging
- **JSON Persistence** — All configurations, whitelists, and activity logs are persisted locally using simple JSON files (`whitelist.json`, `activity_logs.json`).
- **In-Memory Caching** — Device connections and print jobs are cached in memory for instantaneous dashboard updates.

### Frontend Optimisation
- **Zero Dependencies** — The frontend uses 0 external frameworks (No React, No Vue). Everything is written in Vanilla JS for immediate parsing and rendering.
- **Responsive Design** — The UI relies on native CSS media queries to adapt flawlessly between desktop monitors and mobile screens.

---

## 📜 License

This project is licensed under the **MIT License** — see the [LICENSE](https://github.com/Vignesh-72/PrintHop/blob/main/LICENSE) file for full details.

```
MIT License — Copyright (c) 2026 Vignesh-72
https://github.com/Vignesh-72/PrintHop
```

You are free to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of this software, provided the copyright notice and permission notice are included in all copies or substantial portions of the Software.
