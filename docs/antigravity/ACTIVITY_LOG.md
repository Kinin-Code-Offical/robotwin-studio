# Activity Log

## 2025-12-26 (M0 Stability)
- **Run (Maintenance)**
    - Model used: PRO
    - **CI**:
      - Enforced deterministic builds for CoreSim (path mapping, build flags).
      - Fixed `update_unity_plugins.ps1` to prevent hash mismatches.
    - **Unity**:
      - Fixed `PanelSettings` wiring in all scenes (Wizard, Main, RunMode).
      - Added `WiringTests.cs` (EditMode) to verify UIDocument connections.
      - Improved `ProjectWizardController` logging.
    - **Sync**: Merged PR (`fix/m0-stability-ci-ui`).

## 2025-12-24 (Gitignore Fix)
- **Run (Governance)**
    - Model used: PRO
    - **Fix**:
      - Corrected `.gitignore` logic for `context_exports`.
      - Untracked accidentally committed export files.
    - **Sync**: Merged PR #49 (`fix/gitignore-context-exports`).

## 2025-12-24 (Governance Hotfix)
- **Run (Governance)**
    - Model used: PRO
    - **Fix**:
      - Updated `.gitignore` to explicitly ignore snapshot files.
      - Fixed CI script execution shell (`pwsh`).
      - Removed ambiguous root `repo_files.txt` (if any), mandated `docs/repo_files.txt`.
    - **Sync**: Merged PR #47 (`fix/snapshot-governance-hotfix`).

## 2025-12-24 (Context Pack Tooling)
- **Run (Tooling)**
    - Model used: PRO
    - **Tooling**:
      - Implemented `export_context_pack.ps1` (MIN/FULL).
      - Wired CI verification for script.
    - **Governance**:
      - Updated `CONTEXT_PACK.md` and workflow docs.
    - **Sync**: Merged PR #45 (`feature/context-pack-tooling`).

## 2025-12-24 (Context Pack Export)
- **Run (Tooling)**
    - Model used: PRO
    - **Tooling**:
      - Created `export_context_pack_min.ps1`.
      - Generated initial context pack JSON.
    - **Governance**:
      - Documented usage in `CONTEXT_PACK.md`.
      - Wired export into workflow.
    - **Sync**: Merged PR #43 (`feature/context-pack-export`).

## 2025-12-24 (Snapshot Governance)
- **Run (Governance)**
    - Model used: PRO
    - **Architecture**:
      - Moved `repo_files.txt` to `docs/repo_files.txt`.
      - Excluded `workspace_snapshot.txt` (root) from git.
    - **Automation**:
      - Updated `tools/update_repo_files.ps1` (Added `-Check` mode).
      - Added `tools/update_workspace_snapshot.ps1`.
      - Updated `.github/workflows/ci.yml` to verify docs index.
    - **Sync**: Merged PR #41 (`feature/snapshot-governance`).

## 2025-12-24 (Repo Files Automation)
- **Run (Automation)**
    - Model used: PRO
    - **Automation**:
      - Implemented `tools/update_repo_files.ps1`.
      - Added CI stale check for `repo_files.txt`.
    - **Governance**:
      - Updated Workflow, PR Template, and Prompt to enforce file indexing.
    - **Sync**: Merged PR #40 (`feature/repo-files-index`).

## 2025-12-24 (Firmware Lab)
- **Run (Firmware Lab MVP-0)**
    - Model used: PRO
    - **CoreSim**: 
      - Implemented Waveforms (Step, Ramp, Sine).
      - Added Waveform Unit Tests.
    - **Unity**:
      - Expanded `RunMode` UI for Multi-Signal Injection.
      - Implemented Waveform Sampling in `RunModeController`.
      - Added Serial Log Stub.
    - **Sync**: Merged PR #41 (`feature/29-waveform-expansion`).

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

## 2025-12-26 (Shared Info Migration)
- **Run (Feature)**
    - Model used: PRO
    - **Status**: Migrated Shared Info pipeline to Direct Drive Sync (Folder Mirror).
    - **Outcome**: Faster/cleaner context persistence without zip artifacts in Drive root.


## 2025-12-26 (CI Stability Fixes)
- **Run (Fixes)**
    - Model used: HEAVY
    - **Status**: Fixed CS8618 Hygiene, Enforced Deterministic Builds, Restored Unity CI Secrets.
    - **Outcome**: CI and local validation green. Merged PR #73.


## 2025-12-26 (Blinky Template)
- **Run (Feature)**
    - Model used: HEAVY
    - **Status**: Implemented Blinky Template (Issue #31).
    - **Outcome**: Clean architecture for templates (BlinkyTemplate.cs). Plugins synced.


## 2025-12-26 (Engine & Telemetry)
- **Run (Feature)**
    - Model used: HEAVY
    - **Status**: Implemented Issue #30.
    - **Outcome**: Restored legacy RunEngine/Recorder. Passed Unity Plugin Sync.


## 2025-12-26 (Smoke Tests)
- **Run (Feature)**
    - Model used: HEAVY
    - **Status**: Implemented Issue #32.
- **Outcome**: Added PlayMode tests and enabled in CI.


## 2025-12-26 (Code Hygiene & Determinism)
- **Run (Maintenance)**
    - Model used: HEAVY
    - **Status**: Enforced C# 11 `required` properties and fixed CS8618 warnings.
    - **Outcome**: Added `Polyfills.cs` for netstandard2.1 support. Synced Unity plugins (hash match).

