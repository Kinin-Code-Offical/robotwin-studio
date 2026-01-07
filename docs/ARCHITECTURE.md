# Architecture Overview

RobotWin Studio is split into four primary parts that coordinate through a fixed-step simulation clock.

## Components

- CoreSim: deterministic scheduler, state orchestration, and IPC coordination.
- NativeEngine: C++ physics and environment runtime.
- FirmwareEngine: firmware emulation and guest OS virtualization.
- RobotWin (Unity): visualization, authoring, and interaction.

## High-level topology

```mermaid
graph TD
  subgraph Frontend
    Unity[RobotWin (Unity)]
  end

  subgraph Orchestration
    Core[CoreSim]
  end

  subgraph Native
    NativeEngine[NativeEngine (C++)]
  end

  subgraph Firmware
    FirmwareEngine[FirmwareEngine]
    AVR[MCU Emulation]
    QEMU[QEMU Guest]
    FirmwareEngine --> AVR
    FirmwareEngine --> QEMU
  end

  Unity -->|calls into| Core
  Core <-->|C-ABI / plugin| NativeEngine
  Core <-->|named pipe (RTFW)| FirmwareEngine
```

## Data flow (per tick)

1. Collect inputs (user actions, external IO, and device state).
2. Step firmware.
3. Solve circuit and signal propagation.
4. Step physics.
5. Publish outputs and telemetry.

## Interfaces

- Named pipes for control and low-rate data.
- Shared memory for high-rate streams (camera, lidar) when enabled.
- A single master clock defines simulation time.

## Authoritative runtime choices

- Firmware timing and IO: FirmwareEngine.
- Physics stepping: NativeEngine.
- UI/rendering: Unity consumes the latest committed outputs and does not advance time.

## Determinism guidance

- Fixed dt and explicit step counters.
- Unity renders the latest committed state and does not advance time.
- Input logs enable replay and regression checks.
