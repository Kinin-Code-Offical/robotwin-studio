param ()

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$LogDir = Join-Path $RepoRoot "logs\\tools"
$LogFile = Join-Path $LogDir "rpi_smoke_test.log"

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
}

Push-Location $RepoRoot
try {
    & dotnet run --project tools/RpiSmokeTest/RpiSmokeTest.csproj 2>&1 | Tee-Object -FilePath $LogFile
}
finally {
    Pop-Location
}
