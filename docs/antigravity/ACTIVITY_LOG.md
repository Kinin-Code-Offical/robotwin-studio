# Activity Log

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
