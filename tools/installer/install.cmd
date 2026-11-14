@echo off
rem Station Countdown installer. Double-click to run install.ps1.
chcp 65001 > nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. See the message above.
)
echo.
pause
