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
        FW[FirmwareEngine]
        AVR[AVR Emulator (Arduino)]
        QEMU[QEMU Guest (Raspberry Pi/Linux)]
        FW --> AVR
        FW --> QEMU
    end

    Input --> Unity
    Unity -->|calls into| Core
    Core <-->|native plugin calls| Physics
    Core <-->|IPC (named pipe)| FW
    Physics <-->|feeds| Thermal
    Physics <-->|feeds| Env
```

> Note: The arrows show the intended responsibility boundaries. Specific transports differ between the **current implementation** and the **final target**.

## Module Descriptions

### 1. CoreSim

- **Role:** Deterministic scheduler and state manager.
- **Responsibility:**
  - Synchronizes all subsystems (Physics, Firmware, UI).
  - Manages the "Global Simulation Time" (GST).
  - Handles serialization/deserialization of the circuit model.
- **Tech Stack:** C# (.NET; currently built/tested with a .NET 9 toolchain). CoreSim targets both a modern runtime (for tools/tests) and a Unity-friendly target (netstandard).

### 2. NativeEngine

- **Role:** High-fidelity multi-physics solver.
- **Responsibility:**
  - **Rigid Body Dynamics:** Mass, inertia, collision, friction.
  - **Aerodynamics:** Lift, drag, and turbulence calculations for drone rotors/wings.
  - **Thermodynamics:** Heat generation (P=IV), thermal mass, conduction, convection, and active cooling (fans/heatsinks).
  - **Environment:** Wind vectors, atmospheric density, magnetic field simulation.
- **Tech Stack:** C++20, SIMD-optimized, OpenMP.

### 3. FirmwareEngine

- **Role:** Instruction-set emulator for embedded targets.
- **Sub-Modules:**
  - **VirtualArduino:** Cycle-accurate AVR emulation for Arduino Uno/Mega/Nano.
  - **VirtualPi (QEMU):** ARM64 virtualization running full Linux (Debian/Ubuntu).
- **Connectivity (target):**
  - Exposes virtual GPIO, I2C, SPI, UART interfaces that map to the simulated world.
  - Supports "Virtual Sensors" (e.g., a virtual IMU driver in Linux consumes simulated accelerometer/gyro data).

### 4. RobotWin (The Window)

- **Role:** Visualization and Interaction layer.
- **Responsibility:**
  - Rendering the 3D scene (Unity rendering pipeline).
  - User Interface for circuit building and debugging.
  - Plotting telemetry graphs.
  - **Note:** The UI is _passive_. If the UI hangs, the simulation continues in the background.

## Authoritative Runtime Choices

- **Firmware:** FirmwareEngine is the authoritative runtime for firmware execution and timing.
- **Circuit/Physics:** NativeEngine is the authoritative circuit + physics solver.
- **Fallbacks:** Unity/CoreSim "virtual" runtimes are treated as fallback or preview paths, not the reference.

## Current Implementation vs Final Target

This repository contains both **implemented paths** and **target architecture** descriptions. When in doubt, treat the code + tests as source of truth.

### Current (implemented today)

- Firmware execution (Arduino-class) runs in `FirmwareEngine` and is driven through the RTFW IPC protocol (named pipe on Windows).
- Raspberry Pi integration currently exists as an MVP bridge that uses shared-memory files for channels (display/camera/GPIO/IMU/time/network) and a host loop in `tools/rpi/*`.
- Unity owns the interactive loop/UI and coordinates stepping via `SimHost`-style orchestration. CoreSim is also used for tooling/tests and Unity plugin packaging.

### Target (final architecture)

- A single `FirmwareEngine` executable hosts both Arduino (AVR) and Raspberry Pi (QEMU) backends.
- The Raspberry Pi host loop moves into FirmwareEngine (C++). The runtime path does not generate mock sensor data; if QEMU is unavailable, the RPi board is marked unavailable.
- Protocol layouts (including Raspberry Pi shared-memory headers) have a single source of truth.

## Key Architectural Patterns

### Shared Memory Transport (SMT)

Where high-rate payloads (camera/audio/lidar) require it, we use shared-memory style transports to minimize copying.

- **Ring Buffers:** Lock-free ring buffers for high-throughput sensor data (Lidar/Camera).
- **State Snapshots:** Double-buffered state atoms for atomic updates of physics transforms.

### Deterministic Lockstep

The simulation runs in fixed time steps (e.g., 1000Hz).

1. **Input Phase:** CoreSim gathers inputs from UI and Firmware.
2. **Solve Phase:** NativeEngine advances physics by dt.
3. **Emulate Phase:** FirmwareEngine executes N cycles of instructions.
4. **Commit Phase:** State is finalized and written to the output buffer.

## Peripherals & Connectivity (Final Target)

RobotWin needs a unified way to model and route "everything that looks like hardware": sensors, network/radio, and user input devices. The final target is a **single binding model** that connects Unity scene objects to simulated devices, and connects simulated devices to firmware endpoints.

### Device Graph

- A device graph defines **devices** (camera, microphone, IMU, GPIO expanders, meters/gauges, radios) and **bindings** (which Unity object feeds which device, which firmware endpoint consumes which device).
- Devices are **disableable**: if a Raspberry Pi board or a virtual radio is not used in the current session, its module should not allocate buffers or run background work.

### Audio → Microphone Capture

- Unity audio is treated as part of the simulated world. The simulation can capture the spatialized mix and feed it into one or more simulated microphones.
- Microphone samples become an input stream that firmware/guest code can consume (for example, via a virtual mic device path for Linux guests, or an ADC-style stream for MCU targets).

### Virtual WiFi/Bluetooth + Bridge-to-Real

- The default model is a virtual RF environment (latency/loss/interference policies).
- A controlled "bridge-to-real" mode can optionally map simulated devices to the host machine's real network/Bluetooth.

### RC / Radio Bus (Inter-Process)

- External programs can act like an RC transmitter by sending channels over a simple IPC transport (UDP or shared-memory ring buffer).
- RobotWin consumes those channels and routes them into a simulated receiver feeding firmware endpoints.

### The "Perfect Sensor" Fallacy

RobotWin avoids ideal sensors. All sensor data passes through a **Noise & Degradation Layer**:

- **IMU:** Adds bias, random walk, and temperature-dependent drift.
- **Camera:** Simulates lens distortion, rolling shutter, and sensor noise.
- **GPS:** Simulates multipath interference and satellite visibility.

## File Formats

- **.rwin:** Project file (JSON/YAML) defining the robot structure, circuit connections, and environment settings.
- **.rsim:** Replay file containing the deterministic input log for perfect session reproduction.
