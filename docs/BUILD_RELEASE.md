# Build and Release

These commands build the main artifacts on Windows.

## Build native and firmware

```powershell
python tools/rt_tool.py build-native
python tools/rt_tool.py build-firmware
```

Logs:

- Native build logs: `logs/native/`
- Firmware build logs: `logs/firmware/`

## Sync Unity plugins

```powershell
python tools/rt_tool.py update-unity-plugins
```

This copies the built CoreSim plugin (and any required native binaries) into `RobotWin/Assets/Plugins/`.

## Build Unity standalone

```powershell
python tools/rt_tool.py build-standalone
```

The standalone build script is `tools/scripts/build_windows_standalone.ps1` and writes logs under `logs/unity/`.
