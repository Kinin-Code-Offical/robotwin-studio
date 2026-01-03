# Windows Development Setup

This project is Windows-first and expects Unity 6000.3.2f1.

## Prerequisites

- Windows 10/11 (x64)
- Unity Hub + Unity 6000.3.2f1
- .NET SDK 9.0
- Python 3.11+
- Git
- CMake 3.26+
- Visual Studio 2022 Build Tools (Desktop C++ workload)
- Node.js 20+ (for integration tests)

## Clone and Bootstrap

1. Clone the repo:

   ```powershell
   git clone https://github.com/<your-org>/robotwin-studio.git
   cd robotwin-studio
   ```

2. Verify .NET and Unity versions:

   ```powershell
   dotnet --version
   ```

3. Open `RobotWin` from Unity Hub.

## Plugin Sync

CoreSim builds into a Unity plugin. After C# changes, sync plugins:

```powershell
python tools/rt_tool.py update-unity-plugins
```

## Environment Notes

- Unity uses the `RobotWin` folder as the project root.
- Logs are written under `logs/`.
- Large Unity temp folders (`RobotWin/Library`, `RobotWin/Temp`) are not tracked.
