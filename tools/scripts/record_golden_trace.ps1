param (
    [string]$Output = "CoreSim/tests/RobotTwin.CoreSim.Tests/Fixtures/golden_trace_v1.json",
    [string]$FirmwareExe = "builds/firmware/RoboTwinFirmwareHost.exe",
    [string]$Pipe = "RoboTwin.FirmwareEngine.Trace",
    [string]$BoardId = "board",
    [string]$BoardProfile = "ArduinoUno",
    [int]$PinPrefix = 16,
    [double]$DtSeconds = 0.02,
    [string]$Bvm = "tools/fixtures/firmware_minimal.bvm"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$LogDir = Join-Path $RepoRoot "logs\\tools"
$LogFile = Join-Path $LogDir "record_golden_trace.log"

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
}

if ($Bvm -and -not (Test-Path (Join-Path $RepoRoot $Bvm))) {
    throw "BVM not found: $Bvm"
}

$argsList = @(
    "--output", $Output,
    "--firmware", $FirmwareExe,
    "--pipe", $Pipe,
    "--board-id", $BoardId,
    "--board-profile", $BoardProfile,
    "--pin-prefix", $PinPrefix.ToString(),
    "--dt", $DtSeconds.ToString()
)

if ($Bvm) {
    $argsList += @("--bvm", $Bvm)
}

$cmdArgs = @(
    "run",
    "--project", "tools/GoldenTraceRecorder/GoldenTraceRecorder.csproj",
    "--"
) + $argsList

& dotnet @cmdArgs 2>&1 | Tee-Object -FilePath $LogFile
