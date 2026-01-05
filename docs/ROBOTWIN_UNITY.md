# RobotWin: Visualization & Interaction

**RobotWin** is the Unity-based frontend. It’s where we do visualization, scene setup, and user interaction.

## Philosophy: The Passive View

In this project, Unity should not be the source of truth for time.

- Unity visualizes state produced by CoreSim/NativeEngine.
- If the Unity frame rate stutters, the goal is to keep simulation stepping stable.

## Key Features

### 1. Photorealistic Rendering (HDRP)

- **Physically Based Materials:** Standard PBR materials for consistent visuals.
- **Ray Tracing:** Optional, depending on project settings and hardware.
- **Volumetric Lighting:** Optional scene effects when needed.

### 2. Synthetic Data Generation (SDG)

RobotWin includes a pipeline for generating labeled training data for Computer Vision models.

- **Ground Truth:** Automatically generates segmentation masks, depth maps, and optical flow vectors.
- **Domain Randomization:** Randomizes lighting, textures, and object placement to train robust AI models.
- **Sensor Injection:** Renders camera frames directly into shared memory for the FirmwareEngine (QEMU) to consume.

### 3. The Builder UI

- **Component Library:** Drag-and-drop interface for assembling robots from CAD parts.
- **Circuit Designer:** Node-based editor for wiring electronics (Power -> ESC -> Motor).
- **Live Telemetry:** Real-time plotting of any signal (Voltage, Current, RPM, Temperature) at 60fps.

## Project Structure

- Assets/Scripts/Core: The bridge to CoreSim.
- Assets/Scripts/Rendering: Custom render passes for sensors (Lidar/Depth).
- Assets/Scripts/UI: UI Toolkit (USS/UXML) definitions for the editor.

## Development Workflow

1. **Build Native Backend:** Ensure NativeEngine and FirmwareEngine are built.
2. **Open Unity:** Launch the project in Unity 6.
3. **Play Mode:** Unity will automatically spin up the CoreSim orchestrator and connect to the native processes.

## Stepping Contract (Unity ↔ Native)

- `Native_Step(dt)` is reserved for **circuit/IO solving** in NativeEngine.
- `Physics_Step(dt)` is reserved for **physics simulation** in NativeEngine.
- Unity must not mix the two in a single loop; circuit stepping is orchestrated by `SimHost` ticks, while physics runs in its own loop.
- When NativeEngine pins are enabled, Unity calls `Native_Step` inside the logic tick to avoid a one-step lag.
