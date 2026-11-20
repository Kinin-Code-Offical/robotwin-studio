/* SPI master: bytes go to the simulator host, which exchanges them with the device whose CS GPIO is
   driven low and whose SCK/MOSI/MISO are really wired to the pins given to begin(). Nothing selected
   (or nothing wired) reads 0xFF like a floating MISO line on a real board. Bus time follows the clock. */
#ifndef RW_SPI_H
#define RW_SPI_H
#include "Arduino.h"

#define SPI_MODE0 0
#define SPI_MODE1 1
#define SPI_MODE2 2
#define SPI_MODE3 3
#define SPI_LSBFIRST 0
#define SPI_MSBFIRST 1
#define FSPI 1
#define HSPI 2
#define VSPI 3

class SPISettings {
public:
    SPISettings() : _clock(1000000), _bitOrder(SPI_MSBFIRST), _dataMode(SPI_MODE0) {}
    SPISettings(uint32_t clock, uint8_t bitOrder, uint8_t dataMode) : _clock(clock), _bitOrder(bitOrder), _dataMode(dataMode) {}
    uint32_t _clock;
    uint8_t _bitOrder;
    uint8_t _dataMode;
};

void rw_spi_notice(void);
int rw_spi_begin(int bus, int sck, int miso, int mosi, int ss);
void rw_spi_end(int bus);
void rw_spi_xfer(int bus, uint32_t hz, int mode, int lsb_first, const uint8_t* out, uint8_t* in, uint32_t n);

class SPIClass {
public:
    explicit SPIClass(uint8_t spi_bus = HSPI) : _bus(spi_bus), _freq(1000000), _mode(SPI_MODE0), _order(SPI_MSBFIRST), _ss(-1), _hwCs(false) {}
    bool begin(int8_t sck = -1, int8_t miso = -1, int8_t mosi = -1, int8_t ss = -1) {
        // Default pins of the ESP32 SPI hosts (Arduino-ESP32 pins_arduino.h).
        const bool hspi = _bus == HSPI;
        if (sck < 0) sck = hspi ? 14 : 18;
        if (miso < 0) miso = hspi ? 12 : 19;
        if (mosi < 0) mosi = hspi ? 13 : 23;
        if (ss < 0) ss = hspi ? 15 : 5;
        _ss = ss;
        rw_spi_begin(_bus, sck, miso, mosi, ss);
        return true;
    }
    void end() { rw_spi_end(_bus); }
    void setHwCs(bool use) {
        _hwCs = use;
        if (use && _ss >= 0) { pinMode(_ss, OUTPUT); digitalWrite(_ss, HIGH); }
    }
    void setBitOrder(uint8_t order) { _order = order; }
    void setDataMode(uint8_t mode) { _mode = mode & 3; }
    void setFrequency(uint32_t freq) { _freq = freq ? freq : 1000000; }
    void setClockDivider(uint32_t div) { _freq = 80000000u / (div ? div : 1); }
    void beginTransaction(SPISettings settings) {
        _freq = settings._clock ? settings._clock : 1000000;
        _order = settings._bitOrder;
        _mode = settings._dataMode & 3;
    }
    void endTransaction(void) {}
    uint8_t transfer(uint8_t data) {
        uint8_t in = 0xFF;
        xfer(&data, &in, 1);
        return in;
    }
    uint16_t transfer16(uint16_t data) {
        uint8_t out[2], in[2];
        if (_order == SPI_MSBFIRST) { out[0] = (uint8_t)(data >> 8); out[1] = (uint8_t)data; }
        else { out[0] = (uint8_t)data; out[1] = (uint8_t)(data >> 8); }
        xfer(out, in, 2);
        return _order == SPI_MSBFIRST ? (uint16_t)((in[0] << 8) | in[1]) : (uint16_t)((in[1] << 8) | in[0]);
    }
    uint32_t transfer32(uint32_t data) {
        uint8_t out[4], in[4];
        for (int i = 0; i < 4; i++) out[i] = _order == SPI_MSBFIRST ? (uint8_t)(data >> (24 - 8 * i)) : (uint8_t)(data >> (8 * i));
        xfer(out, in, 4);
        uint32_t r = 0;
        for (int i = 0; i < 4; i++) r |= _order == SPI_MSBFIRST ? ((uint32_t)in[i] << (24 - 8 * i)) : ((uint32_t)in[i] << (8 * i));
        return r;
    }
    void transfer(void* data, uint32_t size) {
        if (data) xfer(static_cast<uint8_t*>(data), static_cast<uint8_t*>(data), size);
    }
    void transferBytes(const uint8_t* data, uint8_t* out, uint32_t size) { xfer(data, out, size); }
    void writeBytes(const uint8_t* data, uint32_t size) { xfer(data, nullptr, size); }
    void write(uint8_t data) { xfer(&data, nullptr, 1); }
    void write16(uint16_t data) { transfer16(data); }
    void write32(uint32_t data) { transfer32(data); }
    void writePixels(const void* data, uint32_t size) { xfer(static_cast<const uint8_t*>(data), nullptr, size); }

private:
    uint8_t _bus;
    uint32_t _freq;
    uint8_t _mode;
    uint8_t _order;
    int8_t _ss;
    bool _hwCs;
    void xfer(const uint8_t* out, uint8_t* in, uint32_t n) {
        if (_hwCs && _ss >= 0) digitalWrite(_ss, LOW);
        rw_spi_xfer(_bus, _freq, _mode, _order == SPI_LSBFIRST, out, in, n);
        delayMicroseconds((uint32_t)((uint64_t)n * 8u * 1000000u / _freq));
        if (_hwCs && _ss >= 0) digitalWrite(_ss, HIGH);
    }
};

extern SPIClass SPI;
#endif
