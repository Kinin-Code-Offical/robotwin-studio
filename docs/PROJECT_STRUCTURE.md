# Project Structure

This repository is a monorepo. Top-level folders map to major subsystems.

```text
/
  CoreSim/            # C# deterministic orchestration
  NativeEngine/       # C++ physics and environment
  FirmwareEngine/     # C++ firmware emulation and virtualization
  RobotWin/           # Unity front end
  tools/              # scripts and automation
  docs/               # documentation
  tests/              # integration and validation tests
  builds/             # build outputs (generated)
  logs/               # logs (generated)
```

## Notes

- `builds/` and `logs/` are generated and should not be hand edited.
- Unity generates `RobotWin/Library`, `RobotWin/Temp`, and `RobotWin/Logs` at runtime.

## RobotWin content locations

- Component source JSON (authoring): `RobotWin/Assets/Resources/Components/`
- Packaged components for runtime (`.rtcomp`): `RobotWin/Assets/StreamingAssets/Components/`
- Templates (Unity assets): `RobotWin/Assets/Templates/`

## Integration tests

- Node/Jest integration tests live under `tests/integration/` and are run via `python tools/rt_tool.py run-qa`.
