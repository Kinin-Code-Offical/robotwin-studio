// SSD1306 128x64 over I2C (0x3C) driven by hand with the bundled Wire library,
// following the Adafruit_SSD1306 begin()/display() byte sequences.
// Fixture for RobotTwin.CoreSim I2cDeviceTests (compiled hex lives next to this folder).
#include <Wire.h>

#define OLED_ADDR 0x3C
#define OLED_W 128
#define OLED_H 64
#define WIRE_MAX 32

static uint8_t buffer[OLED_W * OLED_H / 8];

static void commandList(const uint8_t *c, uint8_t n) {
  Wire.beginTransmission(OLED_ADDR);
  Wire.write((uint8_t)0x00); // Co = 0, D/C = 0: command stream
  uint8_t bytesOut = 1;
  while (n--) {
    if (bytesOut >= WIRE_MAX) {
      Wire.endTransmission();
      Wire.beginTransmission(OLED_ADDR);
      Wire.write((uint8_t)0x00);
      bytesOut = 1;
    }
    Wire.write(*c++);
    bytesOut++;
  }
  Wire.endTransmission();
}

static void command1(uint8_t c) {
  Wire.beginTransmission(OLED_ADDR);
  Wire.write((uint8_t)0x00);
  Wire.write(c);
  Wire.endTransmission();
}

static void display() {
  static const uint8_t dlist1[] = {
      0x22, 0, 0xFF, // page address 0..7 (0xFF is masked by the controller)
      0x21, 0};      // column start 0 (end goes in a separate transaction)
  commandList(dlist1, sizeof(dlist1));
  command1(OLED_W - 1);

  Wire.setClock(400000);
  uint16_t count = sizeof(buffer);
  const uint8_t *ptr = buffer;
  Wire.beginTransmission(OLED_ADDR);
  Wire.write((uint8_t)0x40); // Co = 0, D/C = 1: data stream
  uint8_t bytesOut = 1;
  while (count--) {
    if (bytesOut >= WIRE_MAX) {
      Wire.endTransmission();
      Wire.beginTransmission(OLED_ADDR);
      Wire.write((uint8_t)0x40);
      bytesOut = 1;
    }
    Wire.write(*ptr++);
    bytesOut++;
  }
  Wire.endTransmission();
  Wire.setClock(100000);
}

static void setPixel(uint8_t x, uint8_t y) {
  buffer[x + (y / 8) * OLED_W] |= (uint8_t)(1 << (y & 7));
}

void setup() {
  Serial.begin(115200);
  Wire.begin();

  // Adafruit_SSD1306::begin(SSD1306_SWITCHCAPVCC, 0x3C) for 128x64
  static const uint8_t init1[] = {0xAE, 0xD5, 0x80, 0xA8};
  commandList(init1, sizeof(init1));
  command1(OLED_H - 1);
  static const uint8_t init2[] = {0xD3, 0x00, 0x40, 0x8D};
  commandList(init2, sizeof(init2));
  command1(0x14);
  static const uint8_t init3[] = {0x20, 0x00, 0xA1, 0xC8};
  commandList(init3, sizeof(init3));
  command1(0xDA);
  command1(0x12);
  command1(0x81);
  command1(0xCF);
  command1(0xD9);
  command1(0xF1);
  static const uint8_t init5[] = {0xDB, 0x40, 0xA4, 0xA6, 0x2E, 0xAF};
  commandList(init5, sizeof(init5));

  // clearDisplay(); display();
  memset(buffer, 0, sizeof(buffer));
  display();

  // Border rectangle
  for (uint8_t x = 0; x < OLED_W; x++) {
    setPixel(x, 0);
    setPixel(x, OLED_H - 1);
  }
  for (uint8_t y = 0; y < OLED_H; y++) {
    setPixel(0, y);
    setPixel(OLED_W - 1, y);
  }
  // 8x8 checker block at (16,16)
  for (uint8_t y = 0; y < 8; y++)
    for (uint8_t x = 0; x < 8; x++)
      if (((x + y) & 1) == 0) setPixel(16 + x, 16 + y);
  // A few single pixels
  setPixel(100, 10);
  setPixel(64, 32);
  setPixel(5, 50);
  display();

  Serial.println(F("SSD1306 DONE"));
}

void loop() {}
