# Closeout: Implemented items (2026-01-05)

Use this as a ready-to-paste **closing comment** for GitHub issues that are already implemented.

## Suggested closing comment (template)

Implemented in repo; closing as done.

Evidence:

- `docs/implementation_plan_todo.md` (marked **DONE (verified)** with evidence pointers)
- `docs/implementation_plan.json` (progress notes + file targets)

Quick verification (optional):

- Grep/confirm relevant symbols exist (see evidence pointers in the TODO).
- Run `tools/rt_tool.py` validations if applicable.

## Issues to close

Phase 0

- #167 Q0-01 Remove editor-only APIs from runtime builds
- #168 Q0-02 Generalize firmware host naming/pathing for non-Arduino boards
- #169 Q0-03 Non-AVR profile QA path (disable firmware cleanly)
- #170 Q0-04 Build/log output audit across tools

Phase 1

- #103 P0-01 Step/Output correlation in RTFW protocol
- #104 P0-02 CoreSim synchronous stepping API
- #105 P0-03 Unity synchronous stepping API for external firmware
- #106 P0-04 Define reference deterministic mode config
- #107 P0-05 FirmwareEngine full pin output sampling
- #108 P0-06 Define 0xFF pin output semantics + enforce in clients
- #109 P0-07 Pipe reader hardening + connection health surfaced
- #110 P1-08 Decide canonical firmware runtime (authoritative path)
- #111 P1-09 Decide canonical circuit model (authoritative path)
- #112 P1-10 Fix NativeEngine co-sim step ordering
- #113 P1-11 Clarify Unity stepping contract (Native_Step vs Physics_Step)
- #114 P1-12 Mark prototype/legacy interfaces (fixed-size SharedState)

Phase 2

- #141 RT-01 Define Realtime Contract (deadline-first)
- #147 RT-07 Observability/metrics + ring-buffer tracing
- #149 RT-09 Windows realtime hardening
- #150 RT-10 Define fidelity/error budgets per tier
- #175 RT-11 Tick jitter/overrun counters + telemetry surface
- #176 RT-12 Telemetry surface for firmware host + realtime mode flags

UI

- #129 P2-27 Make active backend choice visible in Unity logs/UI
