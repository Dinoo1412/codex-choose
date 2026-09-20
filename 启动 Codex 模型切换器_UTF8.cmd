@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0CodexModelSwitcher_UTF8.ps1"
if errorlevel 1 (
  echo.
  echo Codex Model Switcher exited with an error.
  pause
)
endlocal
