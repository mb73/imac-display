@echo off
rem Opens iMac-Display and looks for a newer version right away (GitHub, Downloads folder); a release
rem zip dropped onto this file is installed directly. Settings, pairing code and ffmpeg stay.
rem Everything in one line: the update replaces this very file while it runs.
start "" "%~dp0imac-display.exe" --update %* & exit /b
