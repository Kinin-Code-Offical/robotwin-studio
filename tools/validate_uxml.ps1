param(
    [string]$Path = "UnityApp/Assets"
)

$ErrorActionPreference = "Stop"
Write-Host "Validating UXML files in $Path..." -ForegroundColor Cyan

$files = Get-ChildItem -Path $Path -Recurse -Filter *.uxml
$failed = $false

foreach ($file in $files) {
    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($file.FullName)
    }
    catch {
        Write-Host "INVALID UXML: $($file.Name)" -ForegroundColor Red
        Write-Host "  Path: $($file.FullName)" -ForegroundColor Red
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        $failed = $true
    }
}

if ($failed) {
    Write-Error "UXML validation failed. See above for details."
}
else {
    Write-Host "All UXML files are valid." -ForegroundColor Green
}
