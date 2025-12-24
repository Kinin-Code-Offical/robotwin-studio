$ErrorActionPreference = "Stop"

$OutputFile = Join-Path $PSScriptRoot "..\repo_files.txt"

# Ensure git is available
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "Git is not installed or not in PATH."
}

# Get files tracked by git, excluding deleted ones
$files = git ls-files | Sort-Object

$content = @()
$content += "# GeneratedUtc: $([DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))"
$content += "# Commit: $(git rev-parse --short HEAD)"
$content += "# FileCount: $($files.Count)"
$content += ""
$content += $files

$content | Set-Content -Path $OutputFile -Encoding utf8
Write-Host "Updated $OutputFile with $($files.Count) files."
