<#
.SYNOPSIS
  Measures the sender's latency on the laptop alone: a millisecond clock is shown, ffmpeg captures
  the screen with the same pipeline as imac-display.exe and sends it over loopback TCP to ffplay.
  Screenshots show the live clock next to the clock seen through the pipeline; the difference
  between the first two readings is the latency of one pass (sender plus a software decoder).

.NOTES
  Run it while only the laptop panel is active: the clock opens on the primary display.
  Screenshots land in %TEMP%\imac-display-latency. Measured on 2026-10-05: ~155 ms with the
  vpp_qsv default async_depth=4, ~105 ms with async_depth=1.
#>
param(
    [int]$OutputIndex = 0,
    [string]$SenderArgs = '-scenario displayremoting -low_delay_brc 1',
    [string]$PlayerArgs = '-threads 1 -vf setpts=0',
    [string]$Tag = 'run'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ffmpeg = Get-ChildItem (Join-Path $root 'tools') -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $ffmpeg) { throw 'ffmpeg not found below tools\ (run setup.cmd first)' }
$bin = $ffmpeg.DirectoryName
$out = Join-Path ([System.IO.Path]::GetTempPath()) 'imac-display-latency'
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Name Dpi2 -Namespace Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][Native.Dpi2]::SetProcessDPIAware()

$clock = Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'clock.ps1'), '-Seconds', '25')
Start-Sleep -Seconds 1

$play = Start-Process "$bin\ffplay.exe" -PassThru -NoNewWindow -RedirectStandardError "$out\ffplay-$Tag.log" -ArgumentList (@(
    '-hide_banner', '-loglevel', 'warning', '-fflags', 'nobuffer', '-flags', 'low_delay', '-framedrop',
    '-probesize', '32', '-analyzeduration', '0', '-sync', 'ext', '-left', '40', '-top', '300',
    '-x', '960', '-y', '600', '-noborder', '-autoexit', '-window_title', 'latencyprobe') +
    ($PlayerArgs -split ' ' | Where-Object { $_ }) + @('"tcp://127.0.0.1:47199?listen"'))
Start-Sleep -Seconds 1

$filter = "ddagrab=output_idx=$($OutputIndex):framerate=60:draw_mouse=1,hwmap=derive_device=qsv,format=qsv,vpp_qsv=format=nv12:async_depth=1[v]"
$send = Start-Process "$bin\ffmpeg.exe" -PassThru -WindowStyle Hidden -RedirectStandardError "$out\sender-$Tag.log" -ArgumentList (@(
    '-hide_banner', '-nostdin', '-loglevel', 'warning',
    '-init_hw_device', 'd3d11va=dx', '-init_hw_device', 'qsv=qs@dx', '-filter_hw_device', 'dx',
    '-filter_complex', "`"$filter`"", '-map', '"[v]"',
    '-c:v', 'h264_qsv', '-profile:v', 'high', '-low_power', '1', '-async_depth', '1', '-bf', '0', '-g', '60',
    '-b:v', '30M', '-maxrate', '30M', '-bufsize', '16M') + ($SenderArgs -split ' ' | Where-Object { $_ }) + @(
    '-flush_packets', '1', '-t', '14', '-f', 'mpegts', 'tcp://127.0.0.1:47199'))

Start-Sleep -Seconds 8
$area = New-Object System.Drawing.Rectangle(0, 0, 1100, 950)
foreach ($i in 1..3) {
    $bmp = New-Object System.Drawing.Bitmap $area.Width, $area.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($area.Left, $area.Top, 0, 0, $bmp.Size)
    $bmp.Save("$out\latency-$Tag-$i.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    Start-Sleep -Milliseconds 700
}

$send.WaitForExit(15000) | Out-Null
$play.WaitForExit(5000) | Out-Null
foreach ($p in $send, $play, $clock) { if (-not $p.HasExited) { $p.Kill() } }
"screenshots in ${out}: " + ((Get-ChildItem $out -Filter "latency-$Tag-*.png").Name -join ', ')
