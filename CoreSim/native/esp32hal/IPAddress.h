/* IPv4 address (Arduino IPAddress). */
#ifndef RW_IPADDRESS_H
#define RW_IPADDRESS_H
#include "Arduino.h"

class IPAddress : public Printable {
public:
    IPAddress() { _addr.dword = 0; }
    IPAddress(uint8_t a, uint8_t b, uint8_t c, uint8_t d) {
        _addr.bytes[0] = a;
        _addr.bytes[1] = b;
        _addr.bytes[2] = c;
        _addr.bytes[3] = d;
    }
    IPAddress(uint32_t address) { _addr.dword = address; }
    IPAddress(const uint8_t* address) { memcpy(_addr.bytes, address, 4); }
    IPAddress(const char* address) { fromString(address); }

    bool fromString(const char* address);
    bool fromString(const String& address) { return fromString(address.c_str()); }

    operator uint32_t() const { return _addr.dword; }
    bool operator==(const IPAddress& addr) const { return _addr.dword == addr._addr.dword; }
    bool operator!=(const IPAddress& addr) const { return _addr.dword != addr._addr.dword; }
    bool operator==(const uint8_t* addr) const { return memcmp(addr, _addr.bytes, 4) == 0; }

    uint8_t operator[](int index) const { return _addr.bytes[index & 3]; }
    uint8_t& operator[](int index) { return _addr.bytes[index & 3]; }

    IPAddress& operator=(const uint8_t* address) {
        memcpy(_addr.bytes, address, 4);
        return *this;
    }
    IPAddress& operator=(uint32_t address) {
        _addr.dword = address;
        return *this;
    }

    size_t printTo(Print& p) const override;
    String toString() const;

private:
    union {
        uint8_t bytes[4];
        uint32_t dword;
    } _addr;
};

extern const IPAddress INADDR_NONE;
extern const IPAddress IN6ADDR_ANY;
#endif
