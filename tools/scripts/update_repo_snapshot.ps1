param (
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$DocsFile = Join-Path $RepoRoot "docs\\repo_files.txt"
$ReadmeFile = Join-Path $RepoRoot "README.md"
$OutputDir = Join-Path $RepoRoot "logs\\tools"
$OutputFile = Join-Path $OutputDir "workspace_snapshot.txt"

function Get-WorkspaceFiles {
    $ExcludedDirs = @(
        "builds",
        "logs",
        "RobotWin\\Library",
        "RobotWin\\Temp",
        "RobotWin\\Logs"
    )
    $ExcludedPattern = '(^|\\)(\\.git|node_modules|\\.venv|\\.vscode)(\\|$)'

    $allFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -Name | ForEach-Object {
        $relativePath = $_

        $exclude = $false
        $top = ($relativePath -split "[/\\\\]")[0]
        if ($top -in @(".git", ".venv", ".vscode", "node_modules")) { $exclude = $true }
        if ($relativePath -match $ExcludedPattern) { $exclude = $true }
        if (-not $exclude) {
            foreach ($dir in $ExcludedDirs) {
                if ($relativePath.StartsWith($dir)) { $exclude = $true; break }
                if ($relativePath -like "RobotWin\\Build*") { $exclude = $true; break }
            }
        }

        if (-not $exclude) { $relativePath }
    } | Sort-Object

    return $allFiles
}

function Get-RepoFiles {
    $files = Get-WorkspaceFiles
    $excludePattern = '(^|\\)(bin|obj)(\\|$)'
    return $files | Where-Object { $_ -notmatch $excludePattern }
}

function Update-RepoFiles {
    param ([string[]]$Files)

    $content = @()
    $content += "# GeneratedUtc: $([DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))"
    $content += "# Commit: $(git rev-parse --short HEAD)"
    $content += "# FileCount: $($Files.Count)"
    $content += ""
    $content += $Files

    $content | Set-Content -Path $DocsFile -Encoding utf8
    Write-Host "Updated $DocsFile with $($Files.Count) files."
}

function Update-WorkspaceSnapshot {
    param ([string[]]$Files)

    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

    $Files | Set-Content -Path $OutputFile -Encoding utf8
    Write-Host "Generated workspace_snapshot.txt with $($Files.Count) files."
}

function Get-TreeLines {
    param (
        [string[]]$Files,
        [int]$MaxDepth = 2
    )

    $root = [ordered]@{}

    foreach ($file in $Files) {
        if ([string]::IsNullOrWhiteSpace($file)) { continue }
        $normalized = $file.Replace('\', '/')
        while ($normalized.StartsWith("../")) {
            $normalized = $normalized.Substring(3)
        }
        if ($normalized.StartsWith("./")) {
            $normalized = $normalized.Substring(2)
        }
        if ([string]::IsNullOrWhiteSpace($normalized)) { continue }
        $parts = $normalized -split "/"
        $node = $root

        for ($i = 0; $i -lt $parts.Length; $i++) {
            if ($i -ge $MaxDepth) {
                if (-not $node.Contains("...")) { $node["..."] = $true }
                break
            }

            $part = $parts[$i]
            $isLast = $i -eq ($parts.Length - 1)

            if ($isLast) {
                if (-not $node.Contains($part)) { $node[$part] = $true }
            }
            else {
                if (-not $node.Contains($part)) { $node[$part] = [ordered]@{} }
                if ($node[$part] -isnot [hashtable]) { break }
                $node = $node[$part]
            }
        }
    }

    function Write-Tree([hashtable]$Node, [string]$Prefix) {
        $dirKeys = $Node.Keys | Where-Object { $Node[$_] -is [hashtable] } | Sort-Object
        $fileKeys = $Node.Keys | Where-Object { $Node[$_] -isnot [hashtable] } | Sort-Object
        if ($fileKeys -contains "...") {
            $fileKeys = ($fileKeys | Where-Object { $_ -ne "..." }) + "..."
        }
        $keys = @($dirKeys + $fileKeys)
        $lines = @()

        for ($idx = 0; $idx -lt $keys.Count; $idx++) {
            $key = $keys[$idx]
            $isLast = $idx -eq ($keys.Count - 1)
            if ($isLast) {
                $connector = "`--"
            }
            else {
                $connector = "|--"
            }
            $lines += "$Prefix$connector $key"

            if ($Node[$key] -is [hashtable]) {
                if ($isLast) {
                    $childPrefix = $Prefix + "    "
                }
                else {
                    $childPrefix = $Prefix + "|   "
                }
                $lines += Write-Tree -Node $Node[$key] -Prefix $childPrefix
            }
        }

        return $lines
    }

    $lines = @(".")
    $lines += Write-Tree -Node $root -Prefix ""
    return $lines
}

function Update-ReadmeTree {
    param ([string[]]$Files)

    if (-not (Test-Path $ReadmeFile)) {
        Write-Warning "README.md not found at $ReadmeFile. Skipping folder tree update."
        return
    }

    $begin = "<!-- BEGIN FOLDER_TREE -->"
    $end = "<!-- END FOLDER_TREE -->"
    $treeFiles = $Files | Where-Object { -not $_.EndsWith(".meta") }
    $treeLines = Get-TreeLines -Files $treeFiles -MaxDepth 3
    $nl = [Environment]::NewLine
    $fence = '```'
    $treeBlock = @()
    $treeBlock += "## Project Tree"
    $treeBlock += ""
    $treeBlock += "$fence" + "text"
    $treeBlock += $treeLines
    $treeBlock += $fence

    $content = Get-Content $ReadmeFile -Raw -Encoding utf8
    if ($content.Contains($begin) -and $content.Contains($end)) {
        $startIndex = $content.IndexOf($begin)
        $endIndex = $content.IndexOf($end)
        if ($startIndex -ge 0 -and $endIndex -gt $startIndex) {
            $endIndex += $end.Length
            $before = $content.Substring(0, $startIndex)
            $after = $content.Substring($endIndex)
            $content = $before + $begin + $nl + ($treeBlock -join $nl) + $nl + $end + $after
        }
    }
    else {
        $content = $content.TrimEnd() + $nl + $nl + $begin + $nl + ($treeBlock -join $nl) + $nl + $end + $nl
    }

    $content | Set-Content -Path $ReadmeFile -Encoding utf8
    Write-Host "Updated folder tree in README.md."
}

$repoFiles = Get-RepoFiles
$workspaceFiles = Get-WorkspaceFiles

if ($Check) {
    if (-not (Test-Path $DocsFile)) {
        Write-Error "File $DocsFile does not exist. Run without -Check to generate it."
    }

    $existingContent = Get-Content $DocsFile -Encoding utf8
    $cleanExisting = $existingContent | Where-Object {
        -not $_.StartsWith("#") -and -not [string]::IsNullOrWhiteSpace($_)
    }

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

Push-Location $RepoRoot
Update-RepoFiles -Files $repoFiles
Update-WorkspaceSnapshot -Files $workspaceFiles
Update-ReadmeTree -Files $repoFiles
Pop-Location
