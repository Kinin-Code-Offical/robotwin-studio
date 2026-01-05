# Testing Strategy

This doc lists the tests we use and how to run them locally.

## 1. Unit Tests (CoreSim)

Validates the deterministic logic of the orchestration layer.

```powershell
dotnet test CoreSim/tests/RobotTwin.CoreSim.Tests/RobotTwin.CoreSim.Tests.csproj
```

## 2. Physics Regression Suite (NativeEngine)

Ensures that physics behavior remains deterministic across commits.

- **Method:** Runs a set of standard scenarios (falling box, pendulum, friction ramp) and compares the final state hash against a known good baseline.
- **Tolerance:** Bit-exact (0 tolerance) for deterministic mode; Epsilon tolerance for float mode.

```powershell
python tools/rt_tool.py test-physics --mode regression
```

## 3. Firmware Integration Tests

Validates that the emulated hardware responds correctly to firmware commands.

- **Scenario:** Boots a virtual Arduino, uploads a test firmware that toggles pins, and verifies the voltage changes in the physics engine.

```powershell
python tools/rt_tool.py test-firmware --board arduino_uno
```

## 4. Hardware-in-the-Loop (HIL)

Validates the simulation against physical reality.

- **Setup:** Connect a real flight controller (e.g., Pixhawk) via USB.
- **Mode:** The simulation feeds sensor data to the USB port and accepts motor commands from the USB port.

```powershell
python tools/rt_tool.py test-hil --port COM3 --baud 115200
```

## 5. Unity Smoke Tests

Ensures the UI and Rendering pipeline do not crash on startup.

```powershell
python tools/rt_tool.py run-unity-smoke
```
