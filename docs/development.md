# Development & Setup

This guide covers how to set up the PrintHop project for local development and highlights known limitations for future contributors.

## 🛠️ Environment Setup

PrintHop is built on the **.NET Framework 4.8** (not .NET Core / .NET 5+). 

### Prerequisites
1. **Visual Studio 2022** (or Visual Studio 2019).
2. The **.NET Framework 4.8 Targeting Pack**. Make sure this is selected in your Visual Studio Installer workloads.

### Building from Source
1. Clone the repository.
2. Open `PrintHop.sln` in Visual Studio.
3. Build the solution (Ctrl + Shift + B).
4. *Important*: If you are testing the firewall functionality (`FirewallService.cs`), you must run Visual Studio as an Administrator. Otherwise, the `netsh` process calls will fail with an Access Denied error.

## 🚧 Known Limitations & TODOs

If you're looking to contribute to PrintHop, these are the areas that currently require the most attention.

### 1. Security Enhancements
- **mTLS / Encryption**: Currently, LAN traffic is unencrypted HTTP. Adding TLS (HTTPS) with self-signed certificates or Mutual TLS would fully secure the webhook notification system and payload transfers against local LAN sniffers.
- **Port Flexibility**: The application currently hardcodes UDP `4221` and TCP `4222`. Future iterations should allow port customization via a config file, gracefully handling port-in-use conflicts.

### 2. Performance & Memory Profiling
- **Unverified Rasterization Limits**: The ingestion phase memory is successfully constrained to an 80KB buffer. However, the GDI+ rasterization phase in `PrintService.cs` has not been empirically profiled on a live 2GB hardware cap. We need hard data (Working Set RSS snapshots) demonstrating the exact memory curve of complex 300MB+ document renders. 

### 3. Client UI Enhancements
- **Error Handling Details**: The Vanilla JS `app.js` handles broad HTTP error codes, but deeper error details (like why a specific image format failed GDI+ validation) could be passed cleanly to the UI.
- **Componentization**: The UI is currently a monolithic HTML/JS bundle. Moving to a lightweight framework (like Preact or standard Web Components) could improve maintainability.
