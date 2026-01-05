# Codex Support Feedback (Live)

This file is meant to be a low-friction handoff between the human/Codex sessions and the Copilot session.
It captures actionable mismatches and build-breakers found while Codex is running.

## 2026-01-05

### Fixed (already applied)

- FirmwareEngine build break: `FirmwareEngine/main.cpp` called `VirtualMcu::PinToPort()` which is private.

  - Fix: switched to `VirtualMcu::SamplePinOutputs()` and defined `kPinValueUnknown = 0xFF` in `FirmwareEngine/Protocol.h`.
  - Verified: `py tools/rt_tool.py build-firmware` succeeded.

- Unity compile risk: `RobotWin/Assets/Scripts/Debug/RemoteCommandServer.cs` used `System.Web.HttpUtility` which is typically unavailable in Unity.

  - Fix: replaced with a small local query-string parser.

- Unity standalone build plugin-collision (template assets): build was failing with `Plugins colliding with each other`.
  - Fix: updated the colliding template `.meta` files to use valid spaces-only YAML and explicitly disable `PluginImporter` for all Standalone targets.
    - `RobotWin/Assets/Templates/arduino-*-starter/Code/U1/app.h.meta`
    - `RobotWin/Assets/Templates/arduino-*-starter/Code/U1/builds/bvm_build/sketch/app.h.meta`
    - `RobotWin/Assets/Templates/arduino-*-starter/Code/U1/builds/bvm_build/sketch/U1.ino.cpp.meta`
  - Verified: `py tools/rt_tool.py build-standalone` succeeded.
  - Tracked: GitHub issue #166.

### Needs an Issue / Follow-up

### Plan Tracking Integrity

- `docs/implementation_plan.json` had drift where many issues were accidentally marked completed.
  - Fix: reverted `progress.completedIssues` to match explicit DONE items in `docs/implementation_plan_todo.md`.
  - Recommendation: treat TODO DONE markers + concrete progress notes as the source of truth.
