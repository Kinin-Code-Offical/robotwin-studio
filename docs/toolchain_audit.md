# Version Audit Report

## Found Pins
| Component | File | Version | Status |
| :--- | :--- | :--- | :--- |
| .NET | `CoreSim.csproj` | `net8.0` | **Latest LTS** |
| .NET SDK | - | - | **Missing global.json** |
| Actions (checkout) | `ci.yml` | `v3` | Outdated (v4 stable) |
| Actions (setup-dotnet)| `ci.yml` | `v3` | Outdated (v4 stable) |
| Unity | - | - | **Missing ProjectSettings** |

## Proposed Stable/LTS Versions
| Component | Target Version | Rationale |
| :--- | :--- | :--- |
| **.NET SDK** | `8.0.400` | Current LTS SDK version |
| **.NET Framework** | `net8.0` | Current LTS Target Framework |
| **Unity Editor** | `2022.3.50f1` | Current LTS (Stable) |
| **Actions** | `v4` | Latest stable major versions |

## Audit Findings
1. **UnityApp**: The project structure is incomplete (missing `ProjectSettings`). It must be initialized using the latest Unity 2022.3 LTS.
2. **.NET**: `CoreSim` is correctly on `net8.0`, but `global.json` is required to ensure consistent builds across environments.
3. **NuGet**: `CoreSim.Tests` dependencies are generally up to date, but will perform a minor review for stable updates.
