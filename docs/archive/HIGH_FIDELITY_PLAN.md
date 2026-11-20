# High Fidelity Plan

Work items for improving simulation realism while keeping determinism.

## Thermal

- Per-part temperature state.
- Ambient exchange and cooling.
- Regression fixtures for thermal curves.

## Environment

- Wind and ambient parameters.
- Deterministic updates per tick.

## Component variation

- Tolerance ranges with seeded randomness.
- Persisted per-part variation data.

## Noise

- Deterministic noise sources with seeded randomness.
- Golden traces for regression.

## Acceptance guidance

For each high-fidelity feature, keep at least one of:

- a minimal fixture/component that exercises it end-to-end
- a deterministic regression check (unit/integration) that fails on drift

Prefer features that can be validated through existing test harnesses (CoreSim tests, golden trace capture, physical override validator).
