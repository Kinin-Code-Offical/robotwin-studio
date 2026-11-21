# RobotWin Studio - Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-11-21

One shared physics now drives everything in the world: circuits, rigid bodies, heat, air, gas and radio feed each other, so robots, aircraft, rockets and industrial cells are built from the same parts and behave as they would in reality.

### Added

- **Help, news and updates:** in-app Help page (F1) with the user guide, News, one-click automatic updates that keep projects and settings, older versions list, performance indicator.
- **Real Montaj:** the assembly is built from the circuit itself. Parts on the PCB stay on the board; every other part (motor, servo, battery, sensor, module) arrives as its real 3D part, nets become real jumper cables. No ready-made vehicle; build chassis, wheels and shells with the world tools.
- **Model-shaped rigid bodies:** every part, board and model collides with the convex outline of its own 3D model (round things roll, wheels stand on their tyres). *Görünüm → Çarpışma şekilleri* shows the shapes the physics uses.
- **Flight:** drawn wings, tails and fins generate lift and drag with stall, validated stability and elevator authority; balsa glider, motor glider UAV and ESP32 quadcopter flown by its own firmware (brushless outrunners, ESC current limit, IMU that feels the motion).
- **Gas physics and rockets:** ideal-gas pressure vessels with species mixing, isentropic choked and supersonic nozzle flow, water expulsion, solid propellant grains (KNSB, KNDX, APCP) with Saint-Robert burn rate and BATES geometry, wall heating, burst at the material's hoop-stress limit; ISA atmosphere. Templates: water rocket and sugar-motor test stand.
- **Air drag of assemblies:** joined parts shield each other's faces, base drag and skin friction are separated, oblique flow blends by projected area.
- **USB-C lithium batteries:** 18650, 2S pack, AA and 4×AA Li-ion with protection board (over-charge, over-discharge, over-current), CC/CV charger from a world USB cable, regulated 1.5 V output.
- **PS5-controlled car rebuilt:** designed controller board (schematic, routed PCB, firmware), real 2WD kit geometry with mirrored motors, battery powered, drives straight and turns from the DualSense over Bluetooth.
- **Heat:** thermal network across parts and boards, heat changes materials (softening, melting, thermal stress), honest motor and battery heating, thermal camera on the 3D models, board heat map in the render.
- **Analysis and render:** offline analysis runs with full recording, FEM stress with mesh convergence and `.inp` export, 2D CFD, 3D streamlines, replay of a recorded run with stress on every part, follow camera, electrical render.
- **Honest measurements:** per-part variance of cloned parts, uncertainty on readings, Monte Carlo bands, bench comparison against an imported measured log.
- **Mechanics:** hollow shells with screwed lids and fit check, X-ray and exploded views, pushrod linkages, brushless ESC, ready wheels and propellers, rope cables, tyre slip, tipping, motors that stall under load, interpenetration prevented inside assemblies.
- **Industrial robot cell:** 6-axis arm, SCARA, inverse kinematics, gripper, vacuum, conveyor, teach pendant.
- **Sensors and cameras:** real line sensing, cameras, LiDAR, IMU, GPS; Scene + Game views.
- **Terrain and world editor:** sculpting, surface layers, real ground mechanics; free lights and 3D positional sound.
- **ESP32:** real SPI bus to wired devices (MCP3008, BMP280), real task watchdog.
- **Cables:** length check (enough / taut / too short), rope tension, voltage drop, plug contact resistance, cable diagnostics.
- **PCB:** hand routing from a ratsnest line (F finishes to the target pad), live lengths, the board's real outline in 3D and in the world, ready board shapes.
- **Automation API and SDK:** Python/Gymnasium, ROS 2 bridge, headless runs (with radio between boards); environment SDK with atmosphere, wind, force fields and physics scripts.
- **External libraries** and a part creator.
- **Documentation site:** every page fully Turkish or fully English, physics and maths reference with formulas, API and physics-script how-tos with code, drawn diagrams and tables.

### Changed

- Installers: `RobotWinStudio-Setup-<version>.exe` is the full package and carries .NET; `RobotWinStudio-Setup-<version>-lite.exe` needs the .NET 9 Desktop Runtime.
- Core simulation messages (battery, regulator, ESP32 build errors) are in Turkish like the rest of the interface.
- Text contrast raised to WCAG level, no text under 12 px, labels wrap (see `docs/ACCESSIBILITY.md`).
- Wood density corrected; balsa, carbon and lead materials added; PET, cardboard and acrylic strength data.

### Fixed

- Serial monitor freeze and a model lost when closing the app.
- Solver collapse when a battery ran completely empty.
- Wheels and other turned bodies drawn lying flat in the render.

## [1.0.0] - 2026-11-20

RobotWin Studio is now a single Windows desktop application: C# .NET 9 WPF (`Studio/RobotWin.Studio`) on top of the C# simulation core `CoreSim`. The Turkish user interface follows one "design-to-real line": Devre → PCB → Kod → Modelle → Montaj → Dünya.

### Added

- **Circuit (Devre):** schematic editor with a searchable library of 256 real parts, wires, labels, power symbols, annotation, find/replace, ERC; live analog simulation (MNA + Newton-Raphson, adaptive steps, thermal model and part damage); BOM, KiCad netlist, SPICE, PNG/SVG and print/PDF export.
- **Instruments:** 4-channel oscilloscope (triggers, math, cursors, automatic measurements, probe loading), 6000-count true-RMS multimeter with fused current inputs, CV/CC bench supply with OVP, component tolerances, Monte Carlo analysis with histogram, fault diagnosis with "why did it burn" explanations.
- **PCB:** 2/4/6-layer boards, stackup and net classes, copper zones, interactive router, auto-placement and auto-routing, DRC, Gerber RS-274X X2 + Excellon + BOM + CPL, KiCad 8 project export, footprint editor, board-as-module parts, 3D board view.
- **Code (Kod):** per-board firmware; Arduino Uno/Nano sketches built with `arduino-cli` and run on a cycle-counted ATmega328P emulator (timers/PWM, USART, ADC, EEPROM, watchdog, TWI with SSD1306, PCF8574 LCD and MPU-6050 models); ESP32 sketches compiled natively with MSVC and co-simulated at Arduino-API level (GPIO, ADC, LEDC, DAC, timers, FreeRTOS, Wire, Servo, simulated Wi-Fi, BLE subset, Bluepad32); serial monitor.
- **Model (Modelle):** Blender-like 3D modeller with primitives, profiles and revolves, edit mode (extrude, inset, subdivide, merge…), CSG union/difference/intersection, mirror and arrays, parametric robot parts, materials and mass, STL/OBJ/PLY import/export, `.rwmodel` files and *send to world*.
- **Assembly (Montaj):** automatic rover or X-quadcopter from the circuit, arenas, cameras, drone controls, fault experiments, `.rwsim` recordings.
- **World (Dünya):** open sandbox with designed boards, catalogue parts, mechanical bodies and imported meshes; real cables (AWG, copper resistance, burn-out); fixed, servo-horn/motor-shaft, hinge and slider joints; rigid-body physics with gravity presets, materials and collisions, coupled both ways to servos and DC motors; ultrasonic sensors that ray-cast the scene; live I²C displays; 2.4 GHz radio (BLE, Wi-Fi, Bluetooth gamepads), IR remotes, PC + USB power and serial; DualSense/DualShock 4/Xbox and on-screen gamepads; scene tree, cable measurements, RF panel; simulation sounds; eight world templates.
- **Library:** custom parts (`.rwpart`), part libraries (`.rwlib`), custom 3D models per part, KiCad symbol and footprint import.
- **Shell:** menu bar, document tabs with drag reordering, split view, detachable pages, focus mode, full screen, command palette (Ctrl+K), autosave backups, recent files, UI scale, splash screen, file-type registration.
- **File formats:** versioned ZIP containers `.rwproj`, `.rwworld`, `.rwpart`, `.rwlib`, `.rwsim`, `.rwmodel`, `.rwfp`.
- **Command line:** `--selftest`, `--snapshot`, `--gallery`, `--export-logo`, `--register-file-types` / `--unregister-file-types`.
- **Installer:** native `RobotWinStudio-Setup-<version>.exe` (wizard, `/S`, `/ALLUSERS`, `/PORTABLE /D=<dir>`; offers the .NET 9 Desktop Runtime) built by `tools/build-installer.ps1`; the release workflow builds it on `v*` tags.
- **Docs:** new README, developer documentation and a Turkish step-by-step user guide in `docs/kullanim/`.

### Changed

- CI builds `RobotWinStudio.sln` with .NET 9, runs the unit tests and the app self-test.
- Documentation of the 1.x design moved to `docs/archive/`.

### Removed

- The Unity front end (`RobotWin/` Unity project) and its documentation. `NativeEngine/` and `FirmwareEngine/` (C++) remain in the repository as legacy code and are not used by the app.

## [0.x] - Unity-based design (history)

### Added

- 🧪 Physics deep validation suite with energy conservation tests
- 🧪 Sensor internal validation suite for line sensor algorithms
- ⚡ SIMD optimization with AVX2 instructions
- ⚡ Fast floating-point math (`/fp:fast`) for performance
- ⚡ Precomputed effective mass in contact resolution
- ⚡ Angular inertia integration in collision response
- 📚 Comprehensive README with badges and feature highlights
- 📚 Enhanced CONTRIBUTING.md with detailed guidelines
- 📚 Expanded CODE_OF_CONDUCT.md with clear expectations
- 📚 Improved SECURITY.md with vulnerability reporting process
- 🔧 Updated .gitignore with comprehensive exclusion patterns

### Fixed

- 🐛 Physics damping time-step dependency (now `damping * dt`)
- 🐛 Ground friction model (switched to proper Coulomb friction)
- 🐛 Restitution coefficient application in collisions
- 🐛 Energy conservation in elastic collisions (now exact)
- 🐛 Sensor test compilation conflicts with Arduino macros

### Changed

- 🎨 Improved build system with Release configuration defaults
- 🎨 Cleaned up binary artifacts from repository
- 🎨 Enhanced PR template with comprehensive checklist

## [0.1.0] - Initial Release

### Added

- 🎯 CoreSim deterministic scheduler (.NET 8)
- 🎯 NativeEngine C++ physics simulation
- 🎯 FirmwareEngine ATmega328P emulation
- 🎯 Unity 6 LTS visualization frontend
- 🎯 Named pipe IPC between components
- 🎯 Basic rigid body physics (sphere and box collision)
- 🎯 Vehicle simulation with Pacejka tire model
- 🎯 Ground plane support with friction
- 🎯 Distance constraints (springs/ropes)
- 🎯 Circuit simulation (nodal solver)
- 🎯 Line sensor array implementation

---

### Emoji Key

- ✨ New feature
- 🐛 Bug fix
- ⚡ Performance improvement
- 📚 Documentation
- 🎨 Code style/refactoring
- 🧪 Tests
- 🔧 Configuration
- 🚀 Deployment
- 💥 Breaking change
- 🔒 Security fix
- 🎯 Core functionality
