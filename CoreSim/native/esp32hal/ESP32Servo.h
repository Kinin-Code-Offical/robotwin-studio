/* Servo for ESP32 (ESP32Servo-compatible subset): 50 Hz LEDC PWM, 544-2400 us pulses by default. */
#ifndef RW_ESP32_SERVO_H
#define RW_ESP32_SERVO_H
#include "Arduino.h"

#define MIN_PULSE_WIDTH 544
#define MAX_PULSE_WIDTH 2400
#define DEFAULT_PULSE_WIDTH 1500
#define DEFAULT_uS_LOW 544
#define DEFAULT_uS_HIGH 2400

class Servo {
public:
    Servo() : _pin(-1), _minUs(MIN_PULSE_WIDTH), _maxUs(MAX_PULSE_WIDTH), _us(DEFAULT_PULSE_WIDTH), _hz(50) {}
    int attach(int pin) { return attach(pin, MIN_PULSE_WIDTH, MAX_PULSE_WIDTH); }
    int attach(int pin, int min, int max) {
        if (pin < 0 || pin > 33) return 0;
        _pin = pin;
        _minUs = min < 500 ? 500 : min;
        _maxUs = max > 2500 ? 2500 : max;
        if (!ledcAttach((uint8_t)pin, (uint32_t)_hz, 16)) {
            _pin = -1;
            return 0;
        }
        writeMicroseconds(_us);
        return 1;
    }
    void detach() {
        if (_pin >= 0) ledcDetach((uint8_t)_pin);
        _pin = -1;
    }
    void write(int value) {
        if (value < MIN_PULSE_WIDTH) {
            if (value < 0) value = 0;
            if (value > 180) value = 180;
            value = (int)map(value, 0, 180, _minUs, _maxUs);
        }
        writeMicroseconds(value);
    }
    void writeMicroseconds(int us) {
        if (us < _minUs) us = _minUs;
        if (us > _maxUs) us = _maxUs;
        _us = us;
        if (_pin >= 0) ledcWrite((uint8_t)_pin, (uint32_t)((uint64_t)us * 65536u * (uint32_t)_hz / 1000000u));
    }
    int read() { return (int)map(_us, _minUs, _maxUs, 0, 180); }
    int readMicroseconds() { return _us; }
    bool attached() { return _pin >= 0; }
    void setPeriodHertz(int hz) {
        if (hz > 0) _hz = hz;
    }

private:
    int _pin;
    int _minUs;
    int _maxUs;
    int _us;
    int _hz;
};

class ESP32PWM {
public:
    static void allocateTimer(int) {}
};
#endif
