#pragma once

const int LED_PIN = 13;

inline void setupPins()
{
  pinMode(LED_PIN, OUTPUT);
}

inline void setLed(bool on)
{
  digitalWrite(LED_PIN, on ? HIGH : LOW);
}
