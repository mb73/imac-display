@echo off
rem Updates imac-display from a newer release zip: from the Downloads folder, from a zip dropped
rem onto this file, or downloaded in the browser. Settings, pairing code and ffmpeg stay.
rem Everything in one line: the update replaces this very file while it runs.
"%~dp0imac-display.exe" --update %* & exit /b
