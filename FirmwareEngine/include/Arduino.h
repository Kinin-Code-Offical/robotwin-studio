#pragma once

#include <cstdint>
#include <cmath>
#include <cstdlib>
#include <cstdio>
#include <algorithm>
#include <iostream>
#include <vector>
#include <string>

// Include Protocol to access pin state structures
#include "../Protocol.h"

// Global State Access (Defined in main.cpp)
extern firmware::StepPayload g_inputState;
extern firmware::OutputStatePayload g_outputState;
extern uint32_t g_millis;
extern std::string g_serialBuffer;

// Standard Types
using std::int16_t;
using std::int32_t;
using std::int8_t;
using std::uint16_t;
using std::uint32_t;
using std::uint8_t;

#ifndef boolean
typedef bool boolean;
#endif

typedef uint8_t byte;

// Arduino Constants
#define HIGH 1
#define LOW 0
#define INPUT 0
#define OUTPUT 1
#define INPUT_PULLUP 2
#define A0 14
#define A1 15
#define A2 16
#define A3 17
#define A4 18
#define A5 19
#define SDA 18
#define SCL 19

// Arduino Math Macros
#ifndef min
#define min(a, b) ((a) < (b) ? (a) : (b))
#endif
#ifndef max
#define max(a, b) ((a) > (b) ? (a) : (b))
#endif
#ifndef abs
#define abs(x) ((x) > 0 ? (x) : -(x))
#endif
#ifndef PI
#define PI 3.1415926535897932384626433832795
#endif
#ifndef isnan
#define isnan(x) std::isnan(x)
#endif

// Standard Functions
void setup();
void loop();

// Arduino Functions Mock
inline uint32_t millis() { return g_millis; }
inline uint32_t micros() { return g_millis * 1000; }
inline void delay(uint32_t ms)
{
    // In HLE, a real delay might block the simulator pipe.
    // For setup(), it's fine. For loop(), it's bad.
    // As a hack, we assume this runs in a thread or we accept the freeze.
    // Use standard sleep to "waste time" conceptually,
    // but typically we want to return control to main loop.
    // For now:
    g_millis += ms;
}
inline void delayMicroseconds(uint32_t us) { g_millis += (us / 1000); }

inline void pinMode(uint8_t pin, uint8_t mode)
{
    // Virtual pins don't need mode setting usually,
    // but we could track it if needed.
}

inline void digitalWrite(uint8_t pin, uint8_t val)
{
    if (pin < firmware::kPinCount)
    {
        g_outputState.pins[pin] = val;
    }
}

inline int digitalRead(uint8_t pin)
{
    if (pin < firmware::kPinCount)
    {
        return g_inputState.pins[pin];
    }
    return LOW;
}

inline int analogRead(uint8_t pin)
{
    if (pin < firmware::kPinCount)
    {
        // Map native analog pin index if needed,
        // strictly Protocol.h has separate analog array?
        // Let's check Protocol.h... yes, StepPayload has 'analog'.
        // But 'pin' here is 0-19 generally.
        // Arduino A0 is usually 14.
        if (pin >= A0)
        {
            uint8_t aIdx = pin - A0;
            if (aIdx < firmware::kAnalogCount)
            {
                return g_inputState.analog[aIdx];
            }
        }
    }
    return 0;
}

inline void analogWrite(uint8_t pin, int val)
{
    if (pin < firmware::kPinCount)
    {
        // PWM simulation -> Just write the value.
        // Unity side interprets specific pins as PWM speed if configured.
        g_outputState.pins[pin] = (uint8_t)val;
    }
}

// Flash String Helper (F macro)
class __FlashStringHelper;
#define F(string_literal) (reinterpret_cast<const __FlashStringHelper *>(string_literal))

// Serial Mock
class SerialMock
{
public:
    void begin(long baud)
    {
        // g_serialBuffer += "[Serial Begin " + std::to_string(baud) + "]\n";
    }
    void print(const char *s) { g_serialBuffer += s; }
    void print(int n) { g_serialBuffer += std::to_string(n); }
    void print(unsigned int n) { g_serialBuffer += std::to_string(n); }
    void print(long n) { g_serialBuffer += std::to_string(n); }
    void print(unsigned long n) { g_serialBuffer += std::to_string(n); }
    void print(double n) { g_serialBuffer += std::to_string(n); }
    void print(float n) { g_serialBuffer += std::to_string(n); }

    // Valid overload for F()
    void print(const __FlashStringHelper *f)
    {
        g_serialBuffer += reinterpret_cast<const char *>(f);
    }

    void println(const char *s)
    {
        g_serialBuffer += s;
        g_serialBuffer += "\n";
    }
    void println(int n)
    {
        g_serialBuffer += std::to_string(n);
        g_serialBuffer += "\n";
    }
    void println(unsigned int n)
    {
        g_serialBuffer += std::to_string(n);
        g_serialBuffer += "\n";
    }
    void println(long n)
    {
        g_serialBuffer += std::to_string(n);
        g_serialBuffer += "\n";
    }
    void println(unsigned long n)
    {
        g_serialBuffer += std::to_string(n);
        g_serialBuffer += "\n";
    }
    void println(double n)
    {
        g_serialBuffer += std::to_string(n);
        g_serialBuffer += "\n";
    }
    void println(float n)
    {
        g_serialBuffer += std::to_string(n);
        g_serialBuffer += "\n";
    }

    void println(const __FlashStringHelper *f)
    {
        g_serialBuffer += reinterpret_cast<const char *>(f);
        g_serialBuffer += "\n";
    }
};

extern SerialMock Serial;
