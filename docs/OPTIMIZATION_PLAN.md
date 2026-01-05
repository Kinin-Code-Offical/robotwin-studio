# Polish & Optimization Plan

## NativeEngine Solver
- P2-16: Sparse MNA solver with factor reuse.
- P2-17: Adaptive convergence with early exit.
- P2-18: Circuit islanding for independent subnets.

## FirmwareEngine Runtime
- P2-19: Reduce SyncInputs allocations with reusable buffers.
- P2-20: Batch serial output messages for realtime.
- P2-21: PWM mapping for Uno + Mega.
- P2-22: External interrupt modes (edge/level).

## Mixed-Signal and ADC
- P2-23: AVR IO electrical modeling tier.
- P2-24: ADC reference/scaling rules consistency.

## Protocol & Replay
- P2-25: Opt-in trace stream for firmware stepping.
- P2-26: Snapshot/restore for deterministic rewind.

## Architecture Refactors
- P3-28: RTFW schema single source of truth.
- P3-29: Centralize BVM parsing/writing.
- P3-30: Unify simulation clock.
- P3-31: Approximation tiers enforcement.
- P3-32: Remove Unity/CoreSim namespace collisions.
- P3-33: Netlist-to-NativeEngine builder.
- P3-34: End-to-end replay system.
