# CoreSim: The Realtime Orchestrator

**CoreSim** is the orchestration layer. It owns the simulation clock, defines step ordering, and coordinates data flow between firmware, circuit/IO, native simulation, and Unity.

## Responsibilities

### 1. Deterministic Scheduling

CoreSim manages the **Global Simulation Time (GST)**. It advances the simulation in discrete, fixed time steps (for example 1 ms or 100 μs), and keeps ordering consistent.

- **Strict Ordering:** Ensures Input -> Physics -> Firmware -> Output happens in the exact same order every frame.
- **Replayability:** By logging the inputs at each tick, CoreSim can replay a session with bit-perfect accuracy.

### 2. Circuit & Signal Propagation

CoreSim owns the "Electrical Graph" of the robot.

- **Netlist Solving:** Resolves connections between components (e.g., Battery -> ESC -> Motor).
- **Signal Routing:** Routes logical signals (PWM, UART, SPI) from FirmwareEngine to the appropriate component models.
- **Fault Injection:** Can simulate broken wires, short circuits, or noisy connections on the fly.

### 3. Inter-Process Communication (IPC) Hub

CoreSim manages the high-speed data highways between modules.

- **Shared Memory:** Allocates and manages the ring buffers used for sensor data.
- **Synchronization Barriers:** Uses named mutexes/semaphores to ensure the Physics engine doesn't run ahead of the Firmware.

## Architecture

CoreSim is a pure C# .NET 8 library, designed to be embedded into the Unity process but capable of running headless.

### The Tick Loop

```csharp
public void Tick()
{
    // 1. Gather Inputs
    var inputs = InputSystem.Poll();

    // 2. Step Physics (Native Interop)
    NativeEngine.Step(dt, inputs);

    // 3. Step Firmware (IPC)
    FirmwareEngine.Step(dt);

    // 4. Resolve Circuit Logic
    CircuitSolver.Solve();

    // 5. Publish State
    Telemetry.Publish();
}
```

## Integration Rules

- **No Unity Dependencies:** CoreSim must remain engine-agnostic to support headless cloud simulation.
- **Zero Allocation:** The hot path (Tick loop) must generate zero garbage (GC) to prevent latency spikes.
- **Thread Safety:** All state access must be thread-safe or confined to the simulation thread.
