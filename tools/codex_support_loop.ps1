param(
    [int]$SleepSeconds = 30,
    [int]$BuildFirmwareEvery = 0,
    [int]$BuildStandaloneEvery = 0,
    [int]$CoreSimTestsEvery = 0,
    [int]$PlanDriftCheckEvery = 10,
    [string]$LogPath = "logs/codex_watch.log",
    [string]$FeedbackPath = "logs/codex_feedback.log"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Ensure log directory exists
$logDir = Split-Path -Parent (Join-Path $repoRoot $LogPath)
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }

$feedbackDir = Split-Path -Parent (Join-Path $repoRoot $FeedbackPath)
if (-not (Test-Path $feedbackDir)) { New-Item -ItemType Directory -Path $feedbackDir | Out-Null }

$lockPath = Join-Path $repoRoot "logs/codex_support_loop.lock"
if (Test-Path $lockPath) {
    # Best-effort: avoid multiple concurrent loops writing interleaved logs.
    Write-Warning "codex_support_loop: lock exists at $lockPath (another instance may be running). Exiting."
    exit 2
}
New-Item -ItemType File -Path $lockPath -Force | Out-Null

function Write-Log([string]$msg) {
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$ts] $msg"
    Add-Content -Path (Join-Path $repoRoot $LogPath) -Value $line
}

function Write-Feedback([string]$msg) {
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$ts] $msg"
    Add-Content -Path (Join-Path $repoRoot $FeedbackPath) -Value $line
    Add-Content -Path (Join-Path $repoRoot $LogPath) -Value $line
}

function Invoke-Step([string]$label, [scriptblock]$action) {
    Write-Log "$label START"
    try {
        & $action | Out-Null
        $code = $LASTEXITCODE
        if ($code -ne 0) {
            Write-Feedback "$label FAILED exit=$code"
        }
        Write-Log "$label EXIT=$code"
    }
    catch {
        Write-Feedback "$label ERROR=$($_.Exception.Message)"
    }
}

function Check-PlanDrift {
    $todoPath = Join-Path $repoRoot "docs/implementation_plan_todo.md"
    $planPath = Join-Path $repoRoot "docs/implementation_plan.json"
    if (-not (Test-Path $todoPath) -or -not (Test-Path $planPath)) {
        return
    }

    try {
        $doneNums = @()
        foreach ($line in Get-Content -Path $todoPath -ErrorAction Stop) {
            if ($line -match "^-\s*DONE(?:\s*\((?:verified|needs rewrite)\))?\s+(\d+)\b") {
                $doneNums += [int]$Matches[1]
            }
        }
        $doneNums = $doneNums | Sort-Object -Unique

        $plan = Get-Content -Path $planPath -Raw -ErrorAction Stop | ConvertFrom-Json
        $completed = @()
        if ($plan.progress -and $plan.progress.completedIssues) {
            $completed = @($plan.progress.completedIssues | ForEach-Object { [int]$_ }) | Sort-Object -Unique
        }

        $extra = Compare-Object -ReferenceObject $doneNums -DifferenceObject $completed -PassThru | Where-Object { $_.SideIndicator -eq ">=" }
        $missing = Compare-Object -ReferenceObject $completed -DifferenceObject $doneNums -PassThru | Where-Object { $_.SideIndicator -eq "<=" }

        if ($extra.Count -gt 0 -or $missing.Count -gt 0) {
            $extraList = ($extra | Sort-Object) -join ","
            $missingList = ($missing | Sort-Object) -join ","
            Write-Feedback "PLAN_DRIFT detected: todo_done_not_in_plan=[$extraList] plan_done_not_in_todo=[$missingList]"
        }
    }
    catch {
        Write-Feedback "PLAN_DRIFT check error: $($_.Exception.Message)"
    }
}

Write-Log "codex_support_loop started pid=$PID (SleepSeconds=$SleepSeconds, BuildFirmwareEvery=$BuildFirmwareEvery, BuildStandaloneEvery=$BuildStandaloneEvery, CoreSimTestsEvery=$CoreSimTestsEvery, PlanDriftCheckEvery=$PlanDriftCheckEvery)"

try {
    $iter = 0
    while ($true) {
        $iter++

        try {
            $changes = (git status --porcelain) 2>$null
            $count = if ($changes) { $changes.Count } else { 0 }
            Write-Log "iter=$iter git_dirty_files=$count"

            if ($PlanDriftCheckEvery -gt 0 -and ($iter % $PlanDriftCheckEvery) -eq 0) {
                Check-PlanDrift
            }

            if ($BuildFirmwareEvery -gt 0 -and ($iter % $BuildFirmwareEvery) -eq 0) {
                Invoke-Step "iter=$iter build-firmware" { py .\tools\rt_tool.py build-firmware }
            }

            if ($BuildStandaloneEvery -gt 0 -and ($iter % $BuildStandaloneEvery) -eq 0) {
                Invoke-Step "iter=$iter build-standalone" { py .\tools\rt_tool.py build-standalone }
            }

            if ($CoreSimTestsEvery -gt 0 -and ($iter % $CoreSimTestsEvery) -eq 0) {
                Invoke-Step "iter=$iter dotnet test" { dotnet test .\CoreSim\tests\RobotTwin.CoreSim.Tests\RobotTwin.CoreSim.Tests.csproj }
            }
        }
        catch {
            Write-Log "iter=$iter ERROR=$($_.Exception.Message)"
        }

        Start-Sleep -Seconds $SleepSeconds
    }
}
finally {
    if (Test-Path $lockPath) {
        Remove-Item -Path $lockPath -Force -ErrorAction SilentlyContinue
    }
}
