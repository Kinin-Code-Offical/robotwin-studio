## Firmware Host --help Fix

### Problem

`RoboTwinFirmwareHost.exe --help` donuyordu - pipe bağlantısı için sonsuz beklemeye giriyordu.

### Root Cause

`main.cpp`'de `--help` parametresi hiç handle edilmiyordu. Program argument parsing'den sonra direkt `PipeManager::Start()` çağırıyordu, bu da Unity'den bağlantı beklemede kalıyordu.

### Solution Applied

```cpp
// Added help flag detection
bool showHelp = false;

for (int i = 1; i < argc; ++i)
{
    if (ParseArg(argv[i], "--help") || ParseArg(argv[i], "-h") || ParseArg(argv[i], "/?"))
    {
        showHelp = true;
        break;
    }
    // ... rest of args
}

if (showHelp)
{
    std::printf("RoboTwinFirmwareHost - CoreSim Firmware Engine\\n\\n");
    std::printf("Usage: RoboTwinFirmwareHost [OPTIONS]\\n\\n");
    // ... full help text with all options
    return 0; // EXIT IMMEDIATELY
}
```

### Implementation Status

- ✅ Code fix applied to `FirmwareEngine/main.cpp`
- ✅ Help text includes all 30+ command-line options
- ⚠️ Cannot test - firmware has other compilation errors in U1/Sensors.h
  - Missing `ErrorLogging::EVENT_MAP_UPDATE` constant
  - Missing `OscillationControl::CONFIDENCE_HALF_LIFE_MS`
  - Type mismatch in U1.cpp

### Verification Plan

Once firmware compiles:

```bash
./builds/firmware/RoboTwinFirmwareHost.exe --help
# Should print help and exit with code 0 (no hang)
```

### Related Issues

Firmware codebase has deeper issues requiring refactoring:

- U1 robot code has incomplete error logging enum
- Sensor configuration constants missing
- Type mismatches in path mapping code

These are **pre-existing issues**, not introduced by this fix.

### Recommendation

**Priority**: Fix is correct but blocked by other issues.  
**Action**: Continue with physics/native engine testing while firmware compilation issues are resolved separately.
