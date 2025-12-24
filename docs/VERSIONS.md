# Versions and Dependencies

## Unity
- **Version**: 2022.3.62f3
- **Scripting Backend**: Mono / .NET Standard 2.1
- **Plugins**: `Assets/Plugins/RobotTwin.CoreSim.dll` (Managed externally)

## CoreSim
- **Target Frameworks**: `net9.0` (Tests/CLI), `netstandard2.1` (Unity Plugin)
- **Sync**: Run `./tools/update_unity_plugins.ps1` to build and copy the plugin to UnityApp.
- **CI**: Enforces plugin synchronization.

## PowerShell
- Scripts typically require PowerShell 7 (`pwsh`), but basic tooling is compatible with Windows PowerShell 5.1.
