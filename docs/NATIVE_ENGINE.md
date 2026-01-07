# NativeEngine

NativeEngine is the C++ runtime for physics and environment simulation.

## Responsibilities

- Rigid body simulation and constraints.
- Environment parameters (wind, ambient temperature).
- Sensor signal generation as needed by the simulation.

## Integration

- NativeEngine is driven from Unity via `RobotWin/Assets/Scripts/Core/NativeBridge.cs`.
- Physics stepping is done via `NativeBridge.Physics_Step(dt)` (see `RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs`).
- Circuit/IO stepping uses `NativeBridge.Native_Step(dt)` and is kept separate from physics stepping.

## Configuration surface

Runtime config is passed via an interop struct (solver iterations, air density, wind, etc.) and is authored on the Unity side (see `NativeBridge.PhysicsConfig`).

## Exported APIs (Unity P/Invoke)

The entry points are declared in `RobotWin/Assets/Scripts/Core/NativeBridge.cs`.

Circuit API (electrical graph):

- `Native_CreateContext()`, `Native_DestroyContext()`
- `Native_AddNode()`
- `Native_AddComponent(type, paramCount, parameters)`
- `Native_Connect(compId, pinIndex, nodeId)`
- `Native_Step(dt)`
- `Native_GetVoltage(nodeId)`

Physics API:

- World lifecycle: `Physics_CreateWorld()`, `Physics_DestroyWorld()`, `Physics_SetConfig(ref config)`
- Bodies: `Physics_AddBody(ref body)`, `Physics_GetBody(id, out body)`, `Physics_SetBody(id, ref body)`
- Stepping: `Physics_Step(dt)`
- Vehicles: `Physics_AddVehicle(...)`, `Physics_SetWheelInput(...)`, `Physics_SetVehicleAero(...)`, `Physics_SetVehicleTireModel(...)`
- Forces/constraints: `Physics_ApplyForce(...)`, `Physics_ApplyForceAtPoint(...)`, `Physics_ApplyTorque(...)`, `Physics_AddDistanceConstraint(...)`
- Queries: `Physics_Raycast(...)`

## Build

- Use `python tools/rt_tool.py build-native`.
- Outputs land in `builds/native/`.

## Interop

- Exposes a C ABI used by CoreSim and Unity.
- Inputs and outputs are fixed-size structs to avoid per-step allocations.

## Determinism

- Fixed step sizes in deterministic mode.
- Avoid nondeterministic sources in hot paths.
