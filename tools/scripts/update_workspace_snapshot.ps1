$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$OutputDir = Join-Path $RepoRoot "logs\\tools"
$OutputFile = Join-Path $OutputDir "workspace_snapshot.txt"
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

# Exclusion patterns (simple string containment for directories, or exact match)
$ExcludedDirs = @(
    ".git",
    "builds",
    "logs",
    "UnityApp\Library",
    "UnityApp\Temp",
    "UnityApp\Logs"
)

# Get all files recursively
$allFiles = Get-ChildItem -Path $RepoRoot -Recurse -File | ForEach-Object {
    # Manual relative path calculation for compatibility
    $fullPath = $_.FullName
    $rootPath = $RepoRoot.Path
    if (-not $rootPath.EndsWith("\")) { $rootPath += "\" }
    
    if ($fullPath.StartsWith($rootPath)) {
        $relativePath = $fullPath.Substring($rootPath.Length)
    }
    else {
        $relativePath = $fullPath
    }

    $exclude = $false
    foreach ($dir in $ExcludedDirs) {
        if ($relativePath.StartsWith($dir)) { $exclude = $true; break }
        if ($relativePath -like "UnityApp\Build*") { $exclude = $true; break }
    }
    
    if (-not $exclude) { $relativePath }
} | Sort-Object

$allFiles | Set-Content -Path $OutputFile -Encoding utf8
Write-Host "Generated workspace_snapshot.txt with $($allFiles.Count) files."
