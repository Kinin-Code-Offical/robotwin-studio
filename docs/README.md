# RobotWin Studio Documentation

This folder contains the authoritative documentation for the RobotWin Studio codebase.

## Contents

- SETUP_WINDOWS.md: Development environment setup on Windows.
- ARCHITECTURE.md: System overview and runtime flow.
- PROJECT_STRUCTURE.md: Repository layout and ownership.
- CORE_SIM.md: CoreSim engine notes and contracts.
- ROBOTWIN_UNITY.md: Unity project structure and UI workflow.
- FIRMWARE_ENGINE.md: Virtual firmware runtime and build notes.
- NATIVE_ENGINE.md: Native engine overview and build notes.
- TOOLS.md: Tooling and automation commands.
- BUILD_RELEASE.md: Build and packaging steps.
- TESTING.md: Test strategy and how to run tests.
- DEBUG_CONSOLE.md: Local web UI for tests and log inspection.
- TROUBLESHOOTING.md: Common issues and fixes.

## Quick Start (Dev)

1. Follow SETUP_WINDOWS.md to install dependencies.
2. Open `RobotWin` in Unity Hub with version 6000.3.2f1.
3. Sync plugins:

   ```powershell
   python tools/rt_tool.py update-unity-plugins
   ```

4. Run smoke validation:

   ```powershell
   python tools/rt_tool.py run-unity-smoke
   ```

## Documentation Rules

- Keep files concise and focused on one topic.
- Prefer concrete steps and commands.
- Update TOOLS.md when scripts change.
