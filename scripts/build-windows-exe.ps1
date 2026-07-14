# Builds the standalone Perfoptimize.exe (UI + engine) for Windows x64.
# Self-contained: the .NET runtime is bundled, so the user needs nothing installed.
#
#   powershell -ExecutionPolicy Bypass -File scripts\build-windows-exe.ps1
#
# The result is a single file: dist\Perfoptimize.exe — double-click it to launch
# the app; it starts a local server and opens the dashboard in your browser.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist"

Write-Host "Building Perfoptimize.exe (win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\Perfoptimize.App") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

$exe = Join-Path $out "Perfoptimize.exe"
if (Test-Path $exe) {
    Write-Host "`nDone: $exe" -ForegroundColor Green
    Write-Host "Double-click it to launch the app." -ForegroundColor Green
} else {
    Write-Error "Build finished but Perfoptimize.exe was not produced."
}
