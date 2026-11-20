/*
 * UART model. Output bytes are handed to the host immediately; their transmission
 * time is charged with a 128-byte hardware FIFO draining at the baud rate (a write
 * only blocks when the FIFO is full, as on the ESP32). Input bytes are pushed by the
 * host (rw_serial_rx) into a 256-byte RX buffer (setRxBufferSize to change).
 */
#include "Arduino.h"
#include "rw_internal.h"

void rw_hal_pin_function(int pin, int mode, int level);

namespace {
const size_t kTxFifo = 128;
const size_t kDefaultRx = 256;
const double kCostSerialCall = 200.0;

void noop_serial_event() {}
}  // namespace

/* serialEvent()/serialEvent1()/serialEvent2() are optional in the sketch (weak in the core). */
#pragma comment(linker, "/alternatename:?serialEvent@@YAXXZ=?rw_default_serial_event@@YAXXZ")
#pragma comment(linker, "/alternatename:?serialEvent1@@YAXXZ=?rw_default_serial_event@@YAXXZ")
#pragma comment(linker, "/alternatename:?serialEvent2@@YAXXZ=?rw_default_serial_event@@YAXXZ")
void rw_default_serial_event() { noop_serial_event(); }

HardwareSerial Serial(0);
HardwareSerial Serial1(1);
HardwareSerial Serial2(2);

HardwareSerial::HardwareSerial(int uart_nr)
    : _uart(uart_nr), _begun(false), _baud(0), _rx(nullptr), _rxSize(0), _rxHead(0), _rxTail(0), _rxCount(0),
      _txDoneNs(0), _rxPin(-1), _txPin(-1), _onReceive(nullptr) {}

HardwareSerial::~HardwareSerial() {
    if (_rx) rw::heap_free(_rx);
    _rx = nullptr;
}

void HardwareSerial::begin(unsigned long baud, uint32_t, int8_t rxPin, int8_t txPin, bool, unsigned long, uint8_t) {
    rw::charge(rw::cost(20000.0));
    if (baud == 0) baud = 115200;
    _baud = (uint32_t)baud;
    if (!_rx) {
        _rxSize = kDefaultRx;
        _rx = static_cast<uint8_t*>(rw::heap_alloc(_rxSize));
    }

    _begun = true;
    static const int kDefaultRxPins[3] = {3, 9, 16};
    static const int kDefaultTxPins[3] = {1, 10, 17};
    int u = _uart >= 0 && _uart < 3 ? _uart : 0;
    _rxPin = rxPin >= 0 ? rxPin : kDefaultRxPins[u];
    _txPin = txPin >= 0 ? txPin : kDefaultTxPins[u];
    // The TX pad idles high (UART mark level); RX has its pull-up enabled.
    if (_txPin >= 0 && !(_txPin >= 6 && _txPin <= 11)) rw_hal_pin_function(_txPin, OUTPUT, 1);
    if (_rxPin >= 0 && !(_rxPin >= 6 && _rxPin <= 11)) rw_hal_pin_function(_rxPin, INPUT_PULLUP, 1);
    if (rw::has_api && rw::api.serial_begin) {
        rw::ApiScope s;
        rw::api.serial_begin(rw::api.user, _uart, _baud);
    }
}

void HardwareSerial::end(bool) {
    flush();
    _begun = false;
}

void HardwareSerial::updateBaudRate(unsigned long baud) {
    if (baud) _baud = (uint32_t)baud;
    if (_begun && rw::has_api && rw::api.serial_begin) {
        rw::ApiScope s;
        rw::api.serial_begin(rw::api.user, _uart, _baud);
    }
}

uint32_t HardwareSerial::baudRate() { return _baud; }

int HardwareSerial::available(void) {
    rw::charge(rw::cost(kCostSerialCall));
    return (int)_rxCount;
}

int HardwareSerial::availableForWrite(void) {
    if (!_begun) return 0;
    uint64_t now = rw::now_ns();
    double byte_ns = 1e10 / (double)(_baud ? _baud : 115200);
    double queued = _txDoneNs > now ? (double)(_txDoneNs - now) / byte_ns : 0.0;
    int free_bytes = (int)kTxFifo - (int)(queued + 0.999);
    return free_bytes < 0 ? 0 : free_bytes;
}

int HardwareSerial::peek(void) {
    rw::charge(rw::cost(kCostSerialCall));
    if (_rxCount == 0) return -1;
    return _rx[_rxTail];
}

int HardwareSerial::read(void) {
    rw::charge(rw::cost(kCostSerialCall));
    if (_rxCount == 0) return -1;
    uint8_t c = _rx[_rxTail];
    _rxTail = (_rxTail + 1) % _rxSize;
    _rxCount--;
    return c;
}

size_t HardwareSerial::read(uint8_t* buffer, size_t size) {
    size_t n = 0;
    while (n < size && _rxCount > 0) buffer[n++] = (uint8_t)read();
    return n;
}

size_t HardwareSerial::readBytes(char* buffer, size_t length) { return Stream::readBytes(buffer, length); }

void HardwareSerial::flush(void) {
    if (_txDoneNs > rw::now_ns()) rw::sleep_until(_txDoneNs);
}

void HardwareSerial::flush(bool txOnly) {
    flush();
    if (!txOnly) {
        _rxHead = _rxTail = _rxCount = 0;
    }
}

size_t HardwareSerial::write(uint8_t c) { return write(&c, 1); }

size_t HardwareSerial::write(const uint8_t* buffer, size_t size) {
    rw::charge(rw::cost(kCostSerialCall));
    if (!_begun || !buffer || size == 0) return 0;
    if (rw::has_api && rw::api.serial_write) {
        rw::ApiScope s;
        rw::api.serial_write(rw::api.user, _uart, buffer, (int)size);
    }

    // Transmission time: bytes leave at the baud rate; the caller only waits when the FIFO is full.
    uint64_t now = rw::now_ns();
    uint64_t byte_ns = (uint64_t)(1e10 / (double)(_baud ? _baud : 115200) + 0.5);
    uint64_t start = _txDoneNs > now ? _txDoneNs : now;
    _txDoneNs = start + byte_ns * size;
    uint64_t backlog = _txDoneNs - now;
    uint64_t allowed = byte_ns * kTxFifo;
    if (backlog > allowed) rw::charge(backlog - allowed);
    return size;
}

size_t HardwareSerial::setRxBufferSize(size_t new_size) {
    if (new_size < 16) new_size = 16;
    uint8_t* p = static_cast<uint8_t*>(rw::heap_alloc(new_size));
    if (!p) return 0;
    size_t n = 0;
    while (_rxCount > 0 && n < new_size) {
        p[n++] = _rx[_rxTail];
        _rxTail = (_rxTail + 1) % _rxSize;
        _rxCount--;
    }

    if (_rx) rw::heap_free(_rx);
    _rx = p;
    _rxSize = new_size;
    _rxHead = n % new_size;
    _rxTail = 0;
    _rxCount = n;
    return new_size;
}

size_t HardwareSerial::setTxBufferSize(size_t new_size) { return new_size; }

bool HardwareSerial::setPins(int8_t rxPin, int8_t txPin, int8_t, int8_t) {
    if (rxPin >= 0) _rxPin = rxPin;
    if (txPin >= 0) _txPin = txPin;
    return true;
}

void HardwareSerial::onReceive(void (*function)(void), bool) { _onReceive = function; }

int HardwareSerial::rwPush(const uint8_t* data, int length) {
    if (!_rx) {
        _rxSize = kDefaultRx;
        _rx = static_cast<uint8_t*>(rw::heap_alloc(_rxSize));
        if (!_rx) return 0;
    }

    int n = 0;
    while (n < length && _rxCount < _rxSize) {
        _rx[_rxHead] = data[n++];
        _rxHead = (_rxHead + 1) % _rxSize;
        _rxCount++;
    }

    if (n > 0) {
        rw::notify(this);
        if (_onReceive && _begun) rw::raise_isr([](void* fn) { reinterpret_cast<void (*)(void)>(fn)(); }, reinterpret_cast<void*>(_onReceive));
    }

    return n;
}

void HardwareSerial::waitForData(unsigned long deadlineMillis) {
    uint64_t deadline = rw::ms_to_ns(deadlineMillis) + rw::config.time_origin_ns;
    if (_rxCount > 0) return;
    rw::wait_on(this, deadline);
}

namespace rw {

int serial_rx_push(int port, const uint8_t* data, int length) {
    switch (port) {
    case 0: return Serial.rwPush(data, length);
    case 1: return Serial1.rwPush(data, length);
    case 2: return Serial2.rwPush(data, length);
    default: return 0;
    }
}

void run_serial_events() {
    if (serialEvent != rw_default_serial_event && Serial.rwHasData()) serialEvent();
    if (serialEvent1 != rw_default_serial_event && Serial1.rwHasData()) serialEvent1();
    if (serialEvent2 != rw_default_serial_event && Serial2.rwHasData()) serialEvent2();
}

}  // namespace rw

/* printf()/ets_printf(): ESP32 stdout is UART0 at 115200 even before Serial.begin(). */
extern "C" int rw_printf(const char* format, ...) {
    char loc[256];
    va_list ap;
    va_start(ap, format);
    int len = vsnprintf(loc, sizeof loc, format, ap);
    va_end(ap);
    if (len <= 0) return len;
    char* text = loc;
    char* heap = nullptr;
    if ((size_t)len >= sizeof loc) {
        heap = new char[(size_t)len + 1];
        va_start(ap, format);
        vsnprintf(heap, (size_t)len + 1, format, ap);
        va_end(ap);
        text = heap;
    }

    rw::charge(rw::cost(kCostSerialCall) + (uint64_t)len * 86806ull);
    if (rw::has_api && rw::api.serial_write) {
        rw::ApiScope s;
        rw::api.serial_write(rw::api.user, 0, reinterpret_cast<const uint8_t*>(text), len);
    }

    delete[] heap;
    return len;
}
