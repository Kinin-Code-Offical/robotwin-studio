# High Fidelity Plan

Work items for pushing correctness and realism further (thermal, environment, tolerances, noise).

## HF-00 Cross-cutting: Determinism + Validation + Budgets

- Determinism is a first-class requirement for HF features (seeded RNG, fixed timestep, reproducible ordering).
- Every HF feature must have at least one:
  - Minimal fixture/component that exercises it.
  - Automated regression check (unit/integration) that fails on drift.
- Budget awareness:
  - Define a per-feature runtime budget (e.g., max ms/frame or update rate) and a safety fallback (reduced update rate, culling by distance, etc).
- Observability:
  - Ensure feature state can be inspected (debug overlay/log/trace hooks) to support correctness-first workflow.

## HF-01 Thermal Simulation Subsystem

- Add per-component thermal state (temperature, thermal mass, resistance).
- Couple electrical power dissipation into temperature rise.
- Allow ambient cooling and thermal exchange with neighbors.

Minimum acceptance

- A component’s temperature changes over time based on power dissipation and ambient.
- A simple scene/fixture demonstrates steady-state vs transient behavior.
- A regression test validates the same trace/curve within tolerance.

## HF-02 Environmental Link (Wind and Ambient)

- Add scene-level ambient controls (wind vector, air density, humidity).
- Feed ambient data into thermal and physics solvers.

Minimum acceptance

- Changing ambient controls produces measurable, deterministic changes in thermal/physics outcomes.
- A regression test validates that ambient updates are correctly applied.

## HF-03 Temperature Dependent Component Models

- Resistor: temperature coefficient affects resistance.
- LED/Diode: forward voltage shifts with temperature.
- Motor: coil resistance changes with heat.

Minimum acceptance

- At least one temperature-dependent model is end-to-end: thermal state → electrical model parameter update → observable behavior change.
- Deterministic test covers a representative component (e.g., resistor with TCR).

## HF-04 Component Tolerance and Aging

- Apply tolerance ranges during component instantiation.
- Persist aging parameters in project metadata.

Minimum acceptance

- Tolerances are reproducible via seeded randomness and persist in saved projects.
- A regression test validates that the same seed produces identical instantiated values.

## HF-05 Electronic Noise Injection

- Add noise floor per net and per sensor.
- Allow deterministic noise with seeded RNG for replay.

Minimum acceptance

- Noise is deterministic for a given seed and differs for different seeds.
- A regression test validates a short “golden” noise trace.

## HF-06 Per-part Physical Material Overrides

- Physical overrides are authored per part in `component.json` inside `.rtcomp`.
- **Volume policy:** `volumeM3` is the source of truth for mass realism. Mesh/bounds volume is only a fallback for preview.
- Sample package: `tools/fixtures/hf06_sample.rtcomp` (validated by `tools/scripts/validate_physical_overrides.py`).

Minimum acceptance

- `volumeM3` drives mass when `massKg` is not authored.
- Collider friction/restitution reflect authored values where present.
- A sample package + validator ensure schema and runtime behavior don’t drift.

## HF-07 Electromagnetics: Magnetic Field + Magnet Behaviors (Quasi-static)

Goal

- Support magnet/magnetic interactions and magnet-related sensors without a full Maxwell/FEM solver.

Scope

- Per-part authored magnetic properties in `.rtcomp` (e.g., dipole model).
- Runtime contributions:
  - Magnetic field sampling for sensors (Hall/reed/magnetometer).
  - Optional magnetic force/torque for simple magnet-mechanics interactions.

Suggested model (minimum viable)

- Permanent magnet: magnetic dipole moment vector `magneticMomentA_m2` in part local space.
- Electromagnet/coil (optional): approximate dipole moment `m ≈ N·I·A` with `turns`, `currentA`, `areaM2`, `axis`.
- Field evaluation: dipole field $B(r)$ with distance clamp and range culling (performance).

Minimum acceptance

- A fixture demonstrates:
  - Hall sensor reading responds to magnet position/orientation.
  - Reed switch toggles at a configurable threshold.
- Deterministic regression test validates sensor output for a seeded scene.
- Performance guardrails exist (max interaction distance and/or neighbor cap).
