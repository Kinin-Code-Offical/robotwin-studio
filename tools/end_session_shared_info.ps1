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

$UploadSuccess = $false
$ShareLink = $null
$FileHash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash

if (Get-Command rclone -ErrorAction SilentlyContinue) {
    try {
        rclone copyto $ZipPath $RemotePath
        Write-Host ">>> Upload Success: $RemotePath" -ForegroundColor Green
        $UploadSuccess = $true
        
        # Try to get link (best effort)
        try {
            $ShareLink = rclone link $RemotePath 2>$null
        }
        catch {
            Write-Warning "Could not generate share link."
        }
    }
    catch {
        Write-Error "Upload failed: $_"
        Write-Warning "Local zip preserved at $ZipPath"
    }
}
else {
    Write-Warning "rclone not found. Skipping upload. Zip remains at $ZipPath"
}

# 5. Generate Tracked Pointers (Always run if zip exists)
$PointerJsonPath = Join-Path "$DocsDir/antigravity" "SHARED_INFO_LATEST.json"
$PointerMdPath = Join-Path "$DocsDir/antigravity" "SHARED_INFO_LATEST_SUMMARY.md"

$PointerData = [Ordered]@{
    "created_at_utc"    = $Timestamp
    "zip_name"          = $ZipName
    "drive_remote_path" = $RemotePath
    "share_link"        = $ShareLink
    "sha256"            = $FileHash
    "git_sha"           = $(git rev-parse HEAD)
    "upload_success"    = $UploadSuccess
}
$PointerData | ConvertTo-Json | Set-Content $PointerJsonPath -Encoding utf8

# Generate MD Summary
$LastRunContent = Get-Content (Join-Path "$DocsDir/antigravity" "LAST_RUN.md") -Raw -ErrorAction SilentlyContinue
$ActivityLogPath = Join-Path "$DocsDir/antigravity" "ACTIVITY_LOG.md"
$ActivityTail = if (Test-Path $ActivityLogPath) { Get-Content $ActivityLogPath -Tail 20 | Out-String } else { "No activity log found." }

$SummaryContent = @"
# Latest Shared Info Summary
**Generated**: $Timestamp (UTC)
**Zip**: $ZipName
**SHA256**: $FileHash
**Link**: $(if ($ShareLink) { $ShareLink } else { "N/A" })

## Last Run Status
$LastRunContent

## Recent Activity (Tail)
$ActivityTail
"@
$SummaryContent | Set-Content $PointerMdPath -Encoding utf8

Write-Host ">>> Created tracked pointers: SHARED_INFO_LATEST.json, SHARED_INFO_LATEST_SUMMARY.md" -ForegroundColor Cyan

# 6. Cleanup (Only if upload success)
if ($UploadSuccess) {
    Remove-Item $ZipPath -Force
    Write-Host ">>> Local cleanup complete." -ForegroundColor Gray
}
else {
    Write-Warning "Skipping cleanup because upload failed or was skipped."
}
