$ErrorActionPreference = "Stop"
$InstallPath = "$env:ProgramFiles\PrintHop"

Write-Host "Installing PrintHop to $InstallPath..."

if (!(Test-Path $InstallPath)) {
    New-Item -Path $InstallPath -ItemType Directory -Force | Out-Null
}

Copy-Item -Path "$PSScriptRoot\PrintHop.exe" -Destination $InstallPath -Force
if (Test-Path "$PSScriptRoot\www") {
    Copy-Item -Path "$PSScriptRoot\www" -Destination $InstallPath -Recurse -Force
}
if (Test-Path "$PSScriptRoot\setup_firewall.bat") {
    Copy-Item -Path "$PSScriptRoot\setup_firewall.bat" -Destination $InstallPath -Force
}

Write-Host "Creating Desktop Shortcut..."
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut([Environment]::GetFolderPath("Desktop") + "\PrintHop.lnk")
$Shortcut.TargetPath = "$InstallPath\PrintHop.exe"
$Shortcut.WorkingDirectory = $InstallPath
$Shortcut.Save()

Write-Host "Setting up Firewall Rules..."
Start-Process -FilePath "$InstallPath\setup_firewall.bat" -Verb RunAs -Wait -WindowStyle Hidden

Write-Host "Installation Complete! You can now start PrintHop from your Desktop."
Read-Host -Prompt "Press Enter to exit"
