/*
 * [mdns] ESPmDNS for the fast mode: the board's name and services are kept by the world's radio medium, which answers
 * the other boards' queries for them (emulated boards' real mDNS packets included) and reads the emulated boards'
 * announcements so that queryHost / queryService here find them too. A query takes the time a responder's answer
 * would (a few ms for a name; a service query collects for its whole 3 s window), and a name nobody answers to the
 * timeout, as the real library waits.
 */
#include "ESPmDNS.h"
#include "rw_internal.h"
#include <string.h>

using namespace rw;

MDNSResponder MDNS;

bool MDNSResponder::begin(const char* hostName) {
    charge(cost(20000.0));
    char buf[64] = {0};
    if (hostName) strncpy(buf, hostName, 63);
    _begun = radio(RW_RADIO_MDNS_BEGIN, buf, sizeof buf, nullptr, 0) == 0;
    return _begun;
}

void MDNSResponder::end() {
    radio(RW_RADIO_MDNS_END, nullptr, 0, nullptr, 0);
    _begun = false;
}

bool MDNSResponder::addService(const char* service, const char* proto, uint16_t port) {
    if (!_begun || !service || !proto) return false;
    unsigned char buf[42] = {0};
    strncpy((char*)buf, service[0] == '_' ? service + 1 : service, 31);
    strncpy((char*)buf + 32, proto[0] == '_' ? proto + 1 : proto, 7);
    buf[40] = (unsigned char)(port & 0xFF);
    buf[41] = (unsigned char)(port >> 8);
    return radio(RW_RADIO_MDNS_ADD_SERVICE, buf, sizeof buf, nullptr, 0) == 0;
}

IPAddress MDNSResponder::queryHost(const char* host, uint32_t timeout) {
    char buf[64] = {0};
    if (host) strncpy(buf, host, 63);
    uint32_t ip = 0;
    if (radio(RW_RADIO_MDNS_QUERY_HOST, buf, sizeof buf, &ip, sizeof ip) == 1) {
        delay(5);   // the responder's answer on the air
        return IPAddress(ip);
    }

    delay(timeout);   // nobody answered: the library waits out its timeout
    return IPAddress();
}

int MDNSResponder::queryService(const char* service, const char* proto) {
    unsigned char in[40] = {0};
    if (!service || !proto) return 0;
    strncpy((char*)in, service[0] == '_' ? service + 1 : service, 31);
    strncpy((char*)in + 32, proto[0] == '_' ? proto + 1 : proto, 7);
    static unsigned char out[16 * 72];
    int n = radio(RW_RADIO_MDNS_QUERY_SVC, in, sizeof in, out, sizeof out);
    if (n < 0) n = 0;
    _count = n > 16 ? 16 : n;
    for (int k = 0; k < _count; k++) {
        memcpy(_results[k].host, out + k * 72, 64);
        _results[k].host[63] = 0;
        memcpy(&_results[k].ip, out + k * 72 + 64, 4);
        _results[k].port = (uint16_t)(out[k * 72 + 68] | out[k * 72 + 69] << 8);
    }

    delay(3000);   // the library collects answers for its whole 3 s window (mdns_query_ptr), found or not
    return _count;
}

String MDNSResponder::hostname(int idx) { return idx >= 0 && idx < _count ? String(_results[idx].host) : String(); }
IPAddress MDNSResponder::address(int idx) { return idx >= 0 && idx < _count ? IPAddress(_results[idx].ip) : IPAddress(); }
uint16_t MDNSResponder::port(int idx) { return idx >= 0 && idx < _count ? _results[idx].port : 0; }
