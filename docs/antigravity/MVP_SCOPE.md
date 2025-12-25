# MVP Scope: RoboTwin Studio (Windows) — Template-Based Generic Arduino Platform

> [!IMPORTANT]
> RoboTwin Studio is a **template-based generic platform** for **any Arduino system** (robotic or non-robotic).
> Any “Line Follower” scenario is only a **Verification/Example Template** for the first end-to-end slice (not the product scope).

## 1) MVP Definition (What “MVP” Means Here)

MVP is the smallest **end-to-end working** version that proves the platform loop:
**Template → Circuit + Firmware → (Optional Robot/World) → Run → Telemetry/Logs → Report/Replay → Tests (PASS/FAIL)**

MVP must support:

- **Circuit-only templates** (no robot/world required)
- **Robotics templates** (robot/world enabled by template)

End-user constraint:

- Windows-only standalone application (end users do **not** install Unity Editor).

## 2) Platform Core (Generic, Template-Based)

The platform supports ANY Arduino-based system by describing it as a **Template** composed of:

- Circuit (optional but common)
- Firmware/controller (required for runtime behavior)
- IO contract (required: signals/types)
- Robot + World (optional; robotics templates use them, circuit-only templates do not)
- Tests (recommended)

### CoreSim (Unity-independent, pure C#)

**Catalogs**

- `ComponentCatalog` (resistors, LEDs, drivers, sensors, batteries/PSU blocks, generic loads)
- `BoardCatalog` (Arduino boards + pin capabilities and roles)
- `TemplateCatalog` (Circuit-only / Mechatronics / Robotics templates)
- (Optional in MVP, allowed): `SensorActuatorCatalog` if robotics templates require it

**Contracts & Specs**

- `IOContract` (standardized signals between Firmware ↔ Circuit ↔ Robot/World)
- `CircuitSpec`, `FirmwareSpec`, `RobotSpec`, `WorldSpec`, `TestSpec`, `RunSession`
- `TemplateSpec` (composition + defaults for a system archetype)

**Runtime (MVP: behavior-first)**

- `VirtualArduino` (behavioral controller runtime; future: compiled firmware execution)
- `CircuitSolver` (MVP: node/graph + behavioral power modeling; future: plugin for SPICE)
- `TestRunner` (runs scenarios + assertions against telemetry/metrics)
- `FailureModels` (MVP subset: power brownout + torque saturation + thermal derating)
- `TelemetryBus` + `Recorder` (frames + events + report/replay artifacts)

### Windows-only Single Application

- Unity is the development engine; we ship a Windows standalone build.
- Project/config data and run artifacts use `Application.persistentDataPath` (or equivalent Windows-safe location).

## 3) Studios Included in MVP (Generic)

### Project Wizard (MVP)

User creates a project by selecting:

- **Blank Template** (generic starter), or
- **Verification Template** (ExampleTemplate-01)

The Wizard must not lock the product into a single robot type.

### Circuit Studio (MVP)

Generic drag-drop blocks + wiring + validation:

- pin role compatibility (PWM/ADC/I2C/SPI/UART/power/GND)
- power rails and common ground checks
- basic power budget warnings (behavioral)

**Instruments / Probes (MVP, behavioral level)**

- Voltmeter and ammeter probes (minimum)
- Optional but recommended: basic waveform view for selected signals (oscilloscope-lite)
- Note: not full SPICE in MVP; architecture remains ready for solver upgrade later.

### Firmware Lab (MVP)

- Generic Virtual Arduino/controller configuration bound to `IOContract`
- Waveform-based input injection for arbitrary signals (time-based functions)
- Serial console / runtime logs (behavioral)

### Robot Studio (MVP, optional per template)

- Quick robot/mechatronics composition (parts + mounts + sensors/actuators)
- Must support templates with **no robot** (circuit-only mode)

### World Studio (MVP, optional per template)

- User defines the environment/track; defaults auto-generated (colliders/material presets where applicable)

### Run Mode + Tests (MVP)

- One-click run
- Live telemetry visualization (basic graphs/indicators)
- Test runner producing PASS/FAIL + reason codes
- Export report + replay (minimum viable)

## 4) Automated Testing Baseline (MVP Minimum)

### CoreSim tests (required)

- Serialization roundtrip tests for specs (`Spec → JSON → Spec`)
- Basic determinism test (same seed + same inputs ⇒ same outputs for N ticks)
- Waveform engine unit tests (piecewise/step/ramp/log/exp basics)
- Failure model unit tests (threshold behavior)

### Unity Test System (MVP Minimum once UnityApp exists)

MVP must include an automated test baseline for UnityApp to prevent regressions in the shipped Windows app.

**Unity EditMode tests (minimum)**

- Project loads successfully in batchmode (developer-side)
- Basic initialization smoke:
  - Wizard screen instantiates without exceptions
  - UI Toolkit root loads

**Unity PlayMode tests (minimum)**

- Simulation “tick” smoke:
  - Start a run session (minimal template) and advance a few fixed steps without exceptions
  - Telemetry publishes at least N frames
  - Logging starts and writes output files (temp/persistent path)

**Build smoke (minimum)**

- CI should produce (or at least validate) a Windows build artifact once UnityApp exists.

> Note: If UnityApp is not yet added to the repo, MVP still requires:
>
> - this documented test plan section, and
> - the first UnityApp PR must add initial EditMode + PlayMode smoke tests.

## 5) Verification Template (ExampleTemplate-01): Line Follower (Demo Only)

Purpose: prove end-to-end integration. Not the product scope.

Example contents (illustrative):

- Circuit: Arduino + motor driver + 2 drive motors + IR line sensor array
- Robot: differential drive chassis
- Firmware: PID-based steering controller
- World: simple track with a line/contrast map
- Test pack examples:
  - `Test_FollowTrack`: complete a lap under a target time (configurable)
  - `Test_StayOnTrack`: deviation below a target threshold (configurable)

## 6) Logging & Replay (MVP)

- Logging starts at run start (or robot spawn for robotics templates)
- Output includes:
  - frame telemetry (fixed rate)
  - event log (faults, resets, assertions)
  - report summary
  - replay data (minimum viable)

## 7) Realistic Failures (MVP Subset)

MVP must demonstrate at least one deterministic failure scenario, visible live and in report:
- Power: battery sag / brownout reset
- Actuation: torque saturation
- Thermal: thermal derating (simple)

### CI/CD
- **Platform**: GitHub Actions (Windows Runners).
- **Core Checks**: `dotnet test` (CoreSim), Governance checks (Repo Index, Ignore Rules).
- **Unity Validation**:
  - Workflow: `.github/workflows/unity_ci.yml`
  - Scope: EditMode logic tests, Compilation.
  - **Conditional**: Runs ONLY if `UNITY_LICENSE` secret is present. Skips gracefully on forks/PRs without secrets.
  - Version: Auto-detected from `ProjectVersion.txt` (`2022.3.62f3`).
- **Artifacts**: Shared Info Zip (Drive), Test Results (GitHub Artifacts).

## 8) Exclusions for MVP (Initial)

- Full SPICE / analog-accurate electronics simulation (behavioral first; architecture ready for plugin later)
- CAD import pipeline and complex rigging
- Multi-system sessions (MVP focuses on one system per run)
- Advanced physics (soft-body, fluids)
- Deep protocol decoding (full I2C/SPI/UART decode can come later)
