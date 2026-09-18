@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-NOVRCameraBindings.ps1" %*
pause
