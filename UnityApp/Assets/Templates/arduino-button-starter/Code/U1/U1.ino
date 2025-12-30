#include "app.h"

void setup()
{
  setupPins();
}

void loop()
{
  digitalWrite(LED_PIN, isPressed() ? HIGH : LOW);
  delay(10);
}
