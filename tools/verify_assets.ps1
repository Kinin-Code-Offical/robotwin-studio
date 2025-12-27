$iconPath = "c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\UnityApp\Assets\UI\Icons\home.png"
if (Test-Path $iconPath) {
    Write-Host "Icons verified."
}
else {
    Write-Host "Icons missing. Run 'fetch_png_icons.py' immediately."
}
