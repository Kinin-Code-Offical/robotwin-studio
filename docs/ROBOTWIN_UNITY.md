# RobotWin: Visualization & Interaction

**RobotWin** is the Unity-based frontend for the RobotWin Studio platform. It serves as the "Lens" through which the user interacts with the simulation.

## Philosophy: The Passive View
In RobotWin Studio, **Unity does not run the simulation.**
- Unity is purely a **visualizer** of the state produced by CoreSim and NativeEngine.
- If Unity crashes or pauses, the simulation (running in background threads/processes) continues uninterrupted.
- This decoupling allows for **photorealistic rendering** (HDRP) without worrying about frame rate dips affecting physics stability.

## Key Features

### 1. Photorealistic Rendering (HDRP)
- **Physically Based Materials:** Accurate representation of aluminum, carbon fiber, and 3D printed plastics.
- **Ray Tracing:** Real-time ray tracing for accurate reflections and shadows (critical for camera sensor simulation).
- **Volumetric Lighting:** Simulates fog, dust, and underwater turbidity for optical sensor testing.

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

