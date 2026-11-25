# Builds the one installer and the components it installs (into builds\installer\release):
#   RobotWinStudio-Setup-<version>.exe        the setup, small: it takes the chosen components from the GitHub release
#                                             (or from its own folder / /SOURCE=), checks each with SHA-256 and installs
#                                             them: full, lite or custom; for this user, all users or portable
#   RobotWinStudio-<version>-app.rwpack       the app (framework-dependent: it finds .NET in its dotnet\ folder first,
#                                             then the PC's)
#   RobotWinStudio-<version>-dotnet.rwpack    the .NET 9 Desktop Runtime, laid out for the app's dotnet\ folder
#   RobotWinStudio-<version>-arduino.rwpack   arduino-cli + the AVR core + the library list (tools\arduino)
#   RobotWinStudio-<version>-occt.rwpack      the Open CASCADE bridge for STEP / IGES (occt\)
# Steps:
#   1. publishes the app (win-x64, framework-dependent, ReadyToRun)
#   2. lays out each component's folder (builds\installer\components\<id>); the Arduino compiler is downloaded once
#      from arduino.cc into builds\installer\arduino-ready and proven by compiling a sketch (-NoArduino leaves it out,
#      for quick local builds); the runtime is this machine's .NET 9 (the SDK's own)
#   3. compiles the native setup (Installer/setup.cpp) with MSVC, packs every component with it (LZMS, Windows
#      Compression API), writes their sizes and SHA-256 into components.h and compiles the setup again with them
#   4. proves the result: the setup installs every component from the release folder into a portable folder, and each
#      file must arrive as it was (-NoCheck skips it)
# Usage:  powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-Version 1.6.1] [-Repo owner/name] [-NoArduino]
param(
    [string]$Version = "1.6.1",
    [string]$Repo = "Kinin-Code-Offical/robotwin-studio",
    [switch]$NoArduino,
    [switch]$NoCheck,
    [string]$ArduinoCliUrl = "https://downloads.arduino.cc/arduino-cli/arduino-cli_latest_Windows_64bit.zip",
    [string]$AvrCore = "1.8.6"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "builds\installer"
$publish = Join-Path $out "publish"
$obj = Join-Path $out "obj"
$parts = Join-Path $out "components"
$release = Join-Path $out "release"
New-Item -ItemType Directory -Force $out, $obj | Out-Null
foreach ($d in $publish, $parts, $release) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
New-Item -ItemType Directory -Force $parts, $release | Out-Null

Write-Host "1/4 Uygulama yayınlanıyor…"
# ReadyToRun: the app's code compiled ahead of time (crossgen2), so the first start and the first opening of each page
# do not wait for the JIT (first window 1174 -> 712 ms on a cold start, World page opened 3.5-4.1 -> 2.9 s).
dotnet publish "$root\Studio\RobotWin.Studio\RobotWin.Studio.csproj" -c Release -r win-x64 --self-contained false `
    -p:Version=$Version -p:DebugType=None -p:PublishReadyToRun=true -o $publish | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız" }

Write-Host "2/4 Bileşenler hazırlanıyor…"
# app: the published app; the Open CASCADE bridge is a component of its own
$app = Join-Path $parts "app"
Move-Item $publish $app
$occtIn = Join-Path $app "occt"
if (Test-Path $occtIn) {
    $occt = Join-Path $parts "occt"
    New-Item -ItemType Directory -Force $occt | Out-Null
    Move-Item $occtIn (Join-Path $occt "occt")
    Copy-Item (Join-Path $root "third_party\occt\LICENSE_LGPL_21.txt"), (Join-Path $root "third_party\occt\OCCT_LGPL_EXCEPTION.txt") (Join-Path $occt "occt")
}

# dotnet: this machine's .NET 9 Desktop Runtime (host\fxr and the two shared frameworks of one version) laid out as a
# .NET installation; RobotWinStudio.exe looks for it in its dotnet\ folder before the PC's (AppHostDotNetSearch).
$dotnetRoot = if ($env:DOTNET_ROOT -and (Test-Path (Join-Path $env:DOTNET_ROOT "shared"))) { $env:DOTNET_ROOT } else { Split-Path -Parent (Get-Command dotnet).Source }
function Get-Versions($dir) { @(Get-ChildItem $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^9\.0\.\d+$' } | ForEach-Object { $_.Name }) }
$desktop = Get-Versions (Join-Path $dotnetRoot "shared\Microsoft.WindowsDesktop.App")
$runtime = @(Get-Versions (Join-Path $dotnetRoot "shared\Microsoft.NETCore.App") | Where-Object { $desktop -contains $_ } | Sort-Object { [version]$_ } -Descending)
if ($runtime.Count -eq 0) { throw ".NET 9 Masaüstü Çalışma Zamanı bulunamadı: $dotnetRoot" }
$rt = $runtime[0]
$fxr = Join-Path $dotnetRoot "host\fxr\$rt"
if (-not (Test-Path $fxr)) {
    $f = @(Get-Versions (Join-Path $dotnetRoot "host\fxr") | Sort-Object { [version]$_ } -Descending)
    if ($f.Count -eq 0) { throw "hostfxr 9.0 bulunamadı: $dotnetRoot" }
    $fxr = Join-Path $dotnetRoot "host\fxr\$($f[0])"
}
$dn = Join-Path $parts "dotnet\dotnet"
foreach ($d in "host\fxr", "shared\Microsoft.NETCore.App", "shared\Microsoft.WindowsDesktop.App") { New-Item -ItemType Directory -Force (Join-Path $dn $d) | Out-Null }
Copy-Item -Recurse $fxr (Join-Path $dn "host\fxr\")
Copy-Item -Recurse (Join-Path $dotnetRoot "shared\Microsoft.NETCore.App\$rt") (Join-Path $dn "shared\Microsoft.NETCore.App\")
Copy-Item -Recurse (Join-Path $dotnetRoot "shared\Microsoft.WindowsDesktop.App\$rt") (Join-Path $dn "shared\Microsoft.WindowsDesktop.App\")
foreach ($f in "LICENSE.txt", "ThirdPartyNotices.txt") { if (Test-Path (Join-Path $dotnetRoot $f)) { Copy-Item (Join-Path $dotnetRoot $f) $dn } }
"  .NET $rt (hostfxr $(Split-Path -Leaf $fxr)) <- $dotnetRoot" | Write-Host

if (-not $NoArduino) {
    Write-Host "  Arduino derleyicisi (arduino-cli + AVR çekirdeği $AvrCore)…"
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

    $dest = Join-Path $parts "arduino\tools\arduino"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $ready "*") $dest
    Copy-Item -Force (Join-Path $root "tools\arduino-OKUBENI.txt") (Join-Path $dest "OKUBENI.txt")
}

Write-Host "3/4 Kurulum programı derleniyor, bileşenler paketleniyor…"
$num = ((($Version -replace '[^0-9.].*$', '').Split('.') | Where-Object { $_ -ne '' }) + @('0','0','0','0'))[0..3] -join ','
@(
    '#pragma once',
    "#define RW_VERSION_W L`"$Version`"",
    "#define RW_VERSION_STR `"$Version`"",
    "#define RW_VERSION_NUM $num"
) | Set-Content -Encoding ascii (Join-Path $obj "version.h")

# The languages the app has: English (its main one), Turkish (the interface's own), then the other built-in packs.
$languages = @()
foreach ($f in Get-ChildItem "$root\Studio\RobotWin.Studio\Lang\*.json" | Sort-Object @{ Expression = { if ($_.BaseName -eq 'en') { 0 } else { 1 } } }, Name) {
    $head = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
    $name = if ($head -match '"language"\s*:\s*"([^"]+)"') { $Matches[1] } else { $f.BaseName }
    $languages += [pscustomobject]@{ Id = $f.BaseName; Name = $name }
    if ($f.BaseName -eq 'en') { $languages += [pscustomobject]@{ Id = 'tr'; Name = 'Türkçe' } }
}
if (-not ($languages | Where-Object { $_.Id -eq 'tr' })) { $languages += [pscustomobject]@{ Id = 'tr'; Name = 'Türkçe' } }

function Write-Components($entries) {
    $lines = @('#pragma once', "#define RW_REPO L`"$Repo`"", 'static const ComponentInfo Components[] =', '{')
    foreach ($e in $entries) { $lines += "    { L`"$($e.Id)`", L`"$($e.File)`", $($e.Size)ULL, $($e.Installed)ULL, $($e.Files)u, `"$($e.Sha256)`" }," }
    $lines += '    { nullptr, nullptr, 0, 0, 0, nullptr }', '};', 'static const LanguageInfo Languages[] =', '{'
    foreach ($l in $languages) { $lines += "    { L`"$($l.Id)`", L`"$($l.Name)`" }," }
    $lines += '    { nullptr, nullptr }', '};'
    [IO.File]::WriteAllLines((Join-Path $obj "components.h"), [string[]]$lines, (New-Object Text.UTF8Encoding $false))
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio C++ araçları bulunamadı" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
function Build-Setup($exe) {
    $cmd = "`"$vcvars`" >nul && cd /d `"$root\Installer`" && rc /nologo /i `"$obj`" /fo `"$obj\setup.res`" setup.rc && " +
           "cl /nologo /I`"$obj`" /std:c++20 /O2 /EHsc /MT /utf-8 /W3 /Fo`"$obj\\`" setup.cpp `"$obj\setup.res`" /link /SUBSYSTEM:WINDOWS /OUT:`"$exe`""
    if (Test-Path $exe) { Remove-Item -Force $exe }
    cmd /c $cmd | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "setup.cpp derlenemedi" }
}

# The setup packs its own components: built once without a list, then again with the list it will install.
Write-Components @()
$packer = Join-Path $obj "setup-packer.exe"
Build-Setup $packer
$entries = @()
foreach ($id in "app", "dotnet", "arduino", "occt") {
    $dir = Join-Path $parts $id
    if (-not (Test-Path $dir)) { continue }
    $file = "RobotWinStudio-$Version-$id.rwpack"
    $target = Join-Path $release $file
    $p = Start-Process -FilePath $packer -ArgumentList "--pack", "`"$dir`"", "`"$target`"" -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0 -or -not (Test-Path $target)) { throw "$id paketlenemedi ($($p.ExitCode))" }
    $files = @(Get-ChildItem $dir -Recurse -File)
    $entries += [pscustomobject]@{
        Id = $id; File = $file; Size = (Get-Item $target).Length; Files = $files.Count
        Installed = [long]($files | Measure-Object -Property Length -Sum).Sum
        Sha256 = (Get-FileHash -Algorithm SHA256 $target).Hash.ToLowerInvariant()
    }
}
Write-Components $entries
$setup = Join-Path $release "RobotWinStudio-Setup-$Version.exe"
Build-Setup $setup
foreach ($e in $entries) { "  {0,-44} {1,8:N1} MB  ({2} dosya, kurulunca {3:N0} MB)" -f $e.File, ($e.Size / 1MB), $e.Files, ($e.Installed / 1MB) | Write-Host }
"  {0,-44} {1,8:N2} MB" -f (Split-Path -Leaf $setup), ((Get-Item $setup).Length / 1MB) | Write-Host

if (-not $NoCheck) {
    Write-Host "4/4 Denetim: kurulum programı her bileşeni bu klasörden taşınabilir bir klasöre kuruyor…"
    $check = Join-Path $obj "install-check"
    if (Test-Path $check) { Remove-Item -Recurse -Force $check }
    $p = Start-Process -FilePath $setup -ArgumentList "/S", "/PORTABLE", "/TYPE=full", "`"/SOURCE=$release`"", "`"/D=$check`"" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Kurulum denetimi başarısız ($($p.ExitCode)); günlük: $env:TEMP\RobotWinStudio-Setup.log" }
    $count = 0
    foreach ($e in $entries) {
        $src = Join-Path $parts $e.Id
        foreach ($f in Get-ChildItem $src -Recurse -File) {
            $rel = $f.FullName.Substring($src.Length + 1)
            $t = Get-Item -LiteralPath (Join-Path $check $rel) -ErrorAction SilentlyContinue
            if (-not $t -or $t.Length -ne $f.Length) { throw "Denetim: $rel kurulmadı" }
            $count++
        }
    }
    foreach ($f in "Uninstall.exe", "install.json", "install.log") { if (-not (Test-Path (Join-Path $check $f))) { throw "Denetim: $f yazılmadı" } }
    "  $count dosya doğru kuruldu ({0:N0} MB)" -f (((Get-ChildItem $check -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1MB) | Write-Host
    Remove-Item -Recurse -Force $check
}
