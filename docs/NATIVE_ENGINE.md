# NativeEngine

NativeEngine contains native libraries and build configuration used by the simulation stack.

## Physics Engine Scope

NativeEngine is the target home for the full rigid-body physics system:

- Rigid-body solver + constraints
- Collision detection (broadphase + narrowphase + CCD)
- Materials, friction, restitution
- Actuator dynamics (servo, motor, thruster)
- Aerodynamics (drone/propeller)
- Particles/fluids (future)
- Controlled imperfection models (noise, drift, variance)

CoreSim provides electrical control intent; NativeEngine computes the physical response.

## Build

```powershell
python tools/rt_tool.py build-native
```

## Output

- Native DLLs land under `builds/native` and may be copied into Unity plugins.

## Notes

- Keep CMake targets small and deterministic.
- Prefer clear ABI boundaries for C# interop.

## Data Contract (Draft)

NativeEngine consumes a `ControlFrame` from CoreSim and emits a `PhysicsFrame`:

- ControlFrame: actuator targets, PWM values, power rails, and step metadata.
- PhysicsFrame: body transforms, velocities, sensor readings, constraint errors.

## Unity Runtime Hooks

- `RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs`: owns the native world and pushes config + ticks.
- `RobotWin/Assets/Scripts/Game/NativePhysicsBody.cs`: registers Unity transforms as physics bodies.
- `RobotWin/Assets/Scripts/Game/NativeVehicle.cs`: vehicle + suspension + tire model binding.
- `RobotWin/Assets/Scripts/Game/NativeRcCarController.cs`: keyboard/axis RC input mapping.
- `RobotWin/Assets/Scripts/Game/NativeCableConstraint.cs`: tension-only rope/cable (force at points).
- `RobotWin/Assets/Scripts/Game/NativePulleyConstraint.cs`: simple pulley ratio constraint.
- `RobotWin/Assets/Scripts/Game/NativeServoMotor.cs`: PD servo torque around an axis.
- `RobotWin/Assets/Scripts/Game/NativeThruster.cs`: directional thrust force (drones/propellers).

Notes:
- Ground contact is currently a simple y=0 plane using `cross_section_area` as radius proxy.
- Torque accumulation affects angular velocity; contact friction damps spin.
- Torque can be applied directly via `Physics_ApplyTorque` for servo-style actuators.
