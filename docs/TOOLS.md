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
| `--snapshot out.png --page world --fault <template> --simulate <s> --wait <s> --perf` | Shows the performance indicator for this run only (nothing is saved); the world, stepped for `--simulate` seconds, then runs live for `--wait` seconds, so the indicator shows each solver's cost per simulated second. |
| `--snapshot out.png --page world --fault <template> --simulate 1 --wait 300 --memory-log file.tsv` | A long live run: the world runs for `--wait` seconds while the managed memory after a full collection and the working set are written every two seconds. A leak shows as a line that keeps rising. |
| `--snapshot out.png --page <page> --theme <theme> --contrast-report file` | Adds to the file every text on the page (and its open windows and popups) that cannot be read in that theme: below 3:1 WCAG contrast against the solid backgrounds behind it, translucent ones mixed in. One line per text: page, theme, ratio, colours, where, text. |
| `python tools/scripts/contrast_check.py <RobotWinStudio.exe> [--quick]` | Runs `--contrast-report` over every page in all 16 themes, and over the guide's picture states (dialogs, popups, tuners) and every Help chapter in four of them; prints the unreadable texts grouped and exits 1 when there is any. |
| `--lang en` | Runs in a language pack's language, for this run only (with `--snapshot`, draws a page in it). |
| `--lang-missing <file>` | With `--snapshot`: adds the page's texts the pack leaves untranslated to the file, one JSON line each (`--page library` adds the whole part catalogue and the examples). Every entry of the run's event logs, world and circuit, is checked too, shown or not. |
| `--api <port> [--world f.rwworld]` | The app as a windowless automation server (see [API.md](API.md)). |
| `--bake-geometry f.rwworld [--out g.json]` | Writes the app's body geometry (model hulls included) beside a world for the headless tool. |
| `--export-logo <dir>` | Exports the logo as PNG sizes + ICO (also on Settings → *Hakkında*). |
| `--register-file-types`, `--unregister-file-types` | Registers / removes `.rwproj`, `.rwworld`, `.rwsim`, `.rwpart`, `.rwlib`, `.rwfp`, `.rwmodel` for the current user (HKCU). Used by the installer. |
| `<file>` | Opens a RobotWin file. |

### Refreshing the guide's pictures

Every picture in the guide is listed in `docs/images/shots.json` with the arguments that make it (and a crop where it is cut from the window). `tools/scripts/doc_images.py` renders them:

```powershell
$exe = "builds\RobotWin.Studio\Release\net9.0-windows\RobotWinStudio.exe"
python tools\scripts\doc_images.py $exe                         # Turkish: docs\images\
python tools\scripts\doc_images.py $exe --lang en               # English: docs\images\en\
python tools\scripts\doc_images.py $exe --lang en home modeler  # only these
```

The English guide uses `docs/images/en/` and falls back to the Turkish picture where an English one is missing. Selectors such as `--frame`, `--arm` and `--pid` take an object's Turkish name; in English they also find the English name an example gives it.

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
