# System Architecture

RobotWin Studio splits the problem into a few clear responsibilities: orchestration (CoreSim), native simulation work (NativeEngine), firmware execution (FirmwareEngine), and visualization/control (Unity). The goal is simple: keep simulation stepping deterministic and testable, while allowing Unity to focus on UI without being in charge of time.

## High-Level Topology

```mermaid
graph TD
    subgraph User Space
        Unity[RobotWin (Unity UI)]
        Input[Input System]
    end

    subgraph Realtime Kernel Space
        Core[CoreSim (Orchestrator)]
        Physics[NativeEngine (C++ Physics)]
        Thermal[Thermal Solver]
        Env[Environment Solver]
    end

    subgraph Emulation Space
        AVR[AVR Emulator (Arduino)]
        QEMU[QEMU Guest (Raspberry Pi/Linux)]
    end

    Unity <-->|Shared Memory| Core
    Core <-->|IPC Ring Buffer| Physics
    Core <-->|Virtual Bus| AVR
    Core <-->|VirtIO| QEMU
    Physics <-->|Heat Map| Thermal
```

## Module Descriptions

### 1. CoreSim

- **Role:** Deterministic scheduler and state manager.
- **Responsibility:**
  - Synchronizes all subsystems (Physics, Firmware, UI).
  - Manages the "Global Simulation Time" (GST).
  - Handles serialization/deserialization of the circuit model.
- **Tech Stack:** .NET 8, High-Performance shared memory.

### 2. NativeEngine

- **Role:** High-fidelity multi-physics solver.
- **Responsibility:**
  - **Rigid Body Dynamics:** Mass, inertia, collision, friction (PhysX/Jolt backend).
  - **Aerodynamics:** Lift, drag, and turbulence calculations for drone rotors/wings.
  - **Thermodynamics:** Heat generation (P=IV), thermal mass, conduction, convection, and active cooling (fans/heatsinks).
  - **Environment:** Wind vectors, atmospheric density, magnetic field simulation.
- **Tech Stack:** C++20, SIMD-optimized, OpenMP.

### 3. FirmwareEngine

- **Role:** Instruction-set emulator for embedded targets.
- **Sub-Modules:**
  - **VirtualArduino:** Cycle-accurate AVR emulation for Arduino Uno/Mega/Nano.
  - **VirtualPi (QEMU):** ARM64 virtualization running full Linux (Debian/Ubuntu).
- **Connectivity:**
  - Exposes virtual GPIO, I2C, SPI, UART interfaces that map directly to CoreSim signals.
  - Supports "Virtual Sensors" (e.g., a virtual MPU6050 driver in Linux talks to the physics engine's simulated accelerometer).

### 4. RobotWin (The Window)

- **Role:** Visualization and Interaction layer.
- **Responsibility:**
  - Rendering the 3D scene (HDRP/URP).
  - User Interface for circuit building and debugging.
  - Plotting telemetry graphs.
  - **Note:** The UI is _passive_. If the UI hangs, the simulation continues in the background.

## Authoritative Runtime Choices

- **Firmware:** FirmwareEngine is the authoritative runtime for firmware execution and timing.
- **Circuit/Physics:** NativeEngine is the authoritative circuit + physics solver.
- **Fallbacks:** Unity/CoreSim "virtual" runtimes are treated as fallback or preview paths, not the reference.

## Key Architectural Patterns

### Shared Memory Transport (SMT)

To achieve <1ms latency between processes, we utilize a zero-copy Shared Memory Transport.

- **Ring Buffers:** Lock-free ring buffers for high-throughput sensor data (Lidar/Camera).
- **State Snapshots:** Double-buffered state atoms for atomic updates of physics transforms.

### Deterministic Lockstep

The simulation runs in fixed time steps (e.g., 1000Hz).

1. **Input Phase:** CoreSim gathers inputs from UI and Firmware.
2. **Solve Phase:** NativeEngine advances physics by dt.
3. **Emulate Phase:** FirmwareEngine executes N cycles of instructions.
4. **Commit Phase:** State is finalized and written to the output buffer.

### The "Perfect Sensor" Fallacy

RobotWin avoids ideal sensors. All sensor data passes through a **Noise & Degradation Layer**:

- **IMU:** Adds bias, random walk, and temperature-dependent drift.
- **Camera:** Simulates lens distortion, rolling shutter, and sensor noise.
- **GPS:** Simulates multipath interference and satellite visibility.

## File Formats

- **.rwin:** Project file (JSON/YAML) defining the robot structure, circuit connections, and environment settings.
- **.rsim:** Replay file containing the deterministic input log for perfect session reproduction.
