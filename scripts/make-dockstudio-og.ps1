# Crea l'anteprima dei link di getdockstudio.it (site/public/og-image.png, 1200x630) da
# site/og/og-image.html con Edge headless.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$page = Join-Path $root 'site\og\og-image.html'
$out = Join-Path $root 'site\public\og-image.png'
$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge non trovato' }
$tmp = Join-Path $env:TEMP "dockstudio-og-$PID"
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    if (Test-Path $out) { Remove-Item $out }
    $url = ([Uri]$page).AbsoluteUri
    $ErrorActionPreference = 'Continue'   # Edge scrive su stderr anche quando va tutto bene
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        --window-size=1200,630 --virtual-time-budget=3000 --allow-file-access-from-files `
        "--user-data-dir=$tmp\profile" "--screenshot=$out" $url 2>$null | Out-Null
    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path $out)) { throw "Screenshot non creato: $out" }
    Write-Host "Creato $out"
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
