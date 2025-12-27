$iconsPath = "c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\UnityApp\Assets\UI\Icons\*.svg"
if (Test-Path $iconsPath) {
    Get-ChildItem $iconsPath | ForEach-Object {
        $_.LastWriteTime = Get-Date
        Write-Host "Touched: $($_.Name)"
    }
}
else {
    Write-Host "No icons found at $iconsPath"
}
