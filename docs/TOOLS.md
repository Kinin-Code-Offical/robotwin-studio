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
| `--bench <file> [--bench-seconds 4] [--bench-filter id,id]` | Frame-rate benchmark: opens every world template (or the ones named), measures it standing and running, and writes fps, frame times, the cost of each part of a frame and where opening the template went. While running it also writes how much of real time the simulation kept up with, and where it ran (its own thread or the UI's frames). The log is written as it goes; its last line names the step under way. |
| `--perf-check <file> [--perf-baseline tools/perf/baseline.json] [--perf-tolerance 25] [--perf-seconds 1] [--perf-filter id,id] [--perf-update]` | Performance regression check: every world template runs headless (a warm-up, then the timed simulated seconds, best of two) and its cost per simulated second is compared with the baseline. Each cost is also divided by a fixed CPU workload timed in the same run (64 boxes falling for 1 s), so a baseline from another computer still compares; on the same computer the raw times are compared. Slower than the tolerance (%) is a regression: listed, and the exit code is 1. With no baseline, or with `--perf-update`, the run becomes the baseline. Each line also says where the time went: the boards' code, the circuit (with its solver's steps per simulated second) and the mechanics' parts. The bodies have the shapes the world page gives the physics (the parts' 3D hulls), so the costs are those of the app. The check starts the app again with .NET's tiered compilation off, so every world is measured with fully optimised code whatever was measured before it. |
| `--startup-report <file>` | A normal start (splash included) that writes each start step's time from the process start, then exits. |
| `--snapshot out.png --page splash` | Draws the splash window as it looks part-way through the start. |
| `--snapshot out.png --page <page> --theme pembe:gul` | Draws a page in a theme and accent, for this run only (nothing is saved). |
| `--sim-thread on\|off` | With `--bench` or `--snapshot`: runs live simulations on their own thread (the default) or inside the UI's frames, for this run only, to compare the two. |
| `--snapshot out.png --page world --fault robot.urdf[#link]` | Imports a robot description (URDF or xacro) into a world of its own and draws it, the named link selected (its motor shows in the inspector). |
| `--snapshot out.png --page world --fault <template> --renderer wpf` | Draws the 3D view with WPF for this run (or with our own renderer, `gpu`); nothing is saved. Works with `--bench` too, to compare the two renderers. |
| `--esp32-build sketch.ino [--esp32-out folder] [--esp32-install]` | Builds the sketch for a real ESP32 with Espressif's toolchain (the hardware-compatible mode); `.bin`, `.bootloader.bin`, `.partitions.bin`, `.merged.bin` and `.elf` are copied to the folder, the log goes to `sketch.esp32build.txt`. `--esp32-install` installs the toolchain first when it is missing. |
| `--snapshot out.png --page schematic --example esp32-dimmer --esp32-mode hardware --simulate 1.5 --fault serial` | Runs every ESP32 of this run, circuit and world, in the hardware-compatible mode (on the Xtensa emulator, nothing is saved; `fast` the other way) and shows the serial monitor (`--fault log`: the events). |
| `--esp32-compare report.md [--esp32-seconds 8]` | The two modes side by side (stage 6): every ESP32 world template runs the same simulated time in the fast mode and on the Xtensa emulator; the report lists, per template, the serial lines (exact and with the numbers masked, boot logs aside), each ESP32 output pin's final level or PWM, and how fast each mode ran; templates needing what the emulator lacks are listed with the reason. |
| `--esp32-compat report.txt` | The two modes' compatibility: every ESP32 sketch in the world templates and examples built with the real toolchain; flash, RAM, time or the first error for each. Exit code 0 when all build. |
| `--render-test file` | Render test: the scene mirror and the GPU renderer, checked by drawing small scenes and reading them back (see [Testing](TESTING.md)). |
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

Publishes the app, lays out the components (the app, its own .NET 9 runtime, the Arduino compiler, the Open CASCADE bridge), packs each into an `.rwpack` with the native setup, compiles the one setup `RobotWinStudio-Setup-<version>.exe` with their sizes and SHA-256, and proves it by installing them all into a portable folder; output in `builds\installer\release`. `-NoArduino` skips the Arduino compiler, `-Repo owner/name` sets where the setup downloads from. See [BUILD_RELEASE.md](BUILD_RELEASE.md).

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.7.0
```

## External tools the app calls

| Tool | Called by | For |
| --- | --- | --- |
| `arduino-cli` | `Studio/Firmware/ArduinoCompiler.cs` | `arduino-cli compile --fqbn arduino:avr:uno` (or `arduino:avr:nano`) → `sketch.ino.hex` in `%LOCALAPPDATA%\RobotWinStudio\Build` |
| MSVC `cl.exe`, `lib.exe` (via `vcvars64.bat`) | `CoreSim/.../Firmware/Esp32/Esp32SketchCompiler.cs` | ESP32 sketch DLLs and the `rw_esp32hal.lib` HAL library, cached in `%LOCALAPPDATA%\RobotWin\esp32hal-cache` |

## Legacy tools

The rest of `tools/` belongs to the 1.x Unity-based design and is not needed for the current app: `rt_tool.py` (setup/build orchestration for Unity and the C++ engines), `scripts/` (native/firmware build scripts, Blender/FBX/GLB conversion, QA helpers), `debug_console/`, `RobotWinFirmwareMonitor/`, `GoldenTraceRecorder/`, `RpiSmokeTest/`, `rpi/`, `fixtures/`, `issue_bodies/`. Their documentation is in [archive/](archive/README.md).
