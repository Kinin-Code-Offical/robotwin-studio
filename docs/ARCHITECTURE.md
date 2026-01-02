# Architecture Overview

RobotWin Studio is a multi-module system that combines a deterministic simulator with a Unity UI front-end and native firmware/runtime support.

## Major Modules

- CoreSim: Pure C# simulation core. Owns circuits, components, and deterministic execution.
- RobotWin: Unity project for UI, visualization, and interaction.
- FirmwareEngine: C++ firmware runtime (VirtualArduinoFirmware.exe).
- NativeEngine: Native performance components and build system.

## Runtime Flow (Typical)

1. User builds or loads a circuit in RobotWin.
2. RobotWin validates and serializes the circuit model.
3. CoreSim executes the simulation tick loop.
4. Optional firmware execution is bridged via FirmwareEngine.
5. RobotWin renders results and displays telemetry.

## Data Boundaries

- CoreSim is Unity-agnostic and should not depend on Unity types.
- RobotWin communicates with CoreSim via serialized models and plugin APIs.
- FirmwareEngine exchanges telemetry via file or IPC contracts defined in CoreSim.

## Design Goals

- Deterministic simulation (CoreSim).
- Responsive UI (RobotWin + UI Toolkit).
- Clear separation of native/managed responsibilities.

## Product Vision

The long-term goal is a full-stack robotics simulator: build a robot and its world, simulate circuits in real time, and include servo/sensor physics so that running the real robot yields nearly identical behavior and telemetry to the simulated result.

## Confirmed Roadmap Milestones

- Raspberry Pi target support with on-device AI inference workflows and deployment tooling.
- C++ physics simulator pipeline that extends CoreSim with higher-fidelity rigid-body and actuator modeling.
- CAD post-processing for robotics parts: import, classify by function, and enable editable properties (mass, materials, tolerances, and mounting metadata).
