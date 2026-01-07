# Tooling and CLI

The primary entry point is `tools/rt_tool.py`.

Run `python tools/rt_tool.py help` for the full command list and short descriptions.

## Setup

```powershell
python tools/rt_tool.py setup
python tools/rt_tool.py setup --check-only
```

Optional flags:

- `--qa`: also run Node/Jest integration tests.
- `--install-com0com`: install/create virtual COM pairs (may require admin).
- `--no-native`, `--no-firmware`, `--no-plugins`: skip selected steps.
- `--standalone`: also build the Unity Windows player.
- `--unity-path <path>`: Unity.exe path used with `--standalone`.

## Build

```powershell
python tools/rt_tool.py build-native
python tools/rt_tool.py build-firmware
python tools/rt_tool.py build-standalone
python tools/rt_tool.py update-unity-plugins
```

## Validation

```powershell
python tools/rt_tool.py validate-uxml
python tools/rt_tool.py audit-runtime
python tools/rt_tool.py audit-build-outputs
python tools/rt_tool.py validate-physical-overrides
```

## Tests

```powershell
python tools/rt_tool.py run-unity-smoke
python tools/rt_tool.py run-qa
python tools/rt_tool.py record-golden-trace
python tools/rt_tool.py rpi-smoke
```

Notes:

- `run-qa` executes `tests/integration` via `npm install` + `npm test` (see `tools/scripts/run_qa.ps1`).
- `run-unity-smoke` compiles the Unity project in batchmode and writes `logs/unity/smoke.log` (see `tools/scripts/run_unity_smoke.ps1`).

## Utilities

```powershell
python tools/rt_tool.py debug-console
python tools/rt_tool.py monitor-unity
python tools/rt_tool.py console --url http://localhost:8085
python tools/rt_tool.py help
```

## Component packages

To (re)build `.rtcomp` packages for bundled components and convert STEP/STP models to `.glb`:

```powershell
py -3.12 -m venv .venv_step
.\.venv_step\Scripts\python -m pip install cadquery-ocp==7.7.2
.\.venv_step\Scripts\python tools/scripts/build_rtcomp_from_components.py
```

## Outputs

- Build artifacts: `builds/`
- Logs: `logs/` (per-subsystem folders plus tool logs)
