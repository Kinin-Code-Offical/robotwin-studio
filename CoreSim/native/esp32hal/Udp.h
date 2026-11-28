/* Arduino's datagram interface (Udp.h): what a library that sends packets over "any UDP" takes (NTPClient, OSC, Art-Net
 * ...). WiFiUDP is one. */
#ifndef RW_UDP_H
#define RW_UDP_H
#include "Stream.h"
#include "IPAddress.h"

class UDP : public Stream {
public:
    virtual uint8_t begin(uint16_t port) = 0;   // 1 when listening on the port
    virtual uint8_t beginMulticast(IPAddress, uint16_t) { return 0; }
    virtual void stop() = 0;
    virtual int beginPacket(IPAddress ip, uint16_t port) = 0;
    virtual int beginPacket(const char* host, uint16_t port) = 0;
    virtual int endPacket() = 0;   // 1 when the packet was sent
    virtual size_t write(uint8_t) = 0;
    virtual size_t write(const uint8_t* buffer, size_t size) = 0;
    virtual int parsePacket() = 0;   // the next packet's size, 0 for none
    virtual int available() = 0;
    virtual int read() = 0;
    virtual int read(unsigned char* buffer, size_t len) = 0;
    virtual int read(char* buffer, size_t len) = 0;
    virtual int peek() = 0;
    virtual void flush() = 0;
    virtual IPAddress remoteIP() = 0;
    virtual uint16_t remotePort() = 0;

protected:
    uint8_t* rawIPAddress(IPAddress& addr) { return addr.raw_address(); }
};
#endif
