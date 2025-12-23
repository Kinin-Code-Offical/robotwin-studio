---
description: Workflow for ensuring main branch is always synchronized and up to date.
---

// turbo-all
# Git Sync Workflow

Every task must conclude with merging to `main` and pushing to GitHub.

1. Ensure all work is committed on the feature branch.
2. Switch to `main` and pull latest.
3. Merge feature branch into `main` (preferably via squash).
4. Push `main` to `origin`.
5. Delete the local and remote feature branch.
6. Record the sync in `LAST_RUN.md` and `ACTIVITY_LOG.md`.
