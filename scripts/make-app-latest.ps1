# Crea latest.json di un'app DockStudio dai file appena compilati in dist\ (installer e zip).
# Stesso formato di screenlove.it/latest.json (e dell'API di GitHub /releases/latest): l'app lo legge per gli
# aggiornamenti (DockStudio.Core.Updates.UpdateClient), il sito per versione, dimensioni e link.
#   .\scripts\make-app-latest.ps1 -App DockSpace -Version 0.1.0 [-NotesFile note.md]
# Scrive dist\<app>\latest.json e dist\SHA256SUMS-<versione>.txt. I link puntano a
# https://getdockstudio.it/<app>/download/<file>, dove li serve il sito (site/lib/downloads.js).
param(
    [Parameter(Mandatory)][string]$App,
    [Parameter(Mandatory)][string]$Version,
    [string]$NotesFile,
    [string]$Site = 'https://getdockstudio.it'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$slug = $App.ToLowerInvariant()
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versione non valida: '$Version'" }

$names = @("$App-Setup-$Version.exe", "$App-$Version-win-x64.zip", "$App-$Version-win-arm64.zip")
$files = $names | ForEach-Object { Join-Path $dist $_ } | Where-Object { Test-Path $_ }
if (-not ($files | Where-Object { $_ -like "*-Setup-*" })) { throw "Manca l'installer $App-Setup-$Version.exe in dist\" }

$assets = @()
$sums = @()
foreach ($f in $files) {
    $item = Get-Item $f
    $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant()
    $sums += "$hash  $($item.Name)"
    $assets += [pscustomobject][ordered]@{
        name = $item.Name
        size = $item.Length
        browser_download_url = "$Site/$slug/download/$($item.Name)"
        sha256 = $hash
    }
}

$notes = if ($NotesFile -and (Test-Path $NotesFile)) { (Get-Content $NotesFile -Raw -Encoding UTF8).Trim() } else { '' }
$latest = [ordered]@{
    tag_name     = "v$Version"
    name         = "$App $Version"
    published_at = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    html_url     = "$Site/$slug/"
    body         = $notes
    assets       = $assets
}

$outDir = Join-Path $dist $slug
New-Item -ItemType Directory -Force $outDir | Out-Null
$utf8 = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $outDir 'latest.json'), ($latest | ConvertTo-Json -Depth 5), $utf8)
[IO.File]::WriteAllText((Join-Path $dist "SHA256SUMS-$Version.txt"), ($sums -join "`n") + "`n", $utf8)
Write-Host "latest.json: $App $Version, $($assets.Count) file"
$sums | ForEach-Object { Write-Host "  $_" }
