# Troubleshooting

Common issues and quick fixes.

## Unity Does Not Open

- Verify Unity 6000.3.2f1 is installed.
- Open `RobotWin` directly from Unity Hub.

## Missing Plugins in Unity

- Run `python tools/rt_tool.py update-unity-plugins`.
- Ensure `RobotWin/Assets/Plugins` has the generated DLLs.

## Unity Compile Errors After Pull

- Delete `RobotWin/Library` (Unity will regenerate).
- Reopen the project and resync plugins.

## Firmware Build Fails

- Ensure Visual Studio Build Tools are installed.
- Confirm CMake and the C++ workload are available.

## Logs

- Unity logs: `logs/unity/`
- Tool outputs: `logs/tools/`
