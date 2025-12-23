# MVP Scope: Generic Arduino Simulation Platform

> [!IMPORTANT]
> The product goal is a **Generic Platform**. The "Line Follower" is only the first **Verification Template**.

## 1. Generic Platform Core
The platform must support ANY Arduino-based system defined by a Template.

### CoreSim
- **Catalogs**:
    - `ComponentCatalog` (Resistors, LEDs, Motors, Sensors)
    - `BoardCatalog` (Arduino Uno, Nano, Mega pinouts)
    - `TemplateCatalog` (Project templates)
- **Runtime**:
    - `VirtualArduino` (interprets compiled firmware or emulates behavior)
    - `CircuitSolver` (Node-based or simplified logic)
    - `IO Contract` (Standardized signals between Firmware and World)

### Project Wizard
- **Selection**: User chooses from `TemplateCatalog`.
- **MVP Default**: "Line Follower Kit" (Template ID: `mvp.linefollower.2servo`).

### Editors
- **Circuit Studio**: Generic block placement and wiring.
- **Firmware Lab**: Code editor + IO mapping.
- **Robot Studio**: Part composition (chassis + actuators).
- **Run Mode**: Generic "Start/Stop" and Telemetry graphs.

## 2. Verification Template: Line Follower
This is the content content used to prove the platform works.

- **Circuit**: Arduino Uno + L298N Driver + 2 DC Motors + 3 IR Sensors.
- **Robot**: 2-Wheel Differential Drive Chassis.
- **Firmware**: PID control loop reading sensors and driving motors.
- **World**: Flat plane with a black curve.
- **Test Pack**:
    - `Test_FollowLine`: Complete lap < 15s.
    - `Test_StayOnTrack`: Deviation < 5cm.

## 3. Exclusions for MVP
- **Complex Physics**: No soft-body or fluid dynamics.
- **Advanced Electronics**: No full SPICE simulation (logic-level only for now).
- **Multi-Robot**: Single active robot per session.
