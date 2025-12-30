# RoboTwin Studio (Windows)

A powerful, General Arduino + Robotics Simulator Platform designed for Windows.

## Overview

RoboTwin Studio empowers users to build and test Arduino circuits, firmware, and complex robots within a high-fidelity Unity-based physics environment.

## Key Features (MVP-0)

- **Project Wizard**: Create projects from templates (Blinky, Blank) or kits (Robot Arm).
- **Circuit Studio**: Drag-and-drop wiring validation (e.g. Arduino Uno + LEDs).
- **Run Mode**: Real-time telemetry and execution logs.
- **CoreSim**: High-performance, deterministic C# simulation engine.

## Requirements

- **OS**: Windows 10/11 (x64)
- **Unity**: 6000.3.2f1 (for development)
- **.NET SDK**: 9.0

## Repository Structure

- `/CoreSim`: Pure C# simulation core (deterministic, no Unity dependencies).
- `/UnityApp`: Unity-based visualization and UI layer.
- `/FirmwareEngine`: C++ based virtual firmware execution environment.
- `/NativeEngine`: Native simulation components and build system (CMake).
- `/docs`: Technical documentation and architecture.

## Getting Started (User)

1. Run `RoboTwinStudio.exe`.
2. Follow the Quickstart Guide in `docs/USER_QUICKSTART.md`.

## Setup & Development

See [DEV_SETUP_WINDOWS.md](docs/DEV_SETUP_WINDOWS.md) for detailed configuration.

### Unity Plugin Sync

CoreSim is built as a plugin for Unity. When changing CoreSim code, sync the plugin:

```powershell
python tools/rt_tool.py update-unity-plugins
```
