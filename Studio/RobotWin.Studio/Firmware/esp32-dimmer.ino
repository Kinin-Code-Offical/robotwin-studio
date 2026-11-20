// ESP32 dimmer: the pot on IO34 sets the brightness of the LED on IO25 (LEDC PWM, 5 kHz, 10 bit).
// The blue on-board LED (GPIO2) blinks as a heartbeat; readings go to the serial monitor.
const int POT = 34;
const int LED = 25;
const int HEARTBEAT = 2;

void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
  ledcAttach(LED, 5000, 10);
  pinMode(HEARTBEAT, OUTPUT);
  Serial.println("ESP32 dimmer hazir");
}

void loop() {
  static unsigned long lastPrint = 0;
  int raw = analogRead(POT);                 // 0..4095
  int duty = map(raw, 0, 4095, 0, 1023);     // 10-bit duty
  ledcWrite(LED, duty);
  digitalWrite(HEARTBEAT, (millis() / 250) % 2);

  if (millis() - lastPrint >= 200) {
    lastPrint = millis();
    Serial.printf("pot=%4d  %4lu mV  parlaklik=%3d%%\n", raw, analogReadMilliVolts(POT), duty * 100 / 1023);
  }
  delay(10);
}
