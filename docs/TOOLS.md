# Tools

Everyday development needs only the .NET CLI, the app's own command-line modes and one PowerShell script.

## .NET CLI

| Task | Command |
| --- | --- |
| Build (debug) | `dotnet build RobotWinStudio.sln -c Debug` |
| Build (release) | `dotnet build RobotWinStudio.sln -c Release` |
| Unit tests | `dotnet test RobotWinStudio.sln` |
| Run the app | `builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe` |
| Clean | `dotnet clean RobotWinStudio.sln` (or delete `builds\`) |
| Vulnerable packages | `dotnet list RobotWinStudio.sln package --vulnerable` |

## App command-line modes

`RobotWinStudio.exe` doubles as a tool. All modes exit when done. Snapshot, benchmark, self-test, gallery and overlap runs start from the default settings, panel layout and an empty recent-files list, and save none of them: the user's own settings are untouched.

| Mode | Use |
| --- | --- |
| `--selftest [--selftest-out file]` | Headless end-to-end check of every example (see [TESTING.md](TESTING.md#self-test)). |
| `--snapshot out.png --page <page> [...]` | Renders a page to PNG for documentation and visual checks (see [TESTING.md](TESTING.md#snapshot-testing)). |
| `--gallery <dir> [--gallery-filter text] [--gallery-columns 4] [--gallery-tile 460x340]` | Contact sheets of every code-generated 3D component model. |
| `--overlap-report <file>` | Builds every world template and lists parts that overlap or do not fit their shell. |
| `--bench <file> [--bench-seconds 4] [--bench-filter id,id]` | Frame-rate benchmark: opens every world template (or the ones named), measures it standing and running, and writes fps, frame times, the cost of each part of a frame and where opening the template went. The log is written as it goes; its last line names the step under way. |
| `--startup-report <file>` | A normal start (splash included) that writes each start step's time from the process start, then exits. |
| `--snapshot out.png --page splash` | Draws the splash window as it looks part-way through the start. |
| `--snapshot out.png --page <page> --theme pembe:gul` | Draws a page in a theme and accent, for this run only (nothing is saved). |
| `--api <port> [--world f.rwworld]` | The app as a windowless automation server (see [API.md](API.md)). |
| `--bake-geometry f.rwworld [--out g.json]` | Writes the app's body geometry (model hulls included) beside a world for the headless tool. |
| `--export-logo <dir>` | Exports the logo as PNG sizes + ICO (also on Settings → *Hakkında*). |
| `--register-file-types`, `--unregister-file-types` | Registers / removes `.rwproj`, `.rwworld`, `.rwsim`, `.rwpart`, `.rwlib`, `.rwfp`, `.rwmodel` for the current user (HKCU). Used by the installer. |
| `<file>` | Opens a RobotWin file. |

### Refreshing the README screenshots

```powershell
$exe = "builds\RobotWin.Studio\Release\net9.0-windows\RobotWinStudio.exe"
& $exe --snapshot docs\images\home.png --page home
& $exe --snapshot docs\images\world-ps5-car.png --page world --fault ps5-car
& $exe --snapshot docs\images\world-sonar.png --page world --fault sonar
& $exe --snapshot docs\images\world-servo-arm.png --page world --fault servo-arm
& $exe --snapshot docs\images\modeler.png --page model
```

Pick the `--example`, `--simulate` and `--size` values that show the feature best; the exact arguments used for the committed images are not recorded.

## tools/build-installer.ps1

Publishes the app, compiles the native setup stub with MSVC and packs everything into `builds\installer\RobotWinStudio-Setup-<version>-lite.exe` (framework-dependent) or, with `-SelfContained`, the full `RobotWinStudio-Setup-<version>.exe`. See [BUILD_RELEASE.md](BUILD_RELEASE.md).

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.1.0
```

## External tools the app calls

| Tool | Called by | For |
| --- | --- | --- |
| `arduino-cli` | `Studio/Firmware/ArduinoCompiler.cs` | `arduino-cli compile --fqbn arduino:avr:uno` (or `arduino:avr:nano`) → `sketch.ino.hex` in `%LOCALAPPDATA%\RobotWinStudio\Build` |
| MSVC `cl.exe`, `lib.exe` (via `vcvars64.bat`) | `CoreSim/.../Firmware/Esp32/Esp32SketchCompiler.cs` | ESP32 sketch DLLs and the `rw_esp32hal.lib` HAL library, cached in `%LOCALAPPDATA%\RobotWin\esp32hal-cache` |

## Legacy tools

The rest of `tools/` belongs to the 1.x Unity-based design and is not needed for the current app: `rt_tool.py` (setup/build orchestration for Unity and the C++ engines), `scripts/` (native/firmware build scripts, Blender/FBX/GLB conversion, QA helpers), `debug_console/`, `RobotWinFirmwareMonitor/`, `GoldenTraceRecorder/`, `RpiSmokeTest/`, `rpi/`, `fixtures/`, `issue_bodies/`. Their documentation is in [archive/](archive/README.md).
