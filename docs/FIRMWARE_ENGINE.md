# FirmwareEngine

FirmwareEngine runs firmware emulation and optional guest OS virtualization. It bridges virtual hardware to the simulated world.

## Targets

- MCU class boards (AVR profiles such as Arduino Uno and Mega).
- Linux guest images through QEMU for board-class targets.

## Responsibilities

- Execute firmware on a fixed time step.
- Expose virtual GPIO, UART, I2C, SPI, PWM, and ADC.
- Bridge device state to CoreSim over IPC.

## Runtime notes

- FirmwareEngine uses the `RTFW` protocol over a named pipe (see `FirmwareEngine/Protocol.h` and `CoreSim/src/RobotTwin.CoreSim/IPC/FirmwareProtocol.cs`).
- A Raspberry Pi backend exists under `FirmwareEngine/Rpi/` and can be smoke-tested via `python tools/rt_tool.py rpi-smoke`.

## Process modes

- Default: lockstep mode (waits for explicit step commands over the pipe).
- Realtime mode: steps boards based on wall-clock time (see `--realtime`).

Lockstep tracing can be enabled with either:

- `--trace-lockstep`
- `RTFW_LOCKSTEP_TRACE=1` (environment variable)

## Supported MCU profiles (host-side)

The host explicitly supports these MCU cores for stepping:

- `ATmega328P` (Arduino Uno-class)
- `ATmega2560` (Arduino Mega2560-class)

## Command-line arguments

Arguments are parsed in `FirmwareEngine/main.cpp`:

- `--pipe <name>`: named pipe name (default `RoboTwin.FirmwareEngine`).
- `--hz <cpu_hz>`: default MCU clock rate (default 16 MHz).
- `--lockstep`: force lockstep mode.
- `--realtime`: realtime stepping mode.
- `--log <path>`: append log output to a file.
- `--self-test`: run a built-in peripheral self-test and exit.
- `--trace-lockstep`: verbose lockstep logging.
- `--ide-com <COMx>`: enable STK500 bridge on a COM port.
- `--ide-board <id>`: board identifier for IDE bridge (default `board`).
- `--ide-profile <profile>`: board profile for IDE bridge (default `ArduinoUno`).

Raspberry Pi backend flags:

- `--rpi-enable`
- `--rpi-allow-mock`
- `--rpi-qemu <path>`
- `--rpi-image <path>`
- `--rpi-shm-dir <dir>`
- `--rpi-display <WxH>`
- `--rpi-camera <WxH>`
- `--rpi-net-mode <nat|...>`
- `--rpi-log <path>`
- `--rpi-cpu-affinity <mask>`
- `--rpi-cpu-max-percent <percent>`
- `--rpi-threads <count>`
- `--rpi-priority <class>`

## Arduino IDE (optional)

For serial workflows, the project includes a helper to create virtual COM pairs (com0com):

- Install/create ports: `python tools/rt_tool.py setup --install-com0com`

## Build

- Use `python tools/rt_tool.py build-firmware`.
- Outputs land in `builds/firmware/`.

## Debugging

- Serial output is available through the firmware host logs.
- Optional GDB stub support for firmware debugging (when enabled in the host).
