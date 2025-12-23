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
## Build Instructions
1. Open terminal at repo root.
2. Run `dotnet restore CoreSim/CoreSim.sln`.
3. Run `dotnet build CoreSim/CoreSim.sln`.
4. Run `dotnet test CoreSim/CoreSim.sln`.
