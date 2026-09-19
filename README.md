# PrintHop

PrintHop is a sleek, modern, and minimalistic application designed to allow local network devices (like mobile phones) to seamlessly send print jobs to a host PC's connected printers. With a built-in HTTP server and a responsive web interface, PrintHop makes mobile printing as easy as scanning a QR code.

## Features

- **Responsive Web Interface**: A clean, minimalistic, and modern UI that adapts flawlessly to mobile and desktop screens.
- **Local Network Printing**: Allows any device on your local network to send print requests to the host PC.
- **QR Code Connectivity**: Easily connect mobile devices by scanning a dynamically generated QR code directly from the host PC.
- **System & Network Printer Separation**: Clearly distinguishes between your PC's local system printers and network printers.
- **Activity & Forensic Logging**: Monitors all print requests and responses in real-time. Gives you the ability to view logs, identify who sent what, and optionally block unwanted devices from printing.
- **Zero Configuration**: A one-click PowerShell installer ensures everything (including firewall rules) is set up automatically.

## How to Use

1. **Installation**
   - Download the latest `PrintHop-Release.zip` from the Releases section.
   - Extract the ZIP file.
   - Right-click `install.ps1` and select **Run with PowerShell**.
   - The installer will automatically place the files in `C:\Program Files\PrintHop`, configure Windows Firewall to allow local network traffic on port `4222`, and create a Desktop shortcut.

2. **Starting the Application**
   - Double-click the **PrintHop** shortcut on your Desktop.
   - The application will run in the background (check your system tray) and host the local web server on port `4222`.

3. **Connecting a Mobile Device**
   - Open your web browser on the PC and navigate to `http://localhost:4222`.
   - Click on the **QR Code icon** to display the connection QR code.
   - Scan the QR code using your mobile phone's camera to instantly open the PrintHop web interface on your phone.

4. **Printing a File**
   - On your mobile device, select the file (PDF, Image, etc.) you want to print.
   - Choose the target printer from the list (local system printers are clearly separated).
   - Tap **Print**. The file will be securely sent to your PC and processed by the selected printer.

5. **Monitoring & Logs**
   - Go to the **Activity Logs** section in the web UI.
   - Here you can monitor all incoming print requests, verify their status (Success/Failed), and see which device sent them.

## Developer Notes

### Architecture

PrintHop is built using C# for the backend and pure HTML/CSS/JS for the frontend.
- **Backend (C#)**: The application utilizes a lightweight, custom `HttpServer.cs` to serve static assets from the `www/` directory and handle API endpoints. The `PrintService.cs` interfaces with the Windows Print Spooler to execute print jobs.
- **Frontend**: A custom-built, modern UI with responsive CSS grid and flexbox. No heavy frameworks are used; pure Vanilla JS handles state, device detection, and API requests.

### Building from Source

1. Clone the repository: `git clone https://github.com/Vignesh-72/PrintHop.git`
2. Open `PrintHop.sln` in Visual Studio or use the .NET CLI.
3. Build the project: `dotnet build -c Release`
4. The frontend assets must be located in the `www/` directory next to the compiled `PrintHop.exe` executable for the web server to serve them correctly.

### Modifying the UI

- The primary UI files are located in `www/index.html`, `www/style.css`, and `www/app.js`.
- Make sure not to introduce any emojis into the UI, per design constraints.
- The UI dynamically adjusts its layout depending on whether it is viewed on a mobile device or a desktop. 

## License

This project is licensed under the MIT License.
