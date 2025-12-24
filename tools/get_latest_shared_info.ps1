param (
    [string]$DriveRemote = "gdrive:",
    [string]$DriveFolder = "robotwin_studio/shared_infos"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$DestDir = Join-Path $RepoRoot ".gpt/shared_info/latest"

# 1. Check Rclone
if (-not (Get-Command rclone -ErrorAction SilentlyContinue)) {
    Write-Error "rclone not found in PATH."
    exit 1
}

# 2. Find Latest Zip
$RemotePath = "$DriveRemote$DriveFolder"
Write-Host ">>> Listing files in $RemotePath..." -ForegroundColor Cyan

# Parse lsjson output to find latest by mod time or name (name has timestamp)
try {
    $Json = rclone lsjson $RemotePath | ConvertFrom-Json
    $Latest = $Json | Where-Object { $_.Name -match "^session_\d{8}_\d{6}Z\.zip$" } | Sort-Object Name -Descending | Select-Object -First 1
}
catch {
    Write-Error "Failed to list files: $_"
    exit 1
}

if (-not $Latest) {
    Write-Warning "No 'session_*.zip' files found in $RemotePath."
    exit 0
}

Write-Host ">>> Found latest: $($Latest.Name)" -ForegroundColor Green

# 3. Prepare Dest
if (Test-Path $DestDir) { Remove-Item $DestDir -Recurse -Force | Out-Null }
New-Item -ItemType Directory -Path $DestDir -Force | Out-Null

# 4. Download
$LocalZip = Join-Path $DestDir $Latest.Name
Write-Host ">>> Downloading to $LocalZip..." -ForegroundColor Cyan
rclone copyto "$RemotePath/$($Latest.Name)" $LocalZip

# 5. Extract
Write-Host ">>> Extracting..." -ForegroundColor Cyan
Expand-Archive -Path $LocalZip -DestinationPath $DestDir -Force

Write-Host ">>> Done! Context extracted to $DestDir" -ForegroundColor Green
Write-Host "See extracted docs in: $(Join-Path $DestDir 'docs')" -ForegroundColor Gray
