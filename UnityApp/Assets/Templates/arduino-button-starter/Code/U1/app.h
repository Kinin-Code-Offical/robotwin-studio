#pragma once

const int LED_PIN = 13;
const int BUTTON_PIN = 2;

inline void setupPins()
{
  pinMode(LED_PIN, OUTPUT);
  pinMode(BUTTON_PIN, INPUT_PULLUP);
}

inline bool isPressed()
{
  return digitalRead(BUTTON_PIN) == LOW;
}
