[Setup]
; App Information
AppName=PrintHop (Official)
AppVersion=1.0.0
AppPublisher=Vignesh Saravanan
AppPublisherURL=https://github.com/Vignesh-72/PrintHop
AppSupportURL=https://github.com/Vignesh-72/PrintHop
AppUpdatesURL=https://github.com/Vignesh-72/PrintHop
LicenseFile=LICENSE.txt
InfoBeforeFile=GITHUB.txt

; Default Installation Folder
DefaultDirName={autopf}\PrintHop
DefaultGroupName=PrintHop

; Installer Output settings
OutputDir=.\Installer
OutputBaseFilename=PrintHop_Setup_Official
SetupIconFile=PrintHop\app.ico
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Privilege requirements
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copy everything from the publish directory
Source: "PrintHop\bin\Release\net48\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Copy the icon for the uninstaller and desktop shortcuts
Source: "PrintHop\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu shortcut
Name: "{group}\PrintHop"; Filename: "{app}\PrintHop.exe"; IconFilename: "{app}\app.ico"
; Desktop shortcut
Name: "{autodesktop}\PrintHop"; Filename: "{app}\PrintHop.exe"; Tasks: desktopicon; IconFilename: "{app}\app.ico"

[Run]
; Launch application after installation (run as original user, not elevated if possible, but UAC is needed first run)
Filename: "{app}\PrintHop.exe"; Description: "{cm:LaunchProgram,PrintHop}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Attempt to gracefully close the application before uninstalling
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM PrintHop.exe"; Flags: runhidden
