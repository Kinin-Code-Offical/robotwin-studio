# CoreSim

CoreSim is the deterministic simulation engine implemented in pure C#.

## Responsibilities

- Circuit graph modeling
- Component behavior and physics-lite rules
- Time-step driven simulation
- Serialization formats for RobotWin and FirmwareEngine

## Key Areas

- `CoreSim/src/RobotTwin.CoreSim/Runtime`: simulation runtime and ticking.
- `CoreSim/src/RobotTwin.CoreSim/Specs`: model specs and versioning.
- `CoreSim/src/RobotTwin.CoreSim/Serialization`: save/load pipeline.

## Integration Rules

- No Unity types in CoreSim.
- Keep deterministic state changes (avoid DateTime.Now, random without seeds).
- Add unit tests for new simulation behavior.
