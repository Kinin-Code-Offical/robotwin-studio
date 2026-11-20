# Project structure

```text
robotwin-studio/
├── RobotWinStudio.sln            App + core + tests (the solution CI builds)
├── global.json                   .NET SDK pin (9.0.112, latestFeature)
├── Directory.Build.props         Sends bin/obj output to builds/<Project>/
├── Studio/RobotWin.Studio/       The desktop app (WPF, net9.0-windows)
├── CoreSim/                      Simulation core, native ESP32 HAL, tests
├── Installer/                    Native Win32 setup program (setup.cpp, setup.rc)
├── tools/build-installer.ps1     Builds RobotWinStudio-Setup-<version>.exe
├── docs/                         Documentation, user guide, screenshots, archive
├── .github/workflows/            ci.yml, release.yml (+ labeler, stale)
├── FirmwareEngine/, NativeEngine/  Legacy C++ engines (1.x, not used by the app)
├── tests/                        Legacy test suites (1.x, not in the solution)
└── builds/                       Build output (git-ignored)
```

## Studio/RobotWin.Studio

| Path | Contents |
| --- | --- |
| `App.xaml(.cs)` | Startup, Turkish culture, crash log, command-line modes (`--selftest`, `--snapshot`, `--gallery`, `--export-logo`, file-type registration) |
| `MainWindow.xaml(.cs)`, `MainWindow.Menu.cs`, `MainWindow.Tabs.cs` | Window chrome, menu bar (Dosya, Düzen, Görünüm, Simülasyon, Pencere, Yardım), shortcuts, document tabs, split view, detach, focus mode, full screen |
| `Shell/` | `FlowRail` (left navigation), `CommandPalette` (Ctrl+K), `Workspace` (open documents, save/load, autosave), `AppSettings`, `FileAssociations`, `UserLibrary`, `WindowBehavior`, dialogs |
| `Pages/` | `HomePage`, `SchematicPage`, `PcbPage`, `CodePage`, `ModelPage`, `AssemblyPage`, `WorldPage` (+ `.SceneTree`, `.Radio`), `LibraryPage`, `SettingsPage` |
| `Editor/` | Schematic canvas (editing, input, menus, shortcuts, export), symbol rendering, code editor, display renderer |
| `Simulation/` | `SimulationSession` (real-time circuit loop), oscilloscope, multimeter, histogram views |
| `Pcb/` | Board canvas, 3D board view, footprint editor, PCB dialogs |
| `Modeling/` | 3D modeller viewport, editor, overlays, controls |
| `Assembly3D/` | Montaj page 3D scene and effects |
| `World/` | `WorldSession` (running world), `WorldScene` (3D view + editing), `ObjectModels`, radio models and overlay |
| `Models3D/` | Code-generated 3D models of boards, modules, packages, mechanical parts; custom model overrides; gallery |
| `Examples/` | `ExampleProjects` (circuit/robot examples), `WorldTemplates` (world templates) |
| `Firmware/` | `ArduinoCompiler` (arduino-cli), sketch templates, bundled `.ino` + prebuilt `.hex` examples |
| `Audio/` | Real-time sound synthesis (`SoundEngine`) and simulation sound mapping (`SimSounds`) |
| `Input/` | XInput, Sony HID (DualSense/DualShock 4) and on-screen virtual gamepads |
| `Theme/`, `Branding/` | Design tokens, controls, icons; logo, splash, file-type icons |

## CoreSim

| Path | Contents |
| --- | --- |
| `src/RobotTwin.CoreSim/Analog/` | MNA solver, sparse LU, device models (`Devices/`) |
| `src/RobotTwin.CoreSim/Design/` | Schematic model, part library and catalogues, circuit compiler, ERC, BOM, SPICE, tolerances, Monte Carlo, diagnostics |
| `src/RobotTwin.CoreSim/Instruments/` | Oscilloscope and multimeter models |
| `src/RobotTwin.CoreSim/Firmware/` | ATmega328P emulator, Intel HEX, I²C bus and devices, AVR co-simulation; `Esp32/` sketch compiler, native loader, co-simulation, radio |
| `src/RobotTwin.CoreSim/Pcb/` | Board, layers, footprints, zones, routers, auto layout, DRC, Gerber, KiCad export |
| `src/RobotTwin.CoreSim/Mechanics/` | Rigid-body physics scene, collision shapes, mesh I/O and BVH, materials, mass properties, vehicles and drones |
| `src/RobotTwin.CoreSim/Modeling/` | Mesh modelling: primitives, CSG, edit operations, robot parts, `.rwmodel` |
| `src/RobotTwin.CoreSim/Sandbox/` | Open world: `WorldDocument`, `WorldCompiler`, `WorldPhysics`, `WorldExamples`, `Radio/` |
| `src/RobotTwin.CoreSim/Formats/` | `.rwproj/.rwpart/.rwlib/.rwsim` containers, KiCad library import, S-expressions |
| `src/RobotTwin.CoreSim/Host`, `IPC`, `Runtime`, `Specs`, `Contracts`, `Engine`, `Infrastructure`, `Serialization`, `Models` | Older building blocks from the 1.x design; not used by the Studio app |
| `native/esp32hal/` | C++ Arduino-ESP32 HAL shim (Arduino.h, WiFi, BLE, Wire, Servo, FreeRTOS, Bluepad32…), embedded into CoreSim and compiled with MSVC on demand |
| `tests/RobotTwin.CoreSim.Tests/` | xUnit tests (see [TESTING.md](TESTING.md)) |
| `CoreSim.sln` | CoreSim-only solution (library + tests) |

## docs

| Path | Contents |
| --- | --- |
| `README.md` | Documentation index |
| `kullanim/` | Turkish user guide, one file per topic |
| `images/` | Screenshots used by the README and the guide |
| `ARCHITECTURE.md`, `SETUP_WINDOWS.md`, `BUILD_RELEASE.md`, `TESTING.md`, `PROJECT_STRUCTURE.md`, `TOOLS.md`, `TROUBLESHOOTING.md` | Developer documentation |
| `archive/` | 1.x (Unity-based) design documents, reports and plans, kept for history |

## Legacy folders

`FirmwareEngine/` (C++ AVR emulator with named-pipe IPC), `NativeEngine/` (C++ physics) and most of `tools/` and `tests/` belong to the earlier Unity-based design. The current app does not build or load them. See [archive/](archive/README.md).
