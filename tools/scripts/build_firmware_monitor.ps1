param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$StopRunning
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$ProjectPath = Join-Path $RepoRoot "tools/RobotWinFirmwareMonitor/RobotWinFirmwareMonitor.csproj"
$OutDir = Join-Path $RepoRoot "builds/RobotWinFirmwareMonitor"
$LogDir = Join-Path $RepoRoot "logs/RobotWinFirmwareMonitor"
$LogPath = Join-Path $LogDir "build.log"
$PublishDir = Join-Path $OutDir "$Configuration/$Runtime"

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force -Path $LogDir | Out-Null }
"" | Set-Content -Path $LogPath

if (-not (Test-Path $ProjectPath)) { Write-Error "Missing project: $ProjectPath" }

Push-Location $RepoRoot
Write-Host "[FirmwareMonitor] Publishing RobotWinFirmwareMonitor ($Configuration, $Runtime, self-contained)..." -ForegroundColor Cyan

$running = Get-Process -Name "RobotWinFirmwareMonitor" -ErrorAction SilentlyContinue
if ($running)
{
    if ($StopRunning)
    {
        foreach ($proc in $running)
        {
            try
            {
                $proc.CloseMainWindow() | Out-Null
            }
            catch {}
        }
        Start-Sleep -Milliseconds 800
        $running = Get-Process -Name "RobotWinFirmwareMonitor" -ErrorAction SilentlyContinue
        if ($running)
        {
            Write-Host "[FirmwareMonitor] Stopping running monitor process(es)..." -ForegroundColor Yellow
            $running | Stop-Process -Force
        }
    }
    else
    {
        Write-Host "[FirmwareMonitor] Monitor is running. Close it or re-run with -StopRunning to avoid file locks." -ForegroundColor Yellow
    }
}

$cmd = @(
    "dotnet",
    "publish",
    $ProjectPath,
    "-c",
    $Configuration,
    "-r",
    $Runtime,
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishDir=$PublishDir",
    "--verbosity",
    "minimal"
)
($cmd -join " ") | Tee-Object -FilePath $LogPath
& $cmd[0] @($cmd[1..($cmd.Length - 1)]) 2>&1 | Tee-Object -FilePath $LogPath -Append
if ($LASTEXITCODE -ne 0) { Write-Error "Firmware monitor build failed. See $LogPath"; exit 1 }

Write-Host "[FirmwareMonitor] Output: $PublishDir" -ForegroundColor Green
Write-Host "[FirmwareMonitor] Log: $LogPath" -ForegroundColor Green
Pop-Location
