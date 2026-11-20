// ESP32 + Bluepad32: kablosuz kumanda (DualSense / DualShock 4 / Xbox) ile iki motorlu araba.
// Motor sürücü: DRV8833 (AIN1/AIN2 = sol motor, BIN1/BIN2 = sağ motor).
//
// Kullanım (RobotWin dünyası):
//   1. Kumandayı seçin, "Eşleştirme modu"na basın (DualSense'te PS + Create'i 3 s basılı tutmak gibi).
//   2. Bu çizim BP32.update() çağırdıkça kumandayı bulur, eşleşir: onConnected() çalışır, ışık çubuğu yeşil olur.
//   3. R2 = ileri gaz, L2 = geri, sol çubuk X = direksiyon (sol çubuk Y de gaz olarak kullanılabilir).
//   4. Kumanda menzil dışına çıkarsa ~2 s sonra bağlantı kopar; motorlar durur. Geri gelince kendiliğinden bağlanır.

#include <Bluepad32.h>

const int AIN1 = 25, AIN2 = 26;   // sol motor
const int BIN1 = 27, BIN2 = 14;   // sağ motor
const int PWM_HZ = 20000, PWM_BITS = 8;

ControllerPtr pad = nullptr;
unsigned long lastPrint = 0;

void setMotor(int in1, int in2, int speed) {
  speed = constrain(speed, -255, 255);
  if (speed >= 0) {
    ledcWrite(in1, speed);
    ledcWrite(in2, 0);
  } else {
    ledcWrite(in1, 0);
    ledcWrite(in2, -speed);
  }
}

void stopMotors() {
  setMotor(AIN1, AIN2, 0);
  setMotor(BIN1, BIN2, 0);
}

void onConnected(ControllerPtr c) {
  if (pad != nullptr) return;              // yalnızca ilk kumanda arabayı sürer
  pad = c;
  ControllerProperties p = c->getProperties();
  Serial.printf("Kumanda baglandi: %s (idx %d, %02X:%02X:%02X:%02X:%02X:%02X)\n", c->getModelName().c_str(), c->index(),
                p.btaddr[0], p.btaddr[1], p.btaddr[2], p.btaddr[3], p.btaddr[4], p.btaddr[5]);
  c->setColorLED(0, 255, 64);              // ışık çubuğu: yeşil
  c->playDualRumble(0, 250, 0x80, 0x40);   // kısa titreşim
}

void onDisconnected(ControllerPtr c) {
  if (c != pad) return;
  pad = nullptr;
  stopMotors();
  Serial.println("Kumanda ayrildi, motorlar durdu");
}

void setup() {
  Serial.begin(115200);
  for (int pin : {AIN1, AIN2, BIN1, BIN2}) {
    ledcAttach(pin, PWM_HZ, PWM_BITS);
    ledcWrite(pin, 0);
  }
  BP32.setup(&onConnected, &onDisconnected);
  const uint8_t* a = BP32.localBdAddress();
  Serial.printf("Bluepad32 hazir, ESP32 BT adresi %02X:%02X:%02X:%02X:%02X:%02X\n", a[0], a[1], a[2], a[3], a[4], a[5]);
}

void loop() {
  BP32.update();
  if (pad && pad->isConnected()) {
    int gas = (pad->throttle() - pad->brake()) / 4;          // R2 ileri, L2 geri: -255..255
    if (abs(pad->axisY()) > 40) gas = -pad->axisY() / 2;      // sol çubuk yukarı = ileri
    int steer = abs(pad->axisX()) > 40 ? pad->axisX() / 3 : 0;
    if (pad->a()) gas = 0;                                     // ✕ = fren
    setMotor(AIN1, AIN2, gas + steer);
    setMotor(BIN1, BIN2, gas - steer);
    if (millis() - lastPrint > 500) {
      lastPrint = millis();
      Serial.printf("gaz=%d direksiyon=%d sol=(%d,%d) R2=%d L2=%d tuslar=0x%04X\n", gas, steer, pad->axisX(), pad->axisY(),
                    pad->throttle(), pad->brake(), pad->buttons());
    }
  }
  delay(10);
}
