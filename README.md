<div align="center">
  <img src="https://via.placeholder.com/150/0f172a/ffffff?text=PrintHop" alt="PrintHop Logo">
  <h1>PrintHop</h1>
  <p><b>Driverless, Zero-Cloud Printer-Sharing for Windows LANs</b></p>
  <p>
    <img src="https://img.shields.io/badge/.NET-4.8-512BD4?style=flat-square" alt=".NET 4.8">
    <img src="https://img.shields.io/badge/Windows-7%20|%2010%20|%2011-0078D6?style=flat-square" alt="Windows Support">
    <img src="https://img.shields.io/badge/Status-Active-success?style=flat-square" alt="Status">
    <img src="https://img.shields.io/badge/Memory_Footprint-2GB_RAM_Target-orange?style=flat-square" alt="Memory">
  </p>
</div>

PrintHop is a lightweight, zero-cloud Windows utility designed to share local printers across a Local Area Network (LAN). Built specifically for small businesses operating on mixed hardware (including legacy machines capped at 2GB RAM), PrintHop eliminates the need for complex driver installations, Active Directory setups, or external cloud subscriptions.

If your device is on the network, it can print.

## ✨ Core Capabilities

- **Zero-Cloud & Fully Offline**: All data stays on your local network. No accounts, no external telemetry, no internet required.
- **Driverless Printing via Web UI**: Users don't need to install printer drivers. They simply navigate to the host's IP address (e.g., `http://192.168.1.50:4222`) in any modern browser (desktop or mobile) and drop their documents.
- **Memory-Efficient Queue System**: Built for low-end hardware. Uploads are streamed directly to disk (bypassing RAM), and print rendering is handled strictly sequentially.
- **Job Control & Auditing**: Real-time queue monitoring, manual job blocking, and asynchronous webhook notifications back to the sender.
- **Auto-Discovery**: Automatic UDP broadcast detection of other PrintHop peers on the LAN.

## 🖥️ Platform Support & Requirements

| OS | Required Runtime | Min RAM | Permissions |
|----|-----------------|---------|-------------|
| Windows 7 SP1 | .NET Framework 4.8 | 2 GB* | UAC Admin (first run) |
| Windows 10 | .NET Framework 4.8 | 2 GB* | UAC Admin (first run) |
| Windows 11 | .NET Framework 4.8 | 2 GB* | UAC Admin (first run) |

*\*2GB RAM is a design target. Due to GDI+ rasterization spikes, complex images may temporarily stress low-memory bounds. See [Architecture](docs/architecture.md) for details.*

### Mobile Behavior
There is **no native mobile app**. Mobile users (iOS/Android) access PrintHop exactly like desktop users: by connecting to the same Wi-Fi network and navigating to the Host's IP address in Safari or Chrome. The Web UI is fully responsive and behaves like a progressive web app.

## 🚀 Installation & Usage

1. **Download**: Grab the latest `PrintHop.exe` from the Releases page.
2. **Run as Administrator (First Time Only)**: PrintHop uses Windows `netsh` to automatically configure Firewall exceptions for port `4221` (UDP) and `4222` (TCP). You will be prompted for UAC elevation.
3. **Connect**: 
   - On the host, right-click the PrintHop tray icon and select "Open Allocation Interface".
   - On peer devices, navigate to `http://<HOST-IP>:4222` in any browser.
4. **Print**: Drag and drop a supported document (PDF, DOCX, PNG, etc.) into the Web UI.

## 🔒 Security Model

PrintHop is designed for **Trusted LAN environments**.
- **No Remote Attack Surface**: It does not punch holes in your router or communicate over the internet.
- **Host Whitelisting**: Devices must be approved by the Host to submit print jobs.
- **Webhook Limits**: Internal LAN notifications rely on unforgeable TCP Source IPs and 128-bit GUID matching.
- *Known Limitation*: Traffic is currently unencrypted (HTTP). Active network attackers (MITM) on your local LAN could theoretically intercept traffic. See [Architecture](docs/architecture.md) for full threat model constraints.

## 📚 Documentation

For deep-dives into how PrintHop works under the hood, check the `/docs` folder:
- [**Architecture & Queue System**](docs/architecture.md): Concurrency limits, memory behavior, and webhook flows.
- [**Development & Setup**](docs/development.md): How to build PrintHop from source and known TODOs.

## 🤝 Contributing

Contributions are welcome! Please ensure that any pull requests adhere to the strict 2GB memory constraint and maintain the zero-cloud philosophy.

## 📄 License

[MIT License](LICENSE)
