# Testing

Three layers of checks, all runnable on a developer machine and in CI:

| Layer | Command | What it proves |
| --- | --- | --- |
| Unit tests | `dotnet test RobotWinStudio.sln` | CoreSim behaves correctly (solver, firmware, PCB, physics, world, formats). |
| Self-test | `RobotWinStudio.exe --selftest` | Every bundled example builds, simulates, places, routes and passes DRC end to end. |
| Snapshot rendering | `RobotWinStudio.exe --snapshot …` | A page renders the way it should (visual check, documentation screenshots). |
| Render test | `RobotWinStudio.exe --render-test file` | Our own 3D renderer reads WPF scenes right and draws them right on the GPU (read back pixel by pixel). |

## Unit tests

```powershell
dotnet test RobotWinStudio.sln
# one area only:
dotnet test RobotWinStudio.sln --filter "FullyQualifiedName~WorldPhysicsTests"
```

The xUnit project is `CoreSim/tests/RobotTwin.CoreSim.Tests`. It needs no UI and no external tools. Main test files:

| Area | Tests |
| --- | --- |
| Analog solver | `AnalogSimulatorTests`, `AnalogActiveDeviceTests`, `SparseSolverTests`, `ThermalModelTests`, `ComponentVariationTests`, `ToleranceAndSourceTests`, `DeterministicNoiseTests` |
| Instruments, diagnostics | `OscilloscopeTests`, `MultimeterTests`, `DiagnosticsTests` |
| Schematic, PCB, KiCad | `SchematicEditorTests`, `DesignAndPcbTests`, `PcbEditorTests`, `PcbPolygonTests`, `KiCadExportTests`, `KiCadImportTests` |
| Firmware | `FirmwareCoSimTests`, `I2cDeviceTests`, `Esp32Tests`, `Esp32RadioTests`, `FirmwareProtocolTests`, `ProtocolFramingTests` |
| Mechanics, modelling | `PhysicsSceneTests`, `VehicleDynamicsTests`, `MechatronicsTests`, `MeshTests`, `ModelingTests`, `PartModel3DTests`, `ModelHullTests` (model-shaped bodies roll like the real shape), `AssemblyDragTests` (covered faces, base drag, oblique flow), `LiquidTests` (hydrostatics, a free tank and its sleep, the dam break against Martin & Moyce, the real pour), `ConservationTests` (momentum and angular momentum through collisions, Newton's restitution, a free body's flip about its middle axis, energy in vacuum, bounces never adding energy), `WorkTeamTests` (the solver threads: every index once per pass, errors carried back, one caller at a time) |
| World, radio | `WorldTests`, `WorldPhysicsTests`, `RadioTests`, `GamepadCarTests` (designed board, one axle, drives straight and turns on the controller) |
| Flight, gas, heat, batteries | `AeroWingTests`, `MotorGliderTests`, `QuadFlightTests`, `GasPhysicsTests` (blowdown, KNSB motor, water rocket, bursting), `ThermalWorldTests`, `UsbcBatteryTests` (CC/CV charging, protection board, regulated AA) |
| SDK | `SdkTests` (terminal velocity, wind, turbulence, expressions, plugins) |
| Files, repo hygiene | `FileFormatTests`, `RtwinRoundTripTests`, `TemplateTests`, `GoldenTraceHarnessTests`, `WorkflowReferenceTests` |

The Studio's own tests are in `Studio/RobotWin.Studio.Tests` (WPF-free parts linked in). `SolverCostsTests` checks the performance overlay's solver table (costs per simulated second, shares, the run's speed) and that every solver name has its English. `LangTests` checks the language packs: exact texts, patterns, joined texts, column-aligned readouts, sketch lines, two-language search, and that every Turkish label in the Studio's XAML has its English (`EveryXamlLabelHasItsEnglish`), so a label added without one fails the build's tests.

ESP32 tests compile real sketches with MSVC. On a machine without the Visual Studio C++ tools (`vcvars64.bat` not found) they are reported as **skipped**, not failed.

## Render test

```powershell
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --render-test render-test.txt
Get-Content render-test.txt
```

First the scene mirror (`Render/SceneMirror.cs`) is checked: a layered body material (an empty charring layer does not make the body clear; a half-charred one carries its cover), a material that only glows (its chosen colour, added to what is behind), a fully clear pick target (not drawn), a frozen gradient brush (turned into a picture) and a changing one (its average colour), nested transforms, the sun, a lamp and a spot. Then small scenes are drawn offscreen by the GPU renderer (`Render/GpuRenderer.cs`) and read back pixel by pixel: a red box lit by the sun and the ground colour, a box's shadow under a slanting sun (the floor in shadow darker than 60% of the lit floor), light of its own over black (exactly the chosen colour), a half see-through blue layer over a white floor, a picture's orientation (red on the left, green on the right), an orthographic camera with WPF's infinite far plane on a clear background (alpha 0 around the square), a perspective with an infinite far plane. On a machine without a GPU (CI) the drawing runs on WARP, Windows' software rasterizer; `RW_RENDER_WARP=1` forces WARP on a machine that has one. The exit code is 0 on success.

## Self-test

```powershell
dotnet build RobotWinStudio.sln -c Debug
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
Get-Content selftest.txt
```

For every example in `Studio/RobotWin.Studio/Examples/ExampleProjects.cs` it:

1. compiles the schematic and runs the analog simulator (100 steps of 2 ms; 1500 steps when the example has a vehicle or drone, which is armed and given throttle);
2. synchronises a 90 × 70 mm PCB, auto-places, auto-routes and runs DRC.

Each example prints one line (`parts`, `nets`, `warn`, `sim=OK`, `routed=x/y`, `vias`, `drcErrors`, plus vehicle mass/distance/altitude). An example fails on an exception, incomplete routing or any DRC error. The last line is `SELFTEST PASS` or `SELFTEST FAIL (n)`; the exit code is 0 on success, 1 on failure.

## Snapshot testing

`--snapshot` renders a page off screen to PNG without user interaction, then exits. It is how the screenshots in `docs/images/` are made and how UI changes are verified.

```powershell
$exe = "builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe"
& $exe --snapshot out\home.png --page home
& $exe --snapshot out\schematic.png --page schematic --example arduino-displays --simulate 1.5
& $exe --snapshot out\pcb.png --page pcb --example motor-driver
& $exe --snapshot out\world.png --page world --fault ps5-car --simulate 2
& $exe --snapshot out\model.png --page model --size 1600x960
```

| Option | Meaning |
| --- | --- |
| `--page` | `home`, `schematic`, `pcb`, `code`, `model`, `assembly`, `world`, `library`, `settings` |
| `--example <id>` | Loads a circuit example first (`led-button`, `arduino-blink`, `arduino-displays`, `esp32-dimmer`, `motor-driver`, `opamp-amp`, `power-7805`, `rc-filter`, `relay-lamp`, `nand-logic`, `line-follower`, `obstacle-avoider`, `quadcopter`) |
| `--simulate <s>` | Runs the simulation for that many simulated seconds before capturing |
| `--fault <name>` | Page-specific scenario. World page: the world template id (`empty`, `two-boards`, `servo-arm`, `displays`, `car`, `sonar`, `motor-wheel`, `ps5-car`, `esp32-quad`, `shell-box`, `motor-glider`, `glider`, `water-rocket`, `sugar-motor`, …). Schematic: e.g. `scope`, `dmm`, `burn`, `diag`, `montecarlo`, `tolerance`, `erc`, `find`, `help`. PCB: `3d`, `fpeditor`, `module`. Any page: `split` (split view). |
| `--collision` | World page: captures with the collision shapes shown |
| `--size WxH` | Window size (default 1600x960); narrow sizes emulate split view / detached windows |
| `--wait <s>` | Extra seconds before capturing |
| `--nav-expanded` | Captures with the navigation rail expanded |

Compare the PNGs by eye or with an image diff tool; there is no automated pixel comparison in CI.

Related: `--overlap-report <file>` builds every world template and lists parts that overlap or do not fit their shell; `--gallery <dir>` writes contact sheets of every generated 3D component model (`--gallery-filter`, `--gallery-columns`, `--gallery-tile WxH`), useful after changes in `Models3D/`.

## Continuous integration

`.github/workflows/ci.yml` runs on pushes and pull requests to `main` (`windows-latest`):

1. checks that `global.json` exists and no build artifacts are tracked;
2. `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`;
3. runs `RobotWinStudio.exe --selftest` and fails the job if it does not pass.

## Legacy test suites

`tests/native` (C++ physics/sensor validation for `NativeEngine`), `tests/integration` (Node) and `tests/python` belong to the 1.x Unity-based design. They are not built by the solution or CI.
