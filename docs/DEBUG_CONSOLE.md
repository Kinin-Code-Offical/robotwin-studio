# Debug Console

The Debug Console is a local web dashboard that runs tests and surfaces logs from the repo.

## Launch

```powershell
python tools/rt_tool.py debug-console
```

The server runs at `http://localhost:8090`.

## Features

- Run CoreSim, physics, Unity smoke, native build, firmware build, and QA tests.
- Capture all command output into `logs/debug_console`.
- Browse logs from `logs/unity`, `logs/firmware`, `logs/native`, and `logs/tools`.
- View detected COM ports (including com0com virtual ports when present).
- Monitor Unity simulation status and component telemetry (when the Unity RemoteCommandServer is running).
- Inspect CoreSim <-> NativeEngine bridge status and the draft contract keys.
- Track NativeEngine physics world status and body counts.
- Stream bridge snapshots from Unity into `logs/native/bridge.log` (via `BridgeLogWriter`).

## Outputs

- Test runs are stored as timestamped `.log` files in `logs/debug_console`.
- Log previews in the UI default to the most recent 400 lines.
