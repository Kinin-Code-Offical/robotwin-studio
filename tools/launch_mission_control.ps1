$ErrorActionPreference = "Stop"

$scriptPath = $PSScriptRoot
$dashboardPath = Join-Path $scriptPath "dashboard"

Write-Host "============================" -ForegroundColor Cyan
Write-Host "ROBOTWIN MISSION CONTROL" -ForegroundColor Cyan
Write-Host "============================" -ForegroundColor Cyan

# 1. NPM Install
Write-Host "[1/3] Installing Dependencies..." -ForegroundColor Yellow
Set-Location $dashboardPath
npm install
if ($LASTEXITCODE -ne 0) {
    Write-Error "npm install failed!"
}

# 2. Open Browser
Write-Host "[2/3] Opening Dashboard..." -ForegroundColor Yellow
Start-Process "http://localhost:3000"

# 3. Start Server
Write-Host "[3/3] Starting Server..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop." -ForegroundColor Gray
node server.js
