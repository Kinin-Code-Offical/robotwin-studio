# Contributing to RobotWin Studio

Thank you for your interest in RobotWin Studio. Bug reports, feature ideas, documentation fixes and code are all welcome.

## Reporting bugs and suggesting features

- Search the [existing issues](https://github.com/Kinin-Code-Offical/robotwin-studio/issues) first.
- Open an issue with: what you did, what you expected, what happened, the app version (status bar or *Yardım → Hakkında*), your Windows version, and if possible a screenshot, the project/world file and `%APPDATA%\RobotWinStudio\crash.log`.
- In the app, *Yardım → Sorun bildir…* opens a new issue.
- Security problems: do **not** open a public issue; see [SECURITY.md](SECURITY.md).

## Development setup

Requirements: Windows 10/11 x64, .NET 9 SDK (pinned in `global.json`), Git. Optional: `arduino-cli` with the `arduino:avr` core (Arduino builds) and Visual Studio 2022 / Build Tools with the C++ workload (ESP32 builds, installer). Details: [docs/SETUP_WINDOWS.md](docs/SETUP_WINDOWS.md).

```powershell
git clone https://github.com/<your-account>/robotwin-studio.git
cd robotwin-studio
dotnet build RobotWinStudio.sln -c Debug
dotnet test RobotWinStudio.sln
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
```

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) before larger changes.

## Where code goes

- **Simulation logic** (solver, parts, firmware, PCB, physics, world, file formats) belongs in `CoreSim/src/RobotTwin.CoreSim`, without UI dependencies, with tests in `CoreSim/tests/RobotTwin.CoreSim.Tests`.
- **UI** belongs in `Studio/RobotWin.Studio`.
- `FirmwareEngine/`, `NativeEngine/`, most of `tools/` and `tests/` are legacy (1.x, Unity era) and are not used by the app. Do not build new features on them.

## Code style

- C#: standard .NET conventions; `PascalCase` for types and public members, `_camelCase` for private fields, nullable reference types enabled. XML doc comments (`/// <summary>`) on public and non-obvious members.
- Keep CoreSim deterministic: no wall-clock time or unseeded randomness in simulation code.
- UI text is **Turkish**. Use correct Turkish characters (ç, ğ, ı, İ, ö, ş, ü) and the existing terminology (Devre, Dünya, Kablo çek, Bağla…).
- Save files as UTF-8. Do not rewrite source or Markdown files with Windows PowerShell 5.1 `Get-Content`/`Set-Content` (they use the ANSI code page and corrupt Turkish text).
- The product name is always **RobotWin Studio**.

## Pull requests

1. Branch from `main` (`feature/…`, `fix/…`).
2. Keep the change focused; describe what and why, and link the issue (`Fixes #123`).
3. Before pushing:
   - `dotnet build RobotWinStudio.sln -c Release` without new warnings,
   - `dotnet test RobotWinStudio.sln`,
   - `RobotWinStudio.exe --selftest` ends with `SELFTEST PASS`,
   - for UI changes, attach a screenshot (`--snapshot` can render one, see [docs/TESTING.md](docs/TESTING.md)).
4. Update the docs when behaviour changes: [README.md](README.md), the user guide in [docs/kullanim/](docs/kullanim/README.md), and [CHANGELOG.md](CHANGELOG.md) under `[Unreleased]`.
5. Do not commit build output (`builds/`, `bin/`, `obj/`, `*.exe`, `*.dll`); CI rejects tracked build artifacts.

CI (`.github/workflows/ci.yml`) must pass before merging.

## Code of conduct and license

Follow the [Code of Conduct](CODE_OF_CONDUCT.md). By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
