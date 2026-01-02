# Project Structure

Top-level repository layout and ownership.

```
/CoreSim         Deterministic simulation core (C#)
/RobotWin        Unity project (UI + rendering + interaction)
/FirmwareEngine  Firmware runtime (C++), outputs VirtualArduinoFirmware.exe
/NativeEngine    Native simulation support (CMake)
/tools           Scripts and automation
/docs            Documentation
/logs            Local logs (not tracked)
/builds          Local build outputs (not tracked)
```

## Module Ownership

- CoreSim: Circuit model, simulation runtime, serialization contracts.
- RobotWin: Unity scenes, UI Toolkit layouts, runtime loader tools.
- FirmwareEngine: Arduino firmware execution.
- NativeEngine: Native DLLs used by CoreSim/RobotWin.
