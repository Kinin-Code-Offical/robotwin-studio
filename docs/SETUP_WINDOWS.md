# Windows setup

How to set up a Windows machine to build, run and test RobotWin Studio from source. To just *use* the app, install it from [Releases](https://github.com/Kinin-Code-Offical/robotwin-studio/releases) instead (see the [README](../README.md#indirme-ve-kurulum)).

## Requirements

| Tool | Needed for | Notes |
| --- | --- | --- |
| Windows 10 or 11, x64 | everything | The app is WPF and Windows-only. |
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | building the app and tests | `global.json` pins 9.0.112 with `rollForward: latestFeature`. |
| Git | cloning | |
| Visual Studio 2022 or Build Tools, **Desktop development with C++** workload | ESP32 sketches, building the installer | Provides `cl.exe`, `lib.exe`, `rc.exe` and `vcvars64.bat`. Not needed to build or run the app itself. |
| `arduino-cli` + `arduino:avr` core | compiling your own Arduino Uno/Nano sketches | Bundled examples ship as prebuilt HEX files. |

Install the optional tools with winget:

```powershell
winget install Microsoft.DotNet.SDK.9
winget install ArduinoSA.CLI
arduino-cli core install arduino:avr
# Visual Studio Build Tools with the C++ workload:
winget install Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive"
```

## Clone, build, run

```powershell
git clone https://github.com/Kinin-Code-Offical/robotwin-studio.git
cd robotwin-studio
dotnet build RobotWinStudio.sln -c Debug
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe
```

`Directory.Build.props` redirects all `bin/` and `obj/` output to `builds\<ProjectName>\`, so the repository tree stays clean. Use `-c Release` for an optimised build (`builds\RobotWin.Studio\Release\net9.0-windows\`).

Visual Studio 2022 (17.12 or later, for .NET 9) or VS Code with the C# Dev Kit can open `RobotWinStudio.sln` directly; set **RobotWin.Studio** as the startup project.

## How the app finds the compilers

- **arduino-cli:** Settings → *Derleyiciler* → *arduino-cli yolu*, then `%ProgramFiles%\Arduino CLI\`, `%LOCALAPPDATA%\Programs\Arduino CLI\`, `%LOCALAPPDATA%\Microsoft\WinGet\Links\`, then `PATH`.
- **MSVC (ESP32):** Settings → *vcvars64.bat yolu*, then `vswhere.exe`, then the standard `Microsoft Visual Studio\{18,2022,2019}\{BuildTools,Community,Professional,Enterprise}\VC\Auxiliary\Build\vcvars64.bat` locations.

## Verify the setup

```powershell
dotnet test RobotWinStudio.sln
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
Get-Content selftest.txt -Tail 1   # SELFTEST PASS
```

Then open the app, load the *Arduino: Gerçek Firmware* example from the Home page, go to **Kod** (Ctrl+3) and press **Derle** (F7) to check arduino-cli; for ESP32, open *ESP32: PWM Işık Ayarı* and start the simulation (F5): the first run compiles the sketch with MSVC, later runs use the cache.

Next: [TESTING.md](TESTING.md), [BUILD_RELEASE.md](BUILD_RELEASE.md), [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
