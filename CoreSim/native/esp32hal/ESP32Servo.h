/*
 * Servo for ESP32, as the ESP32Servo library (3.x) the hardware build installs drives it, so a sketch moves its servos the
 * same in both modes: an LEDC channel at the period (50 Hz unless setPeriodHertz) with a 10-bit timer, the pulse given
 * in timer ticks (µs / (20000 / 1024) × Hz / 50, truncated: 19.5 µs steps at 50 Hz) and kept within min..max; angles
 * (values below 500) map onto min..max (544-2400 µs by default); attach() sends no pulse until the first write.
 */
#ifndef RW_ESP32_SERVO_H
#define RW_ESP32_SERVO_H
#include "Arduino.h"

#define DEFAULT_uS_LOW 544
#define DEFAULT_uS_HIGH 2400
#define DEFAULT_TIMER_WIDTH 10
#define MINIMUM_TIMER_WIDTH 10
#define MAXIMUM_TIMER_WIDTH 20
#define MIN_PULSE_WIDTH 500
#define MAX_PULSE_WIDTH 2500
#define DEFAULT_PULSE_WIDTH 1500
#define DEFAULT_PULSE_WIDTH_TICKS 4825
#define REFRESH_USEC 20000

class ESP32PWM {
public:
    static void allocateTimer(int) {}
};

class Servo {
public:
    Servo() : _pin(-1), _minUs(DEFAULT_uS_LOW), _maxUs(DEFAULT_uS_HIGH), _ticks(DEFAULT_PULSE_WIDTH_TICKS),
              _width(DEFAULT_TIMER_WIDTH), _widthTicks(1 << DEFAULT_TIMER_WIDTH), _hz(50) {}

    int attach(int pin) { return attach(pin, DEFAULT_uS_LOW, DEFAULT_uS_HIGH); }

    int attach(int pin, int min, int max) {
        if (pin < 0 || pin > 33) return 0;
        if (_pin < 0) {
            _ticks = DEFAULT_PULSE_WIDTH_TICKS;
            _width = DEFAULT_TIMER_WIDTH;
            _widthTicks = 1 << _width;
        }

        if (min < MIN_PULSE_WIDTH) min = MIN_PULSE_WIDTH;
        if (max > MAX_PULSE_WIDTH) max = MAX_PULSE_WIDTH;
        _minUs = min;
        _maxUs = max;
        if (!ledcAttach((uint8_t)pin, (uint32_t)_hz, (uint8_t)_width)) return 0;
        _pin = pin;
        ledcWrite((uint8_t)pin, 0);   // no pulse until the first write
        return channelOf(pin);
    }

    void detach() {
        if (_pin >= 0) ledcDetach((uint8_t)_pin);
        _pin = -1;
    }

    void write(int value) {
        // values below MIN_PULSE_WIDTH (500) are angles in degrees
        if (value < MIN_PULSE_WIDTH) {
            if (value < 0) value = 0;
            else if (value > 180) value = 180;
            value = (int)map(value, 0, 180, _minUs, _maxUs);
        }

        writeMicroseconds(value);
    }

    void writeMicroseconds(int value) { writeTicks(usToTicks(value)); }

    void writeTicks(int value) {
        if (!attached()) return;
        if (value < usToTicks(_minUs)) value = usToTicks(_minUs);
        else if (value > usToTicks(_maxUs)) value = usToTicks(_maxUs);
        _ticks = value;
        ledcWrite((uint8_t)_pin, (uint32_t)_ticks);
    }

    void release() {
        if (attached()) ledcWrite((uint8_t)_pin, 0);
    }

    int read() { return (int)map(readMicroseconds(), _minUs, _maxUs, 0, 180); }
    int readMicroseconds() { return attached() ? ticksToUs(_ticks) : 0; }
    int readTicks() { return _ticks; }
    bool attached() { return _pin >= 0; }
    void setPeriodHertz(int hertz) { _hz = hertz; }
    int readTimerWidth() { return _width; }

    void setTimerWidth(int value) {
        if (value < MINIMUM_TIMER_WIDTH) value = MINIMUM_TIMER_WIDTH;
        else if (value > MAXIMUM_TIMER_WIDTH) value = MAXIMUM_TIMER_WIDTH;
        int difference = _width - value;
        if (difference > 0) _ticks = difference * _ticks;
        else if (difference < 0) _ticks = _ticks / -difference;
        _width = value;
        _widthTicks = 1 << _width;
        if (attached()) {
            ledcDetach((uint8_t)_pin);
            ledcAttach((uint8_t)_pin, (uint32_t)_hz, (uint8_t)_width);
        }
    }

    int usToTicks(int usec) { return (int)((double)usec / ((double)REFRESH_USEC / (double)_widthTicks) * ((double)_hz / 50.0)); }
    int ticksToUs(int ticks) { return (int)((double)ticks * ((double)REFRESH_USEC / (double)_widthTicks) / ((double)_hz / 50.0)); }

private:
    /* The library returns its LEDC channel; channels are handed out in attach order. */
    static int channelOf(int pin) {
        static int pins[16] = {-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1};
        for (int i = 0; i < 16; i++)
            if (pins[i] == pin) return i;
        for (int i = 0; i < 16; i++)
            if (pins[i] < 0) {
                pins[i] = pin;
                return i;
            }
        return 0;
    }

    int _pin;
    int _minUs;
    int _maxUs;
    int _ticks;
    int _width;
    int _widthTicks;
    int _hz;
};
#endif
