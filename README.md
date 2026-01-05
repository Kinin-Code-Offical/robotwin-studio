# RobotWin Studio: The Ultimate Digital Twin Platform

**RobotWin Studio** is the world's most advanced, high-fidelity robotics simulation environment, designed to bridge the gap between virtual prototyping and physical reality with zero compromise.

It is not just a simulator; it is a **deterministic, bit-perfect digital twin engine** capable of emulating entire embedded systems (AVR, ARM64/Linux), simulating complex physical phenomena (thermal, aerodynamic, electrical), and executing realtime control loops with microsecond precision.

## Core Capabilities

### 1. Full System Emulation (Silicon-Level)
- **Hybrid Virtualization:** Seamlessly runs Arduino (AVR) firmware and Raspberry Pi (ARM64/Linux) operating systems side-by-side.
- **QEMU Integration:** Runs full Linux distributions (Ubuntu/Debian) inside the simulation for true-to-life ROS2, Python, and AI workloads.
- **Peripheral Accuracy:** Emulates GPIO, I2C, SPI, UART, and PWM at the register level.

### 2. High-Fidelity Physics Engine
- **Multi-Domain Solver:** Unified simulation of Rigid Body Dynamics, Aerodynamics, Thermodynamics, and Fluid Dynamics.
- **Component Realism:** Simulates non-ideal behaviors including thermal throttling, voltage sag, signal noise, friction variance, and manufacturing tolerances.
- **Environmental Coupling:** Wind, temperature, humidity, and magnetic interference affect sensor readings and actuator performance dynamically.

### 3. Realtime Determinism
- **Hard Realtime Kernel:** Custom Windows kernel extensions ensure simulation steps occur with strict timing guarantees (<10s jitter).
- **Hardware-in-the-Loop (HIL):** Connect physical controllers to the virtual robot, or virtual controllers to physical hardware.
- **Shared Memory Transport:** Zero-copy IPC architecture for ultra-low latency communication between Unity, Physics, and Firmware modules.

### 4. AI & Computer Vision
- **Synthetic Data Generation:** Photorealistic rendering pipeline for training ML models (segmentation, depth, object detection).
- **NPU Emulation:** Simulates AI accelerator performance and constraints (Hailo, Coral, Jetson).

## Architecture

The platform consists of four tightly integrated pillars:
1.  **CoreSim (C#):** The orchestration layer managing the simulation lifecycle and deterministic state.
2.  **NativeEngine (C++):** The high-performance physics and environmental solver.
3.  **FirmwareEngine (C++/QEMU):** The virtualization host for AVR and ARM64 targets.
4.  **RobotWin (Unity):** The visualization frontend and user interface.

## Getting Started

### Prerequisites
- Windows 10/11 (Realtime Kernel Mode enabled)
- Visual Studio 2022 (C++ / C# Workloads)
- Unity 6 (LTS)
- QEMU 8.0+ (Bundled)

### Installation
1.  Clone the repository recursively.
2.  Run 	ools/setup_environment.ps1 to configure the realtime environment and download QEMU images.
3.  Open RobotWin in Unity Hub.
4.  Open RobotWin.sln in Visual Studio to build the native backend.

## Documentation
- [Architecture Overview](docs/ARCHITECTURE.md)
- [Native Engine & Physics](docs/NATIVE_ENGINE.md)
- [Firmware & QEMU Emulation](docs/FIRMWARE_ENGINE.md)
- [Realtime System Setup](docs/SETUP_WINDOWS.md)

---
*RobotWin Studio: Where Reality Meets Simulation.*
