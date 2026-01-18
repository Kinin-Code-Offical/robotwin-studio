param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$OutDir = Join-Path $RepoRoot "builds/firmware"
$LogDir = Join-Path $RepoRoot "logs/firmware"
$LogPath = Join-Path $LogDir "build.log"

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force -Path $LogDir | Out-Null }
"" | Set-Content -Path $LogPath

$Sources = @(
    "FirmwareEngine/main.cpp",
    "FirmwareEngine/PipeManager.cpp",
    "FirmwareEngine/VirtualMcu.cpp",
    "FirmwareEngine/BoardProfile.cpp",
    "FirmwareEngine/Rpi/RpiBackend.cpp",
    "FirmwareEngine/Rpi/RpiShm.cpp",
    "FirmwareEngine/Rpi/QemuProcess.cpp",
    "FirmwareEngine/U1/U1.cpp",
    "FirmwareEngine/U1/U1Globals.cpp",
    "NativeEngine/src/MCU/ATmega328P_ISA.c"
)

$OutputExe = Join-Path $OutDir "RoboTwinFirmwareHost.exe"
$ResourceFile = "FirmwareEngine/RoboTwinFirmwareHost.rc"
$ResourceObj = Join-Path $OutDir "RoboTwinFirmwareHost.res.o"
$IncludeDirs = @(
    "FirmwareEngine",
    "FirmwareEngine/include",
    "FirmwareEngine/U1",
    "FirmwareEngine/Rpi",
    "NativeEngine/include"
)

$IncludeArgs = $IncludeDirs | ForEach-Object { "-I$($_)" }

Push-Location $RepoRoot
Write-Host "[Firmware] Building RoboTwinFirmwareHost.exe ($Configuration)..." -ForegroundColor Cyan

$Flags = @("-std=c++17", "-O2")
if ($Configuration -eq "Debug") { $Flags = @("-std=c++17", "-O0", "-g") }

$Windres = Get-Command windres -ErrorAction SilentlyContinue
if (-not $Windres) { $Windres = Get-Command x86_64-w64-mingw32-windres -ErrorAction SilentlyContinue }
if ($Windres -and (Test-Path $ResourceFile)) {
    & $Windres $ResourceFile -O coff -o $ResourceObj 2>&1 | Tee-Object -FilePath $LogPath -Append
}
elseif (Test-Path $ResourceFile) {
    Write-Warning "windres not found; firmware metadata/icon resources will be skipped."
}

$ResourceArgs = @()
if (Test-Path $ResourceObj) { $ResourceArgs = @($ResourceObj) }

$cmd = @("g++") + $Flags + @("-o", $OutputExe) + $Sources + $IncludeArgs + $ResourceArgs + @("-static-libgcc", "-static-libstdc++")
($cmd -join " ") | Tee-Object -FilePath $LogPath
& $cmd[0] @($cmd[1..($cmd.Length - 1)]) 2>&1 | Tee-Object -FilePath $LogPath -Append
if ($LASTEXITCODE -ne 0) { Write-Error "Firmware build failed. See $LogPath"; exit 1 }

$CompileDbPath = Join-Path $OutDir "compile_commands.json"
$CompileEntries = @()
$RepoRootPath = $RepoRoot.Path
$IncludeArgsAbs = $IncludeDirs | ForEach-Object { "-I" + (Join-Path $RepoRootPath $_) }
foreach ($Source in $Sources) {
    $AbsSource = Join-Path $RepoRootPath $Source
    $CompileCmd = @("g++") + $Flags + @("-c", $AbsSource) + $IncludeArgsAbs
    $CompileEntries += @{
        directory = $RepoRootPath
        command   = ($CompileCmd -join " ")
        file      = $AbsSource
    }
}
$CompileEntries | ConvertTo-Json -Depth 4 | Set-Content -Path $CompileDbPath

Write-Host "[Firmware] Output: $OutputExe" -ForegroundColor Green
Write-Host "[Firmware] Log: $LogPath" -ForegroundColor Green
Write-Host "[Firmware] Compile DB: $CompileDbPath" -ForegroundColor Green
Pop-Location
