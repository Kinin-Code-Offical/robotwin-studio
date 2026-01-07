# Implementation Plan TODO

This file tracks active work items. Keep entries short and verifiable.

## Now

- [ ] Stabilize build and setup scripts.
- [ ] Keep Unity plugin sync reliable.
- [ ] Maintain deterministic tick ordering across subsystems.
- [ ] ALP: Deterministic IR + TCS34725 device bridge.

## Next

- [ ] Expand regression coverage for physics and firmware.
- [ ] Improve QEMU guest lifecycle handling.
- [ ] Document supported board profiles.
- [ ] Robot authoring pipeline: define required inputs (model/joints/IO/firmware) and add a “sample robot package”.
- [ ] Realistic mechanics: DC motor + gearbox + wheel traction.
- [ ] Realistic mechanics: servo PWM + torque-limited actuation.
- [ ] Power integrity: droop + brownout/reset behavior.

### Robot authoring pipeline — required inputs

To build “the robot you showed” inside RobotWin Studio, we need a minimum set of assets/specs so the editor can instantiate parts, connect joints, and map firmware IO.

- **RobotSpec (CoreSim)**

  - `RobotSpec.Name`
  - `RobotSpec.Parts[]`: `InstanceID`, `CatalogID`, optional `Config`
  - `RobotSpec.Joints[]`: parent/child part IDs + mount points (and optional offsets)

- **Geometry / visuals (Unity)**

  - Preferred: per-part prefab (one prefab per `CatalogID`)
  - Alternative: single FBX/GLB with a documented hierarchy and part name mapping

- **Kinematics / joints**

  - Joint list with: joint type (revolute/prismatic/fixed), axis, limits (min/max), default pose
  - Naming: stable IDs so serialized packages remain valid

- **Electronics / IO map**

  - Board profile (e.g., Arduino Uno) and mapping: actuator/sensor -> pin
  - Any required power nets (USB, VIN) and expected supply rails

- **Firmware/protocol contract**

  - What commands exist (e.g., set target, read encoder/sensor)
  - Tick/update rates and units (deg/rad, mm/m, PWM range)

- **Acceptance criteria (MVP)**
  - RobotStudio: load a sample robot package
  - Unity: spawn robot + move at least 1 joint from UI
  - CoreSim: produces deterministic state updates for the joint(s)

## Later

- [ ] Expand device graph bindings in Unity.
- [ ] Add fixtures for thermal and environment models.
- [ ] ALP golden-trace fixtures (motors/servos/sensors).
