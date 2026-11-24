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

## Other components

Add additional third-party notices here as dependencies are introduced.
