# Building the installer and releasing

RobotWin Studio ships as **one setup program**, `RobotWinStudio-Setup-<version>.exe`, and the components it installs. The setup is small (about half a megabyte): it carries the list of its release's components with their sizes and SHA-256, and takes the chosen ones from the GitHub release, from its own folder (offline) or from another source. `tools/build-installer.ps1` produces all of it from the WPF app and the native installer in `Installer/`.

| Component | File | Installed as |
| --- | --- | --- |
| RobotWin Studio (always) | `RobotWinStudio-<version>-app.rwpack` | the app, framework-dependent |
| .NET 10 runtime | `RobotWinStudio-<version>-dotnet.rwpack` | `dotnet\` beside the app: the Microsoft.NETCore.App and Microsoft.WindowsDesktop.App runtimes and their host |
| Arduino compiler | `RobotWinStudio-<version>-arduino.rwpack` | `tools\arduino`: arduino-cli, the AVR core and the library list |
| STEP / IGES support | `RobotWinStudio-<version>-occt.rwpack` | `occt\`: the Open CASCADE bridge and its licence |

One build of the app serves every kind of installation: its exe looks for .NET in its own `dotnet\` folder first, then `DOTNET_ROOT`, then the PC's installed .NET (`AppHostDotNetSearch` / `AppHostRelativeDotNet` in `RobotWin.Studio.csproj`).

## Build the installer locally

Requirements: .NET 10 SDK (with the .NET 10 Desktop Runtime, which the Windows SDK has) and Visual Studio 2022 / Build Tools with the C++ workload (the script locates it with `vswhere`).

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.7.0
# quicker, without the Arduino compiler (downloaded from arduino.cc on the first build):
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.7.0 -NoArduino
```

Output in `builds\installer\release\`: the setup and the `.rwpack` files. What the script does:

1. `dotnet publish` of the app for `win-x64`, framework-dependent, ReadyToRun, `-p:Version=<version>`.
2. Lays out each component's folder in `builds\installer\components\<id>`: the app without `occt\`; the Open CASCADE bridge with its LGPL licence; this machine's .NET 10 runtime (host\fxr and the two shared frameworks of one version, with Microsoft's licence notes); the Arduino compiler, downloaded once into `builds\installer\arduino-ready` and proven by compiling a blink sketch.
3. Compiles `Installer\setup.cpp` with MSVC (`cl /std:c++20 /O2 /MT`) and packs every component with it (`setup.exe --pack <folder> <file.rwpack>`: each file compressed with the Windows Compression API's LZMS, kept as is when that does not help). It writes the components' sizes and SHA-256 into `components.h` and compiles the setup again with them: a setup only installs its own release's files.
4. Proves the result: the setup installs every component from the release folder into a portable folder (`/S /PORTABLE /TYPE=full /SOURCE=<release>`), and every file must arrive with its size. `-NoCheck` skips it.

`-Repo owner/name` sets the GitHub repository the setup downloads from (the release workflow passes its own).

## What the setup program does

`Installer/setup.cpp` is a native Win32 program with no runtime dependencies. Its wizard has three questions:

1. **Language and kind of installation.** The language is the wizard's and the app's (it starts at this user's app language). **Full** installs every component (everything works offline at once), **Lite** the app alone on the PC's .NET 10 Desktop Runtime (its own copy of .NET only when the PC has none), **Custom** the components ticked on the next page.
2. **Components** (Custom only), each with its download size.
3. **Where and how:** for this user, for all users (the setup restarts itself as administrator and shows its own progress) or **portable** (a folder only: no shortcuts, registry entries or file types); the folder; desktop shortcut, file types, start afterwards. The source line says where the files come from.

Then it downloads what it needs (WinHTTP: the system's proxy, redirects, a broken download goes on where it stopped, six attempts), checks each file's SHA-256 before anything in the installation changes, unpacks, removes what the previous installation had and this one does not (its `install.log`), and writes:

| File | Purpose |
| --- | --- |
| `Uninstall.exe` | the setup itself: removes the installation, or with `/MODIFY` opens the wizard on it (add or remove components; the ones that stay are not downloaded again) |
| `install.json` | the version, kind, components, language and where it went; the app reads it (the language is the default of users who start it for the first time) |
| `install.log` | every installed file, under its component |

An installation recorded in *Apps* (Windows) is found again: a newer setup updates it in place with the same kind and components (a full one gets every component of the new release), and *Apps → Modify* opens `Uninstall.exe /MODIFY`. So does **Settings → Updates → Change components…** in the app. The downloads are kept in `%TEMP%\RobotWinStudio-Setup-<version>` until the installation succeeds; the log of the last run is `%TEMP%\RobotWinStudio-Setup.log`.

| Command line | |
| --- | --- |
| `RobotWinStudio-Setup-1.7.0.exe` | wizard |
| `... /S` | silent; `/PASSIVE`: no questions, progress window only (the in-app updater uses it) |
| `/TYPE=full`, `/TYPE=lite`, `/COMPONENTS=app,arduino,occt` | the kind, or exactly these components (`app` is always installed, `dotnet` too when the PC has no .NET 10) |
| `/LANG=en`, `/LANG=tr` | the app's language |
| `/D=<folder>`, `/ALLUSERS`, `/PORTABLE` | where |
| `/NODESKTOP`, `/NOTYPES`, `/LAUNCH` | no desktop shortcut, no file types, start the app afterwards |
| `/SOURCE=<folder or URL>` | take the components from a folder (offline, a network share) or a mirror instead of GitHub |
| `/KEEP` | keep the downloaded components |

Offline installation: put the `.rwpack` files next to the setup (or anywhere, with `/SOURCE=`); each one found with the right size is checked and used, the rest are downloaded. Default folders: `%LOCALAPPDATA%\Programs\RobotWin Studio` (this user), `%ProgramFiles%\RobotWin Studio` (all users), `RobotWin Studio` beside the setup (portable). File associations are registered by running `RobotWinStudio.exe --register-file-types` (and removed with `--unregister-file-types`).

## Release workflow

`.github/workflows/release.yml` runs on every pushed tag matching `v*` (and manually via *workflow_dispatch* with an existing tag):

1. **windows** (`windows-latest`): runs `tools/build-installer.ps1 -Version <tag without v> -Repo <this repository>` (which ends with the install check), installs the full set as a portable copy with the setup itself and zips it as `RobotWinStudio-<version>-win-x64-portable.zip`, starts that copy with `--selftest` and checks from the .NET host's trace that it ran on its own `dotnet\` folder, and publishes `tools/RobotWin.Headless` for `win-x64` as `RobotWin-Headless-<version>-win-x64.zip` after a one-second batch run of the car example.
2. **linux** (`ubuntu-latest`): publishes the headless simulator for `linux-x64` (self-contained), runs the same batch run on Linux and packs `RobotWin-Headless-<version>-linux-x64.tar.gz`; writes the source at the tag as `RobotWinStudio-<version>-source.zip` and `.tar.gz` (`git archive`).
3. **publish**: collects both jobs' files, writes `SHA256SUMS.txt` (`sha256sum`) and attaches everything to the GitHub release for that tag (`softprops/action-gh-release`).

| File | Contents |
| --- | --- |
| `RobotWinStudio-Setup-<version>.exe` | The Windows x64 installer: full, lite or custom; this user, all users or portable |
| `RobotWinStudio-<version>-app.rwpack`, `-dotnet.rwpack`, `-arduino.rwpack`, `-occt.rwpack` | The components the setup downloads (or takes from its folder) |
| `RobotWinStudio-<version>-win-x64-portable.zip` | The full app as a folder: unzip and run |
| `RobotWin-Headless-<version>-win-x64.zip` | Headless simulator and automation server, Windows |
| `RobotWin-Headless-<version>-linux-x64.tar.gz` | Headless simulator and automation server, Linux (ESP32 boards run without firmware there: sketches compile to Windows DLLs) |
| `RobotWinStudio-<version>-source.zip`, `.tar.gz` | The source at the tag |
| `SHA256SUMS.txt` | SHA-256 of every file |

The in-app updater downloads the setup and runs it on the installed copy (`/S /PASSIVE`, the same folder, kind and choices); the setup then downloads the components that copy has. For a release with components it shows the setup plus those components as the update's size. Releases up to 1.6 had two installers (the full one, plain or `-tam.exe`, and `-lite.exe`); the updater still recognises them.

## Release checklist

1. `main` is green in CI (`.github/workflows/ci.yml`: build, `dotnet test`, `--selftest`).
2. Add a `## [x.y.z] - YYYY-MM-DD` section to [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).
3. Optionally build the installer locally and smoke-test it (install, open an example, run a world, change the components, uninstall).
4. Tag and push:

   ```powershell
   git tag v1.7.0
   git push origin v1.7.0
   ```

5. When the workflow finishes, edit the release notes on GitHub (copy the changelog section): one file to download, `RobotWinStudio-Setup-<version>.exe`; the `.rwpack` files are what it downloads, and with them beside it the setup installs offline.
