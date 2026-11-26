# Third-Party Notices

RobotWin Studio may bundle or integrate third-party software. If you redistribute binaries that include third-party components, you are responsible for complying with their licenses.

## QEMU

- Project: QEMU (Quick EMUlator)
- Homepage: https://www.qemu.org/
- Source: https://gitlab.com/qemu-project/qemu
- License: QEMU is open source. Major portions are GPL-2.0-or-later, with some components under LGPL and other compatible licenses.

If RobotWin Studio distributes QEMU binaries:

- Include the relevant license texts.
- Provide the corresponding source code for the exact version shipped (including modifications).
- Keep a clear version identifier (commit or tag) for the shipped build.

## Arduino compiler (bundled with the installers)

The installers and the portable app carry, unmodified, under `tools\arduino\`:

| Component | License | Source |
| --- | --- | --- |
| arduino-cli | GPL-3.0 | https://github.com/arduino/arduino-cli |
| Arduino AVR core (`arduino:avr`, the version is in `tools\arduino\data\packages\arduino\hardware\avr`) | LGPL-2.1 | https://github.com/arduino/ArduinoCore-avr |
| avr-gcc, GNU Binutils | GPL-3.0 (with the GCC runtime library exception) | https://github.com/arduino/toolchain-avr |
| AVR Libc | Modified BSD | https://github.com/avrdudes/avr-libc |
| AVRDUDE | GPL-2.0 | https://github.com/avrdudes/avrdude |
| Arduino library index (`library_index.json`) | data published by Arduino | https://downloads.arduino.cc/libraries/library_index.json |

They are downloaded at build time from arduino.cc by `tools/build-installer.ps1` exactly as Arduino publishes them; each package keeps its own license file. RobotWin Studio starts `arduino-cli.exe` as a separate program and does not link to it. The source of each component, for the exact version shipped, is at the links above (tags matching the versions in `tools\arduino\data`); on request we provide a copy of it.

## Adafruit CAD Parts (3D part models)

- Project: Adafruit_CAD_Parts
- Source: https://github.com/adafruit/Adafruit_CAD_Parts
- License: MIT, Copyright (c) 2016 Adafruit Industries
- Used in: `Studio/RobotWin.Studio/Models3D/Bundled/*.obj`, converted from these STEP files
  (tessellated, coloured, moved into each part's model frame; geometry otherwise unchanged):
  - `3777 TT Motor/3777 TT Motor.step` → `tt-motor.obj`, `tt-motor-shaft.obj`
  - `711 DC Toy Motor/711 DC Toy Motor.step` → `motor-130.obj`
  - `1143 Micro Servo - High Torque Metal Gear/1143 Micro Servo High Torque Metal Gear.step` → `servo-micro.obj`, `servo-micro-spline.obj`

```
MIT License

Copyright (c) 2016 Adafruit Industries

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The conversion was done offline with cascadio (MIT; uses Open CASCADE Technology, LGPL-2.1 with exception)
and trimesh (MIT). Neither tool ships with RobotWin Studio.

## Open CASCADE Technology (STEP / IGES exchange)

RobotWin Studio reads STEP, IGES and BREP files and writes STEP files through a thin bridge
(`occt/RobotWin.Occt.dll`, source in `CoreSim/native/occt/rw_occt.cpp`) to **Open CASCADE Technology 8.0.2**,
built from the official sources (https://github.com/Open-Cascade-SAS/OCCT, tag V8_0_2) by
`tools/scripts/build_occt.bat` with only the toolkits the bridge needs and no third-party libraries.
This product makes use of facilities provided by the Open CASCADE Technology software.

Open CASCADE Technology is licensed under the GNU Lesser General Public License version 2.1 with the
Open CASCADE exception (`third_party/occt/LICENSE_LGPL_21.txt`, `third_party/occt/OCCT_LGPL_EXCEPTION.txt`).
Its libraries ship as separate DLLs in the app's `occt` folder; you may replace them with your own build of the
same version (the bridge's source and build script are in this repository). Modelling, physics and drawing in
RobotWin do not use Open CASCADE.

The `occt` folder also carries the Microsoft Visual C++ runtime DLLs the bridge needs (msvcp140, msvcp140_2,
vcruntime140, vcruntime140_1), redistributed app-locally under the Visual Studio redistribution terms.

## .NET runtime (bundled with the installers)

The installer's `dotnet` component and the portable app carry, unmodified, Microsoft's .NET 10 runtime
(Microsoft.NETCore.App, Microsoft.WindowsDesktop.App and hostfxr) in the app's `dotnet\` folder, with its own
`LICENSE.txt` (MIT) and `ThirdPartyNotices.txt`. Source: https://github.com/dotnet/runtime, https://github.com/dotnet/wpf,
https://github.com/dotnet/winforms.

## NuGet packages

Every package the app, the simulation core and the headless server use, with its version and licence, is listed in
`docs/DEPENDENCIES.md`, generated from what the projects resolve; CI fails when it is out of date or a licence is not
on the allowed list. The shipped ones are under MIT (Vortice, SharpGen.Runtime, System.Text.Json and its dependencies
for the netstandard build).

## Other components

Add additional third-party notices here as dependencies are introduced.
