<#
.SYNOPSIS
  Development helper: packs the Mac sources and pushes them to the Mac, where mpv dumps them to a file
  (the macOS firewall lets mpv through, nc not). Start this on the Mac first:

    cd ~/LaptopScreen && /Applications/mpv.app/Contents/MacOS/mpv --no-config --stream-dump=laptopscreen.tgz "tcp://0.0.0.0:50030?listen" && tar -xzf laptopscreen.tgz && sh build.sh && { pkill -x LaptopScreen; sleep 1; open LaptopScreen.app; }

.NOTES
  mpv accepts exactly one connection, so there is no probe: every connection attempt is the transfer,
  repeated once a second until mpv listens. Without a VERSION file next to ~/LaptopScreen the build
  reports version 0.0.0, so LaptopScreen offers the laptop's version right away - handy for testing
  the self-update; otherwise just decline the offer.
#>
param(
    [string]$Mac,               # address of the Mac; default: the first one imac-display.exe --test finds (cable first)
    [int]$Port = 50030,
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $Mac) {
    $found = & (Join-Path $root 'imac-display.exe') --test | Select-String '^\s+.+ \((\d+\.\d+\.\d+\.\d+):\d+\)$' | Select-Object -First 1
    if (-not $found) { throw 'No Mac found via Bonjour (is LaptopScreen running?). Pass -Mac <address>.' }
    $Mac = $found.Matches[0].Groups[1].Value
}

$archive = Join-Path ([System.IO.Path]::GetTempPath()) 'laptopscreen.tgz'
& tar.exe -czf $archive -C (Join-Path $root 'mac') build.sh install.sh Info.plist AppIcon.icns Sources
if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
$bytes = [System.IO.File]::ReadAllBytes($archive)
Write-Host "Packed $($bytes.Length) bytes; waiting for mpv on ${Mac}:$Port ..."

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $pending = $client.BeginConnect($Mac, $Port, $null, $null)
        if ($pending.AsyncWaitHandle.WaitOne(1000) -and $client.Connected) {
            $client.EndConnect($pending)
            $stream = $client.GetStream()
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
            $client.Client.Shutdown([System.Net.Sockets.SocketShutdown]::Send)
            Start-Sleep -Milliseconds 500
            Write-Host 'Sent. The Mac now unpacks, builds and restarts LaptopScreen.'
            return
        }
    } catch {
        # mpv is not listening yet (PowerShell wraps the SocketException, so catch everything here)
    } finally {
        $client.Close()
    }
    Start-Sleep -Seconds 1
}
throw "mpv did not listen on ${Mac}:$Port within $TimeoutMinutes minutes."
