/* Small optional libraries: EEPROM (RAM backed) and the SPI master (through the host). */
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
    rw::log_once(0x800, RW_LOG_WARN, "[%6lu][W][SPI.cpp] begin(): this simulator host has no SPI bus; transfers read 0xFF",
                 (unsigned long)(rw::uptime_ns() / 1000000ull));
}

/* SPI through the host request channel (ops 0x60-0x62, see Esp32RadioOp). */
int rw_spi_begin(int bus, int sck, int miso, int mosi, int ss) {
    int32_t in[5] = {bus, sck, miso, mosi, ss};
    int rc = rw::radio(0x60, in, (int)sizeof(in), nullptr, 0);
    if (rc == RW_RADIO_UNSUPPORTED) rw_spi_notice();
    return rc;
}

void rw_spi_end(int bus) {
    int32_t in[1] = {bus};
    rw::radio(0x62, in, (int)sizeof(in), nullptr, 0);
}

void rw_spi_xfer(int bus, uint32_t hz, int mode, int lsb_first, const uint8_t* out, uint8_t* in, uint32_t n) {
    static uint8_t req[12 + 1024];
    static uint8_t rsp[1024];
    while (n > 0) {
        uint32_t k = n > 1024 ? 1024 : n;
        int32_t b = bus;
        uint16_t kk = (uint16_t)k;
        memcpy(req, &b, 4);
        memcpy(req + 4, &hz, 4);
        req[8] = (uint8_t)mode;
        req[9] = (uint8_t)(lsb_first ? 1 : 0);
        memcpy(req + 10, &kk, 2);
        if (out) memcpy(req + 12, out, k);
        else memset(req + 12, 0xFF, k);   // write-only calls clock out whatever is in the buffer; MOSI idles high
        int rc = rw::radio(0x61, req, (int)(12 + k), rsp, (int)k);
        if (in) {
            if (rc == (int)k) memcpy(in, rsp, k);
            else memset(in, 0xFF, k);
        }

        if (rc == RW_RADIO_UNSUPPORTED) rw_spi_notice();
        if (out) out += k;
        if (in) in += k;
        n -= k;
    }
}
