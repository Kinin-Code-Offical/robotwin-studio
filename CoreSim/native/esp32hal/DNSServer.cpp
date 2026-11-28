/* RoboTwin ESP32 simulation - DNSServer (see DNSServer.h). */
#include "DNSServer.h"
#include "rw_internal.h"

namespace {

String lower(const String& s) {
    String out = s;
    out.toLowerCase();
    return out;
}

}  // namespace

bool DNSServer::start(const uint16_t& port, const String& domainName, const IPAddress& resolvedIP) {
    _port = port;
    _domain = lower(domainName);
    if (_domain.startsWith("www.")) _domain = _domain.substring(4);
    _ip = resolvedIP;
    return _udp.begin(port) == 1;
}

void DNSServer::stop() {
    if (_port) _udp.stop();
    _port = 0;
}

void DNSServer::processNextRequest() {
    rw::charge(rw::cost(1500.0));
    int size = _udp.parsePacket();
    if (size < 12 || size > 512) return;
    uint8_t q[512];
    int n = _udp.read(q, (size_t)size);
    if (n < 12) return;
    bool query = (q[2] & 0x80) == 0, opcodeQuery = ((q[2] >> 3) & 0x0F) == DNS_OPCODE_QUERY;
    int questions = q[4] << 8 | q[5];

    // the question's name, as dotted text, and where it ends
    String name;
    int pos = 12;
    while (pos < n && q[pos] != 0) {
        int len = q[pos];
        if (len > 63 || pos + 1 + len > n) return;
        if (name.length()) name += '.';
        for (int i = 0; i < len; i++) name += (char)q[pos + 1 + i];
        pos += 1 + len;
    }

    pos++;   // the root label
    if (pos + 4 > n) return;
    int type = q[pos] << 8 | q[pos + 1];
    int questionEnd = pos + 4;
    String asked = lower(name);
    if (asked.startsWith("www.")) asked = asked.substring(4);
    bool ours = query && opcodeQuery && questions == 1 && type == 1 && (_domain == "*" || asked == _domain);

    uint8_t r[512];
    memcpy(r, q, (size_t)questionEnd);
    r[2] = 0x80 | (q[2] & 0x79) | 0x04;   // a response, authoritative, the query's opcode and RD
    r[3] = ours ? 0x00 : (uint8_t)_errorReplyCode;
    r[4] = 0;
    r[5] = questions > 0 ? 1 : 0;
    r[6] = 0;
    r[7] = ours ? 1 : 0;                   // one answer
    r[8] = r[9] = r[10] = r[11] = 0;
    int len = questionEnd;
    if (ours) {
        const uint8_t answer[] = {0xC0, 0x0C,                          // the name: a pointer to the question's
                                  0x00, 0x01, 0x00, 0x01,              // A, IN
                                  (uint8_t)(_ttl >> 24), (uint8_t)(_ttl >> 16), (uint8_t)(_ttl >> 8), (uint8_t)_ttl,
                                  0x00, 0x04, _ip[0], _ip[1], _ip[2], _ip[3]};
        memcpy(r + len, answer, sizeof answer);
        len += (int)sizeof answer;
    }

    _udp.beginPacket(_udp.remoteIP(), _udp.remotePort());
    _udp.write(r, (size_t)len);
    _udp.endPacket();
}
