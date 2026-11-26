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
| ESP32 emulator | `XtensaCoreTests`: our own Xtensa LX6 core; programs assembled by Espressif's real `xtensa-esp32-elf-as` run on it (arithmetic, shifts, loads/stores and the literal pool, S32C1I, branches, the zero-overhead loop, recursive fibonacci through real window handlers, the CCOMPARE interrupt and WAITI, divide-by-zero and FPU-off exceptions, the FPU). Skipped when the ESP32 toolchain is not installed; one hand-encoded test runs everywhere. `Esp32SocPeripheralTests`: the chip's peripherals through their registers, as IDF drives them (the clock, the LAC counter, a timer alarm and its interrupt, the cross-core interrupt, LEDC duty and hardware fade, GPIO output and open drain, the LEDC routed to a pin through the matrix, Schmitt inputs and a pin interrupt, the eFuse, Espressif's ADC characterisation, a SAR ADC conversion); no firmware needed. `Esp32SocBringUpTests`: given `RW_ESP32_ELF`, runs a real firmware (`RW_ESP32_PAD=34=1.0` pad voltages, `RW_ESP32_PROFILE=1` a call graph with callers, `RW_ESP32_PEEK=name,name` firmware variables) and writes its report beside the ELF (`RW_ESP32_SEND=text`, `RW_ESP32_SEND_AT=cycles`: text on UART0's receive line). `Esp32EmulatedBoardTests`: the hardware-compatible board in a circuit: the dimmer firmware (`RW_ESP32_ELF`) reads a 10k/4.7k divider within 5 mV and drives its LED's PWM; an echo firmware (`RW_ESP32_ECHO_ELF`) prints a line longer than the UART FIFO and echoes what the serial monitor sends in capitals; an I2C firmware (`RW_ESP32_I2C_ELF`) scans the bus, writes a module's registers and reads them back through a repeated START, writes 40 bytes past the 32-byte FIFO and gets a missing module's NACK; an SPI firmware (`RW_ESP32_SPI_ELF`) exchanges bytes, a 16-bit word and a 40-byte buffer with a device selected by its GPIO, and reads 0xFF with it deselected; a hung firmware (`RW_ESP32_WDT_ELF`: interrupts off, a spin) trips the interrupt watchdog, one core reports the panic, the board restarts (`SW_CPU_RESET`) and the sketch runs again; a deep-sleep firmware (`RW_ESP32_SLEEP_ELF`) sleeps 200 ms twice and wakes on the timer with its `RTC_DATA_ATTR` counter kept; an edge counter (`RW_ESP32_EDGES_ELF`) counts exactly ten rising edges on GPIO4 with `attachInterrupt`; a `touchRead` firmware (`RW_ESP32_TOUCH_ELF`) reads 75 untouched and falls to 13 with a finger through IDF's software filter; a NeoPixel firmware (`RW_ESP32_NEOPIXEL_ELF`) sends `rgbLedWrite`'s GRB bytes to GPIO5 and nine bytes of `rmtWrite` to GPIO18 through IDF's RMT driver, and three Adafruit_NeoPixel pixels to GPIO19; in the fast mode `Esp32PcntTests` decodes a JGA25 encoder motor's quadrature ×4 with `pulse_cnt` edge for edge and shows `ESP_ERROR_CHECK` aborting into a PANIC restart; a pulse counter firmware (`RW_ESP32_PCNT_ELF`) counts LEDC's 1 kHz PWM with IDF's `pulse_cnt` driver at one edge a millisecond, its watch point at 100 reached each time, and the same sketch's ×4 decoding (`RW_ESP32_QUAD_ELF`) counts a JGA25 encoder motor in the circuit edge for edge on the emulator; an MCPWM firmware (`RW_ESP32_MCPWM_ELF`) drives a 20 kHz complementary pair with 2 µs dead time (26 % / 66 %), the same as `Esp32PcntTests` in the fast mode, and `Esp32SocPeripheralTests` checks the MCPWM's registers (up, up-down, toggle, stop, force, dead time); `--esp32-compare report.md` runs every ESP32 world template in both modes and lists where they differ (see TOOLS.md); `WorldRunnerRunsTheHardwareModeOnTheEmulator` drives the headless runner's hardware mode and `ServoPulsesComeInTheLibrarysTimerTicks` checks the fast mode's servo pulses against the library's ticks; a Wi-Fi sketch (`RW_ESP32_WIFI_ELF`: joins the medium's network by DHCP, UDP both ways, a TCP client and its own TCP server, through the bare chip and through the board; no network in range ends at WL_NO_SSID_AVAIL; `RW_ESP32_WIFIAP_ELF`: the chip's own soft-AP takes a station that talks TCP and UDP to it; `TheStackAnswersArpAndDhcpAsTheAccessPoint` needs no ELF), a light-sleep firmware (`RW_ESP32_LSLEEP_ELF`) sleeps 0.5 s on the timer each time (`millis()` goes on by it, cause 4) and a GPIO4 low level wakes it at once (cause 7), as `Esp32PcntTests` shows in the fast mode with a button; a flash firmware (`RW_ESP32_FLASH_ELF`) keeps a boot counter in Preferences (NVS) and writes and reads a LittleFS file, with no core dump error at boot (the Schmitt trigger, the pin interrupt, the matrix and IDF's GPIO ISR service). `Esp32SocPeripheralTests` also checks UART2 on its own: its text, its baud rate, bytes arriving at 9600 baud. |
| World, radio | `WorldTests`, `WorldPhysicsTests`, `RadioTests`, `GamepadCarTests` (designed board, one axle, drives straight and turns on the controller) |
| Flight, gas, heat, batteries | `AeroWingTests`, `MotorGliderTests`, `QuadFlightTests`, `GasPhysicsTests` (blowdown, KNSB motor, water rocket, bursting), `ThermalWorldTests`, `UsbcBatteryTests` (CC/CV charging, protection board, regulated AA) |
| SDK | `SdkTests` (terminal velocity, wind, turbulence, expressions, plugins) |
| Files, repo hygiene | `FileFormatTests`, `RtwinRoundTripTests`, `TemplateTests`, `GoldenTraceHarnessTests`, `WorkflowReferenceTests` |

The Studio's own tests are in `Studio/RobotWin.Studio.Tests` (WPF-free parts linked in). `SolverCostsTests` checks the performance overlay's solver table (costs per simulated second, shares, the run's speed) and that every solver name has its English. `LangTests` checks the language packs: exact texts, patterns, joined texts, column-aligned readouts, sketch lines, two-language search, and that every Turkish label in the Studio's XAML has its English (`EveryXamlLabelHasItsEnglish`), so a label added without one fails the build's tests.

ESP32 tests compile real sketches with MSVC. On a machine without the Visual Studio C++ tools (`vcvars64.bat` not found) they are reported as **skipped**, not failed.

## Render test

```powershell
builds\RobotWin.Studio\Debug\net10.0-windows\RobotWinStudio.exe --render-test render-test.txt
Get-Content render-test.txt
```

First the scene mirror (`Render/SceneMirror.cs`) is checked: a layered body material (an empty charring layer does not make the body clear; a half-charred one carries its cover), a material that only glows (its chosen colour, added to what is behind), a fully clear pick target (not drawn), a frozen gradient brush (turned into a picture) and a changing one (its average colour), nested transforms, the sun, a lamp and a spot. Then small scenes are drawn offscreen by the GPU renderer (`Render/GpuRenderer.cs`) and read back pixel by pixel: a red box lit by the sun and the ground colour, a box's shadow under a slanting sun (the floor in shadow darker than 60% of the lit floor), light of its own over black (exactly the chosen colour), a half see-through blue layer over a white floor, a picture's orientation (red on the left, green on the right), an orthographic camera with WPF's infinite far plane on a clear background (alpha 0 around the square), a perspective with an infinite far plane, cascaded shadows (from 6 m up, a small box 12 m ahead and a big one 40 m ahead both cast theirs). `RW_RENDER_TEST_PNG=folder` writes each drawn scene as a PNG, to look at when a check fails. On a machine without a GPU (CI) the drawing runs on WARP, Windows' software rasterizer; `RW_RENDER_WARP=1` forces WARP on a machine that has one. The exit code is 0 on success.

## Self-test

```powershell
dotnet build RobotWinStudio.sln -c Debug
builds\RobotWin.Studio\Debug\net10.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
Get-Content selftest.txt
```

For every example in `Studio/RobotWin.Studio/Examples/ExampleProjects.cs` it:

1. compiles the schematic and runs the analog simulator (100 steps of 2 ms; 1500 steps when the example has a vehicle or drone, which is armed and given throttle);
2. synchronises a 90 × 70 mm PCB, auto-places, auto-routes and runs DRC.

Each example prints one line (`parts`, `nets`, `warn`, `sim=OK`, `routed=x/y`, `vias`, `drcErrors`, plus vehicle mass/distance/altitude). An example fails on an exception, incomplete routing or any DRC error. The last line is `SELFTEST PASS` or `SELFTEST FAIL (n)`; the exit code is 0 on success, 1 on failure.

## Snapshot testing

`--snapshot` renders a page off screen to PNG without user interaction, then exits. It is how the screenshots in `docs/images/` are made and how UI changes are verified.

```powershell
$exe = "builds\RobotWin.Studio\Debug\net10.0-windows\RobotWinStudio.exe"
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

`tests/native` (C++ physics/sensor validation for `NativeEngine`) and `tests/python` belong to the 1.x Unity-based design. They are not built by the solution or CI. (The Node/Jest UI tests of that design, `tests/integration`, drove the Unity app and were removed with it.)
