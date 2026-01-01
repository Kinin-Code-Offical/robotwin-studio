param(
    [string]$UnityPath
)

$envUnityPath = $env:UNITY_PATH
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$projectPath = Join-Path $repoRoot "UnityApp"
$buildDir = Join-Path $repoRoot "builds\\windows"
$buildPath = Join-Path $buildDir "RobotwinStudio.exe"
$logDir = Join-Path $repoRoot "logs\\unity"
$logFile = Join-Path $logDir "build.log"

# Make sure build dir exists
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Write-Host "Starting Build for Windows Standalone..."
Write-Host "Output: $buildPath"
Write-Host "Log: $logFile"

$possiblePaths = @(
    "C:\Program Files\Unity\Hub\Editor\6000.3.2f1\Editor\Unity.exe",
    "C:\Program Files\Unity\Hub\Editor\6000.3.1f1\Editor\Unity.exe",
    "C:\Program Files\Unity\Hub\Editor\6000.2.0f1\Editor\Unity.exe"
)

$unityExe = $null
if ($UnityPath -and (Test-Path $UnityPath)) {
    $unityExe = $UnityPath
}
elseif ($envUnityPath -and (Test-Path $envUnityPath)) {
    $unityExe = $envUnityPath
}

foreach ($p in $possiblePaths) {
    if ($unityExe) { break }
    if (Test-Path $p) {
        $unityExe = $p
        break
    }
}

if ($unityExe) {
    Push-Location $repoRoot
    Write-Host "Unity found at $unityExe. Building..."
    $process = Start-Process -FilePath $unityExe -ArgumentList "-quit", "-batchmode", "-projectPath", "`"$projectPath`"", "-buildWindowsPlayer", "`"$buildPath`"", "-logFile", "`"$logFile`"" -Wait -PassThru
    if ($process.ExitCode -eq 0) {
        Write-Host "Build Success!"
    }
    else {
        Write-Host "Build Failed with Exit Code $($process.ExitCode). Check $logFile."
        Pop-Location
        exit 1
    }
    Pop-Location
}
else {
    Write-Error "Unity Editor not found. Set UNITY_PATH or pass -UnityPath to build."
}
