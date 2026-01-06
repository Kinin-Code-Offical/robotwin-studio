# Third-Party Notices

This project may bundle or integrate third-party software.

If you redistribute RobotWin Studio binaries that include any third-party components, you are responsible for complying with their licenses.

## QEMU

- Project: QEMU (Quick EMUlator)
- Homepage: https://www.qemu.org/
- Source: https://gitlab.com/qemu-project/qemu (official) and read-only GitHub mirrors may exist
- License: QEMU is open source; major portions are licensed under GPL-2.0-or-later, with some components under LGPL and other compatible licenses.
  - See the QEMU source tree for authoritative licensing information (e.g., `COPYING`, `LICENSES/`, per-file headers).

### Intended usage in RobotWin Studio

RobotWin Studio’s long-term plan includes running Raspberry Pi guests under QEMU as part of the FirmwareEngine product distribution.

If RobotWin Studio distributes a QEMU build (modified or unmodified):

- Include the relevant QEMU license texts in the distribution.
- Provide the corresponding source code for the exact QEMU version shipped (and your modifications, if any), in the manner required by the QEMU license(s).
- Keep a clear version identifier (commit/tag) for the shipped QEMU build.

## Special Thanks

- QEMU contributors

(Additional third-party notices can be added here as dependencies are introduced.)
