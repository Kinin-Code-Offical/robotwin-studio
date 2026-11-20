/*
 * TwoWire (I2C master) for the RoboTwin simulation. Transfers go byte by byte to the
 * host's RobotTwin.CoreSim.Firmware.I2cBus (Wire = bus 0, Wire1 = bus 1); the bus time
 * (9 clocks per byte at the configured clock) is charged to the calling task.
 * Slave mode (begin(address), onReceive/onRequest) is not simulated.
 */
#ifndef RW_WIRE_H
#define RW_WIRE_H
#include "Arduino.h"

#ifndef I2C_BUFFER_LENGTH
#define I2C_BUFFER_LENGTH 128
#endif

typedef void (*user_onRequest)(void);
typedef void (*user_onReceive)(int);

typedef enum {
    I2C_ERROR_OK = 0,
    I2C_ERROR_DEV,
    I2C_ERROR_ACK,
    I2C_ERROR_TIMEOUT,
    I2C_ERROR_BUS,
    I2C_ERROR_BUSY,
    I2C_ERROR_MEMORY,
    I2C_ERROR_CONTINUE,
    I2C_ERROR_NO_BEGIN
} i2c_err_t;

class TwoWire : public Stream {
public:
    explicit TwoWire(uint8_t bus_num);
    ~TwoWire() {}

    bool setPins(int sda, int scl);
    bool begin() { return begin(-1, -1, static_cast<uint32_t>(0)); }
    bool begin(int sda, int scl, uint32_t frequency = 0);
    bool begin(uint8_t slaveAddr, int sda, int scl, uint32_t frequency);
    bool begin(int slaveAddr) { return begin((uint8_t)slaveAddr, -1, -1, 0); }
    bool end();

    size_t setBufferSize(size_t bSize) { return bSize; }
    void setTimeOut(uint16_t timeOutMillis) { _timeOutMillis = timeOutMillis; }
    uint16_t getTimeOut() { return _timeOutMillis; }
    bool setClock(uint32_t frequency);
    uint32_t getClock();

    void beginTransmission(uint16_t address);
    void beginTransmission(uint8_t address) { beginTransmission((uint16_t)address); }
    void beginTransmission(int address) { beginTransmission((uint16_t)address); }

    uint8_t endTransmission(bool sendStop);
    uint8_t endTransmission(void) { return endTransmission(true); }

    size_t requestFrom(uint16_t address, size_t size, bool sendStop);
    uint8_t requestFrom(uint16_t address, uint8_t size, bool sendStop) { return (uint8_t)requestFrom(address, (size_t)size, sendStop); }
    uint8_t requestFrom(uint16_t address, uint8_t size, uint8_t sendStop) { return (uint8_t)requestFrom(address, (size_t)size, sendStop != 0); }
    size_t requestFrom(uint8_t address, size_t len, bool stopBit) { return requestFrom((uint16_t)address, len, stopBit); }
    uint8_t requestFrom(uint16_t address, uint8_t size) { return (uint8_t)requestFrom(address, (size_t)size, true); }
    uint8_t requestFrom(uint8_t address, uint8_t size, uint8_t sendStop) { return (uint8_t)requestFrom((uint16_t)address, (size_t)size, sendStop != 0); }
    uint8_t requestFrom(uint8_t address, uint8_t size) { return (uint8_t)requestFrom((uint16_t)address, (size_t)size, true); }
    uint8_t requestFrom(int address, int size, bool sendStop) { return (uint8_t)requestFrom((uint16_t)address, (size_t)size, sendStop); }
    uint8_t requestFrom(int address, int size, int sendStop) { return (uint8_t)requestFrom((uint16_t)address, (size_t)size, sendStop != 0); }
    uint8_t requestFrom(int address, int size) { return (uint8_t)requestFrom((uint16_t)address, (size_t)size, true); }

    size_t write(uint8_t) override;
    size_t write(const uint8_t*, size_t) override;
    int available(void) override;
    int read(void) override;
    int peek(void) override;
    void flush(void) override;

    inline size_t write(const char* s) { return write(reinterpret_cast<const uint8_t*>(s), strlen(s)); }
    inline size_t write(unsigned long n) { return write((uint8_t)n); }
    inline size_t write(long n) { return write((uint8_t)n); }
    inline size_t write(unsigned int n) { return write((uint8_t)n); }
    inline size_t write(int n) { return write((uint8_t)n); }

    void onReceive(void (*)(int));
    void onRequest(void (*)(void));

    uint8_t lastError() { return _lastError; }
    char* getErrorText(uint8_t err);

private:
    uint8_t _bus;
    int _sda;
    int _scl;
    uint32_t _frequency;
    bool _begun;
    uint16_t _timeOutMillis;
    uint16_t _txAddress;
    bool _transmitting;
    uint8_t _tx[I2C_BUFFER_LENGTH];
    size_t _txLength;
    uint8_t _rx[I2C_BUFFER_LENGTH];
    size_t _rxLength;
    size_t _rxIndex;
    uint8_t _lastError;
    void chargeBytes(size_t bytes);
};

extern TwoWire Wire;
extern TwoWire Wire1;

#endif
