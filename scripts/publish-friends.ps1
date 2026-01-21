# publish-friends.ps1
# Builds a portable ZIP distribution for friends testing
#
# Usage:
#   .\scripts\publish-friends.ps1
#   .\scripts\publish-friends.ps1 -Version "0.2.0"
#
# Prerequisites:
#   - .NET 10 SDK installed
#   - Your appsettings.json and audio_config.json with real API keys
#     must exist in GameScreenTranslator/ directory

param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot "GameScreenTranslator"
$projectFile = Join-Path $projectDir "GameScreenTranslator.csproj"
$distDir = Join-Path $repoRoot "dist"
$outDir = Join-Path $distDir "GameScreenTranslator-Friends-v$Version"

Write-Host "=== Game Screen Translator - Friends Build ===" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host "Output:  $outDir"
Write-Host ""

# Verify source configs exist with real keys
$appSettingsSource = Join-Path $projectDir "appsettings.json"
$audioConfigSource = Join-Path $projectDir "audio_config.json"
$promptsSource = Join-Path $projectDir "configs" "prompts.yaml"
$soundSource = Join-Path $projectDir "Assets" "Sounds" "tap-notification-357575.mp3"
$quickstartSource = Join-Path $repoRoot "product" "prds" "QUICKSTART.txt"

if (-not (Test-Path $appSettingsSource)) {
    Write-Error "Missing $appSettingsSource - create it with your API keys first"
    exit 1
}
if (-not (Test-Path $audioConfigSource)) {
    Write-Error "Missing $audioConfigSource - create it with your TTS/STT API keys first"
    exit 1
}

# Check that API keys are not placeholder values
$appSettingsContent = Get-Content $appSettingsSource -Raw
if ($appSettingsContent -match "YOUR_.*_HERE" -or $appSettingsContent -match "YOUR-FOUNDRY-RESOURCE") {
    Write-Warning "WARNING: appsettings.json appears to contain placeholder values!"
    Write-Warning "Make sure you've replaced all YOUR_*_HERE placeholders with real API keys."
    $confirm = Read-Host "Continue anyway? (y/N)"
    if ($confirm -ne "y") {
        exit 1
    }
}

Write-Host "Building release..." -ForegroundColor Yellow
dotnet publish $projectFile -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

# Find the published EXE
$publishDir = Join-Path $projectDir "bin" "Release" "net10.0-windows" "win-x64" "publish"
$exePath = Join-Path $publishDir "GameScreenTranslator.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "Published EXE not found at: $exePath"
    exit 1
}

Write-Host "Preparing distribution package..." -ForegroundColor Yellow

# Clean and create output directory
if (Test-Path $outDir) {
    Remove-Item -Recurse -Force $outDir
}
New-Item -ItemType Directory -Path $outDir | Out-Null
New-Item -ItemType Directory -Path (Join-Path $outDir "configs") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $outDir "Assets" "Sounds") | Out-Null

# Copy files
Copy-Item $exePath $outDir
Copy-Item $appSettingsSource $outDir
Copy-Item $audioConfigSource $outDir
Copy-Item $promptsSource (Join-Path $outDir "configs")
Copy-Item $soundSource (Join-Path $outDir "Assets" "Sounds")
Copy-Item $quickstartSource $outDir

# Create ZIP
$zipPath = Join-Path $distDir "GameScreenTranslator-Friends-v$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath
}
Compress-Archive -Path $outDir -DestinationPath $zipPath

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host "Folder: $outDir"
Write-Host "ZIP:    $zipPath"
Write-Host ""
Write-Host "Contents:" -ForegroundColor Cyan
Get-ChildItem $outDir -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Substring($outDir.Length + 1)
    if ($_.PSIsContainer) {
        Write-Host "  [DIR]  $relativePath"
    } else {
        $sizeKB = [math]::Round($_.Length / 1KB, 1)
        Write-Host "  $relativePath ($sizeKB KB)"
    }
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Test the build by running: $outDir\GameScreenTranslator.exe"
Write-Host "  2. Upload $zipPath to OneDrive"
Write-Host "  3. Share the link with friends"
