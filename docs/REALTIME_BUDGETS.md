# Realtime Budgets

These budgets define target execution windows for a single simulation tick.
They are per-board/per-scene defaults and can be tuned per product tier.

These numbers are starting points; adjust based on the scene and the hardware.

## Default Budgets (per tick)

- Firmware step (MCU): 0.4 ms
- Circuit solve (CoreSim): 0.8 ms
- Physics step (NativeEngine): 1.6 ms
- IPC + logging: 0.2 ms
- Total target: 3.0 ms

## Tier Targets

- Reference tier: maximize accuracy, allow up to 8.0 ms per tick.
- Realtime tier: honor 3.0 ms budget, drop to fast-path on overrun.
- Ultra tier: 1.5 ms budget, drop to aggressive fast-path on overrun.

## Error Budgets

- MCU timing jitter: <= 50 us per tick
- Circuit voltage error: <= 1% for stable nets
- Physics integration error: <= 0.5% over 1 second

## Overrun Policy

- If total budget exceeded for N consecutive ticks (default N=3):
  - Switch to fast-path.
  - Emit telemetry warning.
  - Attempt recovery every M ticks (default M=10).
