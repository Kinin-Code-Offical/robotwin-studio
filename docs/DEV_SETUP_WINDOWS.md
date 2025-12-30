# Developer Setup (Windows)

## Prerequisites
1. **Unity**: Install the version specified in `ProjectSettings/ProjectVersion.txt` (once generated).
2. **IDE**: VS Code or Visual Studio 2022.
3. **Git**: Ensure `git` is in your PATH.
4. **GitHub CLI**: Ensure `gh` is in your PATH and authenticated.

## Repository Layout
- `/CoreSim`: Pure C# logic. Open as a standalone C# project or via the Unity generated solution.
- `/UnityApp`: Open this folder in Unity Hub.

## Build Instructions
1. Open terminal at repo root.

## Building Native Components (C++)
Required for FirmwareEngine and NativeEngine.

**Prerequisites:**
- **CMake**: 3.20 or later.
- **Compiler**: MSVC (Visual Studio 2022 Build Tools) or MinGW-w64.

```powershell
# Build FirmwareEngine (VirtualArduino)
python tools/rt_tool.py build-firmware

# Build NativeEngine (DLL)
python tools/rt_tool.py build-native
```

## Building CoreSim
```powershell
dotnet restore CoreSim/CoreSim.sln
dotnet test CoreSim/CoreSim.sln
```
