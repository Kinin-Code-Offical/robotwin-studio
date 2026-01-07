# Realtime Design Notes

Working notes for realtime execution.

## Scheduling

- Fixed dt with explicit tick counters.
- Per-subsystem budgets and deadline tracking.
- Fast-path for overrun recovery.

## Data flow

- Inputs and outputs are double-buffered.
- High-rate streams use shared memory when enabled.
- Low-rate control uses IPC.

## Engineering rules

- Avoid allocations in hot loops.
- Keep ordering identical across platforms.
- Record inputs for replay when validating changes.

## Fast-path vs corrective-path

When the system is overloaded, the runtime can switch to a reduced-work tick:

- Fast-path: reuse last stable circuit/physics state, prioritize firmware stepping, and publish telemetry about the downgrade.
- Corrective-path: perform a full solve once budget allows and resynchronize outputs.

The Unity runtime tracks fast-path and overrun counters in `RobotWin/Assets/Scripts/Game/SimHost.cs`.

## Observability

Recommended signals to watch when profiling or debugging realtime behavior:

- tick duration and jitter counters
- budget overrun counters
- fast-path / corrective-path counters
- firmware perf counters (cycles, ADC samples, serial bytes, transfers)
