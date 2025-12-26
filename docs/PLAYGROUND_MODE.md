# Robotwin Studio: Playground Mode

**Playground Mode** is an unrestricted simulation environment designed for rapid prototyping without the constraints of a formal Project lifecycle.

## Features
- **Instant Access**: No project name or file creation required initially.
- **Infinite Canvas**: Unlimited workspace components.
- **Live Injection**: Real-time signal overrides (RunMode).
- **Physics Engine**: Active Thermal and Battery simulation (3S LiPo + Overheating visuals).
- **Transient State**: Changes persist only in memory until explicitly saved.
- **Debug Tools**: Full access to Telemetry Bus inspection.

## Access
1. Launch Robotwin Studio.
2. In the Project Wizard, select **New Project**.
3. Choose the **Empty Grid** template (Default).
4. You are now in a pseudo-playground state.

## Implementation Details
Currently, "Playground Mode" mirrors the standard "New Project" flow but utilizes the `CreateEmpty` template logic.
Future updates will add a dedicated `Playground` scene that bypasses the Project Manifest persistence layer entirely for scratchpad usage.
