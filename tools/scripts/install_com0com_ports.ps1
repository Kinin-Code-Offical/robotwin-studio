param(
    [Parameter(Mandatory = $true)]
    [string]$SetupcPath,
    [Parameter(Mandatory = $true)]
    [string]$Pairs
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SetupcPath)) {
    Write-Error "setupc.exe not found at $SetupcPath"
    exit 1
}

$driverDir = Split-Path -Parent $SetupcPath
if (-not (Test-Path $driverDir)) {
    Write-Error "Driver directory not found: $driverDir"
    exit 1
}

$pairList = $Pairs -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($pairList.Count -eq 0) {
    Write-Host "No COM port pairs requested."
    exit 0
}

Push-Location $driverDir
foreach ($pair in $pairList) {
    $ports = $pair -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
    if ($ports.Count -lt 2) { continue }
    $a = $ports[0]
    $b = $ports[1]
    Write-Host "Installing COM pair: $a <-> $b"
    & $SetupcPath install "PortName=$a" "PortName=$b"
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to install COM pair: $a <-> $b"
    }
}
Pop-Location
