// RobotWin Studio örneği: potansiyometre (A0) değerini 16x2 I2C LCD'de metin,
// 128x64 SSD1306 OLED'de büyük rakam + kayan grafik olarak gösterir.
// Yalnızca Arduino çekirdeğiyle gelen Wire kütüphanesini kullanır (ek kütüphane yok).
#include <Wire.h>

// ------------------------------------------------------------------ LCD (PCF8574 @ 0x27)
#define LCD_ADDR 0x27
#define LCD_BL 0x08
#define LCD_EN 0x04
#define LCD_RS 0x01

static void lcdExpander(uint8_t data) {
  Wire.beginTransmission(LCD_ADDR);
  Wire.write(data | LCD_BL);
  Wire.endTransmission();
}

static void lcdNibble(uint8_t value) {
  lcdExpander(value);
  lcdExpander(value | LCD_EN);
  delayMicroseconds(1);
  lcdExpander(value & ~LCD_EN);
  delayMicroseconds(50);
}

static void lcdSend(uint8_t value, uint8_t mode) {
  lcdNibble((value & 0xF0) | mode);
  lcdNibble(((value << 4) & 0xF0) | mode);
}

static void lcdCommand(uint8_t c) { lcdSend(c, 0); }

static void lcdBegin() {
  delay(50);
  lcdExpander(0);
  delay(100);
  lcdNibble(0x30); delayMicroseconds(4500);
  lcdNibble(0x30); delayMicroseconds(4500);
  lcdNibble(0x30); delayMicroseconds(150);
  lcdNibble(0x20);
  lcdCommand(0x28);           // 4 bit, 2 satır, 5x8
  lcdCommand(0x0C);           // ekran açık, imleç kapalı
  lcdCommand(0x01); delay(2); // temizle
  lcdCommand(0x06);           // soldan sağa
}

static void lcdAt(uint8_t col, uint8_t row) { lcdCommand(0x80 | (col + (row ? 0x40 : 0))); }

static void lcdPrint(const char *s) {
  while (*s) lcdSend((uint8_t)*s++, LCD_RS);
}

// ------------------------------------------------------------------ OLED (SSD1306 @ 0x3C)
#define OLED_ADDR 0x3C
static uint8_t frame[128 * 64 / 8];
static uint8_t history[128];

static void oledCommands(const uint8_t *c, uint8_t n) {
  Wire.beginTransmission(OLED_ADDR);
  Wire.write((uint8_t)0x00);
  while (n--) Wire.write(*c++);
  Wire.endTransmission();
}

static void oledBegin() {
  static const uint8_t init[] = {0xAE, 0xD5, 0x80, 0xA8, 63, 0xD3, 0x00, 0x40, 0x8D, 0x14,
                                 0x20, 0x00, 0xA1, 0xC8, 0xDA, 0x12, 0x81, 0xCF, 0xD9, 0xF1,
                                 0xDB, 0x40, 0xA4, 0xA6, 0x2E, 0xAF};
  oledCommands(init, 15);
  oledCommands(init + 15, sizeof(init) - 15);
}

static void oledShow() {
  static const uint8_t window[] = {0x22, 0, 7, 0x21, 0, 127};
  oledCommands(window, sizeof(window));
  Wire.setClock(400000);
  for (uint16_t i = 0; i < sizeof(frame); i += 16) {
    Wire.beginTransmission(OLED_ADDR);
    Wire.write((uint8_t)0x40);
    for (uint8_t k = 0; k < 16; k++) Wire.write(frame[i + k]);
    Wire.endTransmission();
  }
  Wire.setClock(100000);
}

static void px(uint8_t x, uint8_t y) {
  if (x < 128 && y < 64) frame[x + (y >> 3) * 128] |= (uint8_t)(1 << (y & 7));
}

// 3x5 rakam yazı tipi (her rakam 15 bit, satır satır)
static const uint16_t digits[10] = {0x7B6F, 0x2C97, 0x73E7, 0x73CF, 0x5BC9, 0x79CF, 0x79EF, 0x7249, 0x7BEF, 0x7BCF};

static void bigDigit(uint8_t x, uint8_t y, uint8_t d, uint8_t s) {
  for (uint8_t r = 0; r < 5; r++)
    for (uint8_t c = 0; c < 3; c++)
      if (digits[d] & (1 << (14 - (r * 3 + c))))
        for (uint8_t dy = 0; dy < s; dy++)
          for (uint8_t dx = 0; dx < s; dx++) px(x + c * s + dx, y + r * s + dy);
}

static void drawFrame(int value) {
  memset(frame, 0, sizeof(frame));
  // Büyük değer: 4 hane, ölçek 3
  int v = value;
  for (int8_t i = 3; i >= 0; i--) {
    bigDigit(4 + i * 12, 1, v % 10, 3);
    v /= 10;
  }
  // Çubuk gösterge
  uint8_t bar = (uint8_t)((long)value * 72 / 1023);
  for (uint8_t x = 0; x < 74; x++) { px(52 + x, 2); px(52 + x, 13); }
  for (uint8_t y = 2; y <= 13; y++) { px(52, y); px(125, y); }
  for (uint8_t x = 0; x < bar; x++)
    for (uint8_t y = 4; y <= 11; y++) px(53 + x, y);
  // Ayırıcı çizgi ve kayan grafik
  for (uint8_t x = 0; x < 128; x += 2) px(x, 18);
  for (uint8_t x = 1; x < 128; x++) {
    uint8_t a = 63 - history[x - 1], b = 63 - history[x];
    uint8_t lo = a < b ? a : b, hi = a < b ? b : a;
    for (uint8_t y = lo; y <= hi; y++) px(x, y);
  }
}

// ------------------------------------------------------------------ program

void setup() {
  Serial.begin(9600);
  Wire.begin();
  lcdBegin();
  oledBegin();
  lcdAt(0, 0);
  lcdPrint("RobotWin Studio");
  Serial.println(F("LCD + OLED hazir"));
}

void loop() {
  int value = analogRead(A0);
  long mv = (long)value * 5000 / 1023;

  char line[17];
  snprintf(line, sizeof(line), "A0=%4d  %ld.%02ldV", value, mv / 1000, (mv % 1000) / 10);
  lcdAt(0, 1);
  lcdPrint(line);

  memmove(history, history + 1, sizeof(history) - 1);
  history[127] = (uint8_t)((long)value * 43 / 1023); // 0..43 piksel
  drawFrame(value);
  oledShow();

  Serial.println(line);
  delay(60);
}
