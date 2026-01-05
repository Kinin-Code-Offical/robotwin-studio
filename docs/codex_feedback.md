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

### Needs an Issue / Follow-up

- Unity standalone build fails due to plugin collisions in template assets.
  - Evidence: `logs/unity/build.log` shows `Plugins colliding with each other` and lists collisions for `Assets/Templates/.../app.h` and `Assets/Templates/.../U1.ino.cpp`.
  - Suggested resolution:
    - Ensure these template files are NOT imported as plugins (or move them out of any `Plugins/` folder scope).
    - Alternatively, adjust importer settings so they don't collide in `<PluginPath>/x86/`.
  - Impact: `py tools/rt_tool.py build-standalone` fails until fixed.

### Plan Tracking Integrity

- `docs/implementation_plan.json` had drift where many issues were accidentally marked completed.
  - Fix: reverted `progress.completedIssues` to match explicit DONE items in `docs/implementation_plan_todo.md`.
  - Recommendation: treat TODO DONE markers + concrete progress notes as the source of truth.
