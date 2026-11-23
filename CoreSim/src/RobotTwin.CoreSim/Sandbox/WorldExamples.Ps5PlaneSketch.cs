namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// [ps5plane] Flight controller of <see cref="Ps5Plane"/>: Bluepad32 reads the DualSense, the MPU-6050 gives
        /// the attitude (gyro integrated through the Euler-rate equations, accelerometer trusted only near 1 g),
        /// "Dengeli" mode turns the sticks into bank / pitch angles, "Manuel" moves the surfaces directly, and a
        /// lost link cuts the motor and glides in a gentle turn. Arm with Options while R2 is released.
        /// </summary>
        public const string Ps5PlaneSketch = @"// ESP32 + Bluepad32 + MPU-6050: PS5 DualSense ile uçan 1 m eğitim uçağı.
// Gerçek bir ESP32'de de çalışan Arduino kodu (Bluepad32 kart paketi + ESP32Servo); RobotWin dünyasında aynı kod çalışır.
//
// Kumanda (DualSense):
//   Options (≡, R2 bırakıkken) : motoru kur          ✕ : motoru kilitle (gaz hemen kesilir)
//   R2                         : gaz                 △ : Dengeli / Manuel mod
//   sağ çubuk sağ / sol        : kanatçıklar (yatış) sağ çubuk geri / ileri : burun yukarı / aşağı
//   sol çubuk sağ / sol        : yön dümeni          yön tuşu yukarı / aşağı : yunuslama trimi
// Işık çubuğu: kırmızı = kilitli, yeşil = Dengeli, turuncu = Manuel. Kurulunca ve mod değişince kumanda titrer.
//
// Dengeli mod: çubuk açı ister (en çok 45° yatış, 25° yunuslama); çubuğu bırakınca uçak kendini düzler.
// Manuel mod: çubuk dümenleri doğrudan oynatır; yalnız hafif bir sönüm vardır.
// Gaz azaldıkça burun kendiliğinden biraz iner (süzülüş hızı korunur), tam gazda biraz kalkar: elle bırakınca da uçak tırmanır ya da süzülür.
// Bağlantı koparsa (Bluetooth menzili) motor kapanır, uçak geniş bir daireyle süzülür; bağlantı gelince kontrol hemen döner.
//
// Eksenler (gövde): x ileri, y sol, z yukarı. yatış + = sol kanat yukarı (sağa yatış), yunuslama + = burun aşağı.

#include <Arduino.h>
#include <Wire.h>
#include <ESP32Servo.h>
#include <Bluepad32.h>

const int ESC_PIN = 25, ELEV_PIN = 26, RUD_PIN = 27, AIL_PIN = 14;
// Servo yönleri (gerçek kartlardaki 'servo ters' ayarı), bağlantılara göre: servo 90°'nin üstüne dönünce
// sağ kanatçığın firar kenarı iner (uçak sola yatar), irtifa dümeni iner (burun aşağı), yön dümeni sağa döner (burun sola).
// Yazılımın kuralı: + kanatçık = sağa yatış, + irtifa = burun aşağı, + yön = burun sağa.
const float AIL_DIR = -1, ELEV_DIR = 1, RUD_DIR = -1;
Servo esc, elev, rud, ail;
ControllerPtr pad = nullptr;

const uint8_t MPU = 0x68;
float gyroBias[3] = {0, 0, 0};
float rollTrim = 0, pitchTrim = 0;
float roll = 0, pitch = 0;   // radyan
unsigned long lastUs = 0, lastPrint = 0, startMs = 0;
bool armed = false, manual = false;
bool optWas = false, triWas = false, upWas = false, downWas = false;
float trimDeg = 2.0f;   // Dengeli: düz uçuşta burnun yukarı açısı (°); Manuel: irtifa trimi
float thr = 0;          // yumuşatılmış gaz 0..1
int tick = 0;

void mpuWrite(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(MPU);
  Wire.write(reg);
  Wire.write(val);
  Wire.endTransmission();
}

bool mpuRead(float a[3], float g[3]) {
  Wire.beginTransmission(MPU);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(MPU, (uint8_t)14, (uint8_t)1) != 14) return false;
  int16_t raw[7];
  for (int i = 0; i < 7; i++) raw[i] = (int16_t)((Wire.read() << 8) | Wire.read());
  for (int i = 0; i < 3; i++) a[i] = raw[i] / 8192.0f;                    // ±4 g
  for (int i = 0; i < 3; i++) g[i] = raw[4 + i] / 65.5f * DEG_TO_RAD;     // ±500 °/s
  return true;
}

void showState() {
  if (pad == nullptr) return;
  if (!armed) pad->setColorLED(255, 0, 0);
  else if (manual) pad->setColorLED(255, 110, 0);
  else pad->setColorLED(0, 255, 64);
}

void onConnected(ControllerPtr c) {
  if (pad != nullptr) return;   // yalnızca ilk kumanda uçağı uçurur
  pad = c;
  Serial.printf(""Kumanda baglandi: %s\n"", c->getModelName().c_str());
  showState();
  c->playDualRumble(0, 200, 0x60, 0x30);
}

void onDisconnected(ControllerPtr c) {
  if (c != pad) return;
  pad = nullptr;
  Serial.println(""Kumanda baglantisi koptu: motor kapali, suzulus"");
}

// Çubuk: ölü bölge ve üstel eğri (merkezde ince ayar), -1..1.
float stick(int32_t v) {
  float x = v / 512.0f;
  const float dead = 0.06f;
  if (fabsf(x) < dead) return 0;
  x = (x - (x > 0 ? dead : -dead)) / (1 - dead);
  x = constrain(x, -1.0f, 1.0f);
  return 0.6f * x + 0.4f * x * x * x;
}

// SG90: 544..2400 us = 0..180°, orta 90°.
void servoOut(Servo& s, float deg, float dir) {
  deg = constrain(deg * dir, -40.0f, 40.0f);
  s.writeMicroseconds((int)(544 + (90 + deg) * (2400 - 544) / 180.0f + 0.5f));
}

void setup() {
  Serial.begin(115200);
  Wire.begin(21, 22, 400000);
  mpuWrite(0x6B, 0x00);   // uyan
  mpuWrite(0x1A, 0x03);   // 44 Hz alçak geçiren süzgeç
  mpuWrite(0x1B, 0x08);   // ±500 °/s
  mpuWrite(0x1C, 0x08);   // ±4 g
  esc.setPeriodHertz(400);
  esc.attach(ESC_PIN, 1000, 2000);
  esc.writeMicroseconds(1000);   // ESC düşük gazda kurulur
  elev.attach(ELEV_PIN);
  rud.attach(RUD_PIN);
  ail.attach(AIL_PIN);
  elev.write(90);
  rud.write(90);
  ail.write(90);

  // Uçak tekerlerinin üstünde dururken: jiroskop sıfırı ve yer duruşu (düz kabul edilir).
  float a[3], g[3];
  int n = 0;
  unsigned long t0 = millis();
  while (millis() - t0 < 1500) {
    if (mpuRead(a, g)) {
      for (int k = 0; k < 3; k++) gyroBias[k] += g[k];
      rollTrim += atan2f(a[1], a[2]);
      pitchTrim += atan2f(-a[0], a[2]);
      n++;
    }
    delay(4);
  }
  if (n > 0) {
    for (int k = 0; k < 3; k++) gyroBias[k] /= n;
    rollTrim /= n;
    pitchTrim /= n;
  }

  BP32.setup(&onConnected, &onDisconnected);
  const uint8_t* bd = BP32.localBdAddress();
  Serial.printf(""Hazir: %d IMU ornegi, BT %02X:%02X:%02X:%02X:%02X:%02X. Kurmak icin R2 birakikken Options.\n"", n, bd[0], bd[1], bd[2], bd[3], bd[4], bd[5]);
  startMs = millis();
  lastUs = micros();
}

void loop() {
  unsigned long now = micros();
  if (now - lastUs < 5000) return;   // 200 Hz
  float dt = (now - lastUs) * 1e-6f;
  lastUs = now;
  if ((tick++ & 1) == 0) BP32.update();   // kumanda raporları 100 Hz

  // ---- Duruş: jiroskop Euler hızlarıyla tümlenir; ivmeölçer yalnızca ~1 g'deyken (dönüş ve kalkış ivmesi dışında) düzeltir.
  float a[3], g[3];
  if (!mpuRead(a, g)) return;
  for (int k = 0; k < 3; k++) g[k] -= gyroBias[k];
  float sr = sinf(roll), cr = cosf(roll);
  float rollRate = g[0] + (g[1] * sr + g[2] * cr) * tanf(pitch);
  float pitchRate = g[1] * cr - g[2] * sr;
  roll += rollRate * dt;
  pitch += pitchRate * dt;
  float an = sqrtf(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
  if (fabsf(an - 1.0f) < 0.06f && fabsf(g[2]) < 0.15f) {   // ~1 g ve dönmüyorken: hızlanma/dönüş ivmesi açıyı bozmasın
    float accRoll = atan2f(a[1], a[2]) - rollTrim, accPitch = atan2f(-a[0], sqrtf(a[1] * a[1] + a[2] * a[2])) - pitchTrim;
    roll += 0.001f * (accRoll - roll);
    pitch += 0.001f * (accPitch - pitch);
  }
  float rollDeg = roll * RAD_TO_DEG, pitchDeg = pitch * RAD_TO_DEG;
  float p = g[0] * RAD_TO_DEG, q = g[1] * RAD_TO_DEG, r = g[2] * RAD_TO_DEG;

  // ---- Kumanda
  bool link = pad != nullptr && pad->isConnected();
  float sRoll = 0, sPitch = 0, sYaw = 0, tIn = 0;
  if (link) {
    sRoll = stick(pad->axisRX());     // sağ = +
    sPitch = stick(pad->axisRY());    // geri çekince + (burun yukarı)
    sYaw = stick(pad->axisX());       // sağ = +
    tIn = pad->throttle() / 1023.0f;
    bool opt = pad->miscStart(), tri = pad->y();
    uint8_t dp = pad->dpad();
    bool up = (dp & DPAD_UP) != 0, down = (dp & DPAD_DOWN) != 0;
    if (opt && !optWas && !armed) {
      if (tIn < 0.05f) {
        armed = true;
        Serial.println(""Motor kuruldu"");
        pad->playDualRumble(0, 150, 0x80, 0x80);
      } else {
        Serial.println(""Kurmak icin once R2'yi birakin"");
      }
      showState();
    }
    if (pad->a() && armed) {
      armed = false;
      Serial.println(""Motor kilitlendi"");
      showState();
    }
    if (tri && !triWas) {
      manual = !manual;
      Serial.println(manual ? ""Mod: Manuel"" : ""Mod: Dengeli"");
      pad->playDualRumble(0, 80, 0x40, 0);
      showState();
    }
    if (up && !upWas) trimDeg += 0.5f;
    if (down && !downWas) trimDeg -= 0.5f;
    optWas = opt;
    triWas = tri;
    upWas = up;
    downWas = down;
  }

  // ---- Gaz: kurulu ve bağlıyken R2; ✕ ya da bağlantı kopunca hemen sıfır.
  float want = (armed && link) ? tIn : 0;
  if (want < thr) thr = want;
  else thr += min(want - thr, 2.0f * dt);   // 0 → tam gaz 0,5 s
  esc.writeMicroseconds(1000 + (int)(thr * 1000));

  // ---- Dümenler (servo derecesi; + kanatçık = sağa yatış, + irtifa = burun aşağı, + yön = burun sağa)
  float ailDeg, elevDeg, rudDeg;
  if (link && manual) {
    ailDeg = 35 * sRoll - 0.03f * p;
    elevDeg = -30 * sPitch - 0.03f * q - (trimDeg - 2.0f);
    rudDeg = 30 * sYaw;
  } else {
    // Dengeli. Gaz–yunuslama karışımı: gaz azaldıkça burun biraz iner (süzülüş hızı korunur), tam gazda biraz kalkar.
    // Bağlantı yoksa: gaz kesik, kanatlar 10° sola yatık, burun aşağıda; geniş bir daireyle süzülür.
    float rollT = link ? 45 * sRoll : -10;
    float pitchT = link ? -trimDeg - 25 * sPitch + 10 * (0.55f - thr) : 4;
    ailDeg = 1.2f * (rollT - rollDeg) - 0.10f * p;
    float turn = 1.0f / max(0.5f, cosf(roll)) - 1.0f;   // yatışta kaybolan kaldırmayı burnu kaldırarak telafi
    elevDeg = 1.6f * (pitchT - pitchDeg) - 0.12f * q - 12.0f * turn;
    rudDeg = 30 * sYaw + 0.25f * ailDeg - 0.10f * r;     // ters sapmaya karşı kanatçıkla birlikte, sapma sönümü
  }
  servoOut(ail, ailDeg, AIL_DIR);
  servoOut(elev, elevDeg, ELEV_DIR);
  servoOut(rud, rudDeg, RUD_DIR);

  if (millis() - lastPrint > 500) {
    lastPrint = millis();
    Serial.printf(""t=%.1f bag=%d kurulu=%d mod=%s gaz=%.0f%% yatis=%.0f yunus=%.0f kanatcik=%.0f irtifa=%.0f yon=%.0f trim=%.1f\n"",
                  (millis() - startMs) * 1e-3f, link, armed, manual ? ""M"" : ""D"", thr * 100, rollDeg, pitchDeg, ailDeg, elevDeg, rudDeg, trimDeg);
  }
}
";
    }
}
