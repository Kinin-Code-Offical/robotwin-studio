/* WiFi (radio medium or stand-alone stub, see WiFi.h) + IPAddress. */
#include "WiFi.h"
#include "rw_internal.h"

void rw_hal_set_wifi(bool on);

const IPAddress INADDR_NONE(0, 0, 0, 0);
const IPAddress IN6ADDR_ANY(0, 0, 0, 0);
WiFiClass WiFi;

namespace rw {
void radio_reset();
}

namespace {
const uint64_t kConnectDelayNs = 1000000000ull;  // stub: ~1 s from begin() to WL_CONNECTED

void stub_notice() {
    if (rw::radio_available()) return;
    rw::log_once(0x700, RW_LOG_INFO, "WiFi is a stub on a lone board: begin() connects after ~1 s of simulated time, no network traffic is possible (place the board in a RobotWin world for real Wi-Fi)");
}

String mac_string(const uint8_t* mac) {
    char buf[18];
    snprintf(buf, sizeof buf, "%02X:%02X:%02X:%02X:%02X:%02X", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    return String(buf);
}

#pragma pack(push, 1)
struct WifiBeginIn {
    char ssid[33];
    char pass[65];
};
struct SoftApIn {
    char ssid[33];
    char pass[65];
    int32_t channel;
    int32_t max_conn;
};
struct WifiStatusOut {
    int32_t status;
    int32_t rssi;
    uint32_t ip;
    uint32_t gw;
    int32_t channel;
    uint8_t bssid[6];
};
#pragma pack(pop)

bool med() { return rw::radio_available(); }

/* lwIP's TCP/IP task: the 3.x core starts it with the first Wi-Fi mode (Network.begin, through WiFi.mode, begin or softAP). */
bool g_stack_up = false;
}  // namespace

/* A socket before the stack is up: on the chip lwIP takes its core lock, which does not exist yet, and FreeRTOS's assert
   restarts the chip; the real-chip build prints this (in the hardware-compatible mode too). */
static void need_stack() {
    if (g_stack_up) return;
    rw::log(RW_LOG_ERROR, "\r\nassert failed: xQueueSemaphoreTake queue.c:1709 (( pxQueue ))\r\n\r\n\r\nBacktrace: 0x40087b6c:0x3ffb1db0 "
            "0x40087b31:0x3ffb1dd0 0x4008e179:0x3ffb1df0 0x400884d2:0x3ffb1f30 0x400f2a9d:0x3ffb1f70\r\n\r\nRebooting...");
    rw::system_restart(ESP_RST_PANIC);
}

namespace rw {
void hal_wifi_reset() {
    WiFi = WiFiClass();
    g_stack_up = false;
    radio_reset();
}
}  // namespace rw

bool WiFiClass::simulatedMedium() { return med(); }

/* ---------------------------------------------------------------- IPAddress */
bool IPAddress::fromString(const char* address) {
    uint16_t acc = 0;
    uint8_t dots = 0;
    uint8_t bytes[4] = {0, 0, 0, 0};
    if (!address || !*address) return false;
    while (*address) {
        char c = *address++;
        if (c >= '0' && c <= '9') {
            acc = (uint16_t)(acc * 10 + (c - '0'));
            if (acc > 255) return false;
        } else if (c == '.') {
            if (dots == 3) return false;
            bytes[dots++] = (uint8_t)acc;
            acc = 0;
        } else {
            return false;
        }
    }

    if (dots != 3) return false;
    bytes[3] = (uint8_t)acc;
    memcpy(_addr.bytes, bytes, 4);
    return true;
}

size_t IPAddress::printTo(Print& p) const {
    size_t n = 0;
    for (int i = 0; i < 3; i++) {
        n += p.print(_addr.bytes[i], DEC);
        n += p.print('.');
    }

    n += p.print(_addr.bytes[3], DEC);
    return n;
}

String IPAddress::toString() const {
    char buf[16];
    snprintf(buf, sizeof buf, "%u.%u.%u.%u", _addr.bytes[0], _addr.bytes[1], _addr.bytes[2], _addr.bytes[3]);
    return String(buf);
}

/* ---------------------------------------------------------------- WiFiClass */
bool WiFiClass::isConnectedConst() const {
    if (med()) return _medStatus == WL_CONNECTED;
    return _began && (_mode & WIFI_MODE_STA) && rw::now_ns() >= _connectAtNs;
}

void WiFiClass::setRadio() { rw_hal_set_wifi(_mode != WIFI_MODE_NULL); }

void WiFiClass::refresh() {
    if (!med()) return;
    WifiStatusOut s;
    memset(&s, 0, sizeof s);
    if (rw::radio(RW_RADIO_WIFI_STATUS, nullptr, 0, &s, sizeof s) < (int)sizeof s) return;
    _medStatus = s.status;
    _medRssi = s.rssi;
    _medIp = s.ip;
    _medGw = s.gw;
    _medChannel = s.channel;
    memcpy(_medBssid, s.bssid, 6);
}

bool WiFiClass::mode(wifi_mode_t m) {
    rw::charge(rw::cost(1000000.0));
    _mode = m;
    if (m != WIFI_OFF) g_stack_up = true;
    if (!(m & WIFI_MODE_STA)) _began = false;
    if (med()) {
        int32_t v = (int32_t)m;
        rw::radio(RW_RADIO_WIFI_MODE, &v, sizeof v, nullptr, 0);
        if (!(m & WIFI_MODE_AP)) _apSsid[0] = 0;
    }

    setRadio();
    return true;
}

wl_status_t WiFiClass::begin(const char* ssid, const char* passphrase, int32_t, const uint8_t*, bool connect) {
    stub_notice();
    rw::charge(rw::cost(2000000.0));
    if (!ssid || !*ssid || strlen(ssid) > 32) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][WiFiSTA.cpp:120] begin(): SSID too long or missing!", (unsigned long)(rw::uptime_ns() / 1000000ull));
        return WL_CONNECT_FAILED;
    }

    if (passphrase && strlen(passphrase) > 64) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][WiFiSTA.cpp:125] begin(): passphrase too long!", (unsigned long)(rw::uptime_ns() / 1000000ull));
        return WL_CONNECT_FAILED;
    }

    strncpy_s(_ssid, sizeof _ssid, ssid, _TRUNCATE);
    strncpy_s(_psk, sizeof _psk, passphrase ? passphrase : "", _TRUNCATE);
    if (!(_mode & WIFI_MODE_STA)) _mode = (wifi_mode_t)(_mode | WIFI_MODE_STA);
    g_stack_up = true;
    setRadio();
    if (connect) {
        _began = true;
        _connectAtNs = rw::now_ns() + kConnectDelayNs;
        if (med()) {
            WifiBeginIn in;
            memset(&in, 0, sizeof in);
            strncpy_s(in.ssid, sizeof in.ssid, _ssid, _TRUNCATE);
            strncpy_s(in.pass, sizeof in.pass, _psk, _TRUNCATE);
            rw::radio(RW_RADIO_WIFI_BEGIN, &in, sizeof in, nullptr, 0);
            _medStatus = WL_DISCONNECTED;
        }
    }

    return WL_DISCONNECTED;
}

wl_status_t WiFiClass::begin() {
    if (!_ssid[0]) return WL_CONNECT_FAILED;
    char ssid[33];
    char psk[65];
    strncpy_s(ssid, sizeof ssid, _ssid, _TRUNCATE);
    strncpy_s(psk, sizeof psk, _psk, _TRUNCATE);
    return begin(ssid, psk);
}

wl_status_t WiFiClass::status() {
    rw::charge(rw::cost(1000.0));
    if (med()) {
        refresh();
        return (wl_status_t)_medStatus;
    }

    if (_mode == WIFI_MODE_NULL) return WL_STOPPED;
    if (!_began) return WL_IDLE_STATUS;
    return rw::now_ns() >= _connectAtNs ? WL_CONNECTED : WL_DISCONNECTED;
}

bool WiFiClass::disconnect(bool wifioff, bool) {
    _began = false;
    if (med()) {
        rw::radio(RW_RADIO_WIFI_DISCONNECT, nullptr, 0, nullptr, 0);
        _medStatus = WL_DISCONNECTED;
    }

    if (wifioff) mode(WIFI_MODE_NULL);
    return true;
}

bool WiFiClass::reconnect() {
    if (!_ssid[0]) return false;
    begin();
    return true;
}

uint8_t WiFiClass::waitForConnectResult(unsigned long timeoutLength) {
    unsigned long start = millis();
    while (status() != WL_CONNECTED && status() != WL_CONNECT_FAILED && millis() - start < timeoutLength) delay(100);
    return (uint8_t)status();
}

bool WiFiClass::config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1, IPAddress) {
    if ((uint32_t)local_ip != 0) _localIP = local_ip;
    if ((uint32_t)gateway != 0) _gateway = gateway;
    if ((uint32_t)subnet != 0) _subnet = subnet;
    if ((uint32_t)dns1 != 0) _dns = dns1;
    return true;
}

bool WiFiClass::setHostname(const char* hostname) {
    if (!hostname) return false;
    strncpy_s(_hostname, sizeof _hostname, hostname, _TRUNCATE);
    return true;
}

IPAddress WiFiClass::localIP() {
    if (med()) {
        refresh();
        return _medStatus == WL_CONNECTED ? IPAddress(_medIp) : IPAddress();
    }

    return isConnectedConst() ? _localIP : IPAddress();
}

IPAddress WiFiClass::subnetMask() { return isConnectedConst() ? _subnet : IPAddress(); }
IPAddress WiFiClass::gatewayIP() {
    if (med()) return isConnectedConst() ? IPAddress(_medGw) : IPAddress();
    return isConnectedConst() ? _gateway : IPAddress();
}
IPAddress WiFiClass::dnsIP(uint8_t) { return gatewayIP(); }
IPAddress WiFiClass::broadcastIP() {
    IPAddress ip = localIP();
    return (uint32_t)ip ? IPAddress(ip[0], ip[1], ip[2], 255) : IPAddress();
}
IPAddress WiFiClass::networkID() {
    IPAddress ip = localIP();
    return (uint32_t)ip ? IPAddress(ip[0], ip[1], ip[2], 0) : IPAddress();
}

uint8_t* WiFiClass::macAddress(uint8_t* mac) {
    if (mac) memcpy(mac, rw::radio_mac(), 6);
    return mac;
}

String WiFiClass::macAddress() { return mac_string(rw::radio_mac()); }

String WiFiClass::softAPmacAddress() {
    uint8_t mac[6];
    memcpy(mac, rw::radio_mac(), 6);
    mac[5]++;
    return mac_string(mac);
}

String WiFiClass::SSID() const { return isConnectedConst() ? String(_ssid) : String(); }
String WiFiClass::psk() const { return isConnectedConst() ? String(_psk) : String(); }

uint8_t* WiFiClass::BSSID(uint8_t* bssid) {
    static uint8_t ap[6] = {0x5C, 0x49, 0x79, 0x10, 0x20, 0x30};
    if (med()) memcpy(ap, _medBssid, 6);
    if (bssid) {
        memcpy(bssid, ap, 6);
        return bssid;
    }

    return ap;
}

String WiFiClass::BSSIDstr() { return mac_string(BSSID()); }

int8_t WiFiClass::RSSI() {
    if (med()) {
        refresh();
        return _medStatus == WL_CONNECTED ? (int8_t)_medRssi : 0;
    }

    if (!isConnectedConst()) return 0;
    return (int8_t)(-55 - (int)(rw::random32() % 7));
}

int32_t WiFiClass::channel() {
    if (med()) {
        refresh();
        return _medStatus == WL_CONNECTED ? _medChannel : 0;
    }

    return isConnectedConst() ? 6 : 0;
}

bool WiFiClass::softAP(const char* ssid, const char* passphrase, int channel, int, int max_connection, bool) {
    stub_notice();
    g_stack_up = true;   // enableAP starts the stack before the arguments are checked
    if (!ssid || !*ssid) return false;
    if (passphrase && *passphrase && strlen(passphrase) < 8) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][WiFiAP.cpp:180] softAP(): passphrase too short!", (unsigned long)(rw::uptime_ns() / 1000000ull));
        return false;
    }

    strncpy_s(_apSsid, sizeof _apSsid, ssid, _TRUNCATE);
    _mode = (wifi_mode_t)(_mode | WIFI_MODE_AP);
    setRadio();
    if (med()) {
        SoftApIn in;
        memset(&in, 0, sizeof in);
        strncpy_s(in.ssid, sizeof in.ssid, ssid, _TRUNCATE);
        strncpy_s(in.pass, sizeof in.pass, passphrase ? passphrase : "", _TRUNCATE);
        in.channel = channel;
        in.max_conn = max_connection;
        rw::radio(RW_RADIO_WIFI_SOFTAP, &in, sizeof in, nullptr, 0);
        _apIP = IPAddress(192, 168, 4, 1);
    }

    return true;
}

bool WiFiClass::softAPdisconnect(bool wifioff) {
    _apSsid[0] = 0;
    _mode = (wifi_mode_t)(_mode & ~WIFI_MODE_AP);
    if (wifioff) _mode = WIFI_MODE_NULL;
    if (med()) rw::radio(RW_RADIO_WIFI_SOFTAP_STOP, nullptr, 0, nullptr, 0);
    setRadio();
    return true;
}

uint8_t WiFiClass::softAPgetStationNum() {
    if (!med()) return 0;
    int32_t n = 0;
    rw::radio(RW_RADIO_WIFI_SOFTAP_INFO, nullptr, 0, &n, sizeof n);
    return (uint8_t)n;
}

int16_t WiFiClass::scanNetworks(bool, bool, bool, uint32_t, uint8_t, const char*, const uint8_t*) {
    stub_notice();
    if (_mode == WIFI_MODE_NULL) {
        _mode = WIFI_MODE_STA;
        setRadio();
    }

    rw::sleep_until(rw::now_ns() + 2000000000ull);  // a real active scan of 13 channels takes ~2 s
    if (!med()) {
        _scanCount = 3;
        return _scanCount;
    }

    static uint8_t buf[4 + RW_WIFI_MAX_SCAN * sizeof(rw_wifi_net)];
    int n = rw::radio(RW_RADIO_WIFI_SCAN, nullptr, 0, buf, sizeof buf);
    if (n < 0) {
        _scanCount = WIFI_SCAN_FAILED;
        return _scanCount;
    }

    if (n > RW_WIFI_MAX_SCAN) n = RW_WIFI_MAX_SCAN;
    for (int i = 0; i < n; i++) {
        const rw_wifi_net* r = reinterpret_cast<const rw_wifi_net*>(buf + 4 + i * sizeof(rw_wifi_net));
        memcpy(_scan[i].ssid, r->ssid, 33);
        _scan[i].ssid[32] = 0;
        _scan[i].rssi = r->rssi;
        _scan[i].channel = r->channel;
        _scan[i].auth = r->auth;
        memcpy(_scan[i].bssid, r->bssid, 6);
    }

    _scanCount = (int16_t)n;
    return _scanCount;
}

String WiFiClass::SSID(uint8_t i) {
    if (!med()) {
        static const char* const names[3] = {"RoboTwin-Lab", "RoboTwin-Guest", "ESP32-Sim"};
        return i < 3 && _scanCount > 0 ? String(names[i]) : String();
    }

    return i < _scanCount && _scanCount > 0 ? String(_scan[i].ssid) : String();
}

wifi_auth_mode_t WiFiClass::encryptionType(uint8_t i) {
    if (!med()) return i == 1 ? WIFI_AUTH_OPEN : WIFI_AUTH_WPA2_PSK;
    return i < _scanCount && _scanCount > 0 ? (wifi_auth_mode_t)_scan[i].auth : WIFI_AUTH_OPEN;
}

int32_t WiFiClass::RSSI(uint8_t i) {
    if (!med()) {
        static const int32_t levels[3] = {-48, -67, -81};
        return i < 3 ? levels[i] : 0;
    }

    return i < _scanCount && _scanCount > 0 ? _scan[i].rssi : 0;
}

int32_t WiFiClass::channel(uint8_t i) {
    if (!med()) return i == 0 ? 6 : i == 1 ? 11 : 1;
    return i < _scanCount && _scanCount > 0 ? _scan[i].channel : 0;
}

uint8_t* WiFiClass::BSSID(uint8_t i, uint8_t* bssid) {
    static uint8_t tmp[6];
    uint8_t* out = bssid ? bssid : tmp;
    memset(out, 0, 6);
    if (med() && i < _scanCount && _scanCount > 0) memcpy(out, _scan[i].bssid, 6);
    return out;
}

String WiFiClass::BSSIDstr(uint8_t i) { return mac_string(BSSID(i, nullptr)); }

int WiFiClass::hostByName(const char* host, IPAddress& result) {
    need_stack();
    result = IPAddress();
    if (host && result.fromString(host)) return 1;
    rw::log_once(0x720, RW_LOG_WARN, "[%6lu][W][WiFiGeneric.cpp] hostByName(): no DNS in the simulated world (use IP addresses)",
                 (unsigned long)(rw::uptime_ns() / 1000000ull));
    return 0;
}

/* ---------------------------------------------------------------- stream sockets */
static void network_notice() {
    rw::log_once(0x710, RW_LOG_WARN, "[%6lu][W][WiFiClient.cpp] connect(): no network on a lone board (WiFi is a stub); connect() fails",
                 (unsigned long)(rw::uptime_ns() / 1000000ull));
}

#pragma pack(push, 1)
struct IpPort {
    uint32_t ip;
    uint16_t port;
};
struct TcpStateOut {
    int32_t connected;
    int32_t available;
    uint32_t ip;
    uint16_t port;
};
#pragma pack(pop)

int WiFiClient::connect(IPAddress ip, uint16_t port) {
    need_stack();
    if (!med()) {
        network_notice();
        rw::sleep_until(rw::now_ns() + 3000000000ull);  // connection timeout
        return 0;
    }

    IpPort in = {(uint32_t)ip, port};
    int id = rw::radio(RW_RADIO_TCP_CONNECT, &in, sizeof in, nullptr, 0);
    if (id <= 0) {
        // unreachable: SYN retries until the timeout; refused: RST comes back at once
        rw::sleep_until(rw::now_ns() + (id == -3 ? 3000000ull : 3000000000ull));
        _id = 0;
        return 0;
    }

    rw::sleep_until(rw::now_ns() + 4000000ull);  // three-way handshake over the air
    _id = id;
    _rxLen = _rxPos = 0;
    return 1;
}

int WiFiClient::connect(const char* host, uint16_t port) {
    IPAddress ip;
    if (!WiFiClass::hostByName(host, ip)) {
        if (!med()) network_notice();
        return 0;
    }

    return connect(ip, port);
}

size_t WiFiClient::write(const uint8_t* buf, size_t size) {
    if (_id <= 0 || !buf || size == 0) return 0;
    static uint8_t frame[4 + 1460];
    size_t done = 0;
    while (done < size) {
        size_t n = size - done > 1460 ? 1460 : size - done;
        memcpy(frame, &_id, 4);
        memcpy(frame + 4, buf + done, n);
        int rc = rw::radio(RW_RADIO_TCP_SEND, frame, (int)(4 + n), nullptr, 0);
        rw::charge(rw::cost(20000.0 + n * 50.0));
        if (rc < 0) break;
        done += n;
    }

    return done;
}

int WiFiClient::fill() {
    if (_rxPos < _rxLen) return _rxLen - _rxPos;
    if (_id <= 0) return 0;
    _rxPos = _rxLen = 0;
    int rc = rw::radio(RW_RADIO_TCP_RECV, &_id, sizeof _id, _rx, sizeof _rx);
    rw::charge(rw::cost(2000.0));
    if (rc > 0) _rxLen = rc;
    return _rxLen;
}

int WiFiClient::available() { return fill(); }

int WiFiClient::read() { return fill() > 0 ? _rx[_rxPos++] : -1; }

int WiFiClient::read(uint8_t* buf, size_t size) {
    size_t n = 0;
    while (n < size && fill() > 0) buf[n++] = _rx[_rxPos++];
    return n == 0 ? -1 : (int)n;
}

int WiFiClient::peek() { return fill() > 0 ? _rx[_rxPos] : -1; }

void WiFiClient::stop() {
    if (_id > 0) rw::radio(RW_RADIO_TCP_CLOSE, &_id, sizeof _id, nullptr, 0);
    _id = 0;
    _rxLen = _rxPos = 0;
}

uint8_t WiFiClient::connected() {
    if (_id <= 0) return 0;
    if (_rxPos < _rxLen) return 1;
    TcpStateOut s;
    memset(&s, 0, sizeof s);
    rw::radio(RW_RADIO_TCP_STATE, &_id, sizeof _id, &s, sizeof s);
    return (s.connected || s.available > 0) ? 1 : 0;
}

IPAddress WiFiClient::remoteIP() {
    TcpStateOut s;
    memset(&s, 0, sizeof s);
    if (_id > 0) rw::radio(RW_RADIO_TCP_STATE, &_id, sizeof _id, &s, sizeof s);
    return IPAddress(s.ip);
}

uint16_t WiFiClient::remotePort() {
    TcpStateOut s;
    memset(&s, 0, sizeof s);
    if (_id > 0) rw::radio(RW_RADIO_TCP_STATE, &_id, sizeof _id, &s, sizeof s);
    return s.port;
}

void WiFiServer::begin(uint16_t port) {
    need_stack();
    if (port) _port = port;
    if (!med()) {
        network_notice();
        return;
    }

    uint16_t in[2] = {_port, 1};
    rw::radio(RW_RADIO_TCP_LISTEN, in, sizeof in, nullptr, 0);
    _listening = true;
}

void WiFiServer::end() {
    if (_listening) {
        uint16_t in[2] = {_port, 0};
        rw::radio(RW_RADIO_TCP_LISTEN, in, sizeof in, nullptr, 0);
    }

    _listening = false;
}

bool WiFiServer::hasClient() {
    if (_pending > 0) return true;
    if (!_listening) return false;
    uint8_t out[10];
    _pending = rw::radio(RW_RADIO_TCP_ACCEPT, &_port, sizeof _port, out, sizeof out);
    if (_pending < 0) _pending = 0;
    return _pending > 0;
}

WiFiClient WiFiServer::accept() {
    rw::charge(rw::cost(5000.0));
    if (!hasClient()) return WiFiClient();
    int id = _pending;
    _pending = 0;
    return WiFiClient(id);
}

/* ---------------------------------------------------------------- UDP */
uint8_t WiFiUDP::begin(uint16_t port) {
    need_stack();
    _port = port;
    if (!med()) return 1;
    uint16_t in[2] = {port, 1};
    rw::radio(RW_RADIO_UDP_BIND, in, sizeof in, nullptr, 0);
    return 1;
}

void WiFiUDP::stop() {
    if (med() && _port) {
        uint16_t in[2] = {_port, 0};
        rw::radio(RW_RADIO_UDP_BIND, in, sizeof in, nullptr, 0);
    }

    _port = 0;
}

int WiFiUDP::beginPacket(IPAddress ip, uint16_t port) {
    need_stack();
    if (!med()) return 0;
    _txIp = (uint32_t)ip;
    _txPort = port;
    _txLen = 0;
    return 1;
}

int WiFiUDP::beginPacket(const char* host, uint16_t port) {
    IPAddress ip;
    if (!WiFiClass::hostByName(host, ip)) return 0;
    return beginPacket(ip, port);
}

size_t WiFiUDP::write(const uint8_t* buf, size_t size) {
    if (_txLen < 0 || !buf) return 0;
    size_t room = sizeof _tx - (size_t)_txLen;
    size_t n = size < room ? size : room;
    memcpy(_tx + _txLen, buf, n);
    _txLen += (int)n;
    return n;
}

int WiFiUDP::endPacket() {
    if (_txLen < 0 || !med()) return 0;
    static uint8_t frame[8 + 1472];
    memcpy(frame, &_txIp, 4);
    memcpy(frame + 4, &_txPort, 2);
    memcpy(frame + 6, &_port, 2);
    memcpy(frame + 8, _tx, (size_t)_txLen);
    int rc = rw::radio(RW_RADIO_UDP_SEND, frame, 8 + _txLen, nullptr, 0);
    rw::charge(rw::cost(30000.0 + _txLen * 40.0));
    _txLen = -1;
    return rc >= 0 ? 1 : 0;
}

int WiFiUDP::parsePacket() {
    _rxLen = _rxPos = 0;
    if (!med() || !_port) return 0;
    static uint8_t buf[8 + 1472];
    int rc = rw::radio(RW_RADIO_UDP_RECV, &_port, sizeof _port, buf, sizeof buf);
    rw::charge(rw::cost(3000.0));
    if (rc < 8) return 0;
    uint32_t ip;
    uint16_t sport, len;
    memcpy(&ip, buf, 4);
    memcpy(&sport, buf + 4, 2);
    memcpy(&len, buf + 6, 2);
    if (len > sizeof _rx) len = sizeof _rx;
    memcpy(_rx, buf + 8, len);
    _rxLen = len;
    _remoteIP = IPAddress(ip);
    _remotePort = sport;
    return _rxLen;
}

int WiFiUDP::read(unsigned char* buf, size_t len) {
    size_t n = 0;
    while (n < len && _rxPos < _rxLen) buf[n++] = _rx[_rxPos++];
    return (int)n;
}
