$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$iconsPath = Join-Path $repoRoot "UnityApp\Assets\UI\Icons\*.png"
if (Test-Path $iconsPath) {
    Get-ChildItem $iconsPath | ForEach-Object {
        $_.LastWriteTime = Get-Date
        Write-Host "Touched: $($_.Name)"
    }
}
else {
    Write-Host "No icons found at $iconsPath"
}
