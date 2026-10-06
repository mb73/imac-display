@echo off
rem Builds imac-display.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Needs no admin rights and no Visual Studio.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "EXE=%~dp0..\imac-display.exe"
set "INFO=%TEMP%\imac-display-assemblyinfo.cs"
if not exist "%CSC%" (
    echo csc.exe nicht gefunden: %CSC%
    exit /b 1
)
rem Name and version for Explorer and the taskbar, taken from VERSION.
set "VERSION=0.0.0"
set /p VERSION=<"%~dp0..\VERSION"
>"%INFO%" echo [assembly: System.Reflection.AssemblyTitle("iMac-Display")]
>>"%INFO%" echo [assembly: System.Reflection.AssemblyProduct("iMac-Display")]
>>"%INFO%" echo [assembly: System.Reflection.AssemblyFileVersion("%VERSION%")]
>>"%INFO%" echo [assembly: System.Reflection.AssemblyInformationalVersion("%VERSION%")]
rem A running exe cannot be overwritten, but it can be renamed; the running instance keeps working.
if exist "%EXE%.old" del /q "%EXE%.old" 2>nul
if exist "%EXE%" move /y "%EXE%" "%EXE%.old" >nul
"%CSC%" /nologo /codepage:65001 /optimize+ /platform:x64 /target:exe /win32icon:"%~dp0imac-display.ico" ^
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
    /out:"%EXE%" "%~dp0src\*.cs" "%INFO%"
if errorlevel 1 (
    if not exist "%EXE%" if exist "%EXE%.old" move /y "%EXE%.old" "%EXE%" >nul
    exit /b 1
)
del /q "%INFO%" 2>nul
echo Fertig: %EXE% (%VERSION%)
