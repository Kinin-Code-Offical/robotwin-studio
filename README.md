# 🤖 RobotWin Studio

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Unity](https://img.shields.io/badge/Unity-6%20LTS-black.svg)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)

**RobotWin Studio** is a high-fidelity, deterministic robotics simulation platform designed for Windows. It provides real-time hardware-in-the-loop (HIL) simulation, combining a Unity visualization front-end with deterministic .NET orchestration and high-performance C++ physics and firmware engines.

## ✨ Features

- **🎯 Deterministic Simulation**: Fixed-step scheduling with reproducible results
- **⚡ High-Performance Physics**: Native C++ engine with AVX2 SIMD optimization
- **🔧 Firmware Emulation**: Full ATmega328P ISA emulation with cycle-accurate timing
- **🎨 Unity Visualization**: Real-time 3D rendering with advanced debugging tools
- **🔌 Hardware-in-the-Loop**: Named pipe IPC for external firmware integration
- **📊 Comprehensive Testing**: Internal validation suites for physics and sensor algorithms
- **🚀 Optimized Performance**: Fast floating-point math, whole program optimization (LTCG)

## 🏗️ Architecture

### Core Components

- **CoreSim** (C#): Deterministic scheduler, state orchestration, and IPC coordination
- **NativeEngine** (C++): Physics simulation, collision detection, and rigid body dynamics
- **FirmwareEngine** (C++): ATmega328P emulation, circuit simulation, and virtual MCU
- **RobotWin** (Unity): 3D visualization, authoring tools, and user interaction

## 🚀 Quick Start (Windows)

### Prerequisites

- **Visual Studio 2022** with C++ Desktop Development workload
- **Unity 6 LTS** (6000.0.23f1 or later)
- **.NET 8.0 SDK**
- **Python 3.11+**
- **CMake 3.20+**
- **Git**

### Installation

```powershell
# 1. Clone the repository
git clone https://github.com/Kinin-Code-Offical/robotwin-studio.git
cd robotwin-studio

# 2. Run setup script
python tools/rt_tool.py setup

# 3. Build native engines
cmake -S NativeEngine -B builds/native
cmake --build builds/native --config Release

# 4. Build CoreSim
dotnet build CoreSim/CoreSim.sln --configuration Release

# 5. Open Unity project
# Open RobotWin/ folder in Unity Hub

# 6. Sync Unity plugins
python tools/rt_tool.py update-unity-plugins
```

### Running Tests

```powershell
# CoreSim unit tests
dotnet test CoreSim/CoreSim.sln

# Physics validation (internal tests)
builds/native/PhysicsDeepValidation.exe

# Sensor validation (internal tests)
builds/native/SensorInternalValidation.exe
```

## 📚 Documentation

### Getting Started

- [Windows Setup Guide](docs/SETUP_WINDOWS.md) - Detailed installation and configuration
- [Architecture Overview](docs/ARCHITECTURE.md) - System design and component interaction
- [Project Structure](docs/PROJECT_STRUCTURE.md) - Repository organization

### Development

- [Tools Guide](docs/TOOLS.md) - Build tools and utilities
- [Testing Guide](docs/TESTING.md) - Running and writing tests
- [Debug Console](docs/DEBUG_CONSOLE.md) - Debugging tools and techniques
- [Troubleshooting](docs/TROUBLESHOOTING.md) - Common issues and solutions

### Reference

- [Native Engine](docs/NATIVE_ENGINE.md) - C++ physics engine documentation
- [Firmware Engine](docs/FIRMWARE_ENGINE.md) - MCU emulation details
- [Performance Testing](docs/PERFORMANCE_TESTING.md) - Benchmarking and optimization
- [Third-Party Notices](THIRD_PARTY_NOTICES.md) - License information

## 🧪 Validation & Testing

RobotWin Studio includes comprehensive internal validation suites:

### Physics Engine Tests

- ✅ Energy conservation in elastic collisions
- ✅ Friction model accuracy (Coulomb friction)
- ✅ Constraint enforcement (distance constraints)
- ✅ Restitution coefficient validation
- ✅ Stability testing (stacked objects)

### Sensor Algorithm Tests

- ✅ Line sensor position calculation
- ✅ Edge case handling (all white, all black)
- ✅ Weighted average algorithm verification

## 🎯 Performance Optimizations

- **SIMD Vectorization**: AVX2 instructions for parallel computation
- **Fast Math**: `/fp:fast` compiler flag for aggressive FP optimization
- **Link-Time Code Generation**: Whole program optimization (LTCG)
- **Precomputed Effective Mass**: Contact impulse denominator cached per-contact
- **Angular Inertia Integration**: Physically-correct collision response

## 🤝 Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Workflow

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Physics engine inspired by modern game physics research
- ATmega328P emulation based on official Atmel documentation
- Unity integration leveraging native plugin architecture

## 📧 Contact

- **Project Owner**: Kinin-Code-Offical
- **Repository**: [robotwin-studio](https://github.com/Kinin-Code-Offical/robotwin-studio)
- **Issues**: [GitHub Issues](https://github.com/Kinin-Code-Offical/robotwin-studio/issues)

<!-- BEGIN FOLDER_TREE -->

## Project Tree

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
-- tools
```

<!-- END FOLDER_TREE -->
