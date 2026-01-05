# Implementation Plan TODO

Phase 1: Foundation
- DONE 103 P0-01 Step/Output correlation in RTFW protocol
- DONE 104 P0-02 CoreSim synchronous stepping API
- DONE 105 P0-03 Unity synchronous stepping API for external firmware
- DONE 106 P0-04 Define reference deterministic mode config
- DONE 107 P0-05 FirmwareEngine full pin output sampling (PinToPort-based)
- DONE 108 P0-06 Define 0xFF pin output semantics + enforce in clients
- DONE 109 P0-07 Pipe reader hardening + connection health surfaced
- 110 P1-08 Decide canonical firmware runtime (authoritative path)
- 111 P1-09 Decide canonical circuit model (authoritative path)
- DONE 112 P1-10 Fix NativeEngine co-sim step ordering (remove one-step lag)
- DONE 113 P1-11 Clarify Unity stepping contract (Native_Step vs Physics_Step)
- DONE 114 P1-12 Mark prototype/legacy interfaces (fixed-size SharedState)

Phase 2: Realtime
- 149 RT-09 Windows realtime hardening (priority/affinity/timers/no-alloc hot loops)
- 146 RT-06 Multi-rate stepping with a master clock (next-event scheduling)
- 148 RT-08 Timestamped protocol stream + backlog/drop policy
- DONE 141 RT-01 Define Realtime Contract (deadline-first)
- 142 RT-02 Deadline-aware scheduler + per-simulation budgets
- 143 RT-03 Fast-Path + Corrective-Path (tiered realtime)
- 144 RT-04 Realtime circuit solving strategy (incremental + sparse + early-exit)
- 145 RT-05 Hybrid MCU/peripheral modeling for realtime (event queues)
- DONE 147 RT-07 Observability/metrics + ring-buffer tracing (non-blocking)
- DONE 150 RT-10 Define fidelity/error budgets per tier (numeric targets)

Phase 3: High Fidelity
- 161 HF-01 Thermal Simulation Subsystem
- 163 HF-03 Temperature Dependent Component Models
- 162 HF-02 Environmental Link (Wind and Ambient)
- 164 HF-04 Component Tolerance and Aging
- 165 HF-05 Electronic Noise Injection

Phase 4: Raspberry Pi
- 152 RP-01 QEMU Process Lifecycle Management
- 159 RP-08 Shared Memory Transport Layer
- 153 RP-02 GPIO and Peripheral Bridge
- 157 RP-06 QEMU Display Output to Unity
- 154 RP-03 Virtual Camera Injection
- 158 RP-07 Network and WiFi Bridge
- 155 RP-04 IMU and Sensor Injection
- 156 RP-05 Time Synchronization Strategy

Phase 5: Polish & Optimization
- 118 P2-16 NativeEngine sparse MNA solver + factor reuse
- 119 P2-17 NativeEngine adaptive convergence + early exit
- 120 P2-18 NativeEngine circuit islanding
- 121 P2-19 FirmwareEngine SyncInputs allocation optimization
- 122 P2-20 FirmwareEngine batch serial output messages (realtime)
- 123 P2-21 VirtualArduino PWM mapping for Uno + Mega
- 124 P2-22 VirtualArduino external interrupt modes (edge/level)
- 125 P2-23 NativeEngine AVR IO electrical modeling tier
- 126 P2-24 ADC reference/scaling rules consistency (end-to-end)
- 127 P2-25 Opt-in protocol trace stream for firmware stepping
- 128 P2-26 FirmwareEngine snapshot/restore for deterministic rewind
- 130 P3-28 RTFW schema single source of truth (codegen/shared)
- 131 P3-29 Centralize BVM parsing/writing shared C++ module
- 132 P3-30 Unify simulation clock across Unity/CoreSim/NativeEngine
- 133 P3-31 Approximation tiers enforcement (reference vs fast)
- 134 P3-32 Remove Unity/CoreSim namespace collisions
- 135 P3-33 Netlist-to-NativeEngine builder (full circuit in native)
- 136 P3-34 End-to-end replay system (firmware+circuit+physics)

Phase 6: User Interface
- DONE 129 P2-27 Make active backend choice visible in Unity logs/UI
