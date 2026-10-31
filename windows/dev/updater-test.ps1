<#
.SYNOPSIS
  Tests Updater.cs end to end. Builds a test harness from windows\src\*.cs plus updater-test\UpdaterTest.cs,
  copies it as imac-display.exe into fake installations of a release and lets each copy update itself from
  test zips: a GitHub-like and a flat zip, the restart, locked and read-only files, a .old that still runs,
  and a simulated virus scanner that deletes or holds the fresh exe. Prints one line per check.

.NOTES
  Safe while iMac-Display runs: nothing touches displays, settings, the pairing code, the real log or the
  Downloads folder; one check reads VERSION from GitHub and is skipped when offline. The test zips carry a
  tiny probe exe (updater-test\Probe.cs), never a real imac-display.exe: a virus scanner that flags one would
  report it as a finding on this computer. Everything happens in %TEMP%\imac-display-updater-test, which is
  removed when all checks pass and kept for inspection otherwise. Takes about 10 s and needs ~250 MB meanwhile.
  Exit code 0 if all checks pass. Not covered: Updater.DownloadRelease (it fetches main from GitHub) and the
  window's messages.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$here = Join-Path $PSScriptRoot 'updater-test'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
$temp = Join-Path ([System.IO.Path]::GetTempPath()) 'imac-display-updater-test'
$bin = Join-Path $temp 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

# compiled like windows\build.cmd: target framework 4.8 (TLS 1.2 by default), the same manifest and references
$info = Join-Path $bin 'assemblyinfo.cs'
[System.IO.File]::WriteAllText($info, '[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]')
& $csc /nologo /codepage:65001 /optimize+ /platform:x64 /target:winexe "/out:$bin\probe.exe" "$here\Probe.cs" $info
if ($LASTEXITCODE -ne 0) { throw 'building probe.exe failed' }
$sources = @(Get-ChildItem (Join-Path $root 'windows\src') -Filter *.cs | ForEach-Object { $_.FullName })
& $csc /nologo /codepage:65001 /optimize+ /platform:x64 /target:exe "/win32manifest:$root\windows\imac-display.manifest" `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /main:ImacDisplay.UpdaterTest `
    "/out:$bin\harness.exe" @sources "$here\UpdaterTest.cs" $info
if ($LASTEXITCODE -ne 0) { throw 'building harness.exe failed' }

# the harness writes UTF-8
$encoding = [Console]::OutputEncoding
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    & "$bin\harness.exe" driver (Join-Path $temp 'work')
    $result = $LASTEXITCODE
}
finally {
    [Console]::OutputEncoding = $encoding
}
if ($result -eq 0) {
    Remove-Item -Recurse -Force $temp
    "All checks passed; removed $temp"
}
else {
    "Kept for inspection: $temp"
}
exit $result
