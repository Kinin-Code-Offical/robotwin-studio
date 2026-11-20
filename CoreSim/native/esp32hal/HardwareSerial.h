/* HardwareSerial: UART0..2. Output goes to the host's serial monitor, input comes from SendSerial(). */
#ifndef RW_HARDWARE_SERIAL_H
#define RW_HARDWARE_SERIAL_H
#ifdef __cplusplus
#include "Stream.h"

#define SERIAL_5N1 0x8000010
#define SERIAL_6N1 0x8000014
#define SERIAL_7N1 0x8000018
#define SERIAL_8N1 0x800001c
#define SERIAL_5N2 0x8000030
#define SERIAL_6N2 0x8000034
#define SERIAL_7N2 0x8000038
#define SERIAL_8N2 0x800003c
#define SERIAL_5E1 0x8000012
#define SERIAL_6E1 0x8000016
#define SERIAL_7E1 0x800001a
#define SERIAL_8E1 0x800001e
#define SERIAL_5O1 0x8000013
#define SERIAL_6O1 0x8000017
#define SERIAL_7O1 0x800001b
#define SERIAL_8O1 0x800001f

class HardwareSerial : public Stream {
public:
    explicit HardwareSerial(int uart_nr);
    ~HardwareSerial();

    void begin(unsigned long baud, uint32_t config = SERIAL_8N1, int8_t rxPin = -1, int8_t txPin = -1,
               bool invert = false, unsigned long timeout_ms = 20000UL, uint8_t rxfifo_full_thrhd = 112);
    void end(bool fullyTerminate = true);
    void updateBaudRate(unsigned long baud);
    uint32_t baudRate();
    operator bool() const { return true; }

    int available(void) override;
    int availableForWrite(void) override;
    int peek(void) override;
    int read(void) override;
    size_t read(uint8_t* buffer, size_t size);
    size_t read(char* buffer, size_t size) { return read(reinterpret_cast<uint8_t*>(buffer), size); }
    size_t readBytes(char* buffer, size_t length) override;
    size_t readBytes(uint8_t* buffer, size_t length) override { return readBytes(reinterpret_cast<char*>(buffer), length); }
    void flush(void) override;
    void flush(bool txOnly);

    size_t write(uint8_t) override;
    size_t write(const uint8_t* buffer, size_t size) override;
    using Print::write;
    inline size_t write(const char* buffer, size_t size) { return write(reinterpret_cast<const uint8_t*>(buffer), size); }
    inline size_t write(unsigned long n) { return write((uint8_t)n); }
    inline size_t write(long n) { return write((uint8_t)n); }
    inline size_t write(unsigned int n) { return write((uint8_t)n); }
    inline size_t write(int n) { return write((uint8_t)n); }

    size_t setRxBufferSize(size_t new_size);
    size_t setTxBufferSize(size_t new_size);
    void setDebugOutput(bool) {}
    void setRxInvert(bool) {}
    bool setPins(int8_t rxPin, int8_t txPin, int8_t ctsPin = -1, int8_t rtsPin = -1);
    bool setRxTimeout(uint8_t) { return true; }
    bool setRxFIFOFull(uint8_t) { return true; }
    void onReceive(void (*function)(void), bool onlyOnTimeout = false);

    /* Simulator side: bytes arriving from the host. Returns how many fitted into the RX buffer. */
    int rwPush(const uint8_t* data, int length);
    bool rwHasData() const { return _rxCount > 0; }

protected:
    void waitForData(unsigned long deadlineMillis) override;

private:
    int _uart;
    bool _begun;
    uint32_t _baud;
    uint8_t* _rx;
    size_t _rxSize;
    size_t _rxHead;
    size_t _rxTail;
    size_t _rxCount;
    unsigned long long _txDoneNs;
    int _rxPin;
    int _txPin;
    void (*_onReceive)(void);
};

extern HardwareSerial Serial;
extern HardwareSerial Serial1;
extern HardwareSerial Serial2;
#define Serial0 Serial

void serialEvent(void);
void serialEvent1(void);
void serialEvent2(void);

#endif
#endif
