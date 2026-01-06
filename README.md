# RobotWin Studio

RobotWin Studio is a simulation/digital-twin workspace we’re building to make the “try it quickly in a virtual setup, then move to hardware” loop more organized and deterministic.

This repo brings together Unity (visualization + controls), .NET (orchestration via CoreSim), and C++ (native engine + firmware bridges).

## What’s in this repo?

- **CoreSim (C#):** Simulation orchestration and deterministic stepping.
- **FirmwareEngine (C++):** Firmware runtime/IPC on Windows.
- **NativeEngine (C++):** Performance-critical simulation pieces.
- **RobotWin (Unity):** Visualization, scenes, and user flows.

Note: The docs are written with the “target end-state product” in mind; parts of the codebase are still evolving.

## Quick Start

### Requirements

- Windows 10/11
- Visual Studio 2022 (C++ / .NET)
- Unity 6 (LTS)

### Setup

1. Clone the repo (use recursive if you rely on submodules).
2. Prepare the environment by running `tools/setup_environment.ps1`.
3. Open the `RobotWin/` folder via Unity Hub.
4. Build via `CoreSim/CoreSim.sln` and/or `NativeEngine/NativeEngine.sln`.

## Documentation

- [Architecture Overview](docs/ARCHITECTURE.md)
- [Native Engine](docs/NATIVE_ENGINE.md)
- [Firmware Engine](docs/FIRMWARE_ENGINE.md)
- [Windows Setup](docs/SETUP_WINDOWS.md)
- [Third-Party Notices](THIRD_PARTY_NOTICES.md)

<!-- BEGIN FOLDER_TREE -->

## Project Tree

```text
.
|-- .gitignore
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
-- tools
```

<!-- END FOLDER_TREE -->
