param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.5"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$launcherProj = Join-Path $root "Launcher.UI\Launcher.UI.csproj"
$installerProj = Join-Path $root "Installer.UI\Installer.UI.csproj"
$launcherPublishDir = Join-Path $root "Launcher.UI\bin\$Configuration\net10.0\win-x64\publish"
$payloadZip = Join-Path $root "Installer.UI\payload.zip"
$installerPublishDir = Join-Path $root "Installer.UI\bin\$Configuration\net10.0\win-x64\publish"
$outputDir = Join-Path $root "Installer\bin\$Configuration"
$distDir = Join-Path $root "dist"

function Require-Path([string]$Path, [string]$Hint) {
    if (-not (Test-Path $Path)) {
        throw "Expected output was not created:`n  $Path`n$Hint"
    }
}

function Require-File([string]$Path, [string]$Hint) {
    Require-Path $Path $Hint
    if ((Get-Item $Path).Length -le 0) {
        throw "Output file is empty:`n  $Path`n$Hint"
    }
}

Write-Host "=== Building DanClient Installer ===" -ForegroundColor Green
Write-Host "Output folder: $outputDir" -ForegroundColor Gray

Write-Host "`n[1/4] Publishing Launcher.UI..." -ForegroundColor Cyan
dotnet publish $launcherProj -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Launcher.UI publish failed" }

Require-File (Join-Path $launcherPublishDir "Launcher.UI.exe") `
    "Launcher publish failed. Check that win-x64 publish completed."

Write-Host "`n[2/4] Creating payload.zip..." -ForegroundColor Cyan
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($launcherPublishDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Require-File $payloadZip "payload.zip was not created."
Write-Host "  Payload: $([math]::Round((Get-Item $payloadZip).Length / 1MB, 1)) MB" -ForegroundColor Gray

Write-Host "`n[3/4] Publishing Installer.UI (embeds payload.zip)..." -ForegroundColor Cyan
dotnet publish $installerProj -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Installer.UI publish failed" }

$installerExe = Join-Path $installerPublishDir "DanClientSetup.exe"
Require-File $installerExe `
    "Installer publish failed. The setup exe should be in:`n  $installerPublishDir"

Write-Host "`n[4/4] Copying to release folders..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$releaseExe = Join-Path $outputDir "DanClientSetup.exe"
$distExe = Join-Path $distDir "DanClientSetup.exe"

Copy-Item $installerExe $releaseExe -Force
Copy-Item $installerExe $distExe -Force

Require-File $releaseExe "Copy to Installer\bin\$Configuration failed."
Require-File $distExe "Copy to dist failed."

if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Single-file installer (launcher embedded):" -ForegroundColor Yellow
Write-Host "  $releaseExe" -ForegroundColor White
Write-Host ""
Write-Host "Also copied to:" -ForegroundColor Yellow
Write-Host "  $distExe" -ForegroundColor White
Write-Host ""
Write-Host "Launcher only (no installer):" -ForegroundColor Yellow
Write-Host "  $(Join-Path $launcherPublishDir 'Launcher.UI.exe')" -ForegroundColor White
Write-Host ""
Write-Host "Installer size: $([math]::Round((Get-Item $releaseExe).Length / 1MB, 1)) MB (includes embedded launcher payload)" -ForegroundColor Gray
Write-Host ""
Write-Host "Note: dotnet build alone does NOT run this script." -ForegroundColor DarkGray
Write-Host "      Use Installer.UI\build-installer.ps1 for the Avalonia setup exe." -ForegroundColor DarkGray
