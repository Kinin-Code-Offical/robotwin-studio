param (
    [ValidateSet("MIN", "FULL")]
    [string]$Mode = "FULL",
    [switch]$SkipGh,
    [string]$DriveRemote = "gdrive:",
    [ValidateSet("ZIP", "DIR")]
    [string]$UploadMode = "DIR",
    [ValidateSet("COPY", "MIRROR")]
    [string]$DirMode = "COPY",
    [string]$DriveDocsSubfolder = "robotwin_studio/shared_infos/latest_docs"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
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

$Timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmssZ")
$ZipName = $null
$FileHash = "N/A"
$RemotePath = $null
$UploadSuccess = $false
$ShareLink = $null
$ArtifactType = $null

if ($UploadMode -eq "ZIP") {
    $ArtifactType = "zip"
    $ZipName = "session_$Timestamp.zip"
    $ZipPath = Join-Path $TempDir $ZipName
    $RemotePath = "$DriveRemote`robotwin_studio/shared_infos/$ZipName"

    Write-Host ">>> Zipping docs folder to $ZipPath..." -ForegroundColor Cyan
    Compress-Archive -Path "$DocsDir/*" -DestinationPath $ZipPath -Force
    $FileHash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash

    if (Get-Command rclone -ErrorAction SilentlyContinue) {
        try {
            Write-Host ">>> Uploading to $RemotePath..." -ForegroundColor Cyan
            rclone copyto $ZipPath $RemotePath
            Write-Host ">>> Upload Success: $RemotePath" -ForegroundColor Green
            $UploadSuccess = $true
            $ShareLink = rclone link $RemotePath 2>$null
        }
        catch {
            Write-Error "Upload failed: $_"
            Write-Warning "Local zip preserved at $ZipPath"
        }
    }
}
elseif ($UploadMode -eq "DIR") {
    $ArtifactType = "dir"
    $RemoteDocsRoot = "$DriveRemote$DriveDocsSubfolder"
    $RemotePath = "$RemoteDocsRoot/docs"
    
    if (Get-Command rclone -ErrorAction SilentlyContinue) {
        try {
            Write-Host ">>> Syncing docs to $RemotePath (Mode: $DirMode)..." -ForegroundColor Cyan
            
            if ($DirMode -eq "MIRROR") {
                # Sync matches destination to source, deleting extras
                rclone sync "$DocsDir" $RemotePath --checksum --create-empty-src-dirs
            }
            else {
                # Copy updates changed files, keeping extras
                rclone copy "$DocsDir" $RemotePath --checksum --create-empty-src-dirs
            }
            
            Write-Host ">>> Upload Success: $RemotePath" -ForegroundColor Green
            $UploadSuccess = $true
            $ShareLink = rclone link $RemotePath 2>$null
        }
        catch {
            Write-Error "Upload failed: $_"
        }
    }
}

if (-not (Get-Command rclone -ErrorAction SilentlyContinue)) {
    Write-Warning "rclone not found. Skipping upload."
}

# 5. Generate Tracked Pointers (Always run)
$PointerJsonPath = Join-Path "$DocsDir/antigravity" "SHARED_INFO_LATEST.json"
$PointerMdPath = Join-Path "$DocsDir/antigravity" "SHARED_INFO_LATEST_SUMMARY.md"

$PointerData = [Ordered]@{
    "created_at_utc"    = $Timestamp
    "artifact_type"     = $ArtifactType
    "zip_name"          = $ZipName
    "dir_mode"          = if ($ArtifactType -eq "dir") { $DirMode } else { $null }
    "drive_remote_path" = $RemotePath
    "share_link"        = $ShareLink
    "sha256"            = $FileHash
    "git_sha"           = $(git rev-parse HEAD)
    "upload_success"    = $UploadSuccess
    "upload_mode"       = $UploadMode
}
$PointerData | ConvertTo-Json | Set-Content $PointerJsonPath -Encoding utf8

# Generate MD Summary
$LastRunContent = Get-Content (Join-Path "$DocsDir/antigravity" "LAST_RUN.md") -Raw -ErrorAction SilentlyContinue
$ActivityLogPath = Join-Path "$DocsDir/antigravity" "ACTIVITY_LOG.md"
$ActivityTail = if (Test-Path $ActivityLogPath) { Get-Content $ActivityLogPath -Tail 20 | Out-String } else { "No activity log found." }

$SummaryContent = @"
# Latest Shared Info Summary
**Generated**: $Timestamp (UTC)
**Type**: $ArtifactType
**Mode**: $DirMode
**Remote Path**: $RemotePath
**Zip**: $(if ($ZipName) { $ZipName } else { "N/A" })
**Link**: $(if ($ShareLink) { $ShareLink } else { "N/A" })
**Success**: $UploadSuccess

## Last Run Status
$LastRunContent

## Recent Activity (Tail)
$ActivityTail
"@
$SummaryContent | Set-Content $PointerMdPath -Encoding utf8

Write-Host ">>> Created tracked pointers: SHARED_INFO_LATEST.json, SHARED_INFO_LATEST_SUMMARY.md" -ForegroundColor Cyan

# 6. Cleanup
if ($UploadMode -eq "ZIP" -and $UploadSuccess) {
    Remove-Item $ZipPath -Force
    Write-Host ">>> Local cleanup complete." -ForegroundColor Gray
}

