# Contributing to RobotWin Studio

Thank you for your interest in RobotWin Studio. Bug reports, feature ideas, documentation fixes and code are all welcome.

## Reporting bugs and suggesting features

- Search the [existing issues](https://github.com/Kinin-Code-Offical/robotwin-studio/issues) first.
- Open an issue with: what you did, what you expected, what happened, the app version (status bar or *Yardım → Hakkında*), your Windows version, and if possible a screenshot, the project/world file and `%APPDATA%\RobotWinStudio\crash.log`.
- In the app, *Yardım → Sorun bildir…* opens a new issue.
- Security problems: do **not** open a public issue; see [SECURITY.md](SECURITY.md).

## Development setup

Requirements: Windows 10/11 x64, .NET 10 SDK (pinned in `global.json`), Git. Optional: `arduino-cli` with the `arduino:avr` core (Arduino builds) and Visual Studio 2022 / Build Tools with the C++ workload (ESP32 builds, installer). Details: [docs/SETUP_WINDOWS.md](docs/SETUP_WINDOWS.md).

```powershell
git clone https://github.com/<your-account>/robotwin-studio.git
cd robotwin-studio
dotnet build RobotWinStudio.sln -c Debug
dotnet test RobotWinStudio.sln
builds\RobotWin.Studio\Debug\net10.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
```

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) before larger changes.

## Where code goes

- **Simulation logic** (solver, parts, firmware, PCB, physics, world, file formats) belongs in `CoreSim/src/RobotTwin.CoreSim`, without UI dependencies, with tests in `CoreSim/tests/RobotTwin.CoreSim.Tests`.
- **UI** belongs in `Studio/RobotWin.Studio`.
- `FirmwareEngine/`, `NativeEngine/`, most of `tools/` and `tests/` are legacy (1.x, Unity era) and are not used by the app. Do not build new features on them.

## Code style

- C#: standard .NET conventions; `PascalCase` for types and public members, `_camelCase` for private fields, nullable reference types enabled. XML doc comments (`/// <summary>`) on public and non-obvious members.
- Keep CoreSim deterministic: no wall-clock time or unseeded randomness in simulation code.
- UI text is written in **Turkish** in the code and shown in the user's language from a language pack: `Studio/RobotWin.Studio/Lang/en.json` maps every Turkish text to English (`strings`), and texts with numbers or names in them through `patterns` (`"{…} kablo": "{0} cables"`). English is the default language, so every new label needs its English line; `LangTests` fails on a XAML label without one. Use correct Turkish characters (ç, ğ, ı, İ, ö, ş, ü) and the existing terminology (Devre, Dünya, Kablo çek, Bağla…).
- Save files as UTF-8. Do not rewrite source or Markdown files with Windows PowerShell 5.1 `Get-Content`/`Set-Content` (they use the ANSI code page and corrupt Turkish text).
- The product name is always **RobotWin Studio**.

## Good first issues

Issues labelled **good first issue** say what to change, where, and how to check it; **help wanted** marks larger ones that are free to take. Comment on one before you start so two people do not do the same work. Tasks that make a good first contribution:

- **A translation:** a Turkish text with no English in `Lang/en.json`, or an English line that reads badly. Add it next to the others; texts with values go in `patterns`.
- **A part:** a resistor, sensor or module the library lacks. Parts are defined in `CoreSim/src/RobotTwin.CoreSim/Design/StandardParts.cs` (`Def(lib, id, name, category, prefix, symbol, footprint, …)`); what its model leaves out goes in `ModelLimits.cs`, and a test in `CoreSim/tests` shows it behaves (a datasheet value at one operating point is enough to start).
- **An example project:** a small circuit in `Studio/RobotWin.Studio/Examples/ExampleProjects.cs`; it appears on the start page as a card drawn from its own circuit, and `--selftest` builds, simulates and routes it.
- **A documentation fix or picture:** the user guide is `docs/kullanim/` (Turkish) and `docs/en/guide/` (English); pictures are rendered by the app itself, `python tools/scripts/doc_images.py <RobotWinStudio.exe> [--lang en] name` (`docs/images/shots.json` says how each is taken).
- **A test:** a behaviour that has none yet (see the templates in [docs/TESTING.md](docs/TESTING.md#test-templates)).

## Checks for UI changes

Run these on a Release build after changing a page (they are what CI and reviewers look at):

- `python tools/scripts/contrast_check.py <exe> --quick`: every text readable (3:1 or better) in four themes;
- `python tools/scripts/layout_check.py <exe> --sizes 1920x1080,960x540`: nothing cut off, overlapping or out of keyboard reach;
- `python tools/scripts/token_scan.py --check`: no new hard-coded colours or sizes outside the design tokens;
- `python tools/scripts/doc_check.py`: the guides name files, versions and menu steps that exist.

The interface follows a few rules: icons with tooltips rather than words on bars, flat colours (no gradients, glows or shadows), every slider also takes a typed value, panels show only the sections in use.

## Plugins and scripts

Physics and behaviour of your own (expressions, C# scripts in a world, plugin DLLs) are described in [docs/gelistirici/SDK.md](docs/gelistirici/SDK.md), with a project template and worked examples (a force field, a circuit part of your own). A plugin runs with the app's rights, so a plugin from someone else needs the same care as any program you download.

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
