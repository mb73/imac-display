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
rem Name and version for Explorer and the taskbar, taken from VERSION. The target framework switches on
rem what .NET Framework only does for apps built for 4.7 or newer: per-monitor DPI in WinForms, TLS 1.2.
set "VERSION=0.0.0"
set /p VERSION=<"%~dp0..\VERSION"
>"%INFO%" echo [assembly: System.Reflection.AssemblyTitle("iMac-Display")]
>>"%INFO%" echo [assembly: System.Reflection.AssemblyProduct("iMac-Display")]
>>"%INFO%" echo [assembly: System.Reflection.AssemblyFileVersion("%VERSION%")]
>>"%INFO%" echo [assembly: System.Reflection.AssemblyInformationalVersion("%VERSION%")]
>>"%INFO%" echo [assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
rem A running exe cannot be overwritten, but it can be renamed; the running instance keeps working.
if exist "%EXE%.old" del /q "%EXE%.old" 2>nul
if exist "%EXE%" move /y "%EXE%" "%EXE%.old" >nul
rem A window program (winexe); the manifest declares Windows 10, imac-display.exe.config next to the exe
rem switches on per-monitor DPI, and the icon goes in twice: for Explorer and for the windows.
"%CSC%" /nologo /codepage:65001 /optimize+ /platform:x64 /target:winexe /win32icon:"%~dp0imac-display.ico" ^
    /win32manifest:"%~dp0imac-display.manifest" /resource:"%~dp0imac-display.ico",imac-display.ico ^
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
    /out:"%EXE%" "%~dp0src\*.cs" "%INFO%"
if errorlevel 1 (
    if not exist "%EXE%" if exist "%EXE%.old" move /y "%EXE%.old" "%EXE%" >nul
    exit /b 1
)
del /q "%INFO%" 2>nul
echo Fertig: %EXE% (%VERSION%)
