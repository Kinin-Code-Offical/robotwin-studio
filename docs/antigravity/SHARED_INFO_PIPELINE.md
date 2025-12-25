# Shared Info Pipeline

## Overview
RoboTwin Studio enforces a "Shared Info" workflow to persist session context off-repo. This ensures that even between sessions or across different LLM instances, the full context (logs, repo map, recent activity) is available without polluting the Git history with artifacts.

**Frequency**: Runs automatically at the end of every agent session.
**Artifact**: `session_YYYYMMDD_HHMMSSZ.zip`
**Location**: `gdrive:robotwin_studio/shared_infos/`

## Zip Content
The zip file contains the `docs/` folder from the workspace, which includes:
- `docs/antigravity/LAST_RUN.md` (End of session status)
- `docs/antigravity/ACTIVITY_LOG.md` (Cumulative history)
- `docs/antigravity/context_exports/latest/` (Full context pack: git log, file list, gh issues, etc.)
- Architecture & Setup docs.

## Prerequisite: Rclone
To participate in this pipeline (upload or download), the machine must have `rclone` installed and configured.
- **Remote Name**: `gdrive` (configurable via script args)
- **Path**: `robotwin_studio/shared_infos/`

*No secrets are stored in this repository.*

## Pointer Files
In addition to the uploaded zip, the pipeline updates two tracked files in this repo:
1. `docs/antigravity/SHARED_INFO_LATEST.json`: Machine-readable metadata (timestamp, zip name, sha256, link).
2. `docs/antigravity/SHARED_INFO_LATEST_SUMMARY.md`: Human-readable summary of the last session's status and activity.

These serve as a fallback if the Drive download fails or for quick reference without downloading.

## Retrieving Context
To pull the latest session context into your local environment:

```powershell
./tools/get_latest_shared_info.ps1
```

This will:
1. List files in the shared drive folder.
2. Identify the most recent `session_*.zip`.
3. Download it to `.gpt/shared_info/latest/`.
4. Extract contents to `.gpt/shared_info/latest/docs/`.
