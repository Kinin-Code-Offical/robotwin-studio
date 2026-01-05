# Realtime Design Notes

This document captures the realtime execution design for the engine. It is a specification baseline
for future code integration.

## RT-02 Deadline-aware scheduler + per-simulation budgets
- Every tick is assigned a deadline from the master clock.
- Each subsystem consumes a budget slice (see REALTIME_BUDGETS.md).
- If a subsystem overruns, the scheduler downgrades to fast-path and logs the miss.

## RT-03 Fast-Path + Corrective-Path (tiered realtime)
- Fast-path: reuse last stable circuit/physics state and advance firmware only.
- Corrective-path: run full solve once budget allows and re-sync outputs.
- Tier selection is per-scene and can change at runtime.

## RT-04 Realtime circuit solving strategy
- Incremental solve using cached factorization.
- Sparse matrix reuse across ticks when topology unchanged.
- Early-exit when delta below epsilon.

## RT-05 Hybrid MCU/peripheral modeling
- MCU core cycles remain deterministic.
- Peripherals (UART/ADC/PWM) are event-queue driven.
- Optional coarse peripheral updates for fast-path.

## RT-06 Multi-rate stepping (master clock)
- Master clock maintains a priority queue of next-event times.
- Firmware, circuit, and physics register their next deadlines.
- Scheduler advances to earliest deadline and processes due tasks.

## RT-08 Timestamped protocol stream
- Every IPC packet includes:
  - `tick_index`
  - `dt_seconds`
  - `sent_utc`
  - `seq`
- Client maintains a backlog window and drops stale packets.

## RT-09 Windows realtime hardening
- Set thread priority to `TIME_CRITICAL` for simulation loops.
- Pin simulation threads to dedicated cores (affinity mask).
- Use high-resolution timers (`timeBeginPeriod(1)`).
- Avoid heap allocation in hot loops; pre-allocate buffers.
