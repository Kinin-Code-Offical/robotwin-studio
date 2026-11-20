// RobotWin Studio örneği: HC-SR04 ile engelden kaçan robot.
// HC-SR04: TRIG = D9, ECHO = D10.  Motor sürücü PWM girişleri: sol D5, sağ D6.
// Önü açıkken düz gider; 30 cm'den yakın engel görünce yerinde sağa döner.
const int TRIG = 9, ECHO = 10;
const int LEFT = 5, RIGHT = 6;

float distanceCm() {
  digitalWrite(TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG, LOW);
  unsigned long us = pulseIn(ECHO, HIGH, 30000UL);
  return us == 0 ? 400.0 : us / 58.0;
}

void drive(int left, int right) {
  analogWrite(LEFT, left);
  analogWrite(RIGHT, right);
}

void setup() {
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
  pinMode(LEFT, OUTPUT);
  pinMode(RIGHT, OUTPUT);
  Serial.begin(9600);
  Serial.println("Engelden kacan robot hazir");
}

void loop() {
  float d = distanceCm();
  if (d < 30) {
    drive(170, 0);          // sağa dön
  } else if (d < 60) {
    drive(170, 110);        // yavaşla, hafif sağa kır
  } else {
    drive(170, 170);        // düz git
  }
  Serial.print("mesafe: ");
  Serial.print(d, 1);
  Serial.println(" cm");
  delay(30);
}
