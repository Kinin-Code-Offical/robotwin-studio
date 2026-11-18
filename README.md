# RobotWin Studio

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Unity](https://img.shields.io/badge/Unity-6%20LTS-black.svg)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)

**RobotWin Studio** is a deterministic robotics simulation platform engineered for Windows environments. It provides real-time hardware-in-the-loop (HIL) simulation by integrating a Unity-based visualization front-end with deterministic .NET orchestration and native C++ physics and firmware emulation engines.

## Key Features

- **Deterministic Simulation**: Fixed-step execution scheduling that ensures reproducible simulation states across runs.
- **Native Physics Engine**: Custom C++ rigid-body dynamics and collision detection accelerated via AVX2 SIMD instruction sets.
- **Cycle-Accurate Firmware Emulation**: Full ATmega328P instruction set architecture (ISA) and peripheral emulation.
- **3D Visualization Front-End**: Real-time scene rendering, authoring tools, and telemetry inspection built on Unity 6 LTS.
- **Hardware-in-the-Loop (HIL) Support**: Low-latency inter-process communication (IPC) via Windows Named Pipes for external firmware integration.
- **Automated Verification Suites**: Dedicated validation binaries for physical accuracy, constraint stability, and sensor mathematics.
- **Compiler-Level Optimizations**: Built with Link-Time Code Generation (LTCG) and fast floating-point evaluation (`/fp:fast`).

## System Architecture

The platform is divided into four primary subsystems:

- **CoreSim (C# / .NET 8.0)**: Manages the deterministic execution loop, state synchronization, and IPC transport layers.
- **NativeEngine (C++)**: Executes rigid-body dynamics, spatial partitioning, collision detection, and constraint resolution.
- **FirmwareEngine (C++)**: Handles virtual MCU execution, ATmega328P register emulation, and electrical circuit modeling.
- **RobotWin (Unity)**: Provides the graphical user interface, 3D environment rendering, and interactive debugging instruments.

## Getting Started (Windows)

### Prerequisites

Ensure the following tools and SDKs are installed on the host machine prior to building:

- **Visual Studio 2022** (with the *Desktop development with C++* workload)
- **Unity 6 LTS** (version 6000.0.23f1 or later)
- **.NET 8.0 SDK**
- **Python** (3.11 or later)
- **CMake** (3.20 or later)
- **Git**

### Build and Installation

Execute the following commands in a PowerShell terminal:

```powershell
# 1. Clone the repository
git clone [https://github.com/Kinin-Code-Offical/robotwin-studio.git](https://github.com/Kinin-Code-Offical/robotwin-studio.git)
cd robotwin-studio

# 2. Initialize the environment
python tools/rt_tool.py setup

# 3. Configure and build the native C++ engines
cmake -S NativeEngine -B builds/native
cmake --build builds/native --config Release

# 4. Build the CoreSim orchestration layer
dotnet build CoreSim/CoreSim.sln --configuration Release

# 5. Synchronize compiled native binaries with the Unity project
python tools/rt_tool.py update-unity-plugins

# 6. Open the 'RobotWin/' directory via Unity Hub
```

### Verification and Testing

Run the automated test suites to verify the build integrity:

```powershell
# Execute CoreSim managed unit tests
dotnet test CoreSim/CoreSim.sln

# Execute native physics verification suite
builds/native/PhysicsDeepValidation.exe

# Execute native sensor algorithm verification suite
builds/native/SensorInternalValidation.exe
```

## Documentation

### System Overview

- [Windows Setup Guide](docs/SETUP_WINDOWS.md) — Environment configuration and build instructions
- [Architecture Overview](docs/ARCHITECTURE.md) — Subsystem design and IPC data flow
- [Project Structure](docs/PROJECT_STRUCTURE.md) — Directory layout and module organization

### Development Guides

- [Tools Guide](docs/TOOLS.md) — Command-line utilities and automation scripts
- [Testing Guide](docs/TESTING.md) — Unit testing and validation procedures
- [Debug Console](docs/DEBUG_CONSOLE.md) — Runtime inspection and diagnostic tools
- [Troubleshooting](docs/TROUBLESHOOTING.md) — Common build and runtime issues

### Technical Reference

- [Native Engine Specification](docs/NATIVE_ENGINE.md) — C++ physics solver implementation details
- [Firmware Engine Specification](docs/FIRMWARE_ENGINE.md) — Virtual MCU architecture and timing model
- [Performance Benchmarking](docs/PERFORMANCE_TESTING.md) — Profiling methodology and optimization metrics
- [Third-Party Notices](THIRD_PARTY_NOTICES.md) — External dependencies and software licenses

## Validation Coverage

RobotWin Studio maintains strict analytical verification benchmarks for its native modules:

### Physics Engine Verification

- Conservation of kinetic energy in elastic collisions
- Coulomb friction model accuracy under static and dynamic conditions
- Iterative distance constraint enforcement and convergence
- Coefficient of restitution accuracy across varying impact velocities
- Multi-body stacking stability and resting contact resolution

### Sensor Model Verification

- Reflectance array line-position estimation accuracy
- Boundary and saturation handling (uniform high/low reflectance states)
- Weighted-average interpolation validation

## Performance Engineering

- **SIMD Vectorization**: Critical math routines utilize AVX2 intrinsics for parallel data processing.
- **Floating-Point Optimization**: Compiled with `/fp:fast` to maximize throughput in non-strict IEEE 754 paths.
- **Whole Program Optimization**: Link-Time Code Generation (LTCG) enabled across native release targets for cross-module inlining.
- **Precomputed Effective Mass**: Contact impulse denominators are cached per solver iteration to minimize redundant divisions.
- **Angular Inertia Integration**: Full 3D inertia tensor transformations applied during collision impulse resolution.

## Contributing

Contributions must adhere to the architectural and coding standards defined in [CONTRIBUTING.md](CONTRIBUTING.md).

### Standard Workflow

1. Fork the repository.
2. Create a dedicated feature or bugfix branch (`git checkout -b feature/descriptive-name`).
3. Commit changes with clear, descriptive commit messages.
4. Push the branch to your fork (`git push origin feature/descriptive-name`).
5. Submit a Pull Request against the main branch for review.

## License

This software is distributed under the MIT License. Refer to the [LICENSE](LICENSE) file for the full license text.

## Acknowledgments

- Rigid-body solver architecture informed by contemporary real-time physics literature.
- ATmega328P instruction set and register specifications derived from official Atmel/Microchip datasheets.
- Unity interop layer structured around the native low-level plugin interface.

## Contact and Support

- **Maintainer**: Kinin-Code-Offical
- **Source Repository**: [github.com/Kinin-Code-Offical/robotwin-studio](https://github.com/Kinin-Code-Offical/robotwin-studio)
- **Issue Tracker**: [GitHub Issues](https://github.com/Kinin-Code-Offical/robotwin-studio/issues)

<!-- BEGIN FOLDER_TREE -->

## Repository Structure

```text
.
|-- .gitignore
|-- .hintrc
|-- .markdownlint.json
|-- CODE_OF_CONDUCT.md
|-- CONTRIBUTING.md
|-- CoreSim
|-- docs
|-- FirmwareEngine
|-- global.json
|-- LICENSE
|-- NativeEngine
|-- README.md
|-- RobotWin
|-- SECURITY.md
|-- tests
|-- THIRD_PARTY_NOTICES.md
`-- tools
```

<!-- END FOLDER_TREE -->
