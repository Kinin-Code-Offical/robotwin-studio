// ESP32 NeoPixel: a rainbow runs along the 8-LED WS2812B strip on IO5; the pot on IO34 sets its brightness.
// Adafruit_NeoPixel works in both run modes (fast simulation and hardware-compatible, on the RMT).
#include <Adafruit_NeoPixel.h>

const int STRIP_PIN = 5;
const int LEDS = 8;
const int POT = 34;

Adafruit_NeoPixel strip(LEDS, STRIP_PIN, NEO_GRB + NEO_KHZ800);

void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
  strip.begin();
  strip.show();   // every LED off
  Serial.println("ESP32 NeoPixel hazir");
}

void loop() {
  static uint16_t hue = 0;
  static unsigned long lastPrint = 0;
  int raw = analogRead(POT);                        // 0..4095
  uint8_t bright = map(raw, 0, 4095, 4, 255);
  strip.setBrightness(bright);
  strip.rainbow(hue, 1, 255, 255, true);           // one turn of the colour wheel along the strip
  strip.show();
  hue += 512;

  if (millis() - lastPrint >= 500) {
    lastPrint = millis();
    Serial.printf("parlaklik=%3d  renk=%5u\n", bright, hue);
  }
  delay(20);
}
