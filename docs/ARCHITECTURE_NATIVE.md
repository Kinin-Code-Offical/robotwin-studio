# Native Architecture (FirmwareEngine & NativeEngine)

## Overview

RoboTwin Studio uses a hybrid architecture where the UI and high-level simulation logic run in Unity (C#), while the low-level firmware execution and heavy physics calculations are offloaded to native C++ components.

## Components

### 1. FirmwareEngine (Virtual Arduino)

**Location:** `/FirmwareEngine`

A standalone C++ executable (`VirtualArduinoFirmware.exe`) that acts as a virtual microcontroller.

- **Role:** Executes compiled Arduino firmware (or BVM bytecode in the future).
- **Communication:** Connects to Unity via Named Pipes (Windows).
- **Protocol:** Binary framed protocol defined in `Protocol.h`.
- **State:** Maintains the internal state of the microcontroller (Registers, RAM, GPIO).

### 2. NativeEngine (Simulation Host)

**Location:** `/NativeEngine`

A shared library (`NativeEngine.dll`) loaded by Unity.

- **Role:** Provides high-performance physics solving and signal propagation.
- **Integration:** PInvoke interface for C# to call C++ functions.
- **Build System:** CMake.

## Inter-Process Communication (IPC)

The `FirmwareClient.cs` in Unity manages the lifecycle of the `FirmwareEngine` process.

1. **Launch:** Unity starts `VirtualArduinoFirmware.exe` with a specific pipe name.
2. **Handshake:** Unity sends `Hello`, Firmware responds with `HelloAck`.
3. **Loop:**
   - Unity sends `Step` (time delta, input pin states).
   - Firmware executes cycles.
   - Firmware responds with `OutputState` (output pin states).

## Build Pipeline

The native components are built using CMake and standard C++ compilers (MSVC on Windows).

```powershell
# Build FirmwareEngine
python tools/rt_tool.py build-firmware

# Build NativeEngine
python tools/rt_tool.py build-native
```
