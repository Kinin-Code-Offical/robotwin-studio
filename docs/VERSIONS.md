# Toolchain Version Policy

## Core Versions
- **.NET SDK**: Latest **8.0 LTS** (pinned in `global.json`).
- **Target Framework**: `net8.0` LTS for CoreSim.
- **Unity**: Latest **2022.3 LTS** (required when UnityApp is initialized).

## Policies
- **No Prerelease**: Use only stable versions for NuGet and Unity packages.
- **Update Strategy**: Toolchain upgrades only via dedicated PRs.
- **CI**: Must verify `global.json` and use stable major versions for Actions.
