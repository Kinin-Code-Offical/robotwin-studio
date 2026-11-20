// MPU-6050 at 0x68 read by hand with the bundled Wire library: register pointer
// write + repeated START + burst read (Wire.endTransmission(false) / requestFrom).
// Fixture for RobotTwin.CoreSim I2cDeviceTests (compiled hex lives next to this folder).
#include <Wire.h>

#define MPU 0x68

static void writeReg(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(MPU);
  Wire.write(reg);
  Wire.write(value);
  Wire.endTransmission();
}

static uint8_t readRegs(uint8_t reg, uint8_t *dst, uint8_t n) {
  Wire.beginTransmission(MPU);
  Wire.write(reg);
  Wire.endTransmission(false); // keep the bus: repeated START follows
  uint8_t got = Wire.requestFrom((uint8_t)MPU, n);
  for (uint8_t i = 0; i < got; i++) dst[i] = Wire.read();
  return got;
}

static int16_t be16(const uint8_t *p) {
  return (int16_t)(((uint16_t)p[0] << 8) | p[1]);
}

void setup() {
  Serial.begin(115200);
  Wire.begin();
  uint8_t buf[14];

  // Still asleep after power-on: data registers hold zeros.
  readRegs(0x3F, buf, 2);
  Serial.print(F("AZ0="));
  Serial.println(be16(buf));

  unsigned long t0 = micros();
  writeReg(0x6B, 0x00); // wake up
  unsigned long t1 = micros();
  Serial.print(F("TWAKE="));
  Serial.println(t1 - t0);

  readRegs(0x75, buf, 1);
  Serial.print(F("WHO="));
  Serial.println(buf[0], HEX);

  uint8_t n = readRegs(0x3B, buf, 14);
  Serial.print(F("N="));
  Serial.println(n);
  Serial.print(F("AX="));
  Serial.println(be16(buf + 0));
  Serial.print(F("AY="));
  Serial.println(be16(buf + 2));
  Serial.print(F("AZ="));
  Serial.println(be16(buf + 4));
  Serial.print(F("TEMP="));
  Serial.println(be16(buf + 6));
  Serial.print(F("GX="));
  Serial.println(be16(buf + 8));
  Serial.print(F("GY="));
  Serial.println(be16(buf + 10));
  Serial.print(F("GZ="));
  Serial.println(be16(buf + 12));

  // +-4 g and +-500 dps, then read accel Z / gyro X again.
  writeReg(0x1C, 0x08);
  writeReg(0x1B, 0x08);
  readRegs(0x3F, buf, 2);
  Serial.print(F("AZ4="));
  Serial.println(be16(buf));
  readRegs(0x43, buf, 2);
  Serial.print(F("GX500="));
  Serial.println(be16(buf));

  // Nobody at 0x50: address NACK -> endTransmission() returns 2.
  Wire.beginTransmission(0x50);
  Serial.print(F("NACK="));
  Serial.println(Wire.endTransmission());
  // requestFrom() on a missing device returns 0 bytes.
  Serial.print(F("RQ="));
  Serial.println(Wire.requestFrom((uint8_t)0x50, (uint8_t)2));

  Serial.println(F("MPU DONE"));
}

void loop() {}
