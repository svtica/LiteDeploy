@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0winget-upgrade.ps1"
exit /b %ERRORLEVEL%
