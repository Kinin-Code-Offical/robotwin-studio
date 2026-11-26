// [neopixel-avr] A WS2812B driven from D6 the way Adafruit_NeoPixel drives it on a 16 MHz AVR: interrupts off and a
// cycle-counted loop per bit (high 5 cycles = 0.31 us for a 0, 12 cycles = 0.75 us for a 1, 17 cycles a bit), MSB first,
// G R B per LED; a low of more than 50 us latches. Written for RobotWin's tests.
static void ws2812Show(const uint8_t *p, uint16_t n) {
  uint8_t hi = PORTD | _BV(6), lo = PORTD & ~_BV(6);
  uint8_t b, bit;
  cli();
  while (n--) {
    b = *p++;
    asm volatile(
      "ldi %[bit], 8\n\t"
      "1:\n\t"
      "out %[port], %[hi]\n\t"
      "nop\n\tnop\n\tnop\n\t"
      "sbrs %[b], 7\n\t"
      "out %[port], %[lo]\n\t"
      "nop\n\tnop\n\tnop\n\tnop\n\tnop\n\tnop\n\t"
      "out %[port], %[lo]\n\t"
      "lsl %[b]\n\t"
      "dec %[bit]\n\t"
      "brne 1b\n\t"
      : [b] "+r"(b), [bit] "=&d"(bit)
      : [port] "I"(_SFR_IO_ADDR(PORTD)), [hi] "r"(hi), [lo] "r"(lo));
  }
  sei();
}

uint8_t pixels[9];

void setup() {
  Serial.begin(115200);
  pinMode(6, OUTPUT);
  digitalWrite(6, LOW);
  delay(1);
  pixels[0] = 0;   pixels[1] = 255; pixels[2] = 0;    // LED0 red
  pixels[3] = 128; pixels[4] = 0;   pixels[5] = 0;    // LED1 half green
  pixels[6] = 0;   pixels[7] = 0;   pixels[8] = 7;    // LED2 dim blue
  ws2812Show(pixels, 9);
  delay(5);
  Serial.println("shown 1");
  pixels[0] = 10; pixels[1] = 20; pixels[2] = 30;
  ws2812Show(pixels, 3);                              // a shorter frame: only LED0 changes
  delay(5);
  Serial.println("shown 2");
}

void loop() {}
