# Mega Pin Map Plan

Goal: correct Arduino Mega2560 pin mapping and IO behavior.

## Phase 1: Core mapping

- Define the full port and pin map for D0-D53 and A0-A15.
- Validate register layout for extended IO space.

## Phase 2: Timers and PWM

- Map timer channels to PWM-capable pins.
- Confirm timer register behavior in the emulator.

## Phase 3: Validation

- Add board-level tests for pin mapping.
- Run sample firmware sketches (blink, PWM, ADC, serial).

## Notes

- Mega2560 uses the `ATmega2560` core; keep port/DDR/PIN semantics consistent with the AVR IO model used by the firmware host.
- Treat pin mapping as a test-backed contract: changes should include fixture updates.
