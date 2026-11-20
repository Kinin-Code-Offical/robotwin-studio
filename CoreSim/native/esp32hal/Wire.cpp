/* TwoWire -> host I2cBus. endTransmission() returns 0 ok, 2 address NACK, 3 data NACK, 4 other error. */
#include "Wire.h"
#include "rw_internal.h"

void rw_hal_pin_function(int pin, int mode, int level);

TwoWire Wire(0);
TwoWire Wire1(1);

TwoWire::TwoWire(uint8_t bus_num)
    : _bus(bus_num), _sda(-1), _scl(-1), _frequency(100000), _begun(false), _timeOutMillis(50), _txAddress(0),
      _transmitting(false), _txLength(0), _rxLength(0), _rxIndex(0), _lastError(0) {}

bool TwoWire::setPins(int sda, int scl) {
    if (_begun) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][Wire.cpp:70] setPins(): bus already initialized. change pins only when not.",
                (unsigned long)(rw::uptime_ns() / 1000000ull));
        return false;
    }

    _sda = sda;
    _scl = scl;
    return true;
}

bool TwoWire::begin(int sda, int scl, uint32_t frequency) {
    rw::charge(rw::cost(50000.0));
    if (sda >= 0) _sda = sda;
    if (scl >= 0) _scl = scl;
    if (_sda < 0) _sda = _bus == 0 ? 21 : 33;
    if (_scl < 0) _scl = _bus == 0 ? 22 : 32;
    if (frequency) _frequency = frequency;
    _begun = true;
    // SDA/SCL become open-drain with the internal pull-ups enabled: they idle high.
    rw_hal_pin_function(_sda, OUTPUT_OPEN_DRAIN | PULLUP, 1);
    rw_hal_pin_function(_scl, OUTPUT_OPEN_DRAIN | PULLUP, 1);
    return true;
}

bool TwoWire::begin(uint8_t slaveAddr, int sda, int scl, uint32_t frequency) {
    rw::log_once(0x600 + _bus, RW_LOG_WARN, "[%6lu][W][Wire.cpp:300] begin(): I2C slave mode (address 0x%02X) is not simulated; using master mode",
                 (unsigned long)(rw::uptime_ns() / 1000000ull), slaveAddr);
    return begin(sda, scl, frequency);
}

bool TwoWire::end() {
    _begun = false;
    return true;
}

bool TwoWire::setClock(uint32_t frequency) {
    if (frequency == 0) return false;
    _frequency = frequency > 1000000u ? 1000000u : frequency;
    return true;
}

uint32_t TwoWire::getClock() { return _frequency; }

void TwoWire::chargeBytes(size_t bytes) {
    // START + bytes (8 data bits + ACK) + STOP, plus driver overhead.
    double ns = (double)bytes * 9.0 * 1e9 / (double)(_frequency ? _frequency : 100000) + 20000.0;
    rw::charge((uint64_t)ns);
}

void TwoWire::beginTransmission(uint16_t address) {
    _txAddress = address;
    _txLength = 0;
    _transmitting = true;
}

size_t TwoWire::write(uint8_t data) {
    if (!_transmitting) return 0;
    if (_txLength >= I2C_BUFFER_LENGTH) return 0;
    _tx[_txLength++] = data;
    return 1;
}

size_t TwoWire::write(const uint8_t* data, size_t quantity) {
    size_t n = 0;
    for (size_t i = 0; i < quantity; i++) {
        if (!write(data[i])) break;
        n++;
    }

    return n;
}

uint8_t TwoWire::endTransmission(bool sendStop) {
    if (!_begun) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][Wire.cpp:500] endTransmission(): Wire.begin() was not called",
                (unsigned long)(rw::uptime_ns() / 1000000ull));
        _lastError = 4;
        return 4;
    }

    if (!_transmitting) {
        _lastError = 4;
        return 4;
    }

    _transmitting = false;
    uint8_t result = 0;
    size_t sent = 0;
    if (rw::has_api && rw::api.i2c_start) {
        rw::ApiScope scope;
        if (!rw::api.i2c_start(rw::api.user, _bus, _txAddress & 0x7F, 0)) {
            result = 2;
        } else {
            for (size_t i = 0; i < _txLength; i++) {
                sent++;
                if (!rw::api.i2c_write(rw::api.user, _bus, _tx[i])) {
                    result = 3;
                    break;
                }
            }
        }

        if (sendStop || result != 0) rw::api.i2c_stop(rw::api.user, _bus);
    } else {
        result = 2;
    }

    chargeBytes(sent + 1);
    _txLength = 0;
    _lastError = result;
    return result;
}

size_t TwoWire::requestFrom(uint16_t address, size_t size, bool sendStop) {
    _rxIndex = 0;
    _rxLength = 0;
    if (!_begun) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][Wire.cpp:450] requestFrom(): Wire.begin() was not called",
                (unsigned long)(rw::uptime_ns() / 1000000ull));
        _lastError = 4;
        return 0;
    }

    if (size > I2C_BUFFER_LENGTH) size = I2C_BUFFER_LENGTH;
    size_t got = 0;
    if (rw::has_api && rw::api.i2c_start) {
        rw::ApiScope scope;
        if (rw::api.i2c_start(rw::api.user, _bus, address & 0x7F, 1)) {
            for (size_t i = 0; i < size; i++) {
                _rx[i] = (uint8_t)rw::api.i2c_read(rw::api.user, _bus, i + 1 < size ? 1 : 0);
                got++;
            }

            if (sendStop) rw::api.i2c_stop(rw::api.user, _bus);
            _lastError = 0;
        } else {
            rw::api.i2c_stop(rw::api.user, _bus);
            _lastError = 2;
        }
    }

    chargeBytes(got + 1);
    _rxLength = got;
    return got;
}

int TwoWire::available(void) { return (int)(_rxLength - _rxIndex); }

int TwoWire::read(void) {
    if (_rxIndex >= _rxLength) return -1;
    return _rx[_rxIndex++];
}

int TwoWire::peek(void) {
    if (_rxIndex >= _rxLength) return -1;
    return _rx[_rxIndex];
}

void TwoWire::flush(void) {
    _rxIndex = 0;
    _rxLength = 0;
    _txLength = 0;
}

void TwoWire::onReceive(void (*)(int)) {
    rw::log_once(0x610 + _bus, RW_LOG_WARN, "[W][Wire.cpp] onReceive(): I2C slave mode is not simulated");
}

void TwoWire::onRequest(void (*)(void)) {
    rw::log_once(0x620 + _bus, RW_LOG_WARN, "[W][Wire.cpp] onRequest(): I2C slave mode is not simulated");
}

char* TwoWire::getErrorText(uint8_t err) {
    static char text[32];
    static const char* const names[] = {"OK", "DEVICE", "ACK", "TIMEOUT", "BUS", "BUSY", "MEMORY", "CONTINUE", "NO_BEGIN"};
    snprintf(text, sizeof text, "%s", err < 9 ? names[err] : "UNKNOWN");
    return text;
}
