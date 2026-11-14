@echo off
rem Station Countdown uninstaller. Double-click to run uninstall.ps1.
chcp 65001 > nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
echo.
pause
