@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Mod.ps1" %*
exit /b %ERRORLEVEL%
