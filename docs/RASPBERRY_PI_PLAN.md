# Raspberry Pi Integration Plan

Work items for adding a QEMU-backed Linux target (Raspberry Pi class devices) to the simulation stack.

## Raspberry Pi 5 note (reality + strategy)

Raspberry Pi 5 hardware is based on BCM2712 + the RP1 I/O controller. Full, register-accurate emulation of that exact board is a large undertaking and may not be available in upstream QEMU at any given time.

RobotWin Studio’s target is therefore defined as:

- **RPi-class product experience (near-term):** provide a “Raspberry Pi 5 profile” using an ARM64 guest under a stable QEMU machine configuration (typically a generic ARM64 platform with VirtIO devices), then expose Pi 5-style ports/modules through the unified device graph (GPIO/I2C/SPI/UART, camera, display, WiFi/BLE world model, USB extensions, etc.).
- **True Pi 5 board emulation (long-term, optional):** if strict Pi 5 firmware + device-tree compatibility is required, pursue an upstream contribution or a maintained fork that adds BCM2712/RP1 device models and a Pi 5 machine.

Either way, “unused board = near-zero overhead” remains a hard requirement: if the Raspberry Pi backend isn’t enabled, QEMU is not launched and no QEMU assets are extracted/loaded.

## RP-01 QEMU Process Lifecycle Management

- Start/stop QEMU with explicit PID tracking.
- Restart on crash with backoff.

## RP-02 GPIO and Peripheral Bridge

- Map GPIO pins to circuit nets.
- Support SPI/I2C/UART pass-through.

## RP-03 Virtual Camera Injection

- Feed Unity camera frames into QEMU via shared memory.

## RP-04 IMU and Sensor Injection

- Provide IMU data stream (accel/gyro/mag) to QEMU devices.

## RP-05 Time Synchronization Strategy

- QEMU time synced to master simulation clock.
- Drift correction on long runs.

## RP-06 QEMU Display Output to Unity

- Stream framebuffer to Unity UI for headless panels.

## RP-07 Network and WiFi Bridge

- Virtual NIC bridged to host for integration tests.

## RP-08 Shared Memory Transport Layer

- Shared memory channel for frames, sensor packets, and logs.
