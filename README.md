# RobotWin Studio

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Unity](https://img.shields.io/badge/Unity-6%20LTS-black.svg)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)

**RobotWin Studio** is a deterministic robotics simulation platform engineered for Windows environments. It provides real-time hardware-in-the-loop (HIL) simulation by integrating a Unity-based visualization front-end with deterministic .NET orchestration and native C++ physics and firmware emulation engines.

## RobotWin Studio 2 (yeni masaüstü uygulaması)

Unity'ye bağlı olmayan, kendi tasarım sistemiyle sıfırdan yazılmış yeni stüdyo `Studio/` altında. Çekirdek motorlar `CoreSim/` içinde.

| Katman | Ne yapar | Kod |
| --- | --- | --- |
| Analog çözücü | Modifiye düğüm analizi, Newton-Raphson, trapez/BE integrasyon, hata kontrollü uyarlamalı adım, olay (breakpoint) zamanlaması, termal model ve parça hasarı (yanma, patlama, termal kaçak, sargı yanması) | `CoreSim/src/RobotTwin.CoreSim/Analog` |
| Parça kataloğu | 240+ gerçek parça: pasifler, yarı iletkenler, op-amp, regülatör, lojik, röle, motor (TT, N20, coreless), motor sürücü (TB6612, L298N), servo, sensörler (TCRT5000, HC-SR04, IR), I²C ekranlar (SSD1306 OLED, 1602/2004 LCD), MPU-6050, bataryalar, Arduino Uno/Nano, uçuş kontrolcüsü | `CoreSim/src/RobotTwin.CoreSim/Design` |
| AVR çekirdeği | ATmega328P: tam komut seti, kesmeler, Timer0/1/2, USART, ADC, EEPROM, GPIO, INT0/1, PCINT, watchdog, zamanlamalı TWI (I²C) master. Gerçek derlenmiş `.hex` firmware'i devreyle lockstep çalıştırır | `CoreSim/src/RobotTwin.CoreSim/Firmware` |
| I²C cihazları | SSD1306 (tüm adresleme modları), PCF8574 + HD44780 (4 bit, CGRAM), MPU-6050 (kayıt dosyası, ölçek ayarları) | `Firmware/Devices` |
| 3D fizik | Rijit cisim (kuaterniyon), zemin/engel/duvar teması, ışın-tekerlek lastik modeli, pervane itki/tork (kT·ω², kQ·ω², yer etkisi), arena (çizgi pisti, engeller) | `CoreSim/src/RobotTwin.CoreSim/Mechanics` |
| Mekatronik | Motor mili ↔ tekerlek/pervane çift yönlü bağlantı (yük torku, atalet), sensörlerin arenayı okuması, uçuş kontrolcüsü (açı→hız kaskad PID, irtifa tutma, X mikser), çarpma/pervane kırılması/devrilme | `Mechanics/Mechatronics.cs` |
| PCB motoru | Senkron, otomatik yerleşim, 2 katman A* rota, DRC (IPC-2221 akım kapasitesi), Gerber RS-274X + Excellon + BOM + CPL, **KiCad 8 projesi** (`.kicad_pro/.kicad_sch/.kicad_pcb`, netlist, sembol ve footprint kütüphaneleri), SPICE netlisti | `CoreSim/src/RobotTwin.CoreSim/Pcb` |
| Dosya biçimleri | `.rwproj` proje paketi, `.rwpart` parça, `.rwlib` kütüphane, `.rwsim` simülasyon kaydı | `CoreSim/src/RobotTwin.CoreSim/Formats` |
| Stüdyo | Şematik editör, canlı simülasyon, osiloskop, seri monitör, PCB editörü, **Kod** (editör + arduino-cli derleme + emüle karta yükleme), **Montaj** (3D robot/drone, pilot kontrolü, arıza efektleri), kütüphane | `Studio/RobotWin.Studio` |

### Dosya biçimleri

Hepsi sürümlü ZIP kapsayıcılarıdır ve içlerinde `manifest.json` (biçim adı, sürüm, uygulama, özet) bulunur. Ayarlar → *Dosya türleri* ile Windows'ta simgeleri ve çift tıkla açmayı (yalnızca bu kullanıcı için, HKCU) etkinleştirebilirsiniz.

| Uzantı | İçerik |
| --- | --- |
| `.rwproj` | `schematic.json`, `pcb/board.json`, `mechanics/assembly.json`, `firmware/<kart>/sketch.ino` + `.hex`, `library/parts.json`, `settings.json`, `preview.png`. Eski tek dosyalık JSON projeler de açılır ve kaydedilince pakete dönüşür. |
| `.rwpart` | Tek kullanıcı parçası: dayandığı simülasyon modeli, parametreler, isteğe bağlı PCB izdüşümü. |
| `.rwlib` | Parça koleksiyonu (paylaşım için). Kullanıcı kütüphanesi `%APPDATA%\RobotWinStudio\Library` altındadır. |
| `.rwsim` | Simülasyon kaydı: osiloskop kanalları (CSV), olay günlüğü, seri çıktı, araç yörüngesi ve projenin kendisi. |

### Kod yazma ve kartlara yükleme

*Kod* sayfasında her programlanabilir kartın yazılımı proje içinde saklanır. **Derle (F7)** gerçek avr-gcc ile `arduino-cli` üzerinden derler; hatalar satırında işaretlenir. **Yükle ve çalıştır (F5)** derlenmiş HEX'i emüle karta yükleyip simülasyonu başlatır. Gerekli araç: `winget install ArduinoSA.CLI` ve `arduino-cli core install arduino:avr`.

Derleme ve çalıştırma (.NET 9 SDK yeterli):

```powershell
dotnet build RobotWinStudio.sln -c Release
dotnet test CoreSim/tests/RobotTwin.CoreSim.Tests
builds/RobotWin.Studio/Release/net9.0-windows/RobotWinStudio.exe
```

Komut satırı modları: `--selftest --selftest-out rapor.txt` (tüm örnekler: simülasyon + araç fiziği + PCB + DRC), `--snapshot ekran.png --page home|schematic|pcb|code|assembly|library|settings [--example quadcopter] [--simulate 1.5] [--fault battery|motor|syntax] [--wait sn]`, `--export-logo klasör`.

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
