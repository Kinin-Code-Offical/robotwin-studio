# Activity Log

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
