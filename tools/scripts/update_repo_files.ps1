param (
    [switch]$Check
)

$ErrorActionPreference = "Stop"

# Define paths
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$DocsFile = Join-Path $RepoRoot "docs\repo_files.txt"

Push-Location $RepoRoot
# Ensure git is available
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "Git is not installed or not in PATH."
}

# Get files tracked by git, excluding deleted ones, sorted
$files = git ls-files | Sort-Object

if ($Check) {
    # READ MODE: Verify docs/repo_files.txt matches git inventory
    if (-not (Test-Path $DocsFile)) {
        Write-Error "File $DocsFile does not exist. Run without -Check to generate it."
    }

    # Read existing file, filter out comments and empty lines
    $existingContent = Get-Content $DocsFile -Encoding utf8
    $cleanExisting = $existingContent | Where-Object { 
        -not $_.StartsWith("#") -and -not [string]::IsNullOrWhiteSpace($_) 
    }

    # Compare
    $diff = Compare-Object -ReferenceObject $files -DifferenceObject $cleanExisting -CaseSensitive
    
    if ($diff) {
        Write-Host "repo_files.txt is STALE. Differences:" -ForegroundColor Red
        $diff | Format-Table -AutoSize
        exit 1
    }
    else {
        Write-Host "repo_files.txt is UP-TO-DATE." -ForegroundColor Green
        exit 0
    }
}
else {
    # WRITE MODE: Generate docs/repo_files.txt
    
    $content = @()
    $content += "# GeneratedUtc: $([DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))"
    $content += "# Commit: $(git rev-parse --short HEAD)"
    $content += "# FileCount: $($files.Count)"
    $content += ""
    $content += $files

    $content | Set-Content -Path $DocsFile -Encoding utf8
    Write-Host "Updated $DocsFile with $($files.Count) files."
}

Pop-Location
