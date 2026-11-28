/*
 * RoboTwin ESP32 simulation - the core's DNS server (DNSServer.h), as captive portals use it: on a UDP port it answers
 * each A query for its domain ("*" for any) with one address, and others with the error reply code (NonExistentDomain by
 * default). Queries come over the world's UDP from other boards; the fast mode's own lookups take dotted addresses only.
 */
#pragma once
#include "Arduino.h"
#include "WiFi.h"
#include "WiFiUdp.h"

#define DNS_QR_QUERY 0
#define DNS_QR_RESPONSE 1
#define DNS_OPCODE_QUERY 0
#define DNS_DEFAULT_TTL 60

enum class DNSReplyCode : unsigned char {
    NoError = 0,
    FormError = 1,
    ServerFailure = 2,
    NonExistentDomain = 3,
    NotImplemented = 4,
    Refused = 5,
    YXDomain = 6,
    YXRRSet = 7,
    NXRRSet = 8,
};

class DNSServer {
public:
    DNSServer() {}
    ~DNSServer() { stop(); }
    void processNextRequest();
    void setErrorReplyCode(const DNSReplyCode& replyCode) { _errorReplyCode = replyCode; }
    void setTTL(const uint32_t& ttl) { _ttl = ttl; }
    bool start() { return start(53, "*", WiFi.softAPIP()); }
    bool start(const uint16_t& port, const String& domainName, const IPAddress& resolvedIP);
    void stop();

private:
    WiFiUDP _udp;
    uint16_t _port = 0;
    String _domain;
    IPAddress _ip;
    uint32_t _ttl = DNS_DEFAULT_TTL;
    DNSReplyCode _errorReplyCode = DNSReplyCode::NonExistentDomain;
};
