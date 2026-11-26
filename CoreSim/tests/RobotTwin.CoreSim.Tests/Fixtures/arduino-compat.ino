// The Arduino API and the AVR core's own libraries, one test per command letter sent on Serial (9600 baud).
// Each test prints its results on one line starting with its letter; pins are watched from outside.
//   T  tone(8, 1000) for 60 ms                    S  SoftwareSerial: "Merhaba" on D11, then echoes what comes on D10
//   P  pulseIn(7, HIGH) of a pulse given on D7    I  attachInterrupt on D2, falling edges counted for 60 ms
//   A  analogWrite on 3, 5, 6, 9, 10, 11          M  micros() over delay(10); D12 toggled by delayMicroseconds(250)
//   E  EEPROM.put / get of a struct               W  Wire: scan the bus
//   X  SPI: an MCP3008's CH0 (CS on D10)          R  Serial.parseInt and readStringUntil
//   D  digitalRead with INPUT_PULLUP, analogRead(A0)
#include <SoftwareSerial.h>
#include <EEPROM.h>
#include <Wire.h>
#include <SPI.h>

SoftwareSerial soft(10, 11);
volatile uint16_t edges = 0;
void falling() { edges++; }

struct Record { float gain; long count; char name[8]; };

void setup() {
  Serial.begin(9600);
  Serial.println("READY");
}

void loop() {
  if (!Serial.available()) return;
  char c = Serial.read();
  switch (c) {
    case 'T':
      tone(8, 1000);
      delay(60);
      noTone(8);
      Serial.println("T done");
      break;
    case 'S': {
      soft.begin(9600);
      soft.print("Merhaba");
      unsigned long start = millis();
      String got;
      while (millis() - start < 80) while (soft.available()) got += (char)soft.read();
      Serial.print("S ");
      Serial.println(got);
      soft.end();
      break;
    }
    case 'P': {
      unsigned long w = pulseIn(7, HIGH, 100000UL);
      Serial.print("P ");
      Serial.println(w);
      break;
    }
    case 'I':
      edges = 0;
      attachInterrupt(digitalPinToInterrupt(2), falling, FALLING);
      delay(60);
      detachInterrupt(digitalPinToInterrupt(2));
      Serial.print("I ");
      Serial.println(edges);
      break;
    case 'A':
      analogWrite(3, 32); analogWrite(5, 64); analogWrite(6, 96);
      analogWrite(9, 128); analogWrite(10, 160); analogWrite(11, 192);
      delay(20);
      Serial.println("A set");
      break;
    case 'M': {
      unsigned long a = micros();
      delay(10);
      unsigned long b = micros();
      pinMode(12, OUTPUT);
      for (int i = 0; i < 20; i++) { digitalWrite(12, HIGH); delayMicroseconds(250); digitalWrite(12, LOW); delayMicroseconds(250); }
      Serial.print("M ");
      Serial.println(b - a);
      break;
    }
    case 'E': {
      Record w = { 3.25f, 123456L, "robot" }, r;
      EEPROM.put(16, w);
      EEPROM.get(16, r);
      Serial.print("E ");
      Serial.print(r.gain, 2); Serial.print(' '); Serial.print(r.count); Serial.print(' '); Serial.println(r.name);
      break;
    }
    case 'W': {
      Wire.begin();
      int found = 0;
      for (uint8_t a = 1; a < 127; a++) { Wire.beginTransmission(a); if (Wire.endTransmission() == 0) found++; }
      Serial.print("W ");
      Serial.println(found);
      break;
    }
    case 'X': {
      // start bit, single-ended CH0, then the ten bits back
      SPI.begin();
      SPI.beginTransaction(SPISettings(1000000, MSBFIRST, SPI_MODE0));
      digitalWrite(10, LOW);
      SPI.transfer(0x01);
      uint8_t hi = SPI.transfer(0x80);
      uint8_t lo = SPI.transfer(0x00);
      digitalWrite(10, HIGH);
      SPI.endTransaction();
      Serial.print("X ");
      Serial.println(((hi & 3) << 8) | lo);
      break;
    }
    case 'R': {
      Serial.setTimeout(200);
      long n = Serial.parseInt();
      String rest = Serial.readStringUntil('\n');
      Serial.print("R ");
      Serial.print(n * 2);
      Serial.print(" [");
      Serial.print(rest);
      Serial.println("]");
      break;
    }
    case 'D': {
      pinMode(4, INPUT_PULLUP);
      int up = digitalRead(4);
      int a0 = analogRead(A0);
      Serial.print("D ");
      Serial.print(up);
      Serial.print(' ');
      Serial.println(a0);
      break;
    }
  }
}
