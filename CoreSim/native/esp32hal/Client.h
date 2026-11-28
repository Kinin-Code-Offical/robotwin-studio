/* Arduino's network client interface (Client.h): what a library that talks over "any connection" takes (PubSubClient,
 * HTTP and Modbus-TCP clients ...). WiFiClient is one. */
#ifndef RW_CLIENT_H
#define RW_CLIENT_H
#include "Print.h"
#include "Stream.h"
#include "IPAddress.h"

class Client : public Stream {
public:
    virtual int connect(IPAddress ip, uint16_t port) = 0;
    virtual int connect(const char* host, uint16_t port) = 0;
    virtual size_t write(uint8_t) = 0;
    virtual size_t write(const uint8_t* buf, size_t size) = 0;
    virtual int available() = 0;
    virtual int read() = 0;
    virtual int read(uint8_t* buf, size_t size) = 0;
    virtual int peek() = 0;
    virtual void flush() = 0;
    virtual void stop() = 0;
    virtual uint8_t connected() = 0;
    virtual operator bool() = 0;

protected:
    uint8_t* rawIPAddress(IPAddress& addr) { return addr.raw_address(); }
};
#endif
