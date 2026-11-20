# RobotWin Studio belgeleri / documentation

## Kullanım kılavuzu (Türkçe)

Uygulamayı kullananlar için adım adım, ekran görüntülü kılavuz: **[kullanim/README.md](kullanim/README.md)**

| | |
| --- | --- |
| [1. Başlarken](kullanim/01-baslarken.md) | [6. Montaj](kullanim/06-montaj.md) |
| [2. Devre](kullanim/02-devre.md) | [7. Dünya](kullanim/07-dunya.md) |
| [3. PCB](kullanim/03-pcb.md) | [8. Ölçü aletleri](kullanim/08-olcu-aletleri.md) |
| [4. Kod](kullanim/04-kod.md) | [9. Klavye kısayolları](kullanim/09-kisayollar.md) |
| [5. Modelle](kullanim/05-modelle.md) | [10. Sık sorulan sorular](kullanim/10-sss.md) |

## Developer documentation (English)

| Document | Contents |
| --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Studio ↔ CoreSim layers, simulation loop, firmware co-simulation, world pipeline, file formats, data locations |
| [SETUP_WINDOWS.md](SETUP_WINDOWS.md) | Tools to install, building and running from source |
| [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) | What lives where in the repository |
| [TESTING.md](TESTING.md) | Unit tests, `--selftest`, snapshot rendering, CI |
| [BUILD_RELEASE.md](BUILD_RELEASE.md) | Building the installer, setup options, the release workflow |
| [TOOLS.md](TOOLS.md) | .NET CLI, the app's command-line modes, `build-installer.ps1`, external compilers |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Common problems when installing, compiling firmware, simulating and building |

Also in the repository root: [README](../README.md), [CHANGELOG](../CHANGELOG.md), [CONTRIBUTING](../CONTRIBUTING.md), [SECURITY](../SECURITY.md).

## Archive

[archive/](archive/README.md) keeps the documents of the 1.x Unity-based design (Unity front end, C++ NativeEngine/FirmwareEngine), old plans and one-off reports for history. They do not describe the current app.

## Rules for these docs

- One topic per file; update this index when adding, renaming or moving a document.
- Describe what the code does today. Planned work goes into the README roadmap, not into feature descriptions.
- Prefer exact commands and the exact Turkish UI labels.
- The product name is always **RobotWin Studio**; versions appear only as version numbers.
- The user guide in `kullanim/` is plain CommonMark (no HTML) with relative links, because it is also rendered outside GitHub.
