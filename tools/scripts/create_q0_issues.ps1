param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-IssueNumberByTitle([string]$title) {
    $json = gh issue list --state all --search $title --json number, title 2>$null | ConvertFrom-Json
    foreach ($i in $json) {
        if ($i.title -eq $title) { return [int]$i.number }
    }
    return $null
}

function New-IssueIfMissing([string]$title, [string]$body) {
    $existing = Find-IssueNumberByTitle $title
    if ($existing) {
        Write-Host "Exists: #$existing $title"
        return $existing
    }

    if ($DryRun) {
        Write-Host "Would create: $title"
        return $null
    }

    $url = gh issue create --title $title --body $body
    if ($url -match '/issues/(\d+)$') { 
        $n = [int]$Matches[1]
        Write-Host "Created: #$n $title"
        return $n
    }

    throw "Could not parse issue number from: $url"
}

$body1 = @'
Goal: prevent editor-only APIs from leaking into player builds.

Local implementation (pending PR):
- Added runtime API audit script: tools/scripts/audit_runtime_apis.py
- Hooked audit into tooling: tools/rt_tool.py

Acceptance:
- Tooling fails fast when forbidden namespaces/assemblies appear in runtime scripts.
- Documented in tooling docs if needed.
'@

$body2 = @'
Goal: make firmware host configuration generic across MCU profiles.

Local implementation (pending PR):
- RobotWin/Assets/Scripts/Game/SessionManager.cs
- RobotWin/Assets/Scripts/Circuit/CircuitController.cs

Acceptance:
- Firmware host override is not Arduino-specific.
- UI/tooltips reflect generic naming.
'@

$body3 = @'
Goal: selecting a non-AVR profile should not attempt to run Arduino firmware.

Local implementation (pending PR):
- RobotWin/Assets/Scripts/Game/SimHost.cs

Acceptance:
- Clear warning and clean short-circuit when no supported MCU profiles exist.
'@

$body4 = @'
Goal: keep tooling outputs consistent and auditable across build steps.

Local implementation (pending PR):
- Added build output audit: tools/scripts/audit_build_outputs.py
- Hooked audit into tooling: tools/rt_tool.py

Acceptance:
- Audit report written to logs/tools/build_output_audit.log
- Tooling can flag unexpected/missing outputs.
'@

$n1 = New-IssueIfMissing 'Q0-01 Remove editor-only APIs from runtime builds (no System.Windows.Forms in player)' $body1
$n2 = New-IssueIfMissing 'Q0-02 Generalize firmware host naming/pathing for non-Arduino boards' $body2
$n3 = New-IssueIfMissing 'Q0-03 Non-AVR profile QA path (STM32/RPi selection should disable firmware cleanly)' $body3
$n4 = New-IssueIfMissing 'Q0-04 Build/log output audit across tools (builds/ + logs/ consistency)' $body4

Write-Host "Q0 issues: Q0-01=#$n1 Q0-02=#$n2 Q0-03=#$n3 Q0-04=#$n4"
