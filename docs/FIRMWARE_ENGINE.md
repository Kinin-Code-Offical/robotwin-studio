# FirmwareEngine

FirmwareEngine hosts the native firmware runtime for Arduino-style firmware.

## Board Profiles

Profiles define memory limits, clocks, bootloader reservation, and IO counts per board.

| Profile | MCU | Flash | SRAM | EEPROM | IO | Pins | Clock | Bootloader |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ArduinoUno | ATmega328P | 32 KB | 2 KB | 1 KB | 256 B | 20 | 16 MHz | 0.5 KB |
| ArduinoNano | ATmega328P | 32 KB | 2 KB | 1 KB | 256 B | 20 | 16 MHz | 0.5 KB |
| ArduinoProMini | ATmega328P | 32 KB | 2 KB | 1 KB | 256 B | 20 | 16 MHz | 0.5 KB |
| ArduinoMega | ATmega2560 | 256 KB | 8 KB | 4 KB | 512 B | 70 | 16 MHz | 8 KB |

Note: The firmware core emulates ATmega328P IO today; Mega is supported for memory limits and will clamp IO pins.

## Output

- `VirtualArduinoFirmware.exe` is built into `builds/firmware`.
- `compile_commands.json` is emitted into `builds/firmware` for tooling.

## Build

```powershell
python tools/rt_tool.py build-firmware
```

## Notes

- The firmware runtime communicates with CoreSim via serialization contracts.
- Protocol includes `board_id` routing and analog pin payloads.
- UART, ADC, and PWM/timer simulation are cycle-based and honor basic AVR register semantics.
- Keep platform-specific code isolated to this module.
