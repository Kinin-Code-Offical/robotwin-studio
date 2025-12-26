---
description: Unified strict workflow: commit often, always merge into main and push after every session, resolve conflicts immediately, keep main never behind any branch, and write run logs.
---

// turbo-all

# Main Force Sync Workflow (Unified, Strict)

This workflow is intentionally strict.

After every task session (even if work is incomplete), you MUST:

- commit frequently (for traceability),
- merge into `main` (always),
- push `main` to GitHub (always),
- resolve conflicts during the session (always),
- generate `workspace_snapshot.txt` (untracked) (always),
- update `docs/repo_files.txt` and commit if changed (always),
- ensure `origin/main` is not behind any other remote branch (always),
- update and commit run logs on `main` (always),
- and comment on the GitHub Issue: “Status Log – Run Reports” (always).

---

## 1) Core rules (non-negotiable)

1. `main` is the active branch (source of truth).
2. Every session ends with `main` updated and pushed (no exceptions).
3. Conflicts must be resolved immediately (no deferral).
4. Main parity is mandatory: **no remote branch may be ahead of `origin/main`**.
5. Commit often; WIP is allowed.
6. Logs are mandatory and must be committed to `main` every session:
   - `docs/antigravity/LAST_RUN.md` (overwrite)
   - `docs/antigravity/ACTIVITY_LOG.md` (append)

---

## 2) Commit often (required)

Minimum expectations:

- Commit after each meaningful change.
- Commit before and after risky operations (rebases, conflict resolution, merges).
- WIP commits are allowed.

Recommended commit prefixes:

- `wip: ...`, `fix: ...`, `chore: ...`, `docs: ...`

---

## 3) Session procedure (must follow in order)

### A) Prepare and fetch

1. Fetch and prune:

   - `git fetch --all --prune`

2. Confirm current branch:

   - `git branch --show-current`

3. Ensure working tree is clean (no uncommitted changes):
   - `git status`
   - If not clean: commit now (WIP allowed).

Terminology: this document uses **WORK_BRANCH** for the branch you worked on in this session.
If you accidentally worked directly on `main`, set `WORK_BRANCH=main` and continue; merging step becomes a no-op, but parity + logs are still required.

---

### B) Update main locally

4. Switch to main and pull latest:
   - `git checkout main`
   - `git pull origin main`

---

### C) Sync WORK_BRANCH with main (resolve conflicts now)

5. Switch to WORK_BRANCH (skip if WORK_BRANCH is already main):

   - `git checkout <WORK_BRANCH>`

6. Integrate latest main into WORK_BRANCH (skip if WORK_BRANCH is main):

   - Preferred:
     - `git rebase origin/main`
     - If conflicts occur: resolve immediately, then `git rebase --continue`
   - If rebase is not appropriate:
     - `git merge origin/main`
     - If conflicts occur: resolve immediately and commit the resolution

7. Push WORK_BRANCH (skip if WORK_BRANCH is main):
   - If rebased:
     - `git push --force-with-lease origin <WORK_BRANCH>`
   - Otherwise:
     - `git push origin <WORK_BRANCH>`

---

### D) Merge WORK_BRANCH into main (always required)

8. Merge into main (no exceptions):

   - `git checkout main`

9. If WORK_BRANCH != main, merge it:

   - Prefer squash merge:
     - `git merge --squash <WORK_BRANCH>`
     - `git commit -m "merge: <WORK_BRANCH>"`
   - If squash is not possible, do a normal merge:
     - `git merge <WORK_BRANCH>`

10. Push main:

- `git push origin main`

---

## 4) Main parity enforcement (main must not be behind any remote branch)

### A) Check ahead/behind for all remote branches

11. Fetch/prune again:

- `git fetch --all --prune`

12. List remote branches:

- `git for-each-ref --format="%(refname:short)" refs/remotes/origin | sort`

13. For each remote branch `origin/<b>` where `<b>` != `main`, compute:

- `git rev-list --left-right --count origin/main...origin/<b>`
- Output is: `<behind> <ahead>`

Interpretation:

- `ahead > 0` means `origin/<b>` has commits not in `origin/main` → main is behind → you must integrate.

### B) Parity loop (repeat until all branches have ahead == 0)

14. For every branch where `ahead > 0`, integrate it into main:

- `git checkout main`
- Preferred (squash integrate remote branch head):
  - `git merge --squash origin/<b>`
  - Resolve conflicts immediately if any
  - `git commit -m "merge: origin/<b>"`
- Push:
  - `git push origin main`

15. Re-run steps 11–14 until:

- for all branches: `ahead == 0`
- Outcome requirement: **no remote branch is ahead of `origin/main`**

---

## 5) Branch cleanup (recommended each session)

16. Delete WORK_BRANCH locally and remotely (to avoid branch sprawl), if WORK_BRANCH != main:

- Local:
  - `git branch -D <WORK_BRANCH>`
- Remote:
  - `git push origin --delete <WORK_BRANCH>`

If you need to continue work:

- create a fresh branch from updated main:
  - `git checkout main`
  - `git pull origin main`
  - `git checkout -b feature/<next-short-name>`

---

## 6) Run logs (mandatory: update + commit to main every session)

Required files:

- `docs/antigravity/LAST_RUN.md` (overwrite)
- `docs/antigravity/ACTIVITY_LOG.md` (append)

17. Update `LAST_RUN.md` (overwrite) with minimum fields:

- Date/time (local)
- WORK_BRANCH merged into main (name)
- Conflicts encountered? (yes/no + note)
- Parity check completed? (yes)
- Branches integrated during parity loop (list)
- Branches deleted (list)
- Next 1–3 actions

18. Append to `ACTIVITY_LOG.md`:

- Date/time (local)
- 3–10 bullets of what happened
- Branch merges/deletions

19. Commit logs on main and push:

- `git checkout main`
- `git add docs/antigravity/LAST_RUN.md docs/antigravity/ACTIVITY_LOG.md`
- `git commit -m "chore: update run logs"`
- `git push origin main`

---

## 7) Status Log issue comment (mandatory)

20. Resolve the GitHub Issue “Status Log – Run Reports” (once per environment):

- `gh issue list --search "Status Log – Run Reports" --state all --json number,title,url --limit 20`

21. Comment every session using:

- `gh issue comment <ISSUE_NUMBER> --body "<paste summary>"`

Suggested comment template:

## Run Summary

- Date/time (local):
- WORK_BRANCH merged:
- Conflicts resolved:
- Parity: main not behind any branch (yes):
- Other branches integrated into main:
- Branches deleted:
- Shared Info Zip:
- Next 1–3 actions:

---

## 8) End-of-session checklist (Strict & Sequential)

- [ ] **Sync Main**: `git pull origin main`
- [ ] **Context Export**: `pwsh ./tools/export_context_pack.ps1 -Mode MAX`
- [ ] **Log Identity**: Ensure `docs/antigravity/*.md` matches `docs/antigravity/context_exports/latest/*.md` (Copy/Sync).
- [ ] **Repo Index**: `pwsh ./tools/update_repo_files.ps1` (commit if changed)
- [ ] **Snapshot**: `pwsh ./tools/update_workspace_snapshot.ps1` (must remain gitignored)
- [ ] **Shared Info**: `pwsh ./tools/end_session_shared_info.ps1` (Folder Sync + Pointers)
- [ ] **Commit Pointers**: `git add docs/antigravity/SHARED_INFO_LATEST* && git commit -m "chore: update shared info pointers" || echo "No changes"`
- [ ] **Final Sync**: `git pull origin main` (resolve conflicts immediately)
- [ ] **Push**: `git push origin main`
- [ ] **Status Log**: Comment on "Status Log – Run Reports" issue.