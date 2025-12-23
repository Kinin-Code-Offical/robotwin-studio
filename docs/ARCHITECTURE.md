# Architecture

## Overview
RoboTwin Studio is a Windows standalone simulation platform for building and testing custom Arduino-based systems (Circuits, Robots, Mechatronics).
It is template-based and supports a wide range of user-defined configurations.

## Core Components

### CoreSim (Pure C#)
The deterministic heart of the simulation. Contains:
- Data Models (`CircuitSpec`, `RobotSpec`, `WorldSpec`, `FirmwareSpec`, `TestSpec`, `TemplateSpec`)
- Catalogs (`ComponentCatalog`, `BoardCatalog`, `SensorActuatorCatalog`, `TemplateCatalog`)
- Contracts (`IOContract` - generic signal definitions)
- Simulation Engine (`RunSession`, `TelemetryBus`)
- Failure Models (Math-based thermal/power logic)

Dependencies: None. (No Unity references allowed).

### UnityApp
The visualization and interaction layer.
- **UI**: Unity UI Toolkit.
- **Physics**: NVIDIA PhysX (via Unity) tailored to 1 unit = 1 meter.
- **Adapters**: Transforms `CoreSim` data into GameObjects.

## Data Flow
1. User defines specs (Circuit, Robot, World) -> Saved as JSON.
2. Run Start: `RunSession` initialized with Seed.
3. Update Loop:
   - `CoreSim` steps logic (virtual firmware, circuit state).
   - `UnityApp` steps physics and updates visualization.
   - Telemetry frame captured.
