## RobotWin Studio - Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
