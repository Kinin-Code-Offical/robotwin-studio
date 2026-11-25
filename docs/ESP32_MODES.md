# The two ESP32 run modes

The same ESP32 sketch runs two ways: today's **fast simulation** and a **hardware-compatible build** that can be flashed to a real board. For the second to run in the simulation too, we need our own emulator that executes real Xtensa instructions; changing the toolchain alone does not bring hardware emulation. This page sets out the plan, how it ties into today's code and the stages. Progress is tracked in the [roadmap](gelistirici/YOL_HARITASI.md).

## How it works today

| Topic | Today |
| --- | --- |
| Compile | `ArduinoSketchPreprocessor` turns the `.ino` into C++; MSVC (`cl.exe /LD`) builds it into a Windows x64 DLL against `rw_esp32hal.lib` (`CoreSim/.../Firmware/Esp32/Esp32SketchCompiler.cs`). The HAL sources live in `CoreSim/native/esp32hal/`; the library is cached by hash. |
| Load | The DLL is opened in-process with `LoadLibraryW` (`Esp32Native.cs`). Contract: `rw_init`, `rw_run_until`, `rw_pin_changed`, `rw_serial_rx` …, ABI version 2 (`rw_host_api.h`). |
| Coverage | GPIO, interrupts, ADC/DAC/touch, LEDC/analogWrite/tone, timers, Serial 0–2, Wire 0–1, SPI, Wi-Fi (TCP/UDP/server), BLE, Bluepad32, FreeRTOS (tasks, queues, semaphores, notifications), task watchdog, camera, EEPROM, deep sleep. No ESP-NOW. |
| Wiring | Every GPIO is bound to the board's `DigitalPin` (`Esp32CoSimulation.cs`); I2C through `I2cBus`, SPI through `SpiBus`, radio through `Esp32RadioBridge`. |
| Time | Lockstep with simulation time: the world calls `rw_run_until` in 100 µs slices. No instruction counting; each HAL call "spends" a fixed time. `getCycleCount` = uptime × MHz. |
| Limits | No instruction-level timing; a 64-bit Windows memory model (8-byte pointers, no 520 KB SRAM limit); none of ESP-IDF's own behaviour (caches, flash, partitions, interrupt priorities); in headless runs on Linux the board runs without firmware. |

We also have an emulator to follow: the cycle-counted ATmega328P (`Firmware/Atmega328P*.cs`: decoder, timers, PWM, UART, ADC, TWI, interrupts; `FirmwareCoSimulation` turns a slice into cycles; `Debug/` reads ELF/DWARF and debugs).

## Mode 1: fast simulation (today's, stays)

- MSVC + RobotWin's Arduino HAL, an in-process DLL. The default.
- Strengths: robotic scenes with many ESP32s (drones, vehicle fleets, radio networks), compiles in seconds, the full API including Wi-Fi/BLE/gamepads.
- To add: ESP-NOW; pointer-size and memory-budget warnings (the sketch's static memory, measured with Mode 2's compiler, is shown, to catch arrays that would overflow on the real board).

## Mode 2: hardware-compatible build

- The sketch is compiled with Espressif's Arduino-ESP32 core (a pinned version, ESP-IDF 5.x underneath) by the real `xtensa-esp32-elf-gcc`. Output: `bootloader.bin`, `partitions.bin`, the application `.bin`, a merged `.bin` and the `.elf` with symbols.
- The toolchain is installed by the `arduino-cli` that ships with RobotWin **into RobotWin's own data folder** (next to `tools/arduino/data`, without touching the user's Arduino IDE). It is downloaded on first use (an approved download), checked by SHA-256, and its version is stored with the project (the same project compiles with the same compiler years later).
- **Upload to board:** pick the board's COM port; the `esptool` that comes with the core writes it. A merged `.bin` is exported for web flashers.
- Compile errors map to `.ino` lines as in Mode 1 (the preprocessor is shared).
- A board set to Mode 2 says plainly when the compiler is not installed or the emulator does not support something yet; it never falls back to Mode 1 silently.

## Mode 2 in the simulation: RobotWin's Xtensa emulator

To run the real firmware in the simulation, we write our own emulator of the ESP32's instructions (not QEMU: the engines are ours; outside tools serve only as compiler and uploader). It follows the AVR emulator's layout.

| Layer | Contents |
| --- | --- |
| Core | Xtensa LX6 with the ESP32's options: a 64-register windowed register file (CALL4/8/12, ENTRY, RETW, window overflow/underflow exceptions), dense (16-bit) instructions, MUL16/MUL32/MULH, DIV32, the single-precision FPU, boolean registers, zero-overhead loops (LOOP), NSA, MIN/MAX/CLAMPS/SEXT, S32C1I (FreeRTOS's atomics), interrupt levels 1–5 + NMI, CCOUNT/CCOMPARE. Cycles counted per instruction. |
| Chip (SoC) | Two cores (PRO_CPU, APP_CPU) sharing memory; IRAM/DRAM (520 KB), the flash cache mapping (IROM/DROM, MMU), RTC memory, the interrupt matrix, eFuse (MAC). Boot: the application `.elf`'s sections are loaded directly and `call_start_cpu0` runs; the mask ROM's functions in use come from the ROM ELF shipped with the toolchain, or are emulated at a high level (HLE). |
| Peripherals | At register level: the GPIO matrix and IO_MUX, UART0–2, LEDC, MCPWM, timer groups and watchdogs, I2C0–1, SPI2–3 (the flash SPI as backing store), SAR ADC1–2, DAC, RMT, PCNT, touch. Each connects to today's CoreSim devices (`DigitalPin`, `I2cBus`/`II2cDevice`, `SpiBus`/`ISpiDevice`): the circuit and world sides need no change. |
| Radio | The closed PHY/MAC code of Wi-Fi/BLE cannot be meaningfully emulated. The `esp_wifi_*`, `esp_netif`/lwIP socket and `esp_bt_*` entry points are found through the ELF symbol table, trapped and connected to the `Esp32RadioBridge` Mode 1 uses (the one part not at register level, and documented as such). |
| Time | Locked to 100 µs slices as in Mode 1; slice × 240 MHz cycles per core. FreeRTOS drives its own scheduler from the real tick interrupt. `WAITI` (idle wait) skips to the next interrupt. |
| Debugging | The existing ELF/DWARF reader and debugger: breakpoints, line stepping, register and memory views, each core on its own. |
| Speed | A C# interpreter with a decoded-instruction cache. Firmware with busy loops may run below real time, so Mode 2 is for validation and testing one board in depth; scenes with many boards stay in Mode 1. Boards in one world choose their mode each (ten boards fast, the board under test emulated). |

### Measured: what a real sketch uses from the ROM

The ELF of the `esp32-dimmer` sketch built with Arduino-ESP32 3.3.13: 7 loadable segments (DROM `0x3F400020`, RTC fast memory, DRAM `0x3FFBDB60`, IRAM `0x40080000`, IROM `0x400D0020`, RTC slow memory), entry `0x40081B08` (`call_start_cpu0`). Its code refers to only 12 of the mask ROM's 347 symbols: libgcc's double-precision helpers (`__adddf3`, `__subdf3`, `__divdf3`, `__extendsfdf2`, `__fixdfsi`), the RF calibration's `esp_rom_regi2c_read_mask`/`write`, and newlib's `open/read/write/close` stubs. It never uses the FPU's divide helper instructions (DIV0.S …). So no ROM image is needed: these functions are trapped at their addresses, read from the toolchain's `esp32.rom*.ld` files, and served in C#.

## Validation

- **Instruction tests:** test firmware compiled with the real compiler that prints its own "PASS/FAIL" over UART, one per instruction group.
- **Mode difference report:** every ESP32 template runs the same time in both modes; serial output and pin traces (`GoldenTraceHarness`) are compared, with the differences and their causes reported.
- **Against a real board (later):** the same firmware running on a real board has its serial output and chosen pins recorded over USB and compared with the emulator's trace.

## Hardware in the loop (HIL, P2)

A real ESP32 becomes part of the simulation: RobotWin connects its UART and chosen pins (through a second "RobotWin probe" board or an I/O expander) to the world; sensor values flow from the simulation to the board and outputs from the board to the simulation. It needs hardware, so it comes after the emulator.

## Interface

- In the board's inspector, **Run mode:** *Fast simulation* / *Hardware-compatible (ESP32 emulator)*. Stored per board in the project and the world file.
- On the code page, in both modes, **Build for the board** (`.bin`) and **Upload to board** (port choice).
- The status bar and event log say which engine runs; in Mode 2 the memory use (IRAM/DRAM/flash) is shown.

## Status (stage 3 done, stage 4 under way)

The `esp32-dimmer` firmware built for the real chip by Arduino-ESP32 3.3.13 (unchanged: its `.elf` and merged `.bin`) boots on our own emulator: FreeRTOS on both cores, the IPC tasks, `setup()` prints "ESP32 dimmer hazir" over Serial, `loop()` reads the pot through the ADC, drives the LEDC at 5 kHz and prints `pot=1058  1000 mV  parlaklik= 25%` every 200 ms (1.0 V on IO34). What getting there turned up: FreeRTOS's dual-core `yield` rests on the cross-core interrupt (DPORT `FROM_CPU_INTR`); the UART FIFO is written through its AHB alias (`0x60000000` = `0x3FF40000`); esp_timer counts on the timer group's LAC counter and sets its divider from the APB clock at start-up (which is why the bootloader's 80 MHz matters); the LEDC driver waits for the hardware to clear `DUTY_START`; `analogReadMilliVolts` rests on the factory Vref in the eFuse.

The ADC converts through Espressif's characterisation inverted (the line and the 11 dB curve IDF calibrates with): it gives the codes a real chip gives, and the calibrated voltage equals the pad's. Fast mode's HAL uses the ideal line (1.0 V → 1241); that difference between the modes goes into stage 6's report.

### Stage 4: peripherals and the sessions

A hardware-compatible board now runs on the emulator on the circuit page and in the world (`IEsp32Board`, `Esp32EmulatedBoard`): the real-chip build is cached by the source's hash and built in the background on the first run. Modelled at register level: the GPIO matrix and IO_MUX, LEDC, the SAR ADCs (Espressif's characterisation), the DAC, the timer groups and their watchdogs, UART0–2, I2C0/1, SPI2/3, the eFuse, deep sleep, the interrupt matrix and cross-core interrupts, one core stalling the other. Checked with real firmware: the dimmer (ADC, LEDC), an echo (UART both ways), I2C (scan, register write and read, NACK), SPI (byte, word, buffer), a hung sketch (the interrupt watchdog's panic and restart), deep sleep (timer wake, RTC memory), the DAC, `attachInterrupt` (every one of ten edges). Six of the seven ESP32 world templates run on the emulator; sketches that use the camera, Wi-Fi or Bluetooth run as the fast mode with the reason logged. The RMT sends WS2812 frames to the strips wired to the board (`rgbLedWrite`, `rmtWrite`: checked byte for byte), and the touch pads read the board's touch values. The pulse counter (PCNT) counts edges and decodes quadrature encoders (checked with IDF's `pulse_cnt` driver on the chip's own PWM). Light sleep halts both cores and the APB clock until the timer, EXT0 or a GPIO level wakes the chip. The MCPWM drives half bridges with dead time (IDF's `mcpwm_prelude` driver, in both modes). Left: MCPWM capture, fault and sync; Wi-Fi/BLE in stage 5.

## Stages

1. The run-mode setting (data model, file format, inspector) + toolchain manager + hardware build + upload to board.
2. The Xtensa LX6 core and instruction tests.
3. The chip: memory map, ELF loading, ROM, two cores, interrupt matrix, FreeRTOS tick → Blink and Serial.
4. Peripherals → every template without radio.
5. Wi-Fi/BLE trapping → the radio templates.
6. Mode difference report, speed (idle skip, decoded-block cache), docs. The report is in: `--esp32-compare report.md`; the six comparable templates agree in both modes (the differences it found are fixed: the RMT, boot time, `millis()`'s origin, tick-aligned `delay()`, servo ticks, the LEDC update wait). Speed is next.
7. (P2) Hardware in the loop.
