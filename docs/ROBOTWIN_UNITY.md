# RobotWin (Unity)

RobotWin is the Unity front end for visualization and interaction.

## Design goals

- Unity renders the latest committed simulation state.
- Simulation time is owned by CoreSim, not the frame rate.
- The UI should never author time.

## Main areas

- Assets/Scripts/Core: CoreSim bridge and session control.
- Assets/Scripts/UI: editor and run mode screens.
- Assets/Scenes: authoring and runtime scenes.

## Main UI flows

- Project wizard: project creation and templates (see `RobotWin/Assets/Scripts/UI/ProjectWizardController.cs`).
- Circuit Studio: wiring and run configuration (see `RobotWin/Assets/Scripts/UI/CircuitStudio/` and `RobotWin/Assets/UI/CircuitStudio/`).
- Component Studio: authoring `.rtcomp` component packages (see `RobotWin/Assets/Scripts/UI/ComponentStudio/` and `RobotWin/Assets/UI/ComponentStudio/`).
- Run mode: runtime visualization and telemetry (see `RobotWin/Assets/Scripts/UI/RunMode/`).

## Simulation orchestration (Unity runtime)

Unity hosts an orchestration loop in `RobotWin/Assets/Scripts/Game/SimHost.cs` which:

- Owns session-level state (selected circuit, enabled backends, runtime config).
- Drives firmware stepping (external firmware host or in-process virtual MCU).
- Drives physics stepping via `NativeBridge.Physics_Step(dt)`.
- Collects telemetry and serial output for UI surfaces.

Key related scripts:

- Session bootstrapping: `RobotWin/Assets/Scripts/Game/SessionManager.cs`
- Physics world: `RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs`
- Realtime scheduling: `RobotWin/Assets/Scripts/Game/RealtimeScheduleConfig.cs`
- Raspberry Pi bridge: `RobotWin/Assets/Scripts/Game/RaspberryPi/RpiRuntimeManager.cs`

## Typical workflow

1. Build native and firmware binaries.
2. Run `python tools/rt_tool.py update-unity-plugins`.
3. Open `RobotWin/` in Unity Hub and enter Play mode.

## Stepping contract

- Logic stepping is driven by CoreSim.
- Physics stepping runs on its own fixed rate.
- Unity consumes outputs; it does not advance the clock.

## Native step separation

- Circuit/IO step: `NativeBridge.Native_Step(dt)`.
- Physics step: `NativeBridge.Physics_Step(dt)`.

These are intentionally separate so a scene can run physics at a different fixed rate than logic/circuit stepping.

## Components and packages

- Component definitions (JSON) are stored under `RobotWin/Assets/Resources/Components/`.
- Packaged runtime components (`.rtcomp`) used by the app are under `RobotWin/Assets/StreamingAssets/Components/`.

The editor exporter lives at `RobotWin/Assets/Scripts/Editor/ComponentPackageExporter.cs`.
