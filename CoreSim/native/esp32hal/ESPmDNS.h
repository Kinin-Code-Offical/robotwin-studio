/*
 * RoboTwin ESP32 simulation - Arduino-ESP32's ESPmDNS (MDNSResponder) on the world's radio medium: MDNS.begin makes the
 * board answer to "name.local" on its network, addService announces _type._proto with its port, queryHost and
 * queryService find the other boards (fast-mode ones and the emulated boards' own responders alike). See rw_mdns.cpp.
 */
#pragma once
#include "Arduino.h"
#include "IPAddress.h"
#include "WString.h"

class MDNSResponder {
public:
    bool begin(const char* hostName);
    bool begin(const String& hostName) { return begin(hostName.c_str()); }
    void end();
    void setInstanceName(const char*) {}
    void setInstanceName(const String&) {}
    bool addService(const char* service, const char* proto, uint16_t port);
    bool addService(const String& service, const String& proto, uint16_t port) { return addService(service.c_str(), proto.c_str(), port); }
    bool addServiceTxt(const char*, const char*, const char*, const char*) { return _begun; }
    void addServiceTxt(const String&, const String&, const String&, const String&) {}
    void enableArduino(uint16_t port = 3232, bool auth = false) { addService("arduino", "tcp", port); (void)auth; }
    void disableArduino() {}
    void enableWorkstation(int = 0) {}
    void disableWorkstation() {}
    IPAddress queryHost(const char* host, uint32_t timeout = 2000);
    IPAddress queryHost(const String& host, uint32_t timeout = 2000) { return queryHost(host.c_str(), timeout); }
    int queryService(const char* service, const char* proto);
    int queryService(const String& service, const String& proto) { return queryService(service.c_str(), proto.c_str()); }
    String hostname(int idx);
    IPAddress address(int idx);
    IPAddress IP(int idx) { return address(idx); }
    uint16_t port(int idx);
    int numTxt(int) { return 0; }
    bool hasTxt(int, const char*) { return false; }
    String txt(int, const char*) { return String(); }

private:
    bool _begun = false;
    struct Result {
        char host[64];
        uint32_t ip;
        uint16_t port;
    };
    Result _results[16];
    int _count = 0;
};

extern MDNSResponder MDNS;
