# Toolchain Version Policy

## Core Versions
- **Unity**: Latest **2022.3 LTS** (e.g., `2022.3.50f1`).
- **.NET SDK**: Latest **8.0 LTS** (pinned via `global.json`).
- **Target Framework**: `net8.0` for all CoreSim projects.

## Policies
1. **No Prerelease**: All NuGet and Unity packages must use stable versions. No `alpha`, `beta`, or `rc` versions allowed in production/main.
2. **LTS Preference**: Always prefer Long-Term Support (LTS) versions for fundamental toolchain components.
3. **Dedicated Upgrades**: Toolchain upgrades (SDKs, Editor versions) must be handled in dedicated `chore/toolchain-*` PRs.
4. **CI Consistency**: GitHub Actions must use versions pinned in `global.json` and latest stable `v4` actions.

## Checklist
- [ ] `global.json` exists and matches CI.
- [ ] `ProjectVersion.txt` matches LTS requirement.
- [ ] `manifest.json` contains no `-preview` or `-alpha` versions.
