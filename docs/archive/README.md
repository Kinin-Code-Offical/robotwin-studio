# Archive (1.x design)

The documents in this folder describe the **1.x design of RobotWin Studio**: a Unity front end talking to C++ engines (`NativeEngine` for physics, `FirmwareEngine` for AVR emulation) and a .NET `CoreSim` host over named pipes. They also include one-off reports, audits and plans written during that period.

That design has been replaced. The Unity project was removed from the repository, and RobotWin Studio is now a single C# .NET 9 WPF application on top of `CoreSim` (see [../ARCHITECTURE.md](../ARCHITECTURE.md)). `FirmwareEngine/` and `NativeEngine/` remain in the repository as legacy code and are not used by the app.

These files are kept for history only. Commands, paths and features in them may no longer exist; do not use them as instructions for the current app.

| Group | Files |
| --- | --- |
| 1.x subsystems | `CORE_SIM.md`, `NATIVE_ENGINE.md`, `FIRMWARE_ENGINE.md`, `HARDWARE_SIMULATION_ARCHITECTURE.md`, `DEBUG_CONSOLE.md`, `GLOBAL_LATENCY_SYNC.md` |
| Real-time contracts | `REALTIME_CONTRACT.md`, `REALTIME_BUDGETS.md`, `REALTIME_DESIGN.md`, `reference_deterministic_mode.json` |
| Plans | `HIGH_FIDELITY_PLAN.md`, `OPTIMIZATION_PLAN.md`, `RASPBERRY_PI_PLAN.md`, `MEGA_PIN_MAP_PLAN.md`, `ALP_ROBOT_IMPLEMENTATION_PLAN.md`, `ALP_SIM_REQUIREMENTS.md`, `implementation_plan_todo.md`, `implementation_plan.json`, `TEST_INSTRUMENTS_SPEC.md`, `3D_ASSET_LIST.md` |
| Reports | `BINARY_AUDIT_REPORT.md`, `COMPLETE_OPTIMIZATION_REPORT.md`, `EDITOR_COMPLETION_REPORT.md`, `FINAL_VALIDATION_REPORT.md`, `FIRMWARE_HELP_FIX.md`, `PERFORMANCE_TESTING.md`, `RENDER_OPTIMIZATION_REPORT.md`, `SENSOR_VISUALIZATION_REPORT.md`, `SYSTEM_OPTIMIZATION_REPORT.md`, `TEST_SYSTEM_REPORT.md`, `codex_feedback.md` |
| Snapshots and samples | `repo_files.txt` (file list of an old commit), `U1/` (reference robot sketch used by the ALP plans) |
