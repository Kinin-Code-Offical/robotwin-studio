param (
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$ProjectDirs = @(
    "CoreSim/src/RobotTwin.CoreSim",
    "CoreSim/src/RobotTwin.CoreSim.Validation" 
)

# Note: Only main CoreSim is currently required in Unity. Validation might be if used in Wizard.
# Based on usage in CircuitStudioController, we need Validation too if it's separate. 
# But looking at repo structure, Validation seemed to be a folder inside src/RobotTwin.CoreSim? 
# Wait, I need to check if Validation is a separate project or just a namespace in the same project.
# Based on files seen, CoreSim/src/RobotTwin.CoreSim/Specs/.. and CoreSim/src/RobotTwin.CoreSim/Validation/.. 
# It seems it's all in one project 'RobotTwin.CoreSim'. 

$ProjectDir = "CoreSim/src/RobotTwin.CoreSim"
$UnityPluginsDir = "UnityApp/Assets/Plugins"

Write-Host "Building CoreSim (netstandard2.1) for Unity..." -ForegroundColor Cyan
dotnet build $ProjectDir -c Release -f netstandard2.1

$SourceDll = "$ProjectDir/bin/Release/netstandard2.1/RobotTwin.CoreSim.dll"
$SourcePdb = "$ProjectDir/bin/Release/netstandard2.1/RobotTwin.CoreSim.pdb"
$DestDll = Join-Path $UnityPluginsDir "RobotTwin.CoreSim.dll"
$DestPdb = Join-Path $UnityPluginsDir "RobotTwin.CoreSim.pdb"

if (-not (Test-Path $SourceDll)) {
    Write-Error "Build failed or output missing: $SourceDll"
}

# Create Plugins dir if missing
if (-not (Test-Path $UnityPluginsDir)) {
    New-Item -ItemType Directory -Path $UnityPluginsDir | Out-Null
}

# Helper to check file difference
function Test-FileDiff($src, $dst) {
    if (-not (Test-Path $dst)) { return $true }
    $srcHash = Get-FileHash $src -Algorithm SHA256
    $dstHash = Get-FileHash $dst -Algorithm SHA256
    return $srcHash.Hash -ne $dstHash.Hash
}

if ($Check) {
    if (Test-FileDiff $SourceDll $DestDll) {
        Write-Host "Error: Unity plugin is out of sync!" -ForegroundColor Red
        Write-Host "Run './tools/update_unity_plugins.ps1' to fix."
        exit 1
    }
    Write-Host "Unity plugins are in sync." -ForegroundColor Green
    exit 0
}

Write-Host "Copying to $UnityPluginsDir..."
Copy-Item $SourceDll $DestDll -Force
Copy-Item $SourcePdb $DestPdb -Force

Write-Host "Success." -ForegroundColor Green
