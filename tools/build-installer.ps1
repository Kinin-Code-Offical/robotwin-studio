# Builds the installers:
#   RobotWinStudio-Setup-<version>.exe       full package, carries .NET (-SelfContained)
#   RobotWinStudio-Setup-<version>-lite.exe  small package, needs the .NET 9 Desktop Runtime (the setup offers it)
#   1. publishes the app (win-x64; self-contained or framework-dependent)
#   1b. adds the Arduino compiler (arduino-cli + the AVR core + the library list) under tools\arduino, so Uno / Nano
#       code builds right after installing; downloaded once from arduino.cc into builds\installer\arduino-ready and
#       proven by compiling a sketch before it is packed (-NoArduino leaves it out, for quick local builds)
#   2. compiles the native setup stub (Installer/setup.cpp) with MSVC
#   3. packs the published files into the stub (LZMS, Windows Compression API)
# Usage:  powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-Version 1.6.0] [-SelfContained] [-NoArduino]
param(
    [string]$Version = "1.6.0",
    [switch]$SelfContained,
    [switch]$NoArduino,
    [string]$ArduinoCliUrl = "https://downloads.arduino.cc/arduino-cli/arduino-cli_latest_Windows_64bit.zip",
    [string]$AvrCore = "1.8.6"
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

if (-not $NoArduino) {
    Write-Host "1b/3 Arduino derleyicisi ekleniyor (arduino-cli + AVR çekirdeği $AvrCore)…"
    $ready = Join-Path $out "arduino-ready"
    $avrDir = Join-Path $ready "data\packages\arduino\hardware\avr"
    if (-not (Test-Path $avrDir)) {
        if (Test-Path $ready) { Remove-Item -Recurse -Force $ready }
        New-Item -ItemType Directory -Force $ready | Out-Null
        $zip = Join-Path $obj "arduino-cli.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = "SilentlyContinue"   # Windows PowerShell's progress bar slows downloads and unzips many times over
        Invoke-WebRequest -UseBasicParsing -Uri $ArduinoCliUrl -OutFile $zip
        Expand-Archive -Force $zip -DestinationPath $ready
        $cli = Join-Path $ready "arduino-cli.exe"
        if (-not (Test-Path $cli)) { throw "arduino-cli.exe zip içinde yok" }
        $env:ARDUINO_DIRECTORIES_DATA = Join-Path $ready "data"
        $env:ARDUINO_DIRECTORIES_DOWNLOADS = Join-Path $obj "arduino-downloads"
        $env:ARDUINO_DIRECTORIES_USER = Join-Path $obj "arduino-user"
        # arduino-cli reports progress and warnings on stderr; under "Stop" Windows PowerShell would turn a line there
        # into a terminating error. Each step's exit code is checked instead.
        $eap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            & $cli core update-index | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "arduino-cli core update-index başarısız" }
            & $cli core install "arduino:avr@$AvrCore" | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "arduino-cli core install arduino:avr@$AvrCore başarısız" }
            & $cli lib update-index | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "arduino-cli lib update-index başarısız" }
            # Proof that the toolchain builds before it is packed: a blink sketch for the Uno.
            $sketch = Join-Path $obj "arduino-check\blink"
            New-Item -ItemType Directory -Force $sketch | Out-Null
            "void setup() { pinMode(13, OUTPUT); }`nvoid loop() { digitalWrite(13, !digitalRead(13)); delay(500); }`n" |
                Set-Content -Encoding ascii (Join-Path $sketch "blink.ino")
            & $cli compile --fqbn arduino:avr:uno --output-dir (Join-Path $obj "arduino-check\out") $sketch | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "Arduino derleyicisi örnek çizimi derleyemedi" }
        }
        finally {
            $ErrorActionPreference = $eap
            Remove-Item Env:ARDUINO_DIRECTORIES_DATA, Env:ARDUINO_DIRECTORIES_DOWNLOADS, Env:ARDUINO_DIRECTORIES_USER -ErrorAction SilentlyContinue
        }
    }

    $dest = Join-Path $publish "tools\arduino"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $ready "*") $dest
    Copy-Item -Force (Join-Path $root "tools\arduino-OKUBENI.txt") (Join-Path $dest "OKUBENI.txt")
    "{0}  ({1:N1} MB)" -f $dest, ((Get-ChildItem $dest -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB) | Write-Host
}

Write-Host "2/3 Kurulum programı derleniyor (MSVC)…"
$num = ($Version.Split('.') + @('0','0','0','0'))[0..3] -join ','
@(
    '#pragma once',
    "#define RW_VERSION_W L`"$Version`"",
    "#define RW_VERSION_STR `"$Version`"",
    "#define RW_VERSION_NUM $num"
) | Set-Content -Encoding ascii (Join-Path $obj "version.h")
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio C++ araçları bulunamadı" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
$stub = Join-Path $obj "setup-stub.exe"
$cmd = "`"$vcvars`" >nul && cd /d `"$root\Installer`" && rc /nologo /i `"$obj`" /fo `"$obj\setup.res`" setup.rc && " +
       "cl /nologo /I`"$obj`" /std:c++20 /O2 /EHsc /MT /utf-8 /W3 /Fo`"$obj\\`" setup.cpp `"$obj\setup.res`" /link /SUBSYSTEM:WINDOWS /OUT:`"$stub`""
cmd /c $cmd | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $stub)) { throw "setup.cpp derlenemedi" }

Write-Host "3/3 Paketleniyor…"
# The full package carries .NET itself and keeps the plain name; the small one is "-lite".
$suffix = if ($SelfContained) { "" } else { "-lite" }
$setup = Join-Path $out "RobotWinStudio-Setup-$Version$suffix.exe"
$p = Start-Process -FilePath $stub -ArgumentList "--pack", "`"$publish`"", "`"$setup`"" -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0 -or -not (Test-Path $setup)) { throw "Paketleme başarısız ($($p.ExitCode))" }
"{0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB) | Write-Host
