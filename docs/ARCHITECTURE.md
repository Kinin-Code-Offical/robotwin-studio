# Architecture

## Overview

RoboTwin Studio is a **Windows-only standalone** simulation platform for building and testing **generic Arduino-based systems** (circuit-only, mechatronics, and robotics). It is **template-based** and supports a wide range of user-defined configurations.

Key intent:

- End-users do **not** install Unity Editor. We ship a Windows application.
- The product is **not** a single robot simulator. Any “Line Follower” scenario is only a **Verification Template**.

## Design Goals

- **Single-application UX**: Wizard-driven, Assisted-first; Expert mode unlocks full control.
- **Generic platform**: templates define the system (circuit/firmware/robot/world/tests); robotics is optional.
- **Determinism**: fixed timestep + seeded randomness; reproducible runs; replay from recorded data.
- **Observability**: live telemetry + event log + exportable report; logging starts from run start/spawn.
- **Extensibility**: catalogs and templates grow over time; electronics fidelity can be upgraded later.
- **Windows-first delivery**: CI/build and distribution optimized for Windows.

## Non-Goals (Initial MVP Boundaries)

- Full SPICE-grade analog simulation (MVP is behavioral-first; architecture stays ready for a solver plugin).
- Complex CAD import pipeline (can be added later).
- Multi-robot sessions (MVP focuses on one system per run).
- Advanced physics (soft-body, fluids).

---

## High-Level Components

### CoreSim (Pure C# / No Unity)

CoreSim is the deterministic heart of the simulator. It contains only .NET code (no Unity references).

Responsibilities:

- **Data Models / Specs**
  - `CircuitSpec`, `FirmwareSpec`, `RobotSpec`, `WorldSpec`, `TestSpec`, `RunSession`
  - `TemplateSpec` (how a system is composed)
- **Catalogs**
  - `ComponentCatalog` (circuit/mechatronics building blocks)
  - `BoardCatalog` (Arduino boards + pin capabilities/roles)
  - `SensorActuatorCatalog` (robot parts and their IO expectations)
  - `TemplateCatalog` (templates: circuit-only, mechatronics, robotics)
- **Contracts**
  - `IOContract` (generic signal definitions, types, units/rates where applicable)
- **Simulation Runtime**
  - `VirtualArduino` (behavioral controller runtime in MVP)
  - `CircuitSim` (behavioral node/rail/power modeling in MVP)
  - `FailureModels` (power/thermal/actuation constraints)
  - `TelemetryBus` (frames + events)
  - `Recorder` (logs + report + replay data)

Core constraints:

- Deterministic update step given `{RunSession.seed, FixedDeltaTime, inputs}`.
- All serialization is versioned (schema versioning) and round-trip tested.

### UnityApp (Unity UI + 3D Physics + Visualization)

UnityApp is the visualization and interaction layer.

Responsibilities:

- **UI**: Unity UI Toolkit screens (Wizard, Circuit, Firmware, Robot, World, Run).
- **Physics/Visualization**: Unity PhysX runtime; real scale convention: **1 Unity unit = 1 meter**.
- **Adapters/Factories**:
  - Convert specs from CoreSim into GameObjects and runtime rigs:
    - `RobotFactory`, `TrackFactory`, `SensorSimulator`, `ActuatorApplier`
- **Run Orchestration**:
  - FixedUpdate loop bridges Unity world state ↔ CoreSim simulation step.
- **Live Telemetry HUD**:
  - Graphs, signal views, fault flags, reason codes, run controls.
- **Persistence**:
  - Uses `Application.persistentDataPath` for run artifacts.

---

## Data Contracts and Serialization

### Specs (JSON)

All user projects are defined via JSON specs. At minimum:

- `TemplateSpec` references: board, default circuit blocks, IO mapping, optional robot/world, test pack.
- `CircuitSpec` defines blocks/components + connections (nets) + power rails.
- `FirmwareSpec` defines controller config + mappings to `IOContract`.
- `RobotSpec` defines parts + transforms (real units; mm converted to meters).
- `WorldSpec` defines track/environment sources and parameters.
- `RunSession` captures seed, fixed dt, versions, and output paths.

All serialized artifacts must include:

- `schemaVersion`
- `createdAt` / `updatedAt` (optional but helpful)
- `engineVersion` / `appVersion` (when available)

### IOContract

`IOContract` is the glue between circuit, firmware, robot, and world.

Minimum capabilities (generic):

- Digital signals: `DigitalIn`, `DigitalOut`
- Analog signals: `AnalogIn`
- PWM signals: `PwmOut`
- Bus contracts (behavioral in MVP): `I2C`, `SPI`, `UART`
- Optional metadata: units, nominal rate, bounds, default values

---

## Runtime Execution Model

### Run Startup

1. User selects a template in Wizard (or blank template).
2. User edits Circuit/Firmware (and optionally Robot/World).
3. Run starts: CoreSim creates `RunSession` (includes seed and fixed timestep).
4. Logging starts immediately (telemetry frames + event log).

### Fixed Timestep Loop (Deterministic)

On each simulation tick:

1. **Input Providers**
   - Waveform injection (test-defined inputs)
   - World-driven sensor sampling (robotics templates)
2. **Firmware Step**
   - `VirtualArduino` reads inputs, updates controller state, writes outputs.
3. **Circuit Step**
   - Behavioral power rails / pin state constraints are applied.
4. **Failure Models**
   - Apply torque saturation, thermal derating, brownout reset (MVP subset).
   - Emit event log entries with reason codes.
5. **Telemetry Capture**
   - Publish a frame to `TelemetryBus` and record it.
6. **Unity Apply**
   - UnityApp updates GameObjects (actuators, visualization, HUD).

### Replay

- Replay consumes recorded telemetry (and optional world snapshots as needed).
- Replays do not re-run physics; they play back recorded outcomes deterministically.

---

## Failure Modeling (MVP Subset + Extensible)

Failures are visible live and appear in reports with reason codes.

MVP minimum behaviors:

- **Power**: battery sag / rail drop → `BROWNOUT_RESET`
- **Actuation**: torque saturation → `TORQUE_SATURATION`
- **Thermal**: thermal derating → `SERVO_OVERHEAT_DERATE`

Extensible categories (later):

- Sensor dropout/noise/latency
- Protocol faults (bus lock, checksum)
- Mechanical slip/backlash
- Component failure modes (configurable injections)

---

## Testing Strategy

### CoreSim Tests (Required Early)

- Serialization round-trip (specs + sessions)
- Determinism tests (same seed + same inputs → same outputs)
- Waveform/test DSL unit tests
- Failure model unit tests (threshold behavior)

### Unity Tests (MVP Minimum Once UnityApp Exists)

- **EditMode smoke**: project loads; UI root instantiates; no exceptions.
- **PlayMode smoke**: run a minimal template for N ticks; telemetry frames produced; logs written.
- **Build smoke** (as feasible): CI produces a Windows artifact; basic launch check.

---

## Repository Layout (Target)

- `/CoreSim` — pure C# library + unit tests
- `/UnityApp` — Unity project (UI/3D runtime)
- `/docs` — architecture, setup, user docs
- `/.github` — workflows, templates
- `/.agent` — agent workflows and operational playbooks

---

## Extensibility Notes

- New components/boards/templates are added via catalogs (data-first).
- Electronics fidelity can be increased by introducing an `ICircuitSolver` plugin interface while keeping specs stable.
- Templates must remain generic; “robot-specific” logic belongs in templates and Unity adapters, not in CoreSim contracts.
