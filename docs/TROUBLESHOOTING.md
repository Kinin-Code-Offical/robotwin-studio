# Troubleshooting

## Unity does not open

- Verify the Unity version from `RobotWin/ProjectSettings/ProjectVersion.txt`.
- Open `RobotWin/` directly from Unity Hub.

## Missing native plugins

- Run `python tools/rt_tool.py update-unity-plugins`.
- Check `RobotWin/Assets/Plugins` for updated DLLs.

## Unity smoke test skips immediately

- `python tools/rt_tool.py run-unity-smoke` uses a default Unity path in `tools/scripts/run_unity_smoke.ps1`.
- If Unity is installed elsewhere, update the script parameter or install the matching editor version.

## Build failures

- Run `python tools/rt_tool.py setup --check-only` to confirm prerequisites.
- Verify CMake and a C++ compiler are on PATH.

## Integration tests fail (Node)

- Run `python tools/rt_tool.py run-qa` once to populate `tests/integration/node_modules`.
- If `npm install` is slow or blocked, verify proxy settings or run it manually in `tests/integration/`.

## Logs

- Unity logs: `logs/unity/`
- Firmware logs: `logs/firmware/`
- Native logs: `logs/native/`
- Tool logs: `logs/tools/`
