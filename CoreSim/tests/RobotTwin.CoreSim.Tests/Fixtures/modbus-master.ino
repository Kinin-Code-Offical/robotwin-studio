// Modbus RTU master in plain Arduino code (no library): 9600 baud 8E1 on Serial to a remote I/O module at
// address 1. It turns coil 0 (DO1) on, reads the discrete inputs back (DO1 is wired to DI3 on the module),
// turns coil 1 (DO2) on only if DI3 came back set, sends a frame with a broken CRC and one to address 7
// (both must go unanswered), writes register 4 by broadcast and reads it back, and finally leaves its
// tally in the module's own holding registers: 1 = answers that held, 2 = answers that failed,
// 3 = silences that were right (bad CRC, other address), 5 = the broadcast value read back.

uint8_t rx[64];
uint16_t good = 0, bad = 0, silent = 0;

uint16_t crc16(const uint8_t *b, uint8_t n) {
  uint16_t c = 0xFFFF;
  for (uint8_t i = 0; i < n; i++) {
    c ^= b[i];
    for (uint8_t k = 0; k < 8; k++) c = (c & 1) ? (c >> 1) ^ 0xA001 : c >> 1;
  }
  return c;
}

// Sends a request (CRC appended unless broken), waits for the answer; its length, 0 = no answer, -1 = CRC wrong.
int transact(uint8_t *req, uint8_t n, bool breakCrc) {
  uint16_t c = crc16(req, n);
  req[n] = c & 0xFF;
  req[n + 1] = c >> 8;
  if (breakCrc) req[n] ^= 0x55;
  while (Serial.available()) Serial.read();
  Serial.write(req, n + 2);
  Serial.flush();
  uint8_t got = 0;
  unsigned long start = millis(), last = micros();
  while (millis() - start < 100) {
    if (Serial.available()) {
      rx[got++] = Serial.read();
      last = micros();
      if (got >= sizeof rx) break;
    } else if (got > 0 && micros() - last > 4000) break;   // 3.5 characters of silence at 9600 baud
  }
  if (got == 0) return 0;
  if (got < 5) return -1;
  uint16_t rc = crc16(rx, got - 2);
  if ((rx[got - 2] | (rx[got - 1] << 8)) != rc) return -1;
  return got;
}

void tally(int r) { if (r > 0 && !(rx[1] & 0x80)) good++; else bad++; }

void setup() {
  Serial.begin(9600, SERIAL_8E1);
  delay(20);
  uint8_t q[12];

  uint8_t a[] = {1, 5, 0, 0, 0xFF, 0, 0, 0};          // coil 0 on
  tally(transact(a, 6, false));
  delay(20);                                          // let DO1 reach DI3 through the cable

  uint8_t b[] = {1, 2, 0, 0, 0, 8, 0, 0};             // read discrete inputs 0-7
  int r = transact(b, 6, false);
  tally(r);
  uint8_t inputs = r > 0 ? rx[3] : 0;

  uint8_t c[] = {1, 5, 0, 1, (uint8_t)((inputs & 4) ? 0xFF : 0), 0, 0, 0};   // coil 1 = DI3
  tally(transact(c, 6, false));

  uint8_t d[] = {1, 3, 0, 0, 0, 1, 0, 0};             // broken CRC: no answer
  if (transact(d, 6, true) == 0) silent++;
  uint8_t e[] = {7, 3, 0, 0, 0, 1, 0, 0};             // another address: no answer
  if (transact(e, 6, false) == 0) silent++;

  uint8_t f[] = {0, 6, 0, 4, 0x12, 0x34, 0, 0};       // broadcast: register 4 = 0x1234, no answer
  if (transact(f, 6, false) == 0) silent++;
  delay(5);
  uint8_t g[] = {1, 3, 0, 4, 0, 1, 0, 0};             // read register 4 back
  r = transact(g, 6, false);
  tally(r);
  uint16_t back = r > 0 ? (rx[3] << 8 | rx[4]) : 0;

  uint8_t h[] = {1, 16, 0, 1, 0, 3, 6, (uint8_t)(good >> 8), (uint8_t)good, (uint8_t)(bad >> 8), (uint8_t)bad, (uint8_t)(silent >> 8), (uint8_t)silent, 0, 0};
  transact(h, 13, false);                             // registers 1-3 = the tally
  uint8_t k[] = {1, 6, 0, 5, (uint8_t)(back >> 8), (uint8_t)back, 0, 0};
  transact(k, 6, false);                              // register 5 = what the broadcast left
}

void loop() {}
