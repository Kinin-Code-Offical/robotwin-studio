param (
    [string]$DriveRemote = "gdrive:",
    [string]$DriveFolder = "robotwin_studio/shared_infos"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$DestDir = Join-Path $RepoRoot ".gpt/shared_info/latest"
$PointerJsonPath = Join-Path $RepoRoot "docs/antigravity/SHARED_INFO_LATEST.json"
$DocsDest = Join-Path $DestDir "docs"

# 1. Check Rclone
if (-not (Get-Command rclone -ErrorAction SilentlyContinue)) {
    Write-Error "rclone not found in PATH."
    exit 1
}

# 2. Try Reading Pointer File
$ArtifactType = "zip"
$RemotePath = $null

if (Test-Path $PointerJsonPath) {
    try {
        $Json = Get-Content $PointerJsonPath -Raw | ConvertFrom-Json
        if ($Json.artifact_type -eq "dir" -and $Json.drive_remote_path) {
            $ArtifactType = "dir"
            $RemotePath = $Json.drive_remote_path
            Write-Host ">>> Found pointer to DIR artifact at $RemotePath" -ForegroundColor Cyan
        }
    }
    catch {
        Write-Warning "Failed to read SHARED_INFO_LATEST.json. Falling back to Zip search."
    }
}

# 3. Fallback to Zip Search if no DIR pointer
if (-not $RemotePath) {
    $RootRemote = "$DriveRemote$DriveFolder"
    Write-Host ">>> Listing files in $RootRemote..." -ForegroundColor Cyan

    try {
        $List = rclone lsjson $RootRemote | ConvertFrom-Json
        $Latest = $List | Where-Object { $_.Name -match "^session_\d{8}_\d{6}Z\.zip$" } | Sort-Object Name -Descending | Select-Object -First 1
        
        if ($Latest) {
            $RemotePath = "$RootRemote/$($Latest.Name)"
            $ArtifactType = "zip"
            Write-Host ">>> Found latest zip: $($Latest.Name)" -ForegroundColor Green
        }
    }
    catch {
        Write-Error "Failed to list files: $_"
        exit 1
    }
}

if (-not $RemotePath) {
    Write-Warning "No shared info found (neither pointer nor zip)."
    exit 0
}

# 4. Prepare Dest
if (Test-Path $DestDir) { Remove-Item $DestDir -Recurse -Force | Out-Null }
New-Item -ItemType Directory -Path $DestDir -Force | Out-Null

# 5. Download
if ($ArtifactType -eq "dir") {
    Write-Host ">>> Downloading folder from $RemotePath to $DocsDest..." -ForegroundColor Cyan
    # Use sync to mirror exact state
    rclone sync $RemotePath $DocsDest --progress
}
else {
    # Zip Mode
    $LocalZip = Join-Path $DestDir "session.zip"
    Write-Host ">>> Downloading zip from $RemotePath to $LocalZip..." -ForegroundColor Cyan
    rclone copyto $RemotePath $LocalZip --progress
    
    Write-Host ">>> Extracting..." -ForegroundColor Cyan
    Expand-Archive -Path $LocalZip -DestinationPath $DestDir -Force
    Remove-Item $LocalZip -Force
}

Write-Host ">>> Done! Context downloaded to $DestDir" -ForegroundColor Green
Write-Host "See extracted docs in: $DocsDest" -ForegroundColor Gray

