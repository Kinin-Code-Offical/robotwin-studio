#pragma once
#include <Arduino.h>

class Servo
{
private:
    int _pin = -1;

public:
    uint8_t attach(int pin)
    {
        _pin = pin;
        pinMode(pin, OUTPUT);
        return 1;
    }

    void detach()
    {
        if (_pin != -1)
        {
            // Optional: signal detach? For now just stop updating.
            _pin = -1;
        }
    }

    void write(int angle)
    {
        if (_pin != -1)
        {
            // Map 0-180 angle to pin output for Unity to read.
            // Unity "SimHost" or "NativeBridge" needs to interpret Pin value 0-180 as angle.
            // Since standard PWM is 0-255, this fits perfectly.
            analogWrite(_pin, angle);
        }
    }

    int read()
    {
        // We rarely read back from servo, but if needed:
        if (_pin != -1)
        {
            // In a real servo, read() returns last written value.
            // We can check the output state?
            // Accessing g_outputState directly here might be cleaner if we include Protocol.
            // But avoiding circular dep header hell is better.
            // Mock: return 90.
            return 90;
        }
        return 0;
    }

    bool attached() { return _pin != -1; }
};
