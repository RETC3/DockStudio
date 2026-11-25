# Crea l'eseguibile autonomo del launcher DockStudio (include .NET: nessuna installazione richiesta).
#   .\scripts\publish-dockstudio.ps1                 -> dist\dockstudio\win-x64\DockStudio.exe
#   .\scripts\publish-dockstudio.ps1 -Arm64          -> anche dist\dockstudio\win-arm64\DockStudio.exe
#   .\scripts\publish-dockstudio.ps1 -Zip            -> crea anche dist\DockStudio-<versione>-<rid>.zip (versione portatile)
#   .\scripts\publish-dockstudio.ps1 -Installer      -> crea anche dist\DockStudio-Setup-<versione>.exe (x64 + ARM64, serve Inno Setup 6)
#   .\scripts\publish-dockstudio.ps1 -Version 0.2.0  -> usa questa versione invece di quella del .csproj
param(
    [switch]$Arm64,
    [switch]$Zip,
    [switch]$Installer,
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$project = "$root\src\DockStudio.Launcher\DockStudio.Launcher.csproj"

# dotnet dal PATH, altrimenti l'installazione per utente
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "SDK .NET 10 non trovato: installalo da https://dotnet.microsoft.com/download/dotnet/10.0" }

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versione non valida: '$Version' (formato atteso: 1.2.3)" }

$iscc = $null
if ($Installer) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw "Inno Setup 6 non trovato: installalo da https://jrsoftware.org/isdl.php" }
}

# L'installer contiene entrambi gli exe.
$profiles = @('win-x64')
if ($Arm64 -or $Installer) { $profiles += 'win-arm64' }

foreach ($p in $profiles) {
    Write-Host "== Pubblicazione $p ($Version) ==" -ForegroundColor Cyan
    & $dotnet publish $project "-p:PublishProfile=$p" "-p:Version=$Version" -nologo
    if ($LASTEXITCODE -ne 0) { throw "Pubblicazione $p non riuscita" }

    $exe = Get-Item "$root\dist\dockstudio\$p\DockStudio.exe"
    Write-Host ("   {0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))

    if ($Zip) {
        $zipPath = "$root\dist\DockStudio-$Version-$p.zip"
        Compress-Archive -Path $exe.FullName -DestinationPath $zipPath -Force
        Write-Host "   $zipPath"
    }
}

if ($Installer) {
    Write-Host "== Installer ==" -ForegroundColor Cyan
    & $iscc /Q "/DAppVersion=$Version" "$root\installer\DockStudio.iss"
    if ($LASTEXITCODE -ne 0) { throw "Creazione dell'installer non riuscita" }
    $setup = Get-Item "$root\dist\DockStudio-Setup-$Version.exe"
    Write-Host ("   {0}  ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
}
