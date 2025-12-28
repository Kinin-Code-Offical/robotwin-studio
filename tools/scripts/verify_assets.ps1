$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$iconPath = Join-Path $repoRoot "UnityApp\Assets\UI\Icons\home.png"
if (Test-Path $iconPath) {
    Write-Host "Icons verified."
}
else {
    Write-Host "Icons missing. Run 'python tools\\rt_tool.py fetch-icons' immediately."
}
