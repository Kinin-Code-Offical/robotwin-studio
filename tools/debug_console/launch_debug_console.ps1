param (
    [int]$Port = 8090
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$Server = Join-Path $RepoRoot "tools\\debug_console\\server.py"
$LogDir = Join-Path $RepoRoot "logs\\debug_console"

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
}

python $Server --port $Port
