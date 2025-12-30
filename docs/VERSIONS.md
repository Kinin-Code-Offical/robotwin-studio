# Versions and Dependencies

## Unity

- **Version**: 6000.3.2f1
- **Scripting Backend**: Mono / .NET Standard 2.1
- **Plugins**: `Assets/Plugins/RobotTwin.CoreSim.dll` (Managed externally)

## CoreSim

- **Target Frameworks**: `net9.0` (Tests/CLI), `netstandard2.1` (Unity Plugin)
- **Sync**: Run `python tools/rt_tool.py update-unity-plugins` to build and copy the plugin to UnityApp.
- **CI**: Enforces plugin synchronization.

## Firmware & Native Engine

- **Language**: C++17 or later
- **Build System**: CMake
- **Components**: `FirmwareEngine` (Virtual Arduino), `NativeEngine` (Simulation Host)

## PowerShell

- Scripts typically require PowerShell 7 (`pwsh`), but basic tooling is compatible with Windows PowerShell 5.1.
