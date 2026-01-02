#include "app.h"

void setup()
{
  setupPins();
}

void loop()
{
  setLed(true);
  delay(500);
  setLed(false);
  delay(500);
}
