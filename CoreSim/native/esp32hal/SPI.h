/* SPI - not simulated electrically: transfers return 0xFF (nothing connected). Present so sketches compile. */
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

class SPIClass {
public:
    explicit SPIClass(uint8_t spi_bus = HSPI) : _bus(spi_bus), _freq(1000000) {}
    bool begin(int8_t sck = -1, int8_t miso = -1, int8_t mosi = -1, int8_t ss = -1) {
        (void)sck; (void)miso; (void)mosi; (void)ss;
        rw_spi_notice();
        return true;
    }
    void end() {}
    void setHwCs(bool) {}
    void setBitOrder(uint8_t) {}
    void setDataMode(uint8_t) {}
    void setFrequency(uint32_t freq) { _freq = freq ? freq : 1000000; }
    void setClockDivider(uint32_t) {}
    void beginTransaction(SPISettings settings) { _freq = settings._clock ? settings._clock : 1000000; }
    void endTransaction(void) {}
    uint8_t transfer(uint8_t) { busTime(1); return 0xFF; }
    uint16_t transfer16(uint16_t) { busTime(2); return 0xFFFF; }
    uint32_t transfer32(uint32_t) { busTime(4); return 0xFFFFFFFFu; }
    void transfer(void* data, uint32_t size) {
        if (data) memset(data, 0xFF, size);
        busTime(size);
    }
    void transferBytes(const uint8_t*, uint8_t* out, uint32_t size) {
        if (out) memset(out, 0xFF, size);
        busTime(size);
    }
    void writeBytes(const uint8_t*, uint32_t size) { busTime(size); }
    void write(uint8_t) { busTime(1); }
    void write16(uint16_t) { busTime(2); }
    void write32(uint32_t) { busTime(4); }
    void writePixels(const void*, uint32_t size) { busTime(size); }

private:
    uint8_t _bus;
    uint32_t _freq;
    void busTime(uint32_t bytes) { delayMicroseconds((uint32_t)((uint64_t)bytes * 8u * 1000000u / _freq)); }
};

extern SPIClass SPI;
#endif
