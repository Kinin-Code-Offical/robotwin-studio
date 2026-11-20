/* Small optional libraries: EEPROM (RAM backed) and the SPI stub. */
#include "EEPROM.h"
#include "SPI.h"
#include "rw_internal.h"

EEPROMClass EEPROM;
SPIClass SPI(VSPI);

bool EEPROMClass::begin(size_t size) {
    if (size == 0 || size > 4096) return false;
    if (_data && _size == size) return true;
    uint8_t* p = static_cast<uint8_t*>(rw::heap_alloc(size));
    if (!p) return false;
    memset(p, 0xFF, size);  // erased flash reads 0xFF
    if (_data) {
        memcpy(p, _data, _size < size ? _size : size);
        rw::heap_free(_data);
    }

    _data = p;
    _size = size;
    rw::charge(rw::cost(200000.0));
    return true;
}

void EEPROMClass::end() {
    if (_data) rw::heap_free(_data);
    _data = nullptr;
    _size = 0;
}

void rw_spi_notice(void) {
    rw::log_once(0x800, RW_LOG_WARN, "[%6lu][W][SPI.cpp] begin(): SPI devices are not simulated; transfers read 0xFF",
                 (unsigned long)(rw::uptime_ns() / 1000000ull));
}
