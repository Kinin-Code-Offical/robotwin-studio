# Model Selection Policy

**You may choose the model level dynamically per task to save time/cost while preserving quality.**

## Tiers
- **LIGHT**: fastest/cheapest model available in the platform.
- **PRO**: Gemini 3 Pro (default for most implementation work).
- **HEAVY**: the strongest/deepest reasoning model available.

## Rules
1. Start with the lowest tier sufficient for the task. Escalate only when needed.
2. Always record the chosen tier in:
   - the Issue description (or a first comment), and
   - the PR description under a section "Model used: <LIGHT|PRO|HEAVY> (why)".
3. If you make two consecutive failed attempts to solve a problem (e.g., build/CI failing, unclear architecture, repeated bug), escalate one tier for the next attempt.

## Recommended tier by work type
### LIGHT
- Creating issues, labels, milestones, PR templates, docs formatting, simple README edits
- Small refactors that do not change behavior
- One-file changes with obvious implementation

### PRO
- Most MVP feature implementation work
- Multi-file changes within one module (CoreSim only OR UnityApp only)
- Writing unit tests and basic playmode/editmode smoke tests
- Implementing wiring validation rules, waveform UI, telemetry serialization

### HEAVY
- Cross-module architecture decisions (CoreSim <-> UnityApp contracts)
- Determinism/replay correctness, race conditions, threading issues
- Debugging non-trivial CI failures or build pipeline problems
- Designing failure models (thermal/power) with calibrated behavior
- Large refactors affecting multiple subsystems

## De-escalation
Once a hard problem is solved, drop back to PRO or LIGHT for routine follow-up (docs, cleanup, small fixes).

## Safety
**Never use LIGHT for decisions that affect system architecture, determinism guarantees, or core data contracts.**
