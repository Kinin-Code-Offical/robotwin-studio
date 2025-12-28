# Build VirtualArduino firmware using dotnet (currently MockFirmware project)
# Usage: ./tools/build_firmware.ps1

$ErrorActionPreference = "Stop"

$SrcParams = "CoreSim/src/MockFirmware"
$OutDir = "build/firmware"

Write-Host "[VirtualArduinoFirmware] Building... " -NoNewline

# Create output dir
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

# Build Release
dotnet build $SrcParams -c Release -o $OutDir

# Check if exe exists
if (Test-Path "$OutDir/MockFirmware.exe") {
    Copy-Item -Path "$OutDir/MockFirmware.exe" -Destination "$OutDir/VirtualArduinoFirmware.exe" -Force
    Write-Host "Success!" -ForegroundColor Green
    Write-Host "   -> $OutDir/VirtualArduinoFirmware.exe"
}
else {
    Write-Error "Build Failed!"
}
