# Mega Pin Map Plan

Goal: provide real Arduino Mega2560 pin mapping and IO behavior without clamping to ATmega328P.

Phase 1: Core architecture
Done:

- AVR IO read/write now supports 16-bit addresses, enabling extended IO space in the core.
- Mega IO register map constants are defined (PORTA-L, DDRx, PINx).
- BoardProfile now exposes Mega pin count without clamping.
  Next:
- Add ATmega2560-specific core behaviors (USART1-3 ISR vectors, Timer3/4/5).

Phase 2: Pin mapping
Done:

- Mega pin map table (D0-D53, A0-A15) to PORT/bit mapping.
- VirtualMcuHal pin maps auto-generate from BoardProfiles for Uno/Mega.
  Next:
- Extend mapping metadata for PWM/Timer/SPI/I2C labels.

Phase 3: Timers/ADC/Interrupts
In progress:

- ADC channel count = 16 (analog inputs present; needs mux mapping for Mega).
- UART1-3 data registers wired with cycle simulation; ISR vectors pending.
  Next:
- Implement Timer3/4/5 for ATmega2560 (registers + PWM on D2-D13, D44-D46).
- Add ISR vectors for USART1-3 and extended PCINT/EXTINT.

Phase 4: Validation
Next:

- Add board-level tests for Mega pin mapping (read/write expected ports).
- Run sample firmware sketches: blink, PWM, ADC, serial across multiple ports.

Risks

- IO register map is larger; ensure memory alignment in core emulation.
- Vector table differs from ATmega328P; update ISR addresses.
