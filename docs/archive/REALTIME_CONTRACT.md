# Realtime Contract

This document defines the realtime stepping contract between CoreSim, NativeEngine, FirmwareEngine, and the Unity front end.

## Goals

- Stable fixed-step cadence under load.
- Deterministic ordering across subsystems.
- Clear behavior on missed deadlines.

## Master clock

- A single master clock advances simulation time.
- Subsystems consume the same dt for a given tick.
- Unity does not advance time.

## Step order (per tick)

1. Ingest inputs.
2. Firmware step.
3. Circuit solve.
4. Physics step.
5. Publish outputs.

## Deadline policy

- Each tick has a target budget (see REALTIME_BUDGETS.md).
- Overruns trigger a fast-path to keep cadence and log the miss.

## Implementation pointers

Unity runtime orchestration and realtime scheduling live in:

- `RobotWin/Assets/Scripts/Game/SimHost.cs` (tick loop, budget counters, fast-path decisions)
- `RobotWin/Assets/Scripts/Game/RealtimeScheduleConfig.cs` (budget configuration)

Firmware stepping uses the `RTFW` protocol (lockstep or realtime mode) and physics stepping uses `NativeBridge.Physics_Step(dt)`.

## Non-negotiables

- dt must be explicit per step and monotonic.
- Every output frame is attributable to a step sequence and tick count.
- Hot loops avoid blocking calls; IO writes are buffered or rate-limited.
