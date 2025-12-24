param (
    [ValidateSet("MIN", "FULL")]
    [string]$Mode = "MIN",
    [string]$OutDir = "",
    [string]$Repo = "",
    [switch]$SkipGh
)

$ErrorActionPreference = "Stop"

# --- Constants & Paths ---
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot "docs\antigravity\context_exports\latest"
}
else {
    # Resolve relative to current location if needed
    if (-not [System.IO.Path]::IsPathRooted($OutDir)) {
        $OutDir = Join-Path (Get-Location) $OutDir
    }
}

# Ensure Output Directory
if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

Write-Host "Exporting Context Pack ($Mode) to $OutDir..."

# --- 1. Manifest ---
$Manifest = [Ordered]@{
    "generatedUtc" = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    "mode"         = $Mode
    "repo"         = $Repo
    "headSha"      = $(git rev-parse HEAD)
    "branch"       = $(git branch --show-current)
    "toolVersion"  = "1.0"
}
if ([string]::IsNullOrWhiteSpace($Manifest["repo"])) {
    try {
        $origin = git remote get-url origin
        # Simple parse
        if ($origin -match "github\.com[:/]([^/]+)/([^/.]+)") {
            $Manifest["repo"] = "$($matches[1])/$($matches[2])"
        }
    }
    catch { 
        $Manifest["repo"] = "unknown"
    }
}
$RepoString = $Manifest["repo"]
$Manifest | ConvertTo-Json | Set-Content (Join-Path $OutDir "manifest.json") -Encoding utf8

# --- 2. GitHub Data (if not skipped) ---
if (-not $SkipGh) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Warning "gh CLI not found. Skipping GitHub options."
    }
    else {
        Write-Host "Fetching GitHub data..."
        
        # Helper
        function Export-Gh {
            param($Cmd, $Name)
            try {
                $res = Invoke-Expression $Cmd
                $res | Out-File (Join-Path $OutDir "$Name.json") -Encoding utf8
            }
            catch {
                Write-Warning "Failed to export $Name : $_"
            }
        }

        # Status Log Issue #15
        Export-Gh "gh issue view 15 --repo $RepoString --json number,title,url,updatedAt,body,comments,labels" "status_log_issue_15"
        
        # Open MVP Issues
        Export-Gh "gh issue list --repo $RepoString --label mvp --state open --limit 200 --json number,title,url,updatedAt,labels,milestone,body,comments" "issues_open_mvp"
        
        # Open PRs
        Export-Gh "gh pr list --repo $RepoString --state open --limit 200 --json number,title,url,updatedAt,headRefName,baseRefName,author,labels" "prs_open_list"
        
        # Merged PRs
        Export-Gh "gh pr list --repo $RepoString --state merged --limit 30 --json number,title,url,mergedAt,headRefName,baseRefName,author" "prs_merged_recent"
        
        # Actions Runs
        Export-Gh "gh run list --repo $RepoString --limit 30 --json databaseId,status,conclusion,createdAt,event,workflowName,headBranch,url" "actions_runs_recent"

        if ($Mode -eq "FULL") {
            # Repo Meta
            Export-Gh "gh repo view $RepoString --json nameWithOwner,description,url,defaultBranchRef,updatedAt" "repo_meta"
            # Milestones
            # gh api or issue list --milestone * ? gh api is cleaner but issue list works. Let's use api if possible or skip.
            # Using simple issue list for milestones? No, api is better.
            Export-Gh "gh api repos/$RepoString/milestones" "milestones"
            # Labels
            Export-Gh "gh api repos/$RepoString/labels" "labels"
        }
    }
}
else {
    Write-Host "Skipping GitHub data (-SkipGh)."
}

# --- 3. Git-Based Exports ---
Write-Host "Calculating branch parity..."
git fetch --all --prune *>&1 | Out-Null
$remotes = git for-each-ref --format="%(refname:short)" refs/remotes/origin
$Parity = [Ordered]@{}
foreach ($branch in $remotes) {
    if ($branch -eq "origin/main" -or $branch -eq "origin/HEAD") { continue }
    $counts = git rev-list --left-right --count origin/main...$branch
    $parts = $counts -split '\s+'
    $Parity[$branch] = "Behind: $($parts[0]), Ahead: $($parts[1]) (vs origin/main)"
}
$Parity | ConvertTo-Json | Set-Content (Join-Path $OutDir "branch_parity.json") -Encoding utf8

if ($Mode -eq "FULL") {
    git log -n 50 --oneline > (Join-Path $OutDir "main_commits_50.txt")
    git ls-files > (Join-Path $OutDir "git_ls_files.txt")
}

# --- 4. Logs Copy ---
Write-Host "Copying logs..."
$DocsDir = Join-Path $RepoRoot "docs\antigravity"
if (Test-Path (Join-Path $DocsDir "LAST_RUN.md")) {
    Copy-Item (Join-Path $DocsDir "LAST_RUN.md") -Destination $OutDir -Force
}
if (Test-Path (Join-Path $DocsDir "ACTIVITY_LOG.md")) {
    Copy-Item (Join-Path $DocsDir "ACTIVITY_LOG.md") -Destination $OutDir -Force
}

Write-Host "Context Pack Export Complete at $OutDir" -ForegroundColor Green
