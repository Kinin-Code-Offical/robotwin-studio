# Project Structure

The repository is organized as a monorepo containing the four major subsystems.

`
/
 CoreSim/                  # [C#] The Deterministic Orchestrator
    src/                  # Source code for the .NET simulation kernel
    tests/                # Unit tests for logic and scheduling

 NativeEngine/             # [C++] High-Fidelity Physics & Environment
    src/
       Physics/          # Rigid Body Dynamics & Constraints
       Thermal/          # Thermodynamics Solver
       Aero/             # Aerodynamics & Fluid Dynamics
       Shared/           # Shared Memory IPC definitions
    CMakeLists.txt        # Build configuration

 FirmwareEngine/           # [C++/QEMU] Virtualization Host
    src/
       AVR/              # Cycle-accurate AVR interpreter
       QEMU/             # QEMU/VirtIO integration layer
       Peripherals/      # Virtual hardware (Sensors, Motors)
    main.cpp              # Host process entry point

 RobotWin/                 # [Unity] Visualization & UI
    Assets/
       Scripts/          # C# scripts for UI and Rendering
       UI/               # UI Toolkit (USS/UXML)
       Scenes/           # Editor and Runtime scenes
    Packages/             # Unity packages (HDRP, Input System)

 tools/                    # [Python/PowerShell] DevOps & CLI
    rt_tool.py            # Main CLI entry point
    qemu/                 # QEMU image management scripts

 docs/                     # Documentation
 builds/                   # [Ignored] Compiled binaries and artifacts
`

## Build Artifacts

- uilds/native/: Contains NativeEngine.dll (Physics) and SharedMemory.dll.
- uilds/firmware/: Contains RoboTwinFirmwareHost.exe and QEMU binaries.
- uilds/unity/: Contains the exported Unity player (for release).

