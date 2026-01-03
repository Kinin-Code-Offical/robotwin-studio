$ErrorActionPreference = "Stop"

# Ensure Output Dir
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$DllDir = Join-Path $RepoRoot "RobotWin/Assets/Plugins/x86_64"
$OutDir = Join-Path $RepoRoot "builds/native"
if (-not (Test-Path $DllDir)) { New-Item -ItemType Directory -Force -Path $DllDir | Out-Null }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$Includes = "-I NativeEngine/include"
# Note: Mixed .cpp and .c files
$Sources = "NativeEngine/src/NativeEngine_Core.cpp", "NativeEngine/src/Core/CircuitContext.cpp", "NativeEngine/src/Core/NodalSolver.cpp", "NativeEngine/src/Core/BvmFormat.cpp", "NativeEngine/src/Physics/PhysicsWorld.cpp", "NativeEngine/src/MCU/ATmega328P_ISA.c"
$MainSrc = "NativeEngine/src/main.cpp"

Push-Location $RepoRoot
Write-Host "[NativeEngine] Building DLL..." -ForegroundColor Cyan
# Build DLL (Unity Plugin)
# Use -I"path" to ensure correct parsing
g++ -shared -o "$DllDir/NativeEngine.dll" $Sources -I"NativeEngine/include" -static-libgcc -static-libstdc++ -g
if ($LASTEXITCODE -ne 0) { Write-Error "DLL Build Failed"; exit 1 }

Write-Host "[NativeEngine] Building Standalone EXE..." -ForegroundColor Cyan
# Build Standalone (Test Runner)
g++ -o "$OutDir/NativeEngine.exe" $MainSrc $Sources -I"NativeEngine/include" -static-libgcc -static-libstdc++ -g
if ($LASTEXITCODE -ne 0) { Write-Error "EXE Build Failed"; exit 1 }

Write-Host "[NativeEngine] Success!" -ForegroundColor Green
Get-Item "$DllDir/NativeEngine.dll" | Select-Object Name, Length, LastWriteTime
Get-Item "$OutDir/NativeEngine.exe" | Select-Object Name, Length, LastWriteTime
Pop-Location

