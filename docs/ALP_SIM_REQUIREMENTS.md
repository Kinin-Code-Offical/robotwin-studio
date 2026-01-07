# ALP Robot – Simulation Requirements (P0–P2) + Repo Gaps

This document captures what is required to simulate the ALP robot (UNO + AFMotor/L293D v1 shield + 2 DC motors + 4 IR line sensors + TCS34725 + 2 servos) with **maximum behavior realism** while keeping the project’s lockstep/realtime constraints.

## Scope + Assumptions

- MCU: Arduino Uno R3 (ATmega328P)
- Motor driver: Arduino Motor Shield v1 style (L293D + 74HC595) used via `AFMotor`
- Sketch wiring (from `new_start.ino`):
  - Servos: `D10` (arm), `D9` (gripper)
  - Line sensors: `A0–A3` read via `digitalRead()` (so modeled as digital-threshold sensors)
  - TCS34725: I2C at address `0x29` over `SDA/SCL` (UNO aliases `SDA=A4`, `SCL=A5`)
  - Motors: `AFMotor` ports **M1** and **M3**

## Current State (Observed)

- Circuit solving in CoreSim is currently primarily resistive/diode/source based.
  - `DCMotor` is treated as a 2-pin resistor (see `AddTwoPinResistor` usage in `CoreSimRuntime`).
- Servo visuals exist; servo physics exists (`NativeServoMotor`), and the 3D view animates servo angle via `servoAngle` property.
- There is a runtime warning that VirtualMcu fallback has limited PWM/timers/interrupt fidelity (see `SimHost`).
- No explicit implementation found for AFMotor/L293D shield semantics or TCS34725 behavior.

## P0 (Must-have for “real firmware behaves like the robot”)

### P0.1 Accurate AFMotor/L293D v1 behavior

- **Port mapping**: Model AFMotor port → physical outputs (M1..M4) without requiring the user to wire every internal shield pin.
- **PWM + direction semantics**:
  - PWM frequency/timer selection differences matter (especially if the sketch relies on motor update rates).
  - Direction changes should include realistic deadtime/enable behavior.
- **H-bridge voltage drop + current limit**:
  - L293D voltage drop vs current should be approximated (even piecewise) so speed/torque depend on load.
  - Thermal/current limiting behavior should exist at least as a clamp (prevents impossible accelerations).

### P0.2 Servo timing fidelity (D9/D10)

- `Servo` library behavior depends on timer interrupts and pulse timing.
- Requirements:
  - Servo pulse generation at ~50Hz with correct pulse width mapping.
  - Jitter and update latency should be bounded and deterministic in lockstep.
  - Mechanical limits / stall torque clamping so the arm can’t “teleport” through collisions.

### P0.3 I2C device model: TCS34725 @ 0x29

- Implement minimal register-level model so Adafruit library reads return plausible values.
- Requirements:
  - Register map subset: enable/integration time/gain + RGBC data registers.
  - Integration time: affects update rate and noise.
  - LED pin behavior (if used): influences measured brightness.

### P0.4 IR line sensors as digital-threshold sensors (A0–A3)

- Even though pins are analog-capable, the sketch uses `digitalRead(A0..A3)`.
- Requirements:
  - Thresholding against simulated surface reflectance.
  - Per-sensor calibration: offset + hysteresis to avoid flicker.
  - Optional noise model (deterministic seeded) to match real-world jitter.

### P0.5 Power integrity + brownout behavior

- Two battery packs are present; in reality motors inject noise/dips.
- Requirements:
  - Battery internal resistance + droop under load.
  - Logic rail stability rules (when VIN vs 5V feeds are used).
  - Brownout/reset behavior for MCU if VCC dips below threshold.

## P1 (Strong realism; improves “feels like hardware”)

### P1.1 Electromechanical DC motor model

- Replace “motor-as-resistor” with a basic DC motor + gearbox model:
  - Back-EMF ($V = k_e \omega$)
  - Torque ($\tau = k_t I$)
  - Rotor inertia + static friction
  - Gear ratio + efficiency
- Wheel traction + load coupling so current spikes under stall.

### P1.2 Sensor/world coupling

- TCS34725 should sample the simulated world color/lighting at the sensor position.
- IR sensors should sample track reflectance/height with configurable distance.

### P1.3 Deterministic faults + wear

- Add fault toggles for:
  - Motor unplugged, reversed polarity, partial short
  - Sensor stuck-high/low
  - Servo gear slip / reduced torque

## P2 (Polish / tooling)

- Golden-trace fixtures for ALP template: save expected pin waveforms for a known scene.
- Visual debugging overlays:
  - IR sensor rays + threshold state
  - TCS34725 sampled color
  - Motor current draw, PWM duty, H-bridge state

## Repo Gaps (What appears missing)

1. **No AFMotor/L293D shield simulation** (only wiring/pins exist as a catalog entry).
2. **No I2C peripheral device emulation** wired to firmware runtime (docs mention I2C as a goal, but no device-level model located).
3. **Motor physics/EE coupling is simplistic** (DC motors modeled as resistors in CoreSim).
4. **Servo firmware-to-physics bridge** likely incomplete (servo visuals exist, but real Servo-library timing fidelity depends on timer interrupts).

## Suggested Implementation Anchors

- Circuit/EE solving and board power behavior: `RobotWin/Assets/Scripts/CoreSim/Engine/CoreSimRuntime.cs`
- Firmware stepping + pin export/import + realtime budgets: `RobotWin/Assets/Scripts/Game/SimHost.cs`
- Servo mechanical actuation: `RobotWin/Assets/Scripts/Game/NativeServoMotor.cs`
- Motor + wheel physics: NativeEngine physics components + a motor actuator script (new)
- I2C bus + devices:
  - Add a minimal I2C bus abstraction in CoreSim runtime contracts
  - Attach device models per component type (e.g., `TCS34725`)

## Acceptance Criteria (for ALP)

- Running the real sketch yields:
  - Motors respond with correct direction/speed vs `AFMotor` commands.
  - Servos move to commanded angles with realistic lag and torque limits.
  - TCS34725 readings change when the sensor moves over differently colored surfaces.
  - IR sensors produce stable digital readings on line/no-line.
  - Under stall or aggressive acceleration, voltage droop can cause observable behavior (slower motors, potential MCU reset if configured).
