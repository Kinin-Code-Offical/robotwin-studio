#pragma once
#include <cstdint>
#include <iostream>
#include <algorithm>
#include <cmath>

using std::uint16_t;
using std::uint32_t;
using std::uint8_t;

#define INPUT 0
#define OUTPUT 1
#define HIGH 1
#define LOW 0
#define INPUT_PULLUP 2

// Mock Pins matches U1 Config
#define A0 14
#define A1 15
#define A2 16
#define A3 17
#define A4 18
#define A5 19

extern uint32_t mock_millis_val;
inline uint32_t millis() { return mock_millis_val; }
inline void delay(uint32_t) {}
inline void pinMode(uint8_t, uint8_t) {}

// Global state for pin values (Test Fixture)
extern int mock_pin_values[256];

inline int digitalRead(uint8_t pin) { return mock_pin_values[pin]; }
inline int analogRead(uint8_t pin) { return mock_pin_values[pin]; }

class Serial_
{
public:
    void begin(int) {}
    void print(const char *) {}
    void println(const char *) {}
    void print(int) {}
    void println(int) {}
    void print(double) {}
};
static Serial_ Serial;

class Wire_
{
public:
    void begin() {}
    void beginTransmission(uint8_t) {}
    void write(uint8_t) {}
    void endTransmission() {}
    void requestFrom(uint8_t, uint8_t) {}
    int read() { return 0; }
    int available() { return 0; }
};
static Wire_ Wire;
