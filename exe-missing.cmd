@echo off
rem Double-click when imac-display.exe is missing, usually because a virus scanner removed it as a
rem false positive: builds it from the sources in this folder and starts it. If it is there after all,
rem it just starts it.
setlocal
set "EXE=%~dp0imac-display.exe"
if exist "%EXE%" (
    start "" "%EXE%"
    exit /b 0
)
echo imac-display.exe fehlt. Meist hat ein Virenscanner sie zu Unrecht als Schadsoftware entfernt.
echo Sie kann aus dem Quelltext in diesem Ordner neu gebaut werden. Auf einem Firmenrechner bitte
echo vorher mit der IT abstimmen.
echo.
choice /c JN /m "Jetzt bauen und starten"
if errorlevel 2 exit /b 1
call "%~dp0windows\build.cmd"
if errorlevel 1 goto failed
if not exist "%EXE%" goto removed
start "" "%EXE%"
exit /b 0

:removed
echo Der Virenscanner hat auch die neu gebaute exe entfernt.
:failed
echo Mehr dazu in README.md unter "Wenn etwas nicht klappt".
pause
exit /b 1
