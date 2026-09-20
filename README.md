<div align="center">
  <img width="192" height="192" alt="logo_transparent_192x192" src="https://github.com/user-attachments/assets/024a5b5c-1f6b-423f-a3d0-8aacd8ecd56f" />
  <h1>PrintHop</h1>
  <p><b>Driverless, Zero-Cloud Printer-Sharing for Windows LANs</b></p>
  
  <p>
    <a href="https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48"><img src="https://img.shields.io/badge/.NET-4.8-512BD4?style=for-the-badge&logo=.net" alt=".NET 4.8"></a>
    <a href="#"><img src="https://img.shields.io/badge/Windows-7%20|%2010%20|%2011-0078D6?style=for-the-badge&logo=windows" alt="Windows Support"></a>
    <a href="#"><img src="https://img.shields.io/badge/Status-Active-success?style=for-the-badge" alt="Status"></a>
    <a href="#"><img src="https://img.shields.io/badge/RAM_Target-2GB-orange?style=for-the-badge" alt="Memory"></a>
  </p>
</div>

---

**PrintHop** is a lightweight, zero-cloud Windows utility designed to seamlessly share local printers across a Local Area Network (LAN). Built specifically for small businesses and legacy hardware environments (running natively on systems with as little as 2GB of RAM), PrintHop eliminates the need for complex driver installations, Active Directory configurations, or paid cloud print subscriptions.

If your device is on the network, you can print.

## ✨ Key Features

- 🔒 **Zero-Cloud & Fully Offline**: All data and documents remain strictly on your local network. No accounts, no external telemetry, no internet connection required.
- 🚀 **Driverless Web Printing**: Users don't need to install any printer drivers or client software. They simply navigate to the host's IP address (e.g., `http://192.168.1.50:4222`) in any modern desktop or mobile browser and drop their documents.
- ⚡ **Optimized for Low-End Hardware**: Rigorously engineered to run on old Windows 7 machines with limited resources. Uploads are streamed directly to disk to prevent RAM spikes, and a sophisticated zero-CPU polling queue ensures smooth operation without locking up the host machine.
- 🛡️ **Access Control & Auditing**: Features real-time queue monitoring, manual job blocking, and a comprehensive audit stream of every print job and connected device.
- 📡 **Smart Auto-Discovery**: Automatic UDP broadcast detection allows PrintHop instances to instantly find each other on the LAN, automatically adapting to the correct network adapter.

## 🖥️ Platform Support & Requirements

| Operating System | Required Runtime | Min RAM | Permissions |
|-----------------|-----------------|---------|-------------|
| **Windows 7 SP1** | .NET Framework 4.8 | 2 GB* | UAC Admin (first run) |
| **Windows 10** | .NET Framework 4.8 | 2 GB* | UAC Admin (first run) |
| **Windows 11** | .NET Framework 4.8 | 2 GB* | UAC Admin (first run) |

*\*2GB RAM is a strict design target. Memory footprint remains incredibly low even under heavy network print loads.*

### 📱 Mobile Experience
There is **no native mobile app** to install. Mobile users (iOS/Android) access PrintHop exactly like desktop users: by connecting to the local Wi-Fi and navigating to the Host's IP address in Safari or Chrome. The Web UI is fully responsive and behaves natively on mobile devices.

## 🚀 Installation & Usage

### 1. Download
Grab the latest `PrintHop.exe` from the [Releases](#) page.

### 2. Initial Setup
Run the executable as **Administrator** for the very first launch. PrintHop uses Windows `netsh` to automatically configure Windows Firewall exceptions for port `4222` (TCP/UDP) so peers can connect. You will be prompted for UAC elevation. *(Subsequent launches do not require Admin rights).*

### 3. Connect & Print
- **On the host PC:** Right-click the PrintHop tray icon and select "Open Print Station".
- **On peer devices (Phones, Laptops):** Navigate to `http://<HOST-IP>:4222` in any browser.
- **Print:** Drag and drop a supported document (PDF, DOCX, XLSX, PNG, JPG) into the Web UI, configure your print settings (copies, duplex, color), and hit Print!

## 🏗️ Architecture & Security Model

PrintHop is built on a custom lightweight C# HTTP Server and is designed strictly for **Trusted LAN environments**.

- **No Remote Attack Surface**: It does not punch holes in your router or communicate over the internet.
- **Host Whitelisting**: Unknown devices must be approved by the Host administrator to submit print jobs.
- **Real-time Webhooks**: Peer-to-peer print status updates are tracked asynchronously using reliable, timeout-safe webhooks.

*Note: Internal LAN traffic is currently transmitted via plain HTTP to ensure zero-configuration compatibility across legacy Windows 7 devices without triggering browser certificate warnings.*

## 📚 Documentation

For deep-dives into how PrintHop works under the hood, check the `/docs` folder:
- [**Architecture & Queue System**](docs/architecture.md): Concurrency limits, memory behavior, and webhook flows.
- [**Development & Setup**](docs/development.md): How to build PrintHop from source.

## 🛠️ Tech Stack
- **Backend:** C# / .NET Framework 4.8 (Custom HttpListener & Queue Management)
- **Frontend:** HTML5, Vanilla JavaScript, Vanilla CSS (Zero dependencies, Custom Glassmorphism UI)
- **Networking:** TCP (Web UI/API), UDP (Peer Discovery)

## 🤝 Contributing

Contributions, issues, and feature requests are highly welcome! 
Please ensure that any pull requests adhere strictly to the **2GB memory constraint**, maintain the zero-cloud philosophy, and do not introduce heavy external dependencies.

## 📄 License

This project is licensed under the [MIT License](LICENSE).
