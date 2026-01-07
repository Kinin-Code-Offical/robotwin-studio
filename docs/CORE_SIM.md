# CoreSim

CoreSim is the orchestration layer that owns simulation time and cross-system ordering.

## Responsibilities

- Maintain the master clock and fixed step cadence.
- Schedule firmware, circuit, and physics in a consistent order.
- Provide a single place for data marshaling and state publication.

## Tick ordering (reference)

CoreSim drives a fixed-step tick and keeps ordering consistent across subsystems:

1. Ingest inputs (UI actions, device inputs, external IO).
2. Step firmware (FirmwareEngine) for the tick's dt.
3. Solve circuit and signal propagation.
4. Step physics (NativeEngine) for the tick's dt.
5. Publish outputs and telemetry.

## Key areas

- Host/SimHost: tick loop and configuration.
- IPC clients: firmware and native engine bridges.
- Circuit model: netlist and signal propagation.

## IPC notes

- Firmware protocol: `RTFW` framing and versioning is implemented in `CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareProtocol.cs`.
- Unity uses a matching client implementation in `RobotWin/Assets/Scripts/CoreSim/FirmwareClient.cs`.

## .rtwin projects

- `.rtwin` is a custom binary package format with magic `RTWN` and version `1`.
- Serialization entry point: `CoreSim/src/RobotTwin.CoreSim/Serialization/SimulationSerializer.cs`.

## Host modes

- Headless / deterministic stepping: `RobotTwin.CoreSim.Host.SimHost.StepOnce()` (synchronous, no sleep).
- Background loop: `RobotTwin.CoreSim.Host.SimHost.Start()` (threaded loop with optional realtime hardening).

`SimHostOptions` lives at `CoreSim/src/RobotTwin.CoreSim/Host/SimHostOptions.cs` and includes deterministic and realtime settings.

## Realtime hardening (Windows)

CoreSim includes an optional hardening scope used by the host loop:

- `CoreSim/src/RobotTwin.CoreSim/Host/RealtimeHardening.cs`
- `CoreSim/src/RobotTwin.CoreSim/Host/RealtimeHardeningOptions.cs`

## Build and tests

- Build: `dotnet build CoreSim/CoreSim.sln`
- Tests: `dotnet test CoreSim/CoreSim.sln`

## Integration rules

- Keep the tick loop allocation-free.
- Avoid Unity dependencies in the core library.
- Use explicit dt and step counters for every output frame.
