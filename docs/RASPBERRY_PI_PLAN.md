# Raspberry Pi Integration Plan

Work items for adding a QEMU-backed Linux target (Raspberry Pi class devices) to the simulation stack.

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
