param (
    [ValidateSet("MIN", "FULL")]
    [string]$Mode = "FULL",
    [switch]$SkipGh,
    [string]$DriveRemote = "gdrive:",
    [string]$DriveFolder = "robotwin_studio/shared_infos"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$DocsDir = Join-Path $RepoRoot "docs"
$TempDir = Join-Path $RepoRoot ".gpt/shared_info"

# 1. Run Context Export
Write-Host ">>> Running Context Export ($Mode)..." -ForegroundColor Cyan
if ($SkipGh) {
    & "$PSScriptRoot/export_context_pack.ps1" -Mode $Mode -SkipGh
}
else {
    & "$PSScriptRoot/export_context_pack.ps1" -Mode $Mode
}

# 2. Prepare Temp Directory
if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force | Out-Null }
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

# 3. Create Zip
$Timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmssZ")
$ZipName = "session_$Timestamp.zip"
$ZipPath = Join-Path $TempDir $ZipName

Write-Host ">>> Zipping docs folder to $ZipPath..." -ForegroundColor Cyan
Compress-Archive -Path "$DocsDir/*" -DestinationPath $ZipPath -Force

# 4. Upload to Drive
$RemotePath = "$DriveRemote$DriveFolder/$ZipName"
Write-Host ">>> Uploading to $RemotePath..." -ForegroundColor Cyan

if (Get-Command rclone -ErrorAction SilentlyContinue) {
    try {
        rclone copyto $ZipPath $RemotePath
        Write-Host ">>> Upload Success: $RemotePath" -ForegroundColor Green
        
        # 5. Cleanup
        Remove-Item $ZipPath -Force
        Write-Host ">>> Local cleanup complete." -ForegroundColor Gray
    }
    catch {
        Write-Error "Upload failed: $_"
        exit 1
    }
}
else {
    Write-Warning "rclone not found. Skipping upload. Zip remains at $ZipPath"
}
