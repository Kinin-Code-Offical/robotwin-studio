# ALP ROBOT: DIGITAL TWIN IMPLEMENTATION PLAN (4-HOUR SPRINT)

**Objective**: Create an engineering-grade, physics-accurate simulation of the "Alp Line Follower" robot in RobotWin Studio. The simulation must replicate the physical imperfections (unequal motor friction) and environmental conditions (A3 paper track, lighting noise) to ensure the `.ino` firmware runs identically in both real and virtual worlds.

---

## 🕒 Hour 1: Mechanical & Physics Reconstruction (✅ COMPLETED)

**Focus:** Converting physical attributes to Unity components with "Real-World Imperfection".

1.  **Chassis & Weight Distribution**:

    - **Action**: Import chassis mesh (or create simplified primitive).
    - **Realism Tweak**: The photos show a heavy rear end (4xAA batteries). Set Center of Mass (CoM) bias towards the rear (-Z axis).
    - **Status**: `3D_ASSET_LIST.md` defined. `RobotAssembler.cs` created for auto-setup.

2.  **Motor & Drive Train (The "70 vs 45" PWM Fix)**:

    - **Action**: Setup 2x Differential Drive Motors on Ports 1 and 3.
    - **Status**: `MotorController.cs` implemented with `Efficiency` parameter to simulate the 0.65 vs 1.0 hardware imbalance.

3.  **Gripper Arm Setup**:
    - **Action**: Rig the 2-Servo mechanism.
    - **Status**: `ServoMechanism.cs` and `GripperAssembly.cs` created to handle gearing logic and physics limits.

## 🕒 Hour 2: Electrical System & Sensor Array (✅ COMPLETED)

**Focus:** replicating the exact pinout and sensor characteristics.

1.  **Motor Shield Emulation (L293D/AFMotor)**:

    - **Logic**: Ensure the simulator handles the PWM frequency.
    - **Status**: `AFMotor.h` mock created in FirmwareEngine, bridging logic to Virtual Pins 60-64.

2.  **Sensor Array (The "Eyes")**:

    - **IR Sensors (x4) - TCRT5000 Analog**:
    - **Realism Tweak**: Add "Analog Noise".
    - **Status**: `AnalogSignalProcessor.cs` implemented with `NoiseProfile`.

3.  **Color Sensor (TCS34725)**:
    - **Status**: `Adafruit_TCS34725.h` mock created.

## 🕒 Hour 3: The Environment (The "Professor's Track") (✅ COMPLETED)

**Focus:** Simulating the specific test conditions using reusable Engine Assets.

1.  **The "A3 Paper" Seamless Track**:
    - **Concept**: Instead of a perfect procedural track, create a mesh that looks like taped-together A3 sheets.
    - **Imperfection**: Add "Seam bumps" every 42cm.
    - **Status**: `SurfaceImperfection.cs` implemented.

## 🕒 Hour 4: General-Purpose Physics Components (The "Engine Core") (🚧 IN PROGRESS)

**Focus:** Developing the core classes that allow _any_ robot to start with "Alp-like" realism.

1.  **Firmware Integration (Reusable Class)**:

    - **Refactor**: Instead of hardcoding "Alp's Motor", extend the base `DC_Motor` class to include `Efficiency` (0.0-1.0) and `StartupTorqueCurve` parameters.
    - **Application**: Configure Alp's Left Motor instance to `Efficiency = 0.9` and Right Motor to `Efficiency = 1.2` (simulating the hardware difference).
    - **Benefit**: Users can now simulate old/damaged motors on _any_ robot.

2.  **Analog Noise Middleware (Reusable Class)**:

    - **Refactor**: Create a `SignalProcessor` middleware for the sensor stack.
    - **Feature**: Implement `GaussianNoise` and `IntermittentDrop` modes.
    - **Application**: Apply `NoiseLevel = 5%` to Alp's IR sensors used in `new_start.ino`.
    - **Benefit**: Proves the engine can simulate dirty signals for advanced filtering algorithms (Kalman, etc.).

3.  **Firmware Integration**:
    - **Action**: Bind `new_start.ino` to the Virtual MCU using the new component properties.
    - **Mocking**: Use the generic `Sim_AFMotor.dll` which now reads the `Efficiency` values from the physics engine.

## 🕒 Hour 5: Visual Fidelity & Lighting (Current Focus)

**Focus:** Simulating the optical properties of the track materials (Glare, Materials).

1.  **Automated Material Logic**:

    - **Step**: Created `TrackMaterialSetup.cs` (Editor Tool) to generate "Paper" (Matte) vs "Tape" (Glossy) materials automatically.
    - **Status**: [x] Completed.

2.  **Optical Interference (Glare)**:

    - **Step**: Created `OpticalSensorInterference.cs`.
    - **Logic**: Calculates `Vector3.Reflect` to simulate sensors going blind (reading Low/White) when hitting glossy tape at the wrong angle.
    - **Status**: [x] Integrated into `AlpRobotInterface`.

3.  **Calibration Run**:

    - Run the simulation.
    - Observe the "Soft Start" loop. Does the robot jitter before moving? (It should).
    - Observe Straight Line Drive: If the logic `45/70` makes it drive straight in sim, the physics tuning (Hour 1) was successful. If it curves, adjust the `DC_Motor.Efficiency` properties exposed in the Inspector.

4.  **Product Feature Verification**:
    - Verify that these realism settings can be toggled via the "Simulation Quality" UI (e.g., "Deterministic Mode" vs "Realistic Mode").

### Hour 6: Integration & Tuning

**Focus:** Finalize the Digital Twin.

---

# 📅 Phase 2: The "Circuit Studio" Expansion (Hardware Simulation)

**Concept:** Before physical assembly, we validate the electronics using "Engineering Grade" simulation.
**Reference:** [Hardware Architecture](HARDWARE_SIMULATION_ARCHITECTURE.md) | [Instruments Spec](TEST_INSTRUMENTS_SPEC.md)

### 1. The Circuit Compactor

- **Task:** Implement the `.rtcomp` file generation logic.
- **Algorithm:**
  - Input: List of components (Resistors, ICs) + Netlist.
  - Process: Calculate Thermal Mass, EMI Coefficient, and MTBF.
  - Output: Single "Black Box" object with performance lookup tables.

### 2. Virtual Instruments (The Lab Bench)

- **Task:** Create the 3D Probe System and UI Canvas for:
  - **Oscilloscope:** With realistic noise floor and 30MHz bandwidth limit.
  - **LCR Meter & Multimeter:** With auto-ranging delays and error margins.
- **Integration:** Instruments must be able to "Probe" the internal nodes of a `.rtcomp` module by querying its simulation state.

### 3. PCB & Breakout Simulation

- **Task:** Procedural PCB Generation.
- **Feature:** Auto-layout components to corners, generate FR4 mesh.
- **Faults:** Visual warping/charring of PCB texture when `Thermal_Limit` is exceeded.

---

# 🤖 CODEX PROMPT (For Automated Execution)

Copy and paste the block below into the Codex Agent to execute this plan:

```text
ACT AS: Senior Robotics Simulation Engineer.
CONTEXT: We are building a Digital Twin of a student project "AlpLineTracker" in RobotWin Studio.
INPUT DATA:
- Local Firmware: 'new_start.ino' (Defines pins: A0-A3 for IR, Port1/3 for Motors).
- Photos: Show custom chassis, heavy rear battery pack, servo arm.

TASK: Execute the 4-Hour Implementation Plan step-by-step.

STEP 1 (PHYSICS):
- Create a vehicle Rigidbody (Mass 0.8kg).
- Configure 'MotorController' Component:
    - Set 'RightMotor.Efficiency' = 1.0.
    - Set 'LeftMotor.Efficiency' = 0.65 (Simulating the gearbox friction that requires lower PWM).
- Center of Mass: Offset by vector (0, -0.05, -0.1) to simulate rear batteries.

STEP 2 (SENSORS):
- Instantiate 4 RaycastSensors at front offset (Y: 0.01m from ground).
- Attach 'AnalogSignalProcessor' Component:
    - Mode: "Raw_Unfiltered".
    - NoiseProfile: "TCRT5000_Standard" (+/- 5% jitter).
- Map to Virtual MCU Pins: A3(Left), A2(MidL), A1(MidR), A0(Right).

STEP 3 (ENV):
- Generate Track Mesh: "PaperTaped" preset.
- Component: Add 'SurfaceImperfection' script -> "SeamBumps" every 0.42m.
- Albedo Map: White paper with 19mm Black Tape line.

STEP 4 (LOGIC):
- Bind 'new_start.ino' to the Virtual MCU.
- Verify 'AFMotor' abstraction layer respects the 'Efficiency' values set in Step 1.
- Verify 'Soft Start' loop executes correctly in Console Logs.

OUTPUT:
- Generate the 'RobotPrefab.prefab' file.
- Generate the 'Env_A3_LabTest.scene'.
- produce a 'calibration_report.txt' confirming motor PID match.
```
