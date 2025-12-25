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

# Files to sync (Core + Deps)
# Note: System.Text.Json 8.x brings in:
# - System.Text.Json.dll
# - System.Text.Encodings.Web.dll
# - Microsoft.Bcl.AsyncInterfaces.dll
# - System.Runtime.CompilerServices.Unsafe.dll
$FilesToSync = @(
    "RobotTwin.CoreSim.dll",
    "RobotTwin.CoreSim.pdb",
    "System.Text.Json.dll",
    "System.Text.Encodings.Web.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.Runtime.CompilerServices.Unsafe.dll"
)

$SourceBase = "$ProjectDir/bin/Release/netstandard2.1"

# Validation Phase
foreach ($File in $FilesToSync) {
    $Src = Join-Path $SourceBase $File
    if (-not (Test-Path $Src)) {
        Write-Error "Build output missing required dependency: $Src"
    }
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
    $OutOfSync = $false
    foreach ($File in $FilesToSync) {
        $Src = Join-Path $SourceBase $File
        $Dst = Join-Path $UnityPluginsDir $File
        if (Test-FileDiff $Src $Dst) {
            Write-Host "Mismatch: $File" -ForegroundColor Red
            $OutOfSync = $true
        }
    }
    
    if ($OutOfSync) {
        Write-Host "Error: Unity plugins are out of sync!" -ForegroundColor Red
        Write-Host "Run './tools/update_unity_plugins.ps1' to fix."
        exit 1
    }
    Write-Host "Unity plugins are in sync." -ForegroundColor Green
    exit 0
}

Write-Host "Copying to $UnityPluginsDir..."
foreach ($File in $FilesToSync) {
    $Src = Join-Path $SourceBase $File
    $Dst = Join-Path $UnityPluginsDir $File
    Copy-Item $Src $Dst -Force
    Write-Host "  $File" -ForegroundColor Gray
}

# Cleanup optional annoying files if they exist (deps, etc)
if (Test-Path "$UnityPluginsDir/RobotTwin.CoreSim.deps.json") { Remove-Item "$UnityPluginsDir/RobotTwin.CoreSim.deps.json" }
if (Test-Path "$UnityPluginsDir/RobotTwin.CoreSim.deps.json.meta") { Remove-Item "$UnityPluginsDir/RobotTwin.CoreSim.deps.json.meta" }

Write-Host "Success." -ForegroundColor Green
