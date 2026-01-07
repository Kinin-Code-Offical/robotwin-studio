param (
    [string]$Output = "CoreSim/tests/RobotTwin.CoreSim.Tests/Fixtures/golden_trace_v1.json",
    [string]$FirmwareExe = "builds/firmware/RoboTwinFirmwareHost.exe",
    [string]$Pipe = "RoboTwin.FirmwareEngine.Trace",
    [string]$BoardId = "board",
    [string]$BoardProfile = "ArduinoUno",
    [int]$PinPrefix = 16,
    [double]$DtSeconds = 0.02,
    [string]$Bvm = "tools/fixtures/firmware_minimal.bvm",
    [string]$FirmwareLog = "",
    [int]$ConnectTimeoutMs = 15000,
    [int]$StepTimeoutMs = 3000,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$LogDir = Join-Path $RepoRoot "logs\\tools"
$LogFile = Join-Path $LogDir "record_golden_trace.log"

Set-Location $RepoRoot

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
}

function Resolve-RepoPath([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return "" }
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $path))
}

$OutputAbs = Resolve-RepoPath $Output
$FirmwareExeAbs = Resolve-RepoPath $FirmwareExe
$BvmAbs = Resolve-RepoPath $Bvm
$FirmwareLogAbs = Resolve-RepoPath $FirmwareLog

if (-not (Test-Path $FirmwareExeAbs)) {
    throw "Firmware exe not found: $FirmwareExeAbs"
}

if ($BvmAbs -and -not (Test-Path $BvmAbs)) {
    throw "BVM not found: $BvmAbs"
}

$argsList = @(
    "--output", $OutputAbs,
    "--firmware", $FirmwareExeAbs,
    "--pipe", $Pipe,
    "--board-id", $BoardId,
    "--board-profile", $BoardProfile,
    "--pin-prefix", $PinPrefix.ToString(),
    "--dt", $DtSeconds.ToString(),
    "--connect-timeout", $ConnectTimeoutMs.ToString(),
    "--step-timeout", $StepTimeoutMs.ToString()
)

if ($BvmAbs) {
    $argsList += @("--bvm", $BvmAbs)
}

if ($FirmwareLogAbs) {
    $argsList += @("--firmware-log", $FirmwareLogAbs)
}

if ($NoLaunch) {
    $argsList += "--no-launch"
}

$cmdArgs = @(
    "run",
    "--project", "tools/GoldenTraceRecorder/GoldenTraceRecorder.csproj",
    "--"
) + $argsList

& dotnet @cmdArgs 2>&1 | Tee-Object -FilePath $LogFile
