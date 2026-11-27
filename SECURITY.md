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
- **Native code from sketches.** ESP32 sketches are compiled with MSVC into a DLL. It runs in a process of its own (`rw_sketch_host.exe`) with an AppContainer token and no capabilities, inside a job object: it cannot reach the network or the user's files, cannot start other processes or touch the desktop, is limited to 1 GB of memory and ends with the app; the app checks everything the host writes into their shared memory before using it. When the host cannot be built or started, the sketch falls back to the app's own process and the event log says so; a sketch DLL from an older build (no `.host` file beside it) also runs in-process. Reports of ways out of this sandbox are in scope. Arduino (AVR) firmware runs in RobotWin's own emulator and executes no native code.
- **Code inside a file from elsewhere.** A project or world that Windows marked as coming from the internet (its Zone.Identifier) and that carries code the PC would run directly (an FMU's native library, C# world scripts) asks before the simulation starts; trusting it unblocks the file. ESP32 sketches run in their sandbox and Arduino code in the emulator, so they are not asked about.
- **External tools.** The app starts `arduino-cli` and the Visual Studio build tools (`vcvars64.bat`, `cl.exe`, `lib.exe`) from configured or well-known locations. Keep those installations trustworthy.
- **Installer.** The setup program downloads the components it installs (the app, its own .NET 10 runtime, the Arduino compiler, Open CASCADE) from this repository's GitHub release over HTTPS and checks each file's size and SHA-256 before unpacking it. File-type registration writes to the current user's registry (HKCU); only an all-users install writes its uninstall entry under HKLM.
- **Network services, only when asked.** Simulated Wi-Fi/BLE traffic stays inside the simulation. The app listens on a port only for what you start: the automation API (`--api`, loopback only unless `--allow-remote`, with an optional token), and the Modbus TCP and OPC UA servers of a world's PLCs and devices while that world runs (bound to 127.0.0.1 unless the device's listen address is changed). A PLC's OPC UA server offers Basic256Sha256 (signed, or signed and encrypted), named users (passwords always sent encrypted with the server's certificate, read-only users possible) and accepts only client certificates in its `pki\trusted` folder unless set to accept all; with no security set it is security None and anonymous, and the event log warns when that is open to the network. The PLC's OPC UA client checks the server's certificate the same way. Checked against an independent stack (FreeOpcUa asyncua, `tools/interop/opcua`).

## For contributors

- Never commit secrets (keys, tokens, passwords).
- Validate everything read from files and from external tool output.
- Check dependencies with `dotnet list RobotWinStudio.sln package --vulnerable`.
