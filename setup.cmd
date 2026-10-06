@echo off
rem One-time setup on the laptop: downloads ffmpeg into tools\ (no admin rights needed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows\setup.ps1"
echo.
pause
