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

## Ownership Matrix (What Lives Where)

- CoreSim: deterministic circuit solving, logic/telemetry, and simulation metadata (no physics).
- NativeEngine: full rigid-body physics, constraints, collision, friction, aerodynamics, and actuator dynamics.
- FirmwareEngine: real firmware execution (BVM/HEX), pin I/O, and serial flow.
- RobotWin (Unity): UI, scene orchestration, visualization, and editor tools.

## Determinism & Noise Model

To reflect real-world imperfections, physics uses deterministic noise:

- Gravity micro-jitter (configurable)
- Time-step micro-jitter (configurable)
- Material variance (planned)
- Actuator response variance (planned)

## CoreSim <-> NativeEngine Data Contract (Draft)

CoreSim produces control intents; NativeEngine produces physical state + sensor feedback.

### CoreSim → NativeEngine (ControlFrame)

- tick_index: int
- dt_seconds: float
- actuators: list
  - id: string
  - type: servo | motor | thruster
  - target: float (angle / rpm / thrust)
  - pwm: float (0..1)
  - torque_limit: float
  - enabled: bool
- power: list
  - board_id: string
  - rail_voltage: float

### NativeEngine → CoreSim (PhysicsFrame)

- tick_index: int
- dt_seconds: float
- bodies: list
  - id: string
  - position: vec3
  - rotation: quat
  - linear_velocity: vec3
  - angular_velocity: vec3
- sensors: list
  - id: string
  - type: imu | encoder | gyro | accel | distance
  - value: float/vec3
- constraints: list
  - id: string
  - error: float
  - impulse: float

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
