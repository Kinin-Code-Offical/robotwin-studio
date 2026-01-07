# RobotWin Studio

RobotWin Studio is a Windows-first robotics simulation workspace. It combines a Unity front end with a deterministic .NET orchestration layer and native C++ engines.

## Repo overview

- CoreSim (C#): deterministic scheduler and IPC bridge.
- NativeEngine (C++): physics and environment simulation.
- FirmwareEngine (C++): firmware emulation and guest OS virtualization.
- RobotWin (Unity): visualization and authoring UI.

## Quick start (Windows)

1. Install prerequisites: Visual Studio 2022, Unity 6 LTS, .NET SDK, Python 3.11+, CMake, and a C++ compiler in PATH.
2. Clone the repo.
3. Run `python tools/rt_tool.py setup`.
4. Open `RobotWin/` in Unity Hub.
5. Sync plugins with `python tools/rt_tool.py update-unity-plugins`.

## Documentation

- docs/SETUP_WINDOWS.md
- docs/ARCHITECTURE.md
- docs/PROJECT_STRUCTURE.md
- docs/TOOLS.md
- docs/TESTING.md
- docs/DEBUG_CONSOLE.md
- docs/TROUBLESHOOTING.md
- THIRD_PARTY_NOTICES.md

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
