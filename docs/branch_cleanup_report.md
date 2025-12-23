# Branch Cleanup Report

## Actions Taken

- **DELETED**: `master` (merged into `main`).

## Branches Kept

| Branch                                  | Why?                                      | Action Recommended                                                      |
| :-------------------------------------- | :---------------------------------------- | :---------------------------------------------------------------------- |
| `main`                                  | Protected / default branch.               | None.                                                                   |
| `feature/coresim-scaffold`              | Active work (has PR).                     | Continue work; merge to main when ready.                                |
| `feature/bootstrap-scaffolding`         | Active PR exists or historical bootstrap. | Verify if redundant; merge or close and delete.                         |
| `feature/unity-wizard`                  | Unmerged commits, no PR.                  | Create a PR or decide to archive/delete via “Branch cleanup decisions”. |
| `feature/bootstrap_v2`                  | Unmerged commits; previous PR closed.     | Create PR or decide via “Branch cleanup decisions”.                     |
| `feature/coresim-generic-models`        | Unmerged commits; previous PR closed.     | Create PR or decide via “Branch cleanup decisions”.                     |
| `chore/toolchain-upgrade-latest-stable` | Toolchain work branch.                    | Open PR or integrate into main per sync workflow.                       |

## Notes

- For branches with unmerged commits and no PR: create/update a single issue “Branch cleanup decisions” and record ahead/behind vs main + last commit date before deleting.
