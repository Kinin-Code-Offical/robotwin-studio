# Testing

This document lists the common local test and validation flows.

## CoreSim tests

```powershell
dotnet test CoreSim/CoreSim.sln
```

## .rtwin format notes

- Magic: `RTWN` (little-endian in the file).
- Version: `1` (see `CoreSim/src/RobotTwin.CoreSim/Serialization/SimulationSerializer.cs`).
- Fixtures live under `CoreSim/tests/RobotTwin.CoreSim.Tests/Fixtures/`.

## Unity smoke test

```powershell
python tools/rt_tool.py run-unity-smoke
```

## Integration tests (Node)

```powershell
python tools/rt_tool.py run-qa
```

The integration tests are in `tests/integration/` and use Jest (see `tests/integration/package.json`).

## Physical overrides validation

```powershell
python tools/rt_tool.py validate-physical-overrides
```

## Golden trace capture

```powershell
python tools/rt_tool.py record-golden-trace
```

Golden trace fixtures are stored under `CoreSim/tests/RobotTwin.CoreSim.Tests/Fixtures/` and are intended to catch deterministic drift across changes.

## Raspberry Pi shared memory smoke

```powershell
python tools/rt_tool.py rpi-smoke
```
