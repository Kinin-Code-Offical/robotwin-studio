# Raspberry Pi Plan

Work items for running a Linux guest under QEMU for board-class targets.

## Goals

- Start and stop QEMU reliably with clean lifecycle handling.
- Map GPIO and serial peripherals to the simulated circuit.
- Bridge camera, display, and sensor streams to the guest.
- Keep overhead near zero when the backend is disabled.

## Milestones

1. Process lifecycle and crash recovery.
2. GPIO, I2C, SPI, and UART bridging.
3. Camera and display streaming.
4. Time sync with the master clock.
5. Test harness and smoke tests.

## Implementation pointers

Host-side code:

- `FirmwareEngine/Rpi/QemuProcess.h` and `FirmwareEngine/Rpi/QemuProcess.cpp` (spawn/stop, affinity, priority, CPU limits)
- `FirmwareEngine/Rpi/RpiBackend.h` and `FirmwareEngine/Rpi/RpiBackend.cpp` (channel wiring and update loop)
- `FirmwareEngine/Rpi/RpiShmProtocol.h` (single source of truth for the shared-memory channel protocol)

Unity-side integration:

- `RobotWin/Assets/Scripts/Game/RaspberryPi/RpiRuntimeManager.cs`
- `RobotWin/Assets/Scripts/Game/RaspberryPi/RpiSharedMemoryTransport.cs`

Validation:

- `python tools/rt_tool.py rpi-smoke` (see `tools/RpiSmokeTest/`)
