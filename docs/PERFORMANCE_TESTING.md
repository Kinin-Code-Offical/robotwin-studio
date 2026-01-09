# Performance Benchmarking & Testing Guidelines

## Overview

This document defines expected performance metrics and regression testing procedures for the RobotWin Studio simulation engine.

## Critical Performance Targets

### Wire Rendering System (WireRope.cs)

| Metric                 | Target       | Critical Threshold | Test Method                                |
| ---------------------- | ------------ | ------------------ | ------------------------------------------ |
| Frame Time (100 wires) | < 2ms        | < 5ms              | Unity Profiler: `WireRope.SimulateRope()`  |
| GC Allocations         | 0 B/frame    | < 100 B/frame      | Unity Profiler: Memory Allocator           |
| Wire Collision Checks  | < 5000/frame | < 20000/frame      | Debug counter in `ResolveWireCollisions()` |

**Regression Test:**

```csharp
// Create test scene with 100 wires in 10x10 grid
for (int i = 0; i < 100; i++) {
    CreateWire(position: (i % 10) * 0.2f, length: 0.15f);
}
// Run for 300 frames, measure average frame time
```

### Hardware Module Simulation (HardwareOptimizedModule.cs)

| Metric                             | Target            | Critical Threshold | Test Method                    |
| ---------------------------------- | ----------------- | ------------------ | ------------------------------ |
| Simulation Step Time (100 modules) | < 0.5ms           | < 2ms              | Profiler: `SimulateStep()`     |
| Determinism                        | 100% reproducible | Must pass          | Record/replay trace comparison |
| Memory Footprint (per module)      | < 2KB             | < 5KB              | Memory snapshot diff           |

**Regression Test:**

```csharp
// Create 100 hardware modules with varying thermal loads
// Run deterministic simulation for 10000 steps
// Compare trace with golden reference file
```

### LINQ Allocation Hotspots (RunModeController.cs)

| Metric                         | Target      | Critical Threshold | Test Method                   |
| ------------------------------ | ----------- | ------------------ | ----------------------------- |
| `.ToList()` calls in hot paths | 0           | 0                  | Static analysis + code review |
| UI Update GC Allocations       | < 1KB/frame | < 5KB/frame        | Profiler: `LateUpdate()`      |

## Determinism Validation

### Requirements

1. **Fixed Timestep:** All physics simulation must use `Time.fixedDeltaTime`
2. **Seeded RNG:** All random number generation must use deterministic seeds
3. **Replay Consistency:** Recorded sessions must replay identically on different machines

### Test Protocol

```csharp
// 1. Record Session
RecordSession(seed: 12345, duration: 60s);

// 2. Replay on Machine A
var traceA = ReplaySession(recording, machine: "DevBox");

// 3. Replay on Machine B
var traceB = ReplaySession(recording, machine: "CI-Server");

// 4. Compare Traces (bit-exact match required)
Assert.Equal(traceA.StateHashes, traceB.StateHashes);
```

## Continuous Integration Checks

### Pre-Commit (Fast)

- [ ] No compiler warnings
- [ ] Static analysis: No new `.ToList()` in performance-critical files
- [ ] Unit tests pass (< 5 seconds)

### Pull Request (Moderate)

- [ ] Integration tests pass (< 30 seconds)
- [ ] Profiler smoke test: 100 wires @ 60 FPS
- [ ] Memory leak detection: 5 minute stress test

### Nightly (Comprehensive)

- [ ] Full determinism validation suite
- [ ] Thermal stress test: 1000 modules for 1 hour
- [ ] Replay regression: Compare with golden traces
- [ ] Performance benchmarks: Generate trend charts

## Performance Regression Detection

### Automated Alerts

Trigger alerts if:

- Frame time increases by > 20% compared to baseline
- GC allocations increase by > 50%
- Any determinism test fails

### Baseline Metrics (as of 2026-01-08)

```
Wire System (100 wires):
  - Frame Time: 1.2ms (min: 0.8ms, max: 2.1ms)
  - GC Allocations: 0 B/frame
  - Collision Checks: 2400/frame (with spatial grid)

Hardware Modules (100 modules):
  - Step Time: 0.3ms
  - Determinism: PASS (10000 steps validated)
```

## Manual Testing Checklist

### Before Release

- [ ] Load test scene with 200+ wires - verify no stuttering
- [ ] Record 10-minute session, replay on different machine - verify exact match
- [ ] Thermal simulation: Verify components fail at correct temperatures
- [ ] Visual inspection: Wire physics looks natural (sag, collision avoidance)

### Known Performance Pitfalls

⚠️ **Avoid These Patterns:**

```csharp
// BAD: Allocates array every frame
var hits = Physics.OverlapSphere(...);

// GOOD: Reuse static buffer
int count = Physics.OverlapSphereNonAlloc(..., buffer);

// BAD: LINQ allocation in hot path
var boards = circuit.Components.Where(...).ToList();

// GOOD: Direct iteration
foreach (var comp in circuit.Components) {
    if (IsBoardType(comp)) { ... }
}
```

## Debugging Performance Issues

### Unity Profiler Analysis

1. Open Unity Profiler (Window > Analysis > Profiler)
2. Enable Deep Profiling for one frame
3. Identify:
   - Methods taking > 1ms
   - GC.Alloc spikes
   - Physics.Simulate time

### Memory Profiler

1. Take memory snapshot at startup
2. Run simulation for 5 minutes
3. Take second snapshot
4. Compare: Look for leaked objects or growing collections

### Determinism Debugging

1. Enable verbose logging: `Debug.Log` state hashes every 100 frames
2. Compare logs from two replay sessions
3. Binary search: Find first frame where divergence occurs
4. Inspect: Check for `UnityEngine.Random` usage or floating point drift

## Contact

For performance regressions or questions: [File an issue with "Performance" label]
