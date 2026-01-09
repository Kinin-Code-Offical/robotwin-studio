#pragma once
#include <Arduino.h>

#include <vector>
#include <algorithm>

class TwoWire
{
public:
    std::vector<uint8_t> _readBuffer;
    size_t _readIndex = 0;

    void begin() {}
    void beginTransmission(uint8_t address) {}
    void write(uint8_t data) {}
    uint8_t endTransmission(bool sendStop = true) { return 0; }

    // Test Helper: Inject data to be read
    void _pushReadData(uint8_t b)
    {
        _readBuffer.push_back(b);
    }
    void _clearReadData()
    {
        _readBuffer.clear();
        _readIndex = 0;
    }

    uint8_t requestFrom(uint8_t address, uint8_t quantity)
    {
        return (uint8_t)(std::min)((size_t)quantity, _readBuffer.size() - _readIndex);
    }

    int read()
    {
        if (_readIndex < _readBuffer.size())
            return _readBuffer[_readIndex++];
        return -1;
    }

    int available()
    {
        return (int)(_readBuffer.size() - _readIndex);
    }

    void setClock(uint32_t freq) {} // Added for U1 compatibility
};

extern TwoWire Wire;
