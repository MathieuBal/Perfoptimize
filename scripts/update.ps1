# Updates Perfoptimize to the latest code and rebuilds the standalone exe.
#
#   powershell -ExecutionPolicy Bypass -File scripts\update.ps1
#
# Pulls the current branch, then rebuilds dist\Perfoptimize.exe. Close the app
# (Quit button) before running this so the .exe isn't locked.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "Fetching the latest code..." -ForegroundColor Cyan
git pull --ff-only

Write-Host "Rebuilding Perfoptimize.exe..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "build-windows-exe.ps1")

Write-Host "`nUpdate complete." -ForegroundColor Green
