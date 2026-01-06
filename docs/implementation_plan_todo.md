# Implementation Plan TODO

Status legend:

- DONE (verified): Implemented and backed by repo evidence (code/docs/tools).
- DONE (needs rewrite): Implemented, but this entry needs wording/evidence cleanup before closing.
- TODO: Not implemented yet.

Phase 0: Stabilization (Blocking/Quality Gates)

- DONE (verified) 167 Q0-01 Remove editor-only APIs from runtime builds (ban editor-only APIs in player) (Evidence: tools/scripts/audit_runtime_apis.py; tools/rt_tool.py; no System.Windows.Forms usage found in runtime sources)
- DONE (verified) 168 Q0-02 Generalize firmware host naming/pathing for non-Arduino boards (Evidence: RobotWin/Assets/Scripts/Game/SessionManager.cs; RobotWin/Assets/Scripts/Circuit/CircuitController.cs)
- DONE (verified) 169 Q0-03 Non-AVR profile QA path (disable external firmware cleanly when no supported MCU profile exists) (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs)
- DONE (verified) 170 Q0-04 Build/log output audit across tools (builds/ + logs/ consistency) (Evidence: tools/scripts/audit_build_outputs.py; tools/rt_tool.py; logs/tools/build_output_audit.log)

Phase 1: Foundation

- DONE (verified) 103 P0-01 Step/Output correlation in RTFW protocol (Evidence: FirmwareEngine/PipeManager.\* stepSequence; CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareClient.cs; RobotWin/Assets/Scripts/CoreSim/FirmwareClient.cs)
- DONE (verified) 104 P0-02 CoreSim synchronous stepping API (Evidence: CoreSim/src/RobotTwin.CoreSim/Host/SimHost.cs StepOnce)
- DONE (verified) 105 P0-03 Unity synchronous stepping API for external firmware (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs StepOnce(dtSeconds))
- DONE (verified) 106 P0-04 Define reference deterministic mode config (Evidence: CoreSim/src/RobotTwin.CoreSim/Host/DeterministicModeConfig.cs; docs/reference_deterministic_mode.json)
- DONE (verified) 107 P0-05 FirmwareEngine full pin output sampling (PinToPort-based) (Evidence: FirmwareEngine/VirtualMcu.cpp SamplePinOutputs; FirmwareEngine/main.cpp)
- DONE (verified) 108 P0-06 Define 0xFF pin output semantics + enforce in clients (Evidence: FirmwareEngine/Protocol.h kPinValueUnknown; FirmwareEngine/VirtualMcu.h; CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareClient.cs; RobotWin/Assets/Scripts/CoreSim/FirmwareClient.cs)
- DONE (verified) 109 P0-07 Pipe reader hardening + connection health surfaced (Evidence: FirmwareEngine/PipeManager.\* IsConnected/EnsurePipe/DisconnectPipe; CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareClient.cs)
- DONE (verified) 110 P1-08 Decide canonical firmware runtime (authoritative path) (Evidence: docs/ARCHITECTURE.md; docs/implementation_plan.json decision entry)
- DONE (verified) 111 P1-09 Decide canonical circuit model (authoritative path) (Evidence: docs/ARCHITECTURE.md; docs/implementation_plan.json decision entry)
- DONE (verified) 112 P1-10 Fix NativeEngine co-sim step ordering (remove one-step lag) (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs comment on one-tick lag)
- DONE (verified) 113 P1-11 Clarify Unity stepping contract (Native_Step vs Physics_Step) (Evidence: docs/ROBOTWIN_UNITY.md; RobotWin/Assets/Scripts/Core/NativeBridge.cs; RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs)
- DONE (verified) 114 P1-12 Mark prototype/legacy interfaces (fixed-size SharedState) (Evidence: NativeEngine/src/NativeEngine_Core.cpp legacy shared state; RobotWin/Assets/Scripts/Core/Bridge_Interface.cs SharedState)
- DONE (verified) 115 P1-13 Cross-layer golden trace harness (Evidence: CoreSim/tests/RobotTwin.CoreSim.Tests/GoldenTraceHarnessTests.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/Fixtures/golden_trace_v1.json; Recorder: tools/GoldenTraceRecorder/Program.cs + tools/scripts/record_golden_trace.ps1; Repro: `python tools/rt_tool.py record-golden-trace` writes fixture successfully)
- DONE (verified) 184 P1-13b FirmwareEngine lockstep OutputState capture (Evidence: FirmwareEngine/main.cpp lockstep contract + SendOutputState; FirmwareEngine/PipeManager.cpp read loop does not time out/disconnect during idle; Repro: `python tools/rt_tool.py record-golden-trace`)
- DONE (verified) 116 P1-14 .rtwin round-trip tests (Evidence: CoreSim/tests/RobotTwin.CoreSim.Tests/RtwinRoundTripTests.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/Fixtures/fixture_v1.rtwin.base64; CoreSim/tests/RobotTwin.CoreSim.Tests/RobotTwin.CoreSim.Tests.csproj)
- DONE (verified) 117 P1-15 Protocol framing tests (magic/version/bounds) (Evidence: CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareProtocol.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/ProtocolFramingTests.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/FirmwareProtocolTests.cs; FirmwareEngine/PipeManager.cpp)

Phase 2: Realtime

- DONE (verified) 179 RT-15 Protocol version negotiation + feature flags (Evidence: FirmwareEngine/Protocol.h; FirmwareEngine/PipeManager.cpp; CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareProtocol.cs; CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareClient.cs; RobotWin/Assets/Scripts/CoreSim/FirmwareClient.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/FirmwareProtocolTests.cs)
- DONE (verified) 178 RT-14 Realtime budget/fast-path counters in telemetry (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs budget stats; RobotWin/Assets/Scripts/Debug/RemoteCommandServer.cs realtime_stats; tools/debug_console/public/\*)
- DONE (verified) 177 RT-13 Firmware host mode control (lockstep vs realtime) + Debug Console UI toggle (Evidence: RobotWin/Assets/Scripts/Debug/RemoteCommandServer.cs firmware-mode; RobotWin/Assets/Scripts/Game/SessionManager.cs; RobotWin/Assets/Scripts/Game/SimHost.cs; tools/debug_console/public/\*)
- DONE (verified) 176 RT-12 Telemetry surface for firmware host + realtime mode flags (Evidence: RobotWin/Assets/Scripts/Debug/RemoteCommandServer.cs firmware_host + realtime payload)
- DONE (verified) 175 RT-11 Tick jitter/overrun counters + telemetry surface (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs timing stats; RobotWin/Assets/Scripts/Debug/RemoteCommandServer.cs; tools/debug_console/public/\*)
- DONE (verified) 149 RT-09 Windows realtime hardening (priority/affinity/timers/no-alloc hot loops) (Evidence: CoreSim/src/RobotTwin.CoreSim/Host/RealtimeHardening.cs; CoreSim/src/RobotTwin.CoreSim/Host/RealtimeHardeningOptions.cs)
- DONE (verified) 146 RT-06 Multi-rate stepping with a master clock (next-event scheduling) (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs next-event scheduler + circuit-only events; RobotWin/Assets/Scripts/Game/RealtimeScheduleConfig.cs accumulator cap/epsilon; RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs external stepping)
- DONE (verified) 148 RT-08 Timestamped protocol stream + backlog/drop policy (Evidence: FirmwareEngine/Protocol.h; FirmwareEngine/PipeManager.cpp; CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareClient.cs; RobotWin/Assets/Scripts/CoreSim/FirmwareClient.cs)
- DONE (verified) 141 RT-01 Define Realtime Contract (deadline-first) (Evidence: docs/REALTIME_CONTRACT.md)
- DONE (verified) 142 RT-02 Deadline-aware scheduler + per-simulation budgets (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs realtime budgets + overruns)
- DONE (verified) 143 RT-03 Fast-Path + Corrective-Path (tiered realtime) (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs fast path counters + skip logic)
- DONE (verified) 144 RT-04 Realtime circuit solving strategy (incremental + sparse + early-exit) (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs input signature + early-exit reuse)
- DONE (verified) 145 RT-05 Hybrid MCU/peripheral modeling for realtime (event queues) (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs firmware input cache/change tracking)
- DONE (verified) 147 RT-07 Observability/metrics + ring-buffer tracing (non-blocking) (Evidence: RobotWin/Assets/Scripts/Game/SimHost.cs tick trace; tools/debug_console/public/\*)
- DONE (verified) 150 RT-10 Define fidelity/error budgets per tier (numeric targets) (Evidence: docs/REALTIME_BUDGETS.md)

Phase 3: High Fidelity

- DONE (verified) 161 HF-01 Thermal Simulation Subsystem (Evidence: CoreSim/src/RobotTwin.CoreSim/Models/Physics/ThermalModel.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/ThermalModelTests.cs; RobotWin/Assets/Scripts/CoreSim/Engine/CoreSimRuntime.cs; NativeEngine/src/Physics/PhysicsWorld.cpp)
- DONE (verified) 163 HF-03 Temperature Dependent Component Models (Evidence: RobotWin/Assets/Scripts/CoreSim/Engine/CoreSimRuntime.cs diode forward temp coeff + resistor temp coeff; CoreSim/src/RobotTwin.CoreSim/Models/Physics/ThermalModel.cs)
- DONE (verified) 162 HF-02 Environmental Link (Wind and Ambient) (Evidence: RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs ambient/wind config + accessors; RobotWin/Assets/Scripts/Game/SimHost.cs AmbientTempC sync; NativeEngine/src/Physics/PhysicsWorld.cpp)
- DONE (verified) 164 HF-04 Component Tolerance and Aging (Evidence: CoreSim/src/RobotTwin.CoreSim/Models/Physics/ComponentVariation.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/ComponentVariationTests.cs; RobotWin/Assets/Scripts/CoreSim/Engine/CoreSimRuntime.cs)
- DONE (verified) 165 HF-05 Electronic Noise Injection (Evidence: CoreSim/src/RobotTwin.CoreSim/Models/Physics/DeterministicNoise.cs; CoreSim/tests/RobotTwin.CoreSim.Tests/DeterministicNoiseTests.cs; RobotWin/Assets/Scripts/CoreSim/Engine/CoreSimRuntime.cs)
- DONE (verified) 180 HF-06 Per-part physical material properties (mass/friction/elasticity/strength) (Evidence: RobotWin/Assets/Scripts/UI/RunMode/Circuit3DView.cs; RobotWin/Assets/Scripts/UI/RunMode/ComponentPhysicalInfo.cs; RobotWin/Assets/Scripts/UI/ComponentStudio/ComponentStudioController.cs; RobotWin/Assets/Scripts/UI/CircuitStudio/CircuitStudioController.cs; RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs; RobotWin/Assets/Scripts/Game/NativePhysicsBody.cs; RobotWin/Assets/Scripts/Core/NativeBridge.cs; NativeEngine/src/NativeEngine_Core.cpp)
- DONE (verified) 181 HF-06b Sample component + regression validation for physical overrides (Evidence: tools/fixtures/hf06_sample.rtcomp; tools/scripts/validate_physical_overrides.py; tools/rt_tool.py)
- DONE (verified) 182 HF-06c Per-part NativePhysicsBody mapping (no equal mass split) (Evidence: RobotWin/Assets/Scripts/UI/RunMode/Circuit3DView.cs; RobotWin/Assets/Scripts/Game/NativePhysicsBody.cs; RobotWin/Assets/Scripts/Game/NativePhysicsWorld.cs; RobotWin/Assets/Scripts/Core/NativeBridge.cs; NativeEngine/src/NativeEngine_Core.cpp)
- DONE (verified) 183 HF-06d Volume source-of-truth for mass realism (Evidence: RobotWin/Assets/UI/ComponentStudio/ComponentStudio.uxml; RobotWin/Assets/Scripts/UI/ComponentStudio/ComponentStudioController.cs; RobotWin/Assets/Scripts/UI/CircuitStudio/CircuitStudioController.cs; docs/HIGH_FIDELITY_PLAN.md)

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
- DONE (verified) 121 P2-19 FirmwareEngine SyncInputs allocation optimization (Evidence: FirmwareEngine/VirtualMcu.cpp; FirmwareEngine/VirtualMcu.h; logs/firmware/build.log)
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

- DONE (verified) 129 P2-27 Make active backend choice visible in Unity logs/UI (Evidence: RobotWin/Assets/Scripts/UI/RunMode/RunModeController.cs Backend log)
