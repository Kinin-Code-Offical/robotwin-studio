# RobotWin Studio - Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
