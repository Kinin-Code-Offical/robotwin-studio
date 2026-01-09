#pragma once

#include <cstdint>
#include <Arduino.h>

// Mock implementation of Adafruit_TCS34725 for RobotWin Simulator.
// This class mimics the I2C interactions.
// In the simulator, color data is injected via the "Analog" array in StepPayload
// or via a special I2C buffer.

// For simplicity in the "God Tier" engine:
// We will map the Color Sensor channels (R, G, B, C) to Analog Pins A4, A5, A6, A7
// (Or virtual analog pins if A4/A5 are taken by SDA/SCL).
// Protocol.h says kAnalogCount = 16. So we have plenty of room.
// Let's use Analog[10] = R, Analog[11] = G, Analog[12] = B, Analog[13] = Clear.

#define TCS34725_INTEGRATIONTIME_50MS 0xEB
#define TCS34725_GAIN_16X 0x01

class Adafruit_TCS34725
{
public:
    Adafruit_TCS34725(uint8_t it = TCS34725_INTEGRATIONTIME_50MS, uint8_t gain = TCS34725_GAIN_16X) {}

    boolean begin()
    {
        // Always return true in sim
        return true;
    }

    void getRawData(uint16_t *r, uint16_t *g, uint16_t *b, uint16_t *c)
    {
        *r = analogRead(10); // Virtual Map to A10
        *g = analogRead(11); // Virtual Map to A11
        *b = analogRead(12); // Virtual Map to A12
        *c = analogRead(13); // Virtual Map to A13
    }

    // New API often used
    void setInterrupt(boolean i) {}

    void enable() {}
    void disable() {}
};
