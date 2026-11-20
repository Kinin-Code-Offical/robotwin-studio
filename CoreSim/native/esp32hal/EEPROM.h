/* EEPROM emulation (RAM only in the simulator: contents are lost on reset/brown-out). */
#ifndef RW_EEPROM_H
#define RW_EEPROM_H
#include "Arduino.h"

class EEPROMClass {
public:
    EEPROMClass() : _data(nullptr), _size(0), _dirty(false) {}
    bool begin(size_t size);
    void end();
    uint8_t read(int address) { return (_data && address >= 0 && (size_t)address < _size) ? _data[address] : 0; }
    void write(int address, uint8_t val) {
        if (_data && address >= 0 && (size_t)address < _size) {
            _data[address] = val;
            _dirty = true;
        }
    }
    void update(int address, uint8_t val) {
        if (read(address) != val) write(address, val);
    }
    bool commit() {
        _dirty = false;
        return _data != nullptr;
    }
    uint8_t* getDataPtr() {
        _dirty = true;
        return _data;
    }
    const uint8_t* getConstDataPtr() const { return _data; }
    uint16_t length() { return (uint16_t)_size; }
    uint8_t operator[](int address) { return read(address); }

    template <typename T> T& get(int address, T& t) {
        if (_data && address >= 0 && (size_t)address + sizeof(T) <= _size) memcpy((uint8_t*)&t, _data + address, sizeof(T));
        return t;
    }
    template <typename T> const T& put(int address, const T& t) {
        if (_data && address >= 0 && (size_t)address + sizeof(T) <= _size) {
            memcpy(_data + address, (const uint8_t*)&t, sizeof(T));
            _dirty = true;
        }
        return t;
    }

    uint8_t readByte(int address) { return read(address); }
    size_t writeByte(int address, uint8_t value) {
        write(address, value);
        return 1;
    }
    int32_t readInt(int address) {
        int32_t v = 0;
        return get(address, v);
    }
    size_t writeInt(int address, int32_t value) {
        put(address, value);
        return 4;
    }
    float readFloat(int address) {
        float v = 0;
        return get(address, v);
    }
    size_t writeFloat(int address, float value) {
        put(address, value);
        return 4;
    }

private:
    uint8_t* _data;
    size_t _size;
    bool _dirty;
};

extern EEPROMClass EEPROM;
#endif
