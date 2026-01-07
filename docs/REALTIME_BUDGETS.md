# Realtime Budgets

These budgets define target execution windows for a single simulation tick. Adjust per scene and hardware.

## Default budgets (per tick)

- Firmware step: 0.4 ms
- Circuit solve: 0.8 ms
- Physics step: 1.6 ms
- IPC and logging: 0.2 ms
- Total target: 3.0 ms

## Overrun policy

- If a subsystem exceeds its budget, log the miss and switch to a fast-path.
- Re-evaluate full stepping once the load returns to normal.

## Where budgets are configured

Unity-side configuration and counters:

- `RobotWin/Assets/Scripts/Game/RealtimeScheduleConfig.cs`
- `RobotWin/Assets/Scripts/Game/SimHost.cs`
