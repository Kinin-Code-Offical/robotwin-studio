param (
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.2f1\Editor\Unity.exe"
)

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
if (-not (Test-Path $UnityPath)) {
    Write-Warning "Unity Editor not found at $UnityPath. Skipping smoke test."
    exit 0
}

$ProjectDir = Join-Path $RepoRoot "RobotWin"
$LogDir = Join-Path $RepoRoot "logs\\unity"
$LogFile = Join-Path $LogDir "smoke.log"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force -Path $LogDir | Out-Null }

Write-Host "Running Unity Smoke Tests (Batchmode)..." -ForegroundColor Cyan

# Run Editor to compile scripts and verify no errors
# Use -quit to exit after compilation/startup if no tests specified (or just -batchmode)
# Ideally we run EditMode tests if any exist.

$proc = Start-Process -FilePath $UnityPath -ArgumentList "-batchmode", "-projectPath", "`"$ProjectDir`"", "-logFile", "`"$LogFile`"", "-quit" -PassThru -NoNewWindow
$proc.WaitForExit()

if ($proc.ExitCode -ne 0) {
    Write-Error "Unity exited with code $($proc.ExitCode). Check $LogFile"
    exit $proc.ExitCode
}

Write-Host "Unity Smoke Test Passed (Compilation verify)." -ForegroundColor Green
exit 0

