# RobotWin (Unity)

RobotWin is the Unity project that powers UI, rendering, and user interaction.

## Project Root

- Unity project folder: `RobotWin/`
- Scenes: `RobotWin/Assets/Scenes`
- UI Toolkit: `RobotWin/Assets/UI`
- Runtime scripts: `RobotWin/Assets/Scripts`

## Key UI Areas

- Component Studio: runtime model loading, transforms, gizmos.
- Circuit Studio: wiring and validation interface.
- Run Mode: telemetry and runtime controls.

## Runtime Model Loading

- Runtime GLTF import via GLTFast.
- Models are normalized and re-centered for stable gizmo behavior.
- Keep model bounds finite to avoid Invalid AABB assertions.

## Tips

- Use `run-unity-smoke` to validate compilation quickly.
- After CoreSim updates, run `update-unity-plugins`.
