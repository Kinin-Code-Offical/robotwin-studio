# Activity Log

## 2025-12-24
- **Run (Refinement)**:
    - Model used: PRO
    - Started Circuit Studio Refinement (Catalogs, Tests).
    - Reconciled logs.
- **Run (MVP-0 Initial)**:
  - Model used: PRO
  - **Governance**: Fixed `MVP_SCOPE` location and reconciled log metadata.
  - **Planning**: Created Epic #26 (MVP-0) and issues #27-32.
  - **Circuit Studio MVP-0 (#27)**:
    - Implemented `CircuitValidator` (CoreSim) + Tests.
    - Implemented Unity UI (`CircuitStudioController`, UXML).
    - Fixed broken Unity-CoreSim dependency (added `Plugins/RobotTwin.CoreSim.dll`).
    - Merged to `main` via PR #33.

## 2025-12-23 (Previous)
- **Run Summary**:
  - Model used: LIGHT (Docs & Reporting)
  - Merged PR #12 (CoreSim Scaffold + Toolchain).
  - Adopted Main-First Workflow.
  - Cleaned up `feature/11-coresim-scaffold` and `chore/toolchain-upgrade-latest-stable`.
  - Created Reporting Policy documents.
  - Performed Branch Consolidation:
    - Identified conflicts in PR #17, #19.
    - Updated Issue #13 with conflict details.
    - Skipped PR #13 (CI unknown).
  - Performed Repo Hygiene & Core Contracts:
    - Created .gitignore and removed binaries (PR #21).
    - Implemented `IOContract`, `TemplateSpec`, `SimulationSerializer`.
    - Unified namespaces to `RobotTwin.CoreSim.*`.
    - Tests passed (Serialization).

## 2024-05-23
- **Run Summary**:
  - Model used: PRO
  - **Repo Hygiene**: Restructured `CoreSim` to `src/tests` layout, fixed missing `csproj`, fixed `.sln`.
  - **CI Enforcement**: Fixed `ci.yml` paths. CI now runs 6 real tests and passes.
  - **Unity Alignment**: Updated `UnityApp` scripts (`SessionManager`, `Wizard`) to use `RobotTwin.CoreSim` namespaces.
  - **Contracts**: Updated `TemplateSpec` with backward-compatible aliases (`ID`, `Name`) and embedded objects (`DefaultCircuit`).
  - **Sync**: Merged PR #24. Main is synchronized and clean.
