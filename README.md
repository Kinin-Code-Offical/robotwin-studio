# RobotWin Studio (Windows)

A powerful Arduino + robotics simulator platform designed for Windows.

## Overview

RobotWin Studio lets users build and test Arduino circuits, firmware, and complex robots within a Unity-based physics environment.

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
- `/RobotWin`: Unity-based visualization and UI layer.
- `/FirmwareEngine`: C++ virtual firmware execution environment.
- `/NativeEngine`: Native simulation components and build system (CMake).
- `/docs`: Project documentation.

## Documentation

Start with `docs/README.md` for the full documentation index.

## Setup & Development

See `docs/SETUP_WINDOWS.md` for detailed configuration.

### Unity Plugin Sync

CoreSim is built as a plugin for Unity. When changing CoreSim code, sync the plugin:

```powershell
python tools/rt_tool.py update-unity-plugins
```

<!-- BEGIN FOLDER_TREE -->
## Folder Tree

```text
.
|-- .gitignore
|-- CoreSim
|-- docs
|-- FirmwareEngine
|-- global.json
|-- LICENSE
|-- NativeEngine
|-- README.md
|-- RobotWin
|-- tests
-- tools
```
<!-- END FOLDER_TREE -->

## Special Thanks

- com0com project for virtual COM port tooling.
- GrabCAD models by Vin.X.Mod: https://grabcad.com/vin.x.mod-2
- GLB model creators and asset authors whose work is referenced in this project.
















