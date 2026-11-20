// HD44780 16x2 behind a PCF8574 backpack (0x27), driven by hand with the bundled
// Wire library. The class below reproduces the byte/delay sequence of the popular
// LiquidCrystal_I2C 1.1.2 library (P0=RS, P1=RW, P2=EN, P3=backlight, P4-P7=D4-D7).
// Fixture for RobotTwin.CoreSim I2cDeviceTests (compiled hex lives next to this folder).
#include <Wire.h>

#define LCD_CLEARDISPLAY 0x01
#define LCD_RETURNHOME 0x02
#define LCD_ENTRYMODESET 0x04
#define LCD_DISPLAYCONTROL 0x08
#define LCD_FUNCTIONSET 0x20
#define LCD_SETCGRAMADDR 0x40
#define LCD_SETDDRAMADDR 0x80
#define LCD_ENTRYLEFT 0x02
#define LCD_ENTRYSHIFTDECREMENT 0x00
#define LCD_DISPLAYON 0x04
#define LCD_CURSOROFF 0x00
#define LCD_BLINKOFF 0x00
#define LCD_4BITMODE 0x00
#define LCD_2LINE 0x08
#define LCD_1LINE 0x00
#define LCD_5x8DOTS 0x00
#define LCD_BACKLIGHT 0x08
#define LCD_NOBACKLIGHT 0x00

#define En 0x04
#define Rw 0x02
#define Rs 0x01

class LcdI2c : public Print {
public:
  LcdI2c(uint8_t addr, uint8_t cols, uint8_t rows) : _addr(addr), _cols(cols), _rows(rows), _backlightval(LCD_NOBACKLIGHT) {}

  void init() {
    Wire.begin();
    _displayfunction = LCD_4BITMODE | LCD_1LINE | LCD_5x8DOTS;
    begin();
  }

  void begin() {
    if (_rows > 1) _displayfunction |= LCD_2LINE;
    delay(50);
    expanderWrite(_backlightval);
    delay(1000);
    write4bits(0x03 << 4);
    delayMicroseconds(4500);
    write4bits(0x03 << 4);
    delayMicroseconds(4500);
    write4bits(0x03 << 4);
    delayMicroseconds(150);
    write4bits(0x02 << 4);
    command(LCD_FUNCTIONSET | _displayfunction);
    _displaycontrol = LCD_DISPLAYON | LCD_CURSOROFF | LCD_BLINKOFF;
    command(LCD_DISPLAYCONTROL | _displaycontrol);
    clear();
    _displaymode = LCD_ENTRYLEFT | LCD_ENTRYSHIFTDECREMENT;
    command(LCD_ENTRYMODESET | _displaymode);
    home();
  }

  void clear() { command(LCD_CLEARDISPLAY); delayMicroseconds(2000); }
  void home() { command(LCD_RETURNHOME); delayMicroseconds(2000); }

  void setCursor(uint8_t col, uint8_t row) {
    int row_offsets[] = {0x00, 0x40, 0x14, 0x54};
    if (row > _rows) row = _rows - 1;
    command(LCD_SETDDRAMADDR | (col + row_offsets[row]));
  }

  void backlight() { _backlightval = LCD_BACKLIGHT; expanderWrite(0); }

  void createChar(uint8_t location, const uint8_t charmap[]) {
    location &= 0x7;
    command(LCD_SETCGRAMADDR | (location << 3));
    for (int i = 0; i < 8; i++) write(charmap[i]);
  }

  // Busy flag + address counter, read as two nibbles with RW=1 (D4-D7 written high
  // so the quasi-bidirectional PCF8574 pins can be pulled low by the LCD).
  uint8_t readStatus() {
    uint8_t hi = readNibble();
    uint8_t lo = readNibble();
    return (hi & 0xF0) | (lo >> 4);
  }

  virtual size_t write(uint8_t value) { send(value, Rs); return 1; }
  using Print::write;

private:
  void command(uint8_t value) { send(value, 0); }

  void send(uint8_t value, uint8_t mode) {
    uint8_t highnib = value & 0xf0;
    uint8_t lownib = (value << 4) & 0xf0;
    write4bits(highnib | mode);
    write4bits(lownib | mode);
  }

  void write4bits(uint8_t value) { expanderWrite(value); pulseEnable(value); }

  void expanderWrite(uint8_t data) {
    Wire.beginTransmission(_addr);
    Wire.write((int)(data) | _backlightval);
    Wire.endTransmission();
  }

  void pulseEnable(uint8_t data) {
    expanderWrite(data | En);
    delayMicroseconds(1);
    expanderWrite(data & ~En);
    delayMicroseconds(50);
  }

  uint8_t readNibble() {
    expanderWrite(0xF0 | Rw | En);
    Wire.requestFrom(_addr, (uint8_t)1);
    uint8_t v = Wire.available() ? Wire.read() : 0xFF;
    expanderWrite(0xF0 | Rw);
    return v;
  }

  uint8_t _addr, _cols, _rows, _backlightval;
  uint8_t _displayfunction, _displaycontrol, _displaymode;
};

LcdI2c lcd(0x27, 16, 2);

const uint8_t heart[8] = {0x00, 0x0A, 0x1F, 0x1F, 0x0E, 0x04, 0x00, 0x00};

void setup() {
  Serial.begin(115200);
  lcd.init();
  lcd.backlight();
  lcd.createChar(0, heart);
  lcd.setCursor(0, 0);
  lcd.print("Hello RobotWin");
  lcd.setCursor(0, 1);
  lcd.print(12345);
  lcd.write((uint8_t)0);
  uint8_t status = lcd.readStatus();
  Serial.print(F("STATUS="));
  Serial.println(status, HEX);
  Serial.println(F("LCD DONE"));
}

void loop() {}
