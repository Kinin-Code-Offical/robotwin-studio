# Architecture

RobotWin Studio is a single Windows desktop process: a C# .NET 9 WPF application (`Studio/RobotWin.Studio`) on top of a C# simulation core (`CoreSim/src/RobotTwin.CoreSim`). There is no separate engine process, no IPC and no game engine. The only native code that runs inside the app is the user's own ESP32 sketch, compiled with MSVC into a DLL (see [Firmware co-simulation](#firmware-co-simulation)).

> The 1.x design (Unity front end + C++ `NativeEngine`/`FirmwareEngine` over named pipes) is described in [archive/](archive/README.md). Those engines are still in the repository but are not used by the app.

## Layers

```text
+--------------------------------------------------------------------------------+
| Studio/RobotWin.Studio  (WPF, net9.0-windows, Turkish UI)                      |
|                                                                                |
|  MainWindow (+ .Menu, .Tabs)   Shell/            Pages/                        |
|  menu bar, tabs, split view,   FlowRail          Home, Schematic, Pcb, Code,   |
|  detach, focus, full screen,   CommandPalette    ModelPage, Assembly, World,   |
|  command palette (Ctrl+K)      Workspace         Library, Settings             |
|                                AppSettings                                     |
|  Editor/  Pcb/  Modeling/  Assembly3D/  World/  Models3D/  Simulation/         |
|  Firmware/ (arduino-cli)  Audio/  Input/ (XInput, Sony HID)  Theme/  Branding/ |
+--------------------------------------------------------------------------------+
                 |  project reference (plain .NET calls)
+--------------------------------------------------------------------------------+
| CoreSim/src/RobotTwin.CoreSim  (net9.0 + netstandard2.1, no UI)                |
|                                                                                |
|  Design/     schematic model, part library (StandardParts, Esp32Parts,         |
|              WirelessParts), CircuitCompiler, ERC, BOM, SPICE, tolerances,     |
|              Monte Carlo, CircuitDiagnostics                                   |
|  Analog/     MNA + Newton-Raphson solver, sparse LU, device models             |
|  Instruments/ oscilloscope, multimeter                                         |
|  Firmware/   ATmega328P emulator, IntelHex, I2C bus + device models,           |
|              FirmwareCoSimulation; Esp32/ sketch compiler + co-simulation      |
|  Pcb/        board model, layers, zones, router, auto layout, DRC,             |
|              Gerber/Excellon, KiCad export                                     |
|  Mechanics/  rigid-body PhysicsScene, collision shapes, meshes, materials,     |
|              vehicles/drones (Mechatronics), mass properties                   |
|  Modeling/   PolyMesh, primitives, CSG, mesh editing, robot parts, .rwmodel    |
|  Sandbox/    WorldDocument, WorldCompiler, WorldPhysics, Radio/                |
|  Formats/    .rwproj/.rwpart/.rwlib/.rwsim containers, KiCad library import    |
+--------------------------------------------------------------------------------+
                 |  embedded resources, compiled on demand with MSVC
+--------------------------------------------------------------------------------+
| CoreSim/native/esp32hal  (C++ Arduino-ESP32 HAL shim -> rw_esp32hal.lib)       |
+--------------------------------------------------------------------------------+
```

Rules of thumb:

- **CoreSim has no UI dependency.** Everything that can be tested headless (solver, firmware, PCB, physics, world compiler, file formats) lives there and is covered by `CoreSim/tests`.
- **Studio owns rendering and interaction.** WPF 3D (`Viewport3D`) draws the board, assembly, world and modeller views; 3D component models are generated in code (`Models3D/`), not loaded from asset files.
- Older CoreSim folders (`Host/`, `IPC/`, `Runtime/`, `Specs/`, `Contracts/`, `Engine/`, `Infrastructure/`, `Serialization/`) come from the 1.x design (deterministic host, named-pipe firmware protocol, specs); the Studio app does not use them.

## Documents and the workspace

`Shell/Workspace.cs` holds the open documents (tabs). A document is either a **circuit/board project** (`ProjectPackage`: schematic, PCB board, mechanical assembly, firmware sources) or a **world** (`WorldDocument`). A board inside a world is opened as an embedded document ("Dünyadaki kart") and edited with the same Schematic/PCB/Code pages.

The left navigation (`Shell/FlowRail.cs`, "Tasarımdan gerçeğe hattı") switches pages: Devre → PCB → Kod → Modelle → Montaj → Dünya, plus Home, Library and Settings. `MainWindow` can show a second page side by side (split view), move a page into its own window (detach), hide the rail (focus mode) and go full screen.

## Circuit simulation loop

1. `Design/CircuitCompiler` turns a `Schematic` into an analog netlist (`AnalogCircuit`) using each part's simulation model from the `PartLibrary`. Tolerances (`Design/Tolerances.cs`) can perturb values with a fixed seed.
2. `Analog/AnalogSimulator` solves it: modified nodal analysis, Newton-Raphson for non-linear devices, trapezoidal / backward-Euler integration, adaptive sub-stepping, sparse LU (`SparseLu`) with dense fallback, a thermal model and part damage (burn, blown fuse, breakdown). It is deterministic.
3. `Studio/Simulation/SimulationSession` drives the simulator from the WPF render loop at the selected speed, samples probes at every accepted solver step for the oscilloscope (`Instruments/Oscilloscope`) and multimeter (`Instruments/Multimeter`), raises failure events and records `.rwsim` files.
4. When the project has a mechanical assembly, `Mechanics/Mechatronics` couples motors and propellers to a rigid-body vehicle or drone (Montaj page) both ways: the circuit drives the motors, the load torque comes back.

## Firmware co-simulation

Firmware runs **in lockstep** with the circuit: the firmware runs for a slice, then the circuit is stepped by the same amount.

- **AVR (Arduino Uno/Nano).** `Firmware/Atmega328P*` is a cycle-counted ATmega328P (full AVRe+ instruction set, interrupts, GPIO with INT0/1 and pin change, Timer0/1/2 with PWM modes, USART0, ADC, EEPROM, watchdog, TWI master, minimal SPI). `FirmwareCoSimulation` hands it the circuit's logic levels and ADC voltages before each 50 µs slice and applies its pin state (LOW/HIGH/pull-up/timer PWM) to the board's pin devices afterwards; brown-out holds the CPU in reset. I²C device models (SSD1306, PCF8574 + HD44780, MPU-6050) sit on its bus. Sketches are compiled by `Studio/Firmware/ArduinoCompiler` with `arduino-cli` into Intel HEX; examples ship as prebuilt `.hex`.
- **ESP32.** `Firmware/Esp32/Esp32SketchCompiler` preprocesses the `.ino`, then compiles it with MSVC `cl.exe` (found through Settings, `vswhere` or the standard Visual Studio folders; `vcvars64.bat` runs once per process) into an x64 DLL linked against `rw_esp32hal.lib`, which is built once from the HAL sources embedded in CoreSim (`CoreSim/native/esp32hal`) and cached under `%LOCALAPPDATA%\RobotWin\esp32hal-cache`. `Esp32Native` loads the DLL; `Esp32CoSimulation` runs it at Arduino-API level in 100 µs slices (world) with ESP32 input thresholds, 45 kΩ pull-ups, LEDC PWM and the DAC. FreeRTOS tasks, timers, Wire, Servo, simulated Wi-Fi, a BLE subset and Bluepad32 are provided by the shim; `Esp32Radio` connects the sketch to the world's radio medium.

## World pipeline

The open world (Dünya page) reuses all of the above on many boards and parts at once:

```text
WorldDocument  --WorldCompiler-->  WorldCircuit (one analog circuit + programmables + cable devices)
     |                                   |
     +----------WorldPhysics-------------+   (rigid bodies, joints, actuators, sensors)
                       |
                 WorldSession (Studio)  advances firmware -> radio -> circuit -> physics each frame
                       |
                 WorldScene (Studio)    draws objects, cables, beams, sparks; editing interaction
```

- **`Sandbox/WorldDocument`** is the saved model: title and environment (`WorldSpec`: floor size and friction, walls, gravity, temperature, obstacles), objects (`Board`, `Component`, `Body`, `Model`, each with pose, material, infill, mass and a fixed flag), cables (two pin ends, AWG, length, colour), joints (`Fixed`, `Hinge`, `Slider`, optionally to a servo horn or motor shaft, with limits and break force), embedded board designs and custom parts. It provides editing helpers (add, connect, mount, move/rotate assembly, remove) and deep copies for undo.
- **`Sandbox/WorldCompiler`** flattens the world into one circuit: each board's schematic and a one-part schematic per free component are merged with names prefixed by the object id (`O3/U1`); every cable becomes a resistor sized from gauge and length with a power rating and a 160 °C burn-out limit; electrically separate groups get their own 0 V reference; a PC + USB cable can power a board. It also collects the programmable boards with their firmware.
- **`Sandbox/WorldPhysics`** builds a `Mechanics/PhysicsScene`: gravity, ground and obstacles, one rigid body per object (box, sphere, cylinder or triangle mesh from the 3D model), a rotating body on a hinge for each servo horn and motor shaft, and the user's joints. Parts of one assembly do not collide. Every 1 ms step it drives actuators from the circuit, steps physics, feeds horn angle/current and shaft load back to the circuit, and every 5 steps ray-casts ultrasonic sensors.
- **`Sandbox/Radio`** simulates a 2.4 GHz medium (BLE advertising/GATT notifications, Wi-Fi with UDP and stream sockets, Bluetooth Classic HID gamepads with pairing), path loss through distance, obstacles and bodies with optional log-normal fading, a line-of-sight IR channel (NEC), and USB (`WorldUsb`).
- **`Studio/World/WorldSession`** owns a running world: compiles, creates the solver, starts AVR (50 µs slices) and ESP32 (100 µs slices) co-simulations, attaches I²C devices reached through cables, builds `WorldPhysics`, and advances everything in lock-step each frame. Failures and impacts become log entries and sounds (`Audio/SimSounds`).

## PCB pipeline

`Pcb/Board` is synchronised from the schematic (`BoardDocument.Synchronize`), placed (`AutoPlacer`) and routed (`AutoRouter`, A* on all layers; `InteractiveRouter` for manual routing), zones are filled by `ZoneFiller`, checked by `Drc`, and exported by `Gerber` (RS-274X X2, Excellon, BOM, CPL in one ZIP) and `KiCadExporter` (KiCad 8 project ZIP).

## File formats

All formats are versioned ZIP containers with a `manifest.json` (format, version, app, title, author, dates, summary), written through a temporary file so a crash cannot leave a half-written file. Code: `Formats/RobotWinFiles.cs`, `Sandbox/WorldDocument.cs`, `Modeling/ModelDocument.cs`.

| Extension | Contents (inside the ZIP) |
| --- | --- |
| `.rwproj` (v2) | `schematic.json`, `pcb/board.json`, `mechanics/assembly.json`, `firmware/index.json`, `firmware/<RefDes>/sketch.ino` + `.hex`, `library/parts.json`, `settings.json`, `preview.png`. v1 projects (a single JSON file) still open. |
| `.rwworld` | `world.json`, `designs/index.json`, `designs/<id>.rwproj` (embedded boards), `preview.png` |
| `.rwpart` | `part.json`: base simulation model, parameters, optional footprint, module sub-circuit and 3D model |
| `.rwlib` | `parts/<id>.json` |
| `.rwsim` | `channels/*.csv`, `events.json`, `trajectory.json`, `serial.txt`, `project.rwproj` |
| `.rwmodel` | `model.json`, `preview.png` |
| `.rwfp` | `footprint.json` |

## User data locations

| What | Where |
| --- | --- |
| Settings | `%APPDATA%\RobotWinStudio\settings.json` |
| User part library, footprints | `%APPDATA%\RobotWinStudio\Library` (`Footprints\`) |
| Recent files, crash log | `%APPDATA%\RobotWinStudio\recent.txt`, `crash.log` |
| Autosave backups | `Documents\RobotWin\Yedekler` |
| arduino-cli builds | `%LOCALAPPDATA%\RobotWinStudio\Build` |
| ESP32 HAL / sketch cache | `%LOCALAPPDATA%\RobotWin\esp32hal-cache` |
| File-type icons | `%LOCALAPPDATA%\RobotWinStudio\Icons` |
