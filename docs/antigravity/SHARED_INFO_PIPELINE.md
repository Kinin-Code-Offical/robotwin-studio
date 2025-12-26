# Shared Info Pipeline

## Overview
RoboTwin Studio enforces a "Shared Info" workflow to persist session context off-repo. This ensures that even between sessions or across different LLM instances, the full context (logs, repo map, recent activity) is available.

**Frequency**: Runs automatically at the end of every agent session.
**Artifact**:
- **Primary**: Direct Folder Sync (Directory Mirror)
- **Legacy**: `session_YYYYMMDD_HHMMSSZ.zip` (Zip Archive)
**Location**: `gdrive:robotwin_studio/shared_infos/latest_docs/docs` (for Folder Sync)

## Folder Sync (Direct Drive)
This mode uploads the `docs/` folder directly to Google Drive, ensuring an up-to-date mirror of the documentation tree.
- **Upload Mode**: `DIR` (Default)
- **Dir Mode**: `COPY` (Overwrites changed files, keeps new remote files) or `MIRROR` (Exact sync).

## Prerequisite: Rclone
To participate in this pipeline (upload or download), the machine must have `rclone` installed and configured.
- **Remote Name**: `gdrive` (configurable via script args)
- **Path**: `robotwin_studio/shared_infos/`

*No secrets are stored in this repository.*

## Pointer Files
In addition to the upload, the pipeline updates two tracked files in this repo:
1. `docs/antigravity/SHARED_INFO_LATEST.json`: Machine-readable metadata (timestamp, type, remote path).
2. `docs/antigravity/SHARED_INFO_LATEST_SUMMARY.md`: Human-readable summary.

These serve as the source of truth for the *next* session to locate and retrieve context.

## Retrieving Context
To pull the latest session context into your local environment:

```powershell
./tools/get_latest_shared_info.ps1
```

This will:
1. Read `SHARED_INFO_LATEST.json` to find the artifact type and location.
2. If `dir`: Synchronize the remote folder to `.gpt/shared_info/latest/docs/`.
3. If `zip`: Download and extract the specific session zip.

