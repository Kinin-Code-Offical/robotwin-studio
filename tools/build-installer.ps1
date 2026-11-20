# Builds RobotWinStudio-Setup-<version>.exe:
#   1. publishes the app (framework-dependent, win-x64; the setup offers .NET 9 if it is missing)
#   2. compiles the native setup stub (Installer/setup.cpp) with MSVC
#   3. packs the published files into the stub (LZMS, Windows Compression API)
# Usage:  powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-Version 2.0.0] [-SelfContained]
param(
    [string]$Version = "2.0.0",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "builds\installer"
$publish = Join-Path $out "publish"
$obj = Join-Path $out "obj"
New-Item -ItemType Directory -Force $out, $obj | Out-Null
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

Write-Host "1/3 Uygulama yayınlanıyor…"
$sc = if ($SelfContained) { "true" } else { "false" }
dotnet publish "$root\Studio\RobotWin.Studio\RobotWin.Studio.csproj" -c Release -r win-x64 --self-contained $sc `
    -p:Version=$Version -p:DebugType=None -o $publish | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız" }

Write-Host "2/3 Kurulum programı derleniyor (MSVC)…"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio C++ araçları bulunamadı" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
$stub = Join-Path $obj "setup-stub.exe"
$cmd = "`"$vcvars`" >nul && cd /d `"$root\Installer`" && rc /nologo /fo `"$obj\setup.res`" setup.rc && " +
       "cl /nologo /std:c++20 /O2 /EHsc /MT /utf-8 /W3 /Fo`"$obj\\`" setup.cpp `"$obj\setup.res`" /link /SUBSYSTEM:WINDOWS /OUT:`"$stub`""
cmd /c $cmd | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $stub)) { throw "setup.cpp derlenemedi" }

Write-Host "3/3 Paketleniyor…"
$setup = Join-Path $out "RobotWinStudio-Setup-$Version.exe"
$p = Start-Process -FilePath $stub -ArgumentList "--pack", "`"$publish`"", "`"$setup`"" -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0 -or -not (Test-Path $setup)) { throw "Paketleme başarısız ($($p.ExitCode))" }
"{0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB) | Write-Host
