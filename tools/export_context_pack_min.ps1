param (
    [string]$RepoString = "" 
)

$ErrorActionPreference = "Stop"

# Paths
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$DocsDir = Join-Path $RepoRoot "docs\antigravity"
$OutputFile = Join-Path $DocsDir "context_pack_min.json"

# Check dependencies
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "Git not found."
}
$HasGh = [bool](Get-Command gh -ErrorAction SilentlyContinue)

# detect repo if not provided
if ([string]::IsNullOrWhiteSpace($RepoString)) {
    try {
        $RepoString = git remote get-url origin
        # Simple parse for https://github.com/owner/repo.git or git@github.com:owner/repo.git
        if ($RepoString -match "github\.com[:/]([^/]+)/([^/.]+)") {
            $RepoString = "$($matches[1])/$($matches[2])"
        }
    }
    catch {
        Write-Warning "Could not detect repo from git remote."
    }
}

Write-Host "Exporting Context Pack for $RepoString..."

# Data Containers
$Data = [Ordered]@{}

# 1. Meta
$Data["meta"] = [Ordered]@{
    "generatedUtc"     = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    "headSha"          = $(git rev-parse HEAD)
    "workingTreeClean" = [bool]((-not $(git status --porcelain)))
    "schemaVersion"    = "1.0"
}

if ($HasGh) {
    # 2. Repo Info
    $Data["repo"] = gh repo view $RepoString --json nameWithOwner, description, url, defaultBranchRef, updatedAt | ConvertFrom-Json

    # 3. Status Log Issue (#15)
    try {
        $Data["status_log_issue_15"] = gh issue view 15 --repo $RepoString --json number, title, url, updatedAt, comments, labels, body | ConvertFrom-Json
    }
    catch {
        $Data["status_log_issue_15"] = "Not Found or Error"
    }

    # 4. MVP Issues
    $Data["issues_open_mvp"] = gh issue list --repo $RepoString --state open --label mvp --limit 100 --json number, title, url, updatedAt, labels, milestone, body | ConvertFrom-Json

    # 5. Open PRs
    $Data["prs_open"] = gh pr list --repo $RepoString --state open --limit 50 --json number, title, url, updatedAt, headRefName, baseRefName, author, labels | ConvertFrom-Json

    # 6. Recent Merged PRs
    $Data["prs_merged_recent"] = gh pr list --repo $RepoString --state merged --limit 20 --json number, title, url, mergedAt, headRefName, baseRefName, author | ConvertFrom-Json

    # 7. Actions Runs
    $Data["actions_runs_recent"] = gh run list --repo $RepoString --limit 20 --json databaseId, status, conclusion, createdAt, event, workflowName, headBranch, url | ConvertFrom-Json

}
else {
    $Data["error"] = "gh cli not found, github data skipped."
}

# 8. Branch Parity
Write-Host "Checking branch parity..."
git fetch --all --prune | Out-Null
$remotes = git for-each-ref --format="%(refname:short)" refs/remotes/origin
$Parity = [Ordered]@{}
foreach ($branch in $remotes) {
    if ($branch -eq "origin/main" -or $branch -eq "origin/HEAD") { continue }
    # count commits behind/ahead of main
    $counts = git rev-list --left-right --count origin/main...$branch
    $parts = $counts -split '\s+'
    $Parity[$branch] = "Behind: $($parts[0]), Ahead: $($parts[1]) (vs origin/main)"
}
$Data["branch_parity"] = $Parity

# 9. Run Logs
$Data["run_logs"] = [Ordered]@{}
$LastRunPath = Join-Path $DocsDir "LAST_RUN.md"
$ActivityLogPath = Join-Path $DocsDir "ACTIVITY_LOG.md"

if (Test-Path $LastRunPath) {
    $Data["run_logs"]["LAST_RUN.md"] = Get-Content $LastRunPath -Raw -Encoding utf8
}
if (Test-Path $ActivityLogPath) {
    # limit activity log to last 200 lines to avoid bloat
    $logContent = Get-Content $ActivityLogPath -Encoding utf8
    if ($logContent.Count -gt 200) {
        $Data["run_logs"]["ACTIVITY_LOG.md"] = $logContent[-200..-1] -join "`n"
        $Data["run_logs"]["ACTIVITY_LOG.md_truncated"] = $true
    }
    else {
        $Data["run_logs"]["ACTIVITY_LOG.md"] = $logContent -join "`n"
    }
}

# Write Output
$Json = $Data | ConvertTo-Json -Depth 20
$Json | Set-Content $OutputFile -Encoding utf8

Write-Host "Context Pack written to $OutputFile"
