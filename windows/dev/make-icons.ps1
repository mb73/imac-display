<#
.SYNOPSIS
  Draws the program icon (a laptop on a big monitor) and writes windows\imac-display.ico for the
  Windows agent and mac\AppIcon.icns for LaptopScreen. Both files are checked in; run this again
  only after changing the design.

.NOTES
  Uses System.Drawing only. Both containers hold PNG images: ICO since Windows Vista, ICNS since
  OS X 10.7. The macOS variant keeps the transparent margin that macOS icons have around their tile.
#>
param(
    [string]$Root = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single](2 * $r)
    $path.AddArc([single]$x, [single]$y, $d, $d, 180, 90)
    $path.AddArc([single]($x + $w - 2 * $r), [single]$y, $d, $d, 270, 90)
    $path.AddArc([single]($x + $w - 2 * $r), [single]($y + $h - 2 * $r), $d, $d, 0, 90)
    $path.AddArc([single]$x, [single]($y + $h - 2 * $r), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

<# Renders one PNG; coordinates below are relative to the tile (0..1), $inset is the margin around it #>
function New-IconPng([int]$size, [double]$inset) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size * (1 - 2 * $inset)
    $o = $size * $inset
    $rect = { param($x, $y, $w, $h, $r) New-RoundedPath ($o + $x * $s) ($o + $y * $s) ($w * $s) ($h * $s) ($r * $s) }
    $points = { param([double[]]$xy) for ($i = 0; $i -lt $xy.Length; $i += 2) { New-Object System.Drawing.PointF([single]($o + $xy[$i] * $s), [single]($o + $xy[$i + 1] * $s)) } }
    $color = { param($hex) [System.Drawing.ColorTranslator]::FromHtml($hex) }

    <# tile: blue gradient #>
    $tile = & $rect 0 0 1 1 0.2237
    $top = New-Object System.Drawing.PointF([single]$o, [single]$o)
    $bottom = New-Object System.Drawing.PointF([single]$o, [single]($o + $s))
    $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($top, $bottom, (& $color '#4AA3FF'), (& $color '#1D4ED8'))
    $tileBrush.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY  # no seam of the start color at the bottom edge
    $g.FillPath($tileBrush, $tile)

    <# monitor: white body with a chin, dark screen, stand #>
    $white = New-Object System.Drawing.SolidBrush((& $color '#FFFFFF'))
    $silver = New-Object System.Drawing.SolidBrush((& $color '#D7DEE8'))
    $g.FillPolygon($silver, [System.Drawing.PointF[]](& $points @(0.445, 0.655, 0.555, 0.655, 0.585, 0.765, 0.415, 0.765)))
    $g.FillPath($white, (& $rect 0.33 0.755 0.34 0.045 0.0225))
    $g.FillPath($white, (& $rect 0.13 0.18 0.74 0.49 0.05))
    $screenBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($top, $bottom, (& $color '#1E3A8A'), (& $color '#0B1530'))
    $screenBrush.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    $g.FillPath($screenBrush, (& $rect 0.17 0.22 0.66 0.37 0.022))

    <# laptop on the screen: lid and base #>
    $laptop = New-Object System.Drawing.SolidBrush((& $color '#7CC0FF'))
    $g.FillPath($laptop, (& $rect 0.37 0.29 0.26 0.17 0.018))
    $g.FillPath($screenBrush, (& $rect 0.39 0.31 0.22 0.13 0.008))
    $g.FillPolygon($laptop, [System.Drawing.PointF[]](& $points @(0.33, 0.475, 0.67, 0.475, 0.70, 0.51, 0.30, 0.51)))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    foreach ($b in $tileBrush, $white, $silver, $screenBrush, $laptop) { $b.Dispose() }
    return , $ms.ToArray()
}

function Write-BigEndian([System.IO.BinaryWriter]$w, [uint32]$value) {
    $bytes = [BitConverter]::GetBytes($value)
    [Array]::Reverse($bytes)
    $w.Write($bytes)
}

<# Windows: ICONDIR, one ICONDIRENTRY per size, then the PNG data #>
$icoSizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $icoSizes) { , (New-IconPng $size 0.02) }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
for ($i = 0; $i -lt $icoSizes.Count; $i++) {
    $dim = if ($icoSizes[$i] -ge 256) { 0 } else { $icoSizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()
$ico = Join-Path $Root 'windows\imac-display.ico'
[System.IO.File]::WriteAllBytes($ico, $out.ToArray())
Write-Host "Wrote $ico"

<# macOS: 'icns' header, then (type, length, PNG) entries; lengths are big-endian and include the 8-byte header #>
$icnsTypes = [ordered]@{ icp4 = 16; ic11 = 32; icp5 = 32; ic12 = 64; ic07 = 128; ic13 = 256; ic08 = 256; ic14 = 512; ic09 = 512; ic10 = 1024 }
$cache = @{}
$entries = New-Object System.IO.MemoryStream
$ew = New-Object System.IO.BinaryWriter($entries)
foreach ($type in $icnsTypes.Keys) {
    $size = $icnsTypes[$type]
    if (-not $cache.ContainsKey($size)) { $cache[$size] = New-IconPng $size (100 / 1024) }
    $png = $cache[$size]
    $ew.Write([System.Text.Encoding]::ASCII.GetBytes($type))
    Write-BigEndian $ew ([uint32]($png.Length + 8))
    $ew.Write($png)
}
$ew.Flush()
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([System.Text.Encoding]::ASCII.GetBytes('icns'))
Write-BigEndian $w ([uint32]($entries.Length + 8))
$w.Write($entries.ToArray())
$w.Flush()
$icns = Join-Path $Root 'mac\AppIcon.icns'
[System.IO.File]::WriteAllBytes($icns, $out.ToArray())
Write-Host "Wrote $icns"

<# preview for a quick look at the design #>
$preview = Join-Path ([System.IO.Path]::GetTempPath()) 'imac-display-icon.png'
[System.IO.File]::WriteAllBytes($preview, (New-IconPng 512 (100 / 1024)))
Write-Host "Preview: $preview"
