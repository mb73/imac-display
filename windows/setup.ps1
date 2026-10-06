<#
.SYNOPSIS
  Downloads the pinned portable ffmpeg build into tools\ and verifies its SHA-256.
  Needs no admin rights. Called by setup.cmd in the project root.

.NOTES
  Source: the gyan.dev "essentials" build, mirrored on GitHub (GyanD/codexffmpeg), because
  gyan.dev itself delivers only a few hundred KB/s. The checksum is the one gyan.dev publishes.
  To update: change $Version and $Sha256 together.
#>
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$Version = '9.0.2'
$Sha256 = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$Name = "ffmpeg-$Version-essentials_build"
$Url = "https://github.com/GyanD/codexffmpeg/releases/download/$Version/$Name.zip"

$tools = Join-Path $Root 'tools'
$target = Join-Path $tools $Name
if (Test-Path (Join-Path $target 'bin\ffmpeg.exe')) {
    Write-Host "ffmpeg $Version ist schon vorhanden: $target"
    return
}

New-Item -ItemType Directory -Force $tools | Out-Null
$zip = Join-Path ([System.IO.Path]::GetTempPath()) "$Name.zip"
Write-Host "Lade ffmpeg $Version herunter (ca. 110 MB) …"
curl.exe -L --fail --progress-bar -o $zip $Url
if ($LASTEXITCODE -ne 0) { throw "Download fehlgeschlagen: $Url" }

$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $Sha256) {
    [System.IO.File]::Delete($zip)
    throw "Prüfsumme stimmt nicht: erwartet $Sha256, erhalten $actual. Datei verworfen."
}

Write-Host 'Prüfsumme stimmt, entpacke …'
Expand-Archive -Path $zip -DestinationPath $tools -Force
[System.IO.File]::Delete($zip)
Write-Host "Fertig: $(Join-Path $target 'bin\ffmpeg.exe')"
