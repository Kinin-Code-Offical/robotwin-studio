# Building the installer and releasing

RobotWin Studio ships as a single setup program, `RobotWinStudio-Setup-<version>.exe`, produced by `tools/build-installer.ps1` from the WPF app and the native installer stub in `Installer/`.

## Build the installer locally

Requirements: .NET 9 SDK and Visual Studio 2022 / Build Tools with the C++ workload (the script locates it with `vswhere`).

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.1.0
# self-contained variant (bundles .NET, no runtime needed on the target PC):
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.1.0 -SelfContained
```

Output in `builds\installer\`:

| File | Contents |
| --- | --- |
| `RobotWinStudio-Setup-1.1.0.exe` | Full package: self-contained app with .NET inside (`-SelfContained`). |
| `RobotWinStudio-Setup-1.1.0-lite.exe` | Lite package: framework-dependent app; the wizard offers to install the .NET 9 Desktop Runtime if missing. |

Release 1.0.0 named the self-contained package `-tam.exe` and the small one plainly; the in-app updater and the download page recognise both schemes.

What the script does:

1. `dotnet publish Studio\RobotWin.Studio\RobotWin.Studio.csproj -c Release -r win-x64` with `-p:Version=<version>` into `builds\installer\publish`.
2. Writes `version.h` and compiles `Installer\setup.cpp` + `setup.rc` with MSVC (`cl /std:c++20 /O2 /MT`) into a stub.
3. Runs `setup-stub.exe --pack <publish> <setup.exe>`: the published files are compressed with the Windows Compression API (LZMS) and appended to a copy of the stub.

## What the setup program does

`Installer/setup.cpp` is a native Win32 installer with no runtime dependencies.

| Mode | Command |
| --- | --- |
| Wizard (per user by default, or all users with elevation) | `RobotWinStudio-Setup-1.1.0.exe` |
| Silent, per user | `... /S` |
| Silent, all users | `... /S /ALLUSERS` |
| Custom folder | `... /S /D=<dir>` |
| Portable (unpack only, no shortcuts or registry) | `... /S /PORTABLE /D=<dir>` |
| Options | `/NODESKTOP` (no desktop shortcut), `/NOTYPES` (no file associations), `/LAUNCH` (start the app afterwards) |
| Uninstall | Windows *Apps* list, or `Uninstall.exe` in the install folder (`/S` for silent) |

Default folders: `%LOCALAPPDATA%\Programs\RobotWin Studio` (per user) or `%ProgramFiles%\RobotWin Studio` (all users). When the .NET 9 Desktop Runtime is missing, the wizard offers to download and install it from Microsoft (`aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe`). File associations are registered by running `RobotWinStudio.exe --register-file-types` (and removed with `--unregister-file-types`).

## Release workflow

`.github/workflows/release.yml` runs on every pushed tag matching `v*` (and manually via *workflow_dispatch* with an existing tag):

1. Checks out the tag on `windows-latest`, sets up .NET 9.
2. Runs `tools/build-installer.ps1 -Version <tag without v>` twice, normal and `-SelfContained`.
3. Uploads `RobotWinStudio-Setup-<version>.exe` (full) and `RobotWinStudio-Setup-<version>-lite.exe` to the GitHub release for that tag (`softprops/action-gh-release`).

## Release checklist

1. `main` is green in CI (`.github/workflows/ci.yml`: build, `dotnet test`, `--selftest`).
2. Add a `## [x.y.z] - YYYY-MM-DD` section to [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).
3. Optionally build the installer locally and smoke-test it (install, open an example, run a world, uninstall).
4. Tag and push:

   ```powershell
   git tag v1.1.0
   git push origin v1.1.0
   ```

5. When the workflow finishes, edit the release notes on GitHub (copy the changelog section) and mention that the lite package needs the .NET 9 Desktop Runtime while the full one carries it.
