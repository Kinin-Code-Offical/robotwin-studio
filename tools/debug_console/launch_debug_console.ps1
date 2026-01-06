param (
    [int]$Port = 8090,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$Server = Join-Path $RepoRoot "tools\\debug_console\\server.py"
$LogDir = Join-Path $RepoRoot "logs\\debug_console"
$Url = $null

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) {
    throw "Python not found on PATH. Install Python 3 and retry."
}

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
}

while (Test-NetConnection -ComputerName 127.0.0.1 -Port $Port -InformationLevel Quiet) {
    $Port += 1
    if ($Port -gt 8100) {
        throw "No free port available between 8090 and 8100."
    }
}

$Url = "http://127.0.0.1:$Port"
Write-Host "[Debug Console] Starting on $Url"
if (-not $NoBrowser) {
    Start-Process $Url
}

python $Server --port $Port
