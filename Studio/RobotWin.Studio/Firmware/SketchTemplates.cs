using System.Collections.Generic;

namespace RobotWin.Studio.Firmware
{
    /// <summary>Starter sketches for the code editor (Arduino core libraries only).</summary>
    public static class SketchTemplates
    {
        public static IReadOnlyList<(string Name, string Source)> All { get; } = new List<(string, string)>
        {
            ("Blink (D13 LED)", @"// D13'teki LED'i saniyede bir yakıp söndürür.
void setup() {
  pinMode(LED_BUILTIN, OUTPUT);
  Serial.begin(9600);
}

void loop() {
  digitalWrite(LED_BUILTIN, HIGH);
  Serial.println(""LED yandi"");
  delay(500);
  digitalWrite(LED_BUILTIN, LOW);
  delay(500);
}
"),
            ("PWM ile parlaklık (D9)", @"// D9 (PWM) üzerindeki LED/motorun gücünü yavaşça artırıp azaltır.
const int PIN = 9;

void setup() {
  pinMode(PIN, OUTPUT);
}

void loop() {
  for (int d = 0; d <= 255; d += 5) {
    analogWrite(PIN, d);
    delay(20);
  }
  for (int d = 255; d >= 0; d -= 5) {
    analogWrite(PIN, d);
    delay(20);
  }
}
"),
            ("Buton ile LED", @"// D2'deki buton (dahili pull-up) basılıyken D13'teki LED yanar.
const int BUTTON = 2;

void setup() {
  pinMode(BUTTON, INPUT_PULLUP);
  pinMode(LED_BUILTIN, OUTPUT);
}

void loop() {
  bool pressed = digitalRead(BUTTON) == LOW;
  digitalWrite(LED_BUILTIN, pressed ? HIGH : LOW);
}
"),
            ("Analog okuma + seri", @"// A0'daki gerilimi okur, seri monitöre yazar.
void setup() {
  Serial.begin(9600);
}

void loop() {
  int raw = analogRead(A0);
  float volts = raw * 5.0 / 1023.0;
  Serial.print(""A0 = "");
  Serial.print(raw);
  Serial.print(""  ("");
  Serial.print(volts, 2);
  Serial.println("" V)"");
  delay(200);
}
"),
            ("HC-SR04 mesafe", @"// HC-SR04: TRIG = D9, ECHO = D10. Mesafeyi cm olarak yazar.
const int TRIG = 9;
const int ECHO = 10;

void setup() {
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
  Serial.begin(9600);
}

void loop() {
  digitalWrite(TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG, LOW);
  unsigned long us = pulseIn(ECHO, HIGH, 30000UL);
  float cm = us / 58.0;
  Serial.print(""Mesafe: "");
  Serial.print(cm, 1);
  Serial.println("" cm"");
  delay(100);
}
"),
            ("Engelden kaçan robot", @"// HC-SR04 (TRIG D9, ECHO D10) ve iki motor sürücü (PWM: sol D5, sağ D6).
// Engel 25 cm'den yakınsa sağa döner, yoksa düz gider.
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

void setup() {
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
  pinMode(LEFT, OUTPUT);
  pinMode(RIGHT, OUTPUT);
  Serial.begin(9600);
}

void loop() {
  float d = distanceCm();
  if (d < 25) {
    analogWrite(LEFT, 200);
    analogWrite(RIGHT, 0);
  } else {
    analogWrite(LEFT, 180);
    analogWrite(RIGHT, 180);
  }
  Serial.println(d);
  delay(40);
}
"),
            ("Çizgi izleyen (2 sensör)", @"// TCRT5000 sensörleri: sol A0, sağ A1 (siyahta yüksek). Motor PWM: sol D5, sağ D6.
const int SL = A0, SR = A1;
const int LEFT = 5, RIGHT = 6;
const int THRESHOLD = 512;

void setup() {
  pinMode(LEFT, OUTPUT);
  pinMode(RIGHT, OUTPUT);
}

void loop() {
  bool leftBlack = analogRead(SL) > THRESHOLD;
  bool rightBlack = analogRead(SR) > THRESHOLD;
  analogWrite(LEFT, leftBlack ? 0 : 170);
  analogWrite(RIGHT, rightBlack ? 0 : 170);
}
"),
            ("I²C tarayıcı", @"// I²C veri yolundaki cihazların adreslerini bulur (A4 = SDA, A5 = SCL).
#include <Wire.h>

void setup() {
  Wire.begin();
  Serial.begin(9600);
  Serial.println(""I2C taraniyor..."");
  int found = 0;
  for (byte addr = 1; addr < 127; addr++) {
    Wire.beginTransmission(addr);
    if (Wire.endTransmission() == 0) {
      Serial.print(""Cihaz bulundu: 0x"");
      if (addr < 16) Serial.print('0');
      Serial.println(addr, HEX);
      found++;
    }
  }
  Serial.print(found);
  Serial.println("" cihaz"");
}

void loop() {}
"),
            ("MPU-6050 ivme", @"// MPU-6050 (0x68) ham ivme ve jiroskop değerlerini okur.
#include <Wire.h>

int16_t read16() {
  int16_t v = Wire.read() << 8;
  return v | Wire.read();
}

void setup() {
  Wire.begin();
  Serial.begin(9600);
  Wire.beginTransmission(0x68);
  Wire.write(0x6B);
  Wire.write(0); // uyandır
  Wire.endTransmission();
}

void loop() {
  Wire.beginTransmission(0x68);
  Wire.write(0x3B);
  Wire.endTransmission(false);
  Wire.requestFrom(0x68, 14);
  int16_t ax = read16(), ay = read16(), az = read16();
  read16(); // sıcaklık
  int16_t gx = read16(), gy = read16(), gz = read16();
  Serial.print(""a: ""); Serial.print(ax); Serial.print(' '); Serial.print(ay); Serial.print(' '); Serial.print(az);
  Serial.print(""  g: ""); Serial.print(gx); Serial.print(' '); Serial.print(gy); Serial.print(' '); Serial.println(gz);
  delay(100);
}
")
        };
    }
}
