# Optimization Plan

Backlog items focused on performance and determinism hygiene.

## NativeEngine

- Solver profiling and hot-path cleanup.
- Reduce per-step allocations.
- Improve broadphase and islanding.

## FirmwareEngine

- Reduce IO marshaling overhead.
- Batch serial output.
- Improve pin mapping performance.

## CoreSim

- Minimize allocations in tick loop.
- Reduce IPC overhead.
- Expand regression coverage for timing and drift.

## Measurement rules

- Prefer changes that can be measured via existing logs or tests.
- When changing timing-sensitive code, capture a before/after trace (tick duration, jitter, overrun counts).
- For determinism-sensitive changes, re-run golden traces and keep fixtures stable.
