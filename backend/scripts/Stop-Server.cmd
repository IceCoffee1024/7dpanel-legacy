@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Stop-Server.ps1" %*
exit /b %ERRORLEVEL%
