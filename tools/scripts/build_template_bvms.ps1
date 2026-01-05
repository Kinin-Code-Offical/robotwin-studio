$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$LogDir = Join-Path $RepoRoot "logs\\tools"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force -Path $LogDir | Out-Null }
$LogFile = Join-Path $LogDir "build_template_bvms.log"

$TemplatesRoot = Join-Path $RepoRoot "RobotWin\\Assets\\Templates"
if (-not (Test-Path $TemplatesRoot)) {
    Write-Error "Templates directory not found at $TemplatesRoot"
    exit 1
}

$Inos = Get-ChildItem -Path $TemplatesRoot -Filter *.ino -Recurse
if ($Inos.Count -eq 0) {
    Write-Error "No .ino files found under $TemplatesRoot"
    exit 1
}

$Python = "python"
$ToolPath = Join-Path $RepoRoot "tools\\scripts\\build_bvm.py"

Write-Host "[Templates] Building BVMs for $($Inos.Count) template(s)..." -ForegroundColor Cyan
foreach ($Ino in $Inos) {
    $CodeRoot = Split-Path $Ino.FullName -Parent
    $OutDir = Join-Path $CodeRoot "builds"
    if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
    $OutPath = Join-Path $OutDir ($Ino.BaseName + ".bvm")

    $Args = @("$ToolPath", "--ino", $Ino.FullName, "--fqbn", "arduino:avr:uno", "--out", $OutPath, "--include", $CodeRoot)
    $LibDir = Join-Path $CodeRoot "lib"
    if (Test-Path $LibDir) {
        $Args += @("--include", $LibDir)
    }

    Write-Host "[Templates] $($Ino.Name) -> $OutPath" -ForegroundColor DarkCyan
    & $Python @Args 2>&1 | Tee-Object -FilePath $LogFile -Append
    if ($LASTEXITCODE -ne 0) {
        Write-Error "BVM build failed for $($Ino.FullName). See $LogFile"
        exit 1
    }
}

Write-Host "[Templates] Done. Log: $LogFile" -ForegroundColor Green
