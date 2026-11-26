// Receives on the USART by polling (its RX interrupt off) and reports every byte as hex with the receiver's
// flags of that byte: F = frame error (stop bit low), O = data overrun (a frame was lost before it), P = parity
// error. EEPROM byte 0 picks the format (0 = 8N1, 1 = 8E1, 2 = 8O1, 3 = 7E2), byte 1 the milliseconds the loop
// waits between looks: with 4, bytes that arrive back to back fill the receiver's two-byte buffer and the next
// ones overrun it, as on the real chip; with 0 it keeps up.
#include <EEPROM.h>

void setup() {
  const uint8_t formats[] = { SERIAL_8N1, SERIAL_8E1, SERIAL_8O1, SERIAL_7E2 };
  uint8_t f = EEPROM.read(0);
  Serial.begin(9600, formats[f < 4 ? f : 0]);
  UCSR0B &= ~_BV(RXCIE0);
  Serial.println("ok");
}

void loop() {
  uint8_t wait = EEPROM.read(1);
  if (wait) delay(wait);
  while (UCSR0A & _BV(RXC0)) {
    uint8_t flags = UCSR0A, data = UDR0;
    if (data < 16) Serial.print('0');
    Serial.print(data, HEX);
    if (flags & _BV(FE0)) Serial.print('F');
    if (flags & _BV(DOR0)) Serial.print('O');
    if (flags & _BV(UPE0)) Serial.print('P');
    Serial.print(' ');
  }
}
