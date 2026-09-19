@echo off
:: Batch file to open Port 4222 for PrintHop in Windows Defender Firewall
echo ========================================================
echo   Configuring Windows Firewall for PrintHop (Port 4222)
echo ========================================================
echo.

:: Add Inbound TCP rule for Web UI and printing
netsh advfirewall firewall delete rule name="PrintHop TCP 4222" >nul 2>&1
netsh advfirewall firewall add rule name="PrintHop TCP 4222" dir=in action=allow protocol=TCP localport=4222 profile=any

:: Add Inbound UDP rule for automatic peer discovery
netsh advfirewall firewall delete rule name="PrintHop UDP 4222" >nul 2>&1
netsh advfirewall firewall add rule name="PrintHop UDP 4222" dir=in action=allow protocol=UDP localport=4222 profile=any

echo.
if %errorlevel% equ 0 (
    echo [SUCCESS] Port 4222 has been opened in Windows Firewall!
    echo Mobile phones and other PCs on your network can now connect immediately.
) else (
    echo [ERROR] Failed to set firewall rules.
    echo Please ensure you right-clicked this file and selected 'Run as administrator'.
)
echo.
pause
