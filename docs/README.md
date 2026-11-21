---
title: Developer docs
permalink: /docs/
---
# Developer documentation

For people who build RobotWin Studio from source, script it, or extend its physics. Using the app: the [user guide](en/guide/README.md). How every solver works: [physics and maths](en/physics/README.md).

| Document | Contents |
| --- | --- |
| [Automation API](API.md) | Headless server, JSON-RPC methods, sensors, actuators, Python / ROS 2 |
| [Physics scripts (SDK)](SDK.md) | Expressions, C# scripts in a world, plugin DLLs, custom circuit parts — with worked examples |
| [Architecture](ARCHITECTURE.md) | Studio ↔ CoreSim layers, simulation loop, firmware co-simulation, world pipeline, file formats, data locations |
| [Setup on Windows](SETUP_WINDOWS.md) | Tools to install, building and running from source |
| [Project structure](PROJECT_STRUCTURE.md) | What lives where in the repository |
| [Testing](TESTING.md) | Unit tests, `--selftest`, snapshot rendering, CI |
| [Build and release](BUILD_RELEASE.md) | Building the installers, setup options, the release workflow |
| [Tools](TOOLS.md) | .NET CLI, the app's command-line modes, `build-installer.ps1`, external compilers |
| [Troubleshooting](TROUBLESHOOTING.md) | Common problems when installing, compiling firmware, simulating and building |
| [Accessibility](ACCESSIBILITY.md) | What works today, known gaps, how to report a problem |

Also in the repository root: [README](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/README.md), [CHANGELOG](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md), [CONTRIBUTING](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CONTRIBUTING.md), [SECURITY](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/SECURITY.md).

## Archive

[archive/](archive/README.md) keeps the documents of the 1.x Unity-based design (Unity front end, C++ NativeEngine/FirmwareEngine), old plans and one-off reports for history. They do not describe the current app.

## Rules for these docs

- Every page exists in Turkish and in English, complete in each language; a page never mixes the two. The pairs are listed in `docs/_data/nav.yml`, which also drives the menus and the language switch.
- One topic per file; update `nav.yml` and both indexes when adding, renaming or moving a document.
- Describe what the code does today. Planned work goes into the README roadmap, not into feature descriptions.
- Prefer exact commands and the exact Turkish UI labels (with an English gloss on English pages).
- Diagrams are Mermaid (```` ```mermaid ````), formulas are ```` ```math ```` blocks and `` $`…`$ `` inline: both render on GitHub and on the site. No text-drawn diagrams.
- The product name is always **RobotWin Studio**; versions appear only as version numbers.
