# Security Policy

## Supported versions

| Version | Supported |
| --- | --- |
| Latest 2.x release | Yes |
| `main` branch | Yes (development) |
| 1.x (Unity-based design) | No |

Please always use the [latest release](https://github.com/Kinin-Code-Offical/robotwin-studio/releases/latest).

## Reporting a vulnerability

- Report privately through **GitHub Security Advisories**: the repository's [Security tab](https://github.com/Kinin-Code-Offical/robotwin-studio/security) → *Report a vulnerability*.
- If that is not possible, open a minimal issue titled "Security concern - private contact needed" **without** any details, and wait for a maintainer to reply.
- Include a description, steps to reproduce (a sample `.rwproj`/`.rwworld` file if relevant), the affected version and the impact you expect.
- Do not publish details before a fix is released.

We aim to acknowledge reports within a few days and will coordinate disclosure with you. There is no bug bounty.

## Security considerations

RobotWin Studio is an offline desktop application, but some parts deserve care:

- **Project files.** `.rwproj`, `.rwworld`, `.rwpart`, `.rwlib`, `.rwsim`, `.rwmodel` and `.rwfp` are ZIP containers with JSON. Only open files from sources you trust; parsing bugs in these readers are in scope.
- **Native code from sketches.** ESP32 sketches are compiled with MSVC into a DLL that is loaded into the app process and runs with your user's rights. A sketch is ordinary C++: opening someone else's project and running its ESP32 board runs their code on your PC. Treat ESP32 sketches like any program you download.
- **External tools.** The app starts `arduino-cli` and the Visual Studio build tools (`vcvars64.bat`, `cl.exe`, `lib.exe`) from configured or well-known locations. Keep those installations trustworthy.
- **Installer.** The setup program downloads the components it installs (the app, its own .NET 10 runtime, the Arduino compiler, Open CASCADE) from this repository's GitHub release over HTTPS and checks each file's size and SHA-256 before unpacking it. File-type registration writes to the current user's registry (HKCU); only an all-users install writes its uninstall entry under HKLM.
- **Network services, only when asked.** Simulated Wi-Fi/BLE traffic stays inside the simulation. The app listens on a port only for what you start: the automation API (`--api`, loopback only unless `--allow-remote`, with an optional token), and the Modbus TCP and OPC UA servers of a world's PLCs and devices while that world runs (bound to 127.0.0.1 unless the device's listen address is changed). The OPC UA server has no encryption or user authentication yet (security policy None): keep it on loopback or a trusted network.

## For contributors

- Never commit secrets (keys, tokens, passwords).
- Validate everything read from files and from external tool output.
- Check dependencies with `dotnet list RobotWinStudio.sln package --vulnerable`.
