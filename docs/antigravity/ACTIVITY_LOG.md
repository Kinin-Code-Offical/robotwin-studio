# Activity Log

## 2025-12-24 (Run Mode)
- **Run (Run Mode MVP-0)**
    - Model used: HEAVY
    - **CoreSim**: 
      - Implemented Runtime Engine (`RunEngine`, `RunSession`, `Telemetry`).
      - Implemented `SimulationRecorder` (JSONL output).
      - Added deterministic unit tests.
    - **Unity**:
      - Created `RunMode` scene + `RunModeController`.
      - Integrated "Run" button in Circuit Studio.
      - Added EditMode Smoke Test.
    - **Sync**: Merged PR #40 (`feature/30-run-mode-telemetry-logging`).

## 2025-12-24 (Integration)
- **Run (Integration)**
    - Model used: PRO
    - **Integration**:
      - Connected `CircuitStudio` to `SessionManager` and Defaults Catalog.
      - Added "Connections" UI panel and wiring logic.
      - Generated missing `ProjectWizard.uxml`.
    - **Validation**:
      - Upgraded `CircuitValidator` with GND, Power, Pin checks.
      - Added `ValidationRulesTests` (Verified passing).
    - **Toolchain**:
      - Generated deterministic `.meta` files for key scripts.
      - Generated Minimal YAML Scenes (`Wizard.unity`, `Main.unity`) to ensure runnability.
      - Configured `EditorBuildSettings`.
    - **Sync**: Merged PR #38.
