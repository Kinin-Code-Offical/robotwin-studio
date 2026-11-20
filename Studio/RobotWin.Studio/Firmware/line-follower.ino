// RobotWin Studio örneği: 5 kanallı dijital çizgi sensörüyle çizgi izleyen robot.
// Sensör OUT1 (sol) ... OUT5 (sağ) -> A0 ... A4.  LOW = beyaz zemin, HIGH = siyah bant.
// DRV8833: sol motor AIN1 = D5, AIN2 = D9; sağ motor BIN1 = D6, BIN2 = D10 (hepsi PWM).
// PD kontrol: bant ortadaysa düz gider; çizgi kaybolursa son gördüğü yöne döner.
// Mat bantta iyi izler; solmuş bantta sensörler beyaz görür, parlak bant pencere ışığını
// sensöre yansıtınca o da beyaz okunur ve robot çizgiyi kaybeder.

const int SENSOR[5] = {A0, A1, A2, A3, A4};
const int AIN1 = 5, AIN2 = 9, BIN1 = 6, BIN2 = 10;

const int BASE = 95;        // düz hız (0-255)
const float KP = 38.0;      // oransal kazanç
const float KD = 120.0;     // türev kazancı
const int SEARCH = 90;      // çizgi kaybolunca dönüş hızı

float lastError = 0;
unsigned long lastPrint = 0;
unsigned long lostSince = 0;

void motor(int in1, int in2, int speed) {
  speed = constrain(speed, -255, 255);
  if (speed >= 0) {
    analogWrite(in1, speed);
    analogWrite(in2, 0);
  } else {
    analogWrite(in1, 0);
    analogWrite(in2, -speed);
  }
}

void drive(int left, int right) {
  motor(AIN1, AIN2, left);
  motor(BIN1, BIN2, right);
}

void setup() {
  for (int i = 0; i < 5; i++) pinMode(SENSOR[i], INPUT);
  pinMode(AIN1, OUTPUT);
  pinMode(AIN2, OUTPUT);
  pinMode(BIN1, OUTPUT);
  pinMode(BIN2, OUTPUT);
  Serial.begin(115200);
  Serial.println("Cizgi izleyen robot hazir (S: sol..sag, 1 = siyah)");
}

void loop() {
  int black[5];
  int count = 0;
  float sum = 0;
  for (int i = 0; i < 5; i++) {
    black[i] = digitalRead(SENSOR[i]) == HIGH ? 1 : 0;
    if (black[i]) {
      count++;
      sum += (i - 2);          // -2 sol ... +2 sag
    }
  }

  int left, right;
  if (count > 0) {
    float error = sum / count;  // + = cizgi sagda
    float d = error - lastError;
    lastError = error;
    float turn = KP * error + KD * d;
    left = BASE + (int)turn;
    right = BASE - (int)turn;
    lostSince = 0;
  } else {
    // Cizgi yok: son gorulen tarafa yerinde don.
    if (lostSince == 0) lostSince = millis();
    if (lastError >= 0) { left = SEARCH; right = -SEARCH; }
    else { left = -SEARCH; right = SEARCH; }
  }

  drive(left, right);

  if (millis() - lastPrint >= 100) {
    lastPrint = millis();
    Serial.print("S:");
    for (int i = 0; i < 5; i++) Serial.print(black[i]);
    Serial.print(count == 0 ? " CIZGI YOK" : " hata ");
    if (count > 0) Serial.print(lastError, 1);
    Serial.print(" L:");
    Serial.print(left);
    Serial.print(" R:");
    Serial.println(right);
  }

  delay(2);
}
