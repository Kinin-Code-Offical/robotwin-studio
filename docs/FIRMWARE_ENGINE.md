# FirmwareEngine

FirmwareEngine hosts the native firmware runtime for Arduino-style firmware.

## Output

- `VirtualArduinoFirmware.exe` is built into `builds/firmware`.

## Build

```powershell
python tools/rt_tool.py build-firmware
```

## Notes

- The firmware runtime communicates with CoreSim via serialization contracts.
- Keep platform-specific code isolated to this module.
