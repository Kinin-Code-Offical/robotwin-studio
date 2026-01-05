# Realtime Contract

This document defines the realtime stepping contract between Unity, CoreSim, NativeEngine, and FirmwareEngine.

## Goals
- Stable step cadence under load.
- Deterministic ordering across domains (firmware, circuit, physics).
- Clear behavior on missed deadlines (drop, catch-up, or slow-mode).

## Master Clock
- A single master clock advances simulation time.
- All subsystems consume dt from the master clock (no local drift).
- Unity visual frame rate must never change simulation time.

## Step Ordering (per tick)
1. Ingest inputs (IO voltages, sensor values, user actions).
2. Firmware step (MCU runtime) consumes inputs and writes outputs.
3. Circuit solve consumes MCU outputs and external sources.
4. Physics step consumes circuit signals and control values.
5. Emit telemetry and logs.

## Deadline Policy
- Each tick has a target budget (see REALTIME_BUDGETS.md).
- If a subsystem exceeds its budget:
  - Fast-path keeps last stable output and logs a "late" marker.
  - Corrective-path performs a full step when budget allows.

## Required Properties
- dt must be explicit and monotonic.
- OutputState must include step_sequence and tick_count.
- No blocking calls in hot loops (IPC writes are buffered).

## Unity Responsibilities
- Unity calls circuit stepping in the logic tick (not per frame).
- Physics stepping remains separate and may run at a higher fixed rate.
- Unity never invents time; it only visualizes the latest committed state.
