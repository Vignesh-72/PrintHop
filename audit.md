# PrintHop Phase 1 Audit

## Project Overview
PrintHop is an open-source, zero-configuration printer-sharing tool for local networks of Windows machines. Any machine with a local printer attached can share it, and any other machine on the LAN can discover that printer and dispatch documents.

This audit covers **Phase 1**, which was implemented entirely in C# (.NET Framework 4.8) as a standalone tray agent with a vanilla static Web UI, following the original design spec.

## Work Completed

### 1. Project Initialization
- Created a modern, SDK-style `PrintHop.csproj` configured for .NET 4.8 `WinExe`.
- Setup the Visual Studio solution (`PrintHop.sln`).
- Initialized a clear directory structure separating Models, Services, and `www/` static assets.

### 2. Core Models (`PrintHop/Models/`)
- `Peer.cs`: Represents a discovered PrintHop peer on the network.
- `PrintJobOptions.cs`: Data structure for print options (Copies, Duplex, Color).
- `AnnouncePacket.cs`: The UDP broadcast packet format.

### 3. Core Services (`PrintHop/Services/`)
- `IPrintService.cs`: Abstracted printing interface to decouple logic from transport layers.
- `PrintService.cs`: 
  - Retrieves local printers using `PrinterSettings.InstalledPrinters`.
  - Image printing (`.png`, `.jpg`, `.bmp`) is done directly through `System.Drawing.Printing.PrintDocument` via GDI+.
  - Document printing (`.pdf`, `.docx`, `.xlsx`) uses Windows native `ShellExecute` with the `printto` verb, which relies on the host operating system's default application handlers, launching them silently in the background.

### 4. Network Services (`PrintHop/Services/`)
- `UdpDiscovery.cs`: Uses `UdpClient` to broadcast on port `4223` every 10s (with ±1.5s jitter) and listens for peers. Maintains a thread-safe list of active peers with a 30-second TTL.
- `HttpServer.cs`: 
  - Lightweight, custom `HttpListener` binding to `http://localhost:4222/` (and local LAN IP). It safely increments the port if `4222` is occupied.
  - Implements API endpoints: `GET /api/self`, `GET /api/peers`.
  - Implements `POST /api/receive-print` parsing multipart data, safely validating file magic bytes, and saving to `%TEMP%\PrintHop\job_<guid>.tmp`.
  - Serves static files directly from the `www/` folder.

### 5. Application Entry Point (`PrintHop/`)
- `Program.cs`: Enforces a single-instance lock using a global `Mutex` (`Global\PrintHop_SingleInstance`).
- `TrayAppContext.cs`: Inherits `ApplicationContext` to run PrintHop silently in the system tray (`NotifyIcon`). Manages the startup and graceful shutdown of HTTP and UDP services. It handles the security whitelist via `MessageBox` prompts on the UI thread when new machines send jobs, saving approvals to `whitelist.json`.

### 6. Web Frontend (`PrintHop/www/`)
- Built using vanilla HTML5, CSS3, and ES5 JS (No bundlers).
- `index.html`: Clean, accessible interface.
- `style.css`: Uses CSS custom variables, smooth transitions, flex/grid layouts, and elegant HSL color mapping for a premium look.
- `app.js`: Fetches the printer list, polls for updates, handles drag-and-drop file uploads, and dispatches the payload via `FormData` to the target machine.

### 7. Compilation Fixes
- Addressed C# 6.0 syntax usage (like `nameof` and string interpolation `$"..."`) and downgraded them to C# 5.0 syntax (`string.Format()`) so that the project successfully compiles with the legacy `csc.exe` bundled natively in Windows (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).

## Developer Notes for IDE Import
- **IDE Choice:** This project can be opened smoothly in Visual Studio 2022 by opening `PrintHop.sln`.
- **Target Framework:** It targets `.NET Framework 4.8`. Make sure this targeting pack is installed in your Visual Studio components.
- **No NuGet:** The project strictly relies on standard BCL references (e.g., `System.Web.Extensions` for JSON parsing). Do not run `dotnet restore`.
- **Debugging:** You can build and run `PrintHop.exe`. It will appear in your system tray. Double-click the tray icon to launch `http://localhost:4222` in your default browser.
- **Compiled Binary:** A compiled `PrintHop.exe` is currently sitting in the root directory for convenience, but the output directory defined in the `.csproj` will be standard `bin\Debug\net48\`. When building from the IDE, remember that the `www` folder will be copied automatically to the output directory.

## Repository Status
- All source code and static assets are fully written, compiled successfully locally, and committed to the `main` branch.
- The project folder has been successfully relocated to `C:\Users\Admin\Documents\PrintHop`.
