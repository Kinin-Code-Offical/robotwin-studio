# Context Pack JSON

**Output Directory**: `docs/antigravity/context_exports/latest` (git-ignored)

## Purpose
The Context Pack allows you to generate a comprehensive snapshot of the repository's state, including GitHub issues, PRs, runs, and local logs. This is essential for sharing context with external LLMs or support teams.

## Content Modes

### MIN (Default)
- **Manifest**: Generation timestamp, branch, SHA.
- **Status Log**: Issue #15 state.
- **Open MVP Issues**: List of active MVP items.
- **Open/Merged PRs**: Recent pull request activity.
- **Branch Parity**: Status of local branches vs origin/main.
- **Logs**: Copies of `LAST_RUN.md` and `ACTIVITY_LOG.md`.

### FULL
- **MIN Pack** contents.
- **Repo Meta**: Description, default branch, etc.
- **Milestones**: Active milestones.
- **Labels**: Repo labels.
- **File Index**: `git_ls_files.txt`.
- **Commit History**: Last 50 commits (`main_commits_50.txt`).

## How to Generate

Run the following command from the repo root:

```powershell
# Default (MIN mode)
pwsh ./tools/export_context_pack.ps1

# Full Export
pwsh ./tools/export_context_pack.ps1 -Mode FULL
```

## How to Share
1. Navigate to `docs/antigravity/context_exports/latest`.
2. Zip the folder contents or upload specific JSON files (e.g., `status_log_issue_15.json`, `manifest.json`) to the chat context.
