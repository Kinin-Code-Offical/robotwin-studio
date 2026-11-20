/* WiFi stub + IPAddress (see WiFi.h). */
#include "WiFi.h"
#include "rw_internal.h"

void rw_hal_set_wifi(bool on);

const IPAddress INADDR_NONE(0, 0, 0, 0);
const IPAddress IN6ADDR_ANY(0, 0, 0, 0);
WiFiClass WiFi;

namespace {
const uint64_t kConnectDelayNs = 1000000000ull;  // ~1 s from begin() to WL_CONNECTED
const uint8_t kStaMac[6] = {0x24, 0x0A, 0xC4, 0x5A, 0x7E, 0x11};

void stub_notice() {
    rw::log_once(0x700, RW_LOG_INFO, "WiFi is a stub in the simulator: begin() connects after ~1 s of simulated time, no network traffic is possible");
}

String mac_string(const uint8_t* mac) {
    char buf[18];
    snprintf(buf, sizeof buf, "%02X:%02X:%02X:%02X:%02X:%02X", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    return String(buf);
}
}  // namespace

namespace rw {
void hal_wifi_reset() { WiFi = WiFiClass(); }
}  // namespace rw

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
bool WiFiClass::isConnectedConst() const { return _began && (_mode & WIFI_MODE_STA) && rw::now_ns() >= _connectAtNs; }

void WiFiClass::setRadio() { rw_hal_set_wifi(_mode != WIFI_MODE_NULL); }

bool WiFiClass::mode(wifi_mode_t m) {
    rw::charge(rw::cost(1000000.0));
    _mode = m;
    if (!(m & WIFI_MODE_STA)) _began = false;
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
    setRadio();
    if (connect) {
        _began = true;
        _connectAtNs = rw::now_ns() + kConnectDelayNs;
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
    if (_mode == WIFI_MODE_NULL) return WL_STOPPED;
    if (!_began) return WL_IDLE_STATUS;
    return rw::now_ns() >= _connectAtNs ? WL_CONNECTED : WL_DISCONNECTED;
}

bool WiFiClass::disconnect(bool wifioff, bool) {
    _began = false;
    if (wifioff) {
        _mode = WIFI_MODE_NULL;
        setRadio();
    }

    return true;
}

bool WiFiClass::reconnect() {
    if (!_ssid[0]) return false;
    _began = true;
    _connectAtNs = rw::now_ns() + kConnectDelayNs;
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

IPAddress WiFiClass::localIP() { return isConnectedConst() ? _localIP : IPAddress(); }
IPAddress WiFiClass::subnetMask() { return isConnectedConst() ? _subnet : IPAddress(); }
IPAddress WiFiClass::gatewayIP() { return isConnectedConst() ? _gateway : IPAddress(); }
IPAddress WiFiClass::dnsIP(uint8_t) { return isConnectedConst() ? _dns : IPAddress(); }
IPAddress WiFiClass::broadcastIP() { return isConnectedConst() ? IPAddress(_localIP[0], _localIP[1], _localIP[2], 255) : IPAddress(); }
IPAddress WiFiClass::networkID() { return isConnectedConst() ? IPAddress(_localIP[0], _localIP[1], _localIP[2], 0) : IPAddress(); }

uint8_t* WiFiClass::macAddress(uint8_t* mac) {
    if (mac) memcpy(mac, kStaMac, 6);
    return mac;
}

String WiFiClass::macAddress() { return mac_string(kStaMac); }

String WiFiClass::softAPmacAddress() {
    uint8_t mac[6];
    memcpy(mac, kStaMac, 6);
    mac[5]++;
    return mac_string(mac);
}

String WiFiClass::SSID() const { return isConnectedConst() ? String(_ssid) : String(); }
String WiFiClass::psk() const { return isConnectedConst() ? String(_psk) : String(); }

uint8_t* WiFiClass::BSSID(uint8_t* bssid) {
    static uint8_t ap[6] = {0x5C, 0x49, 0x79, 0x10, 0x20, 0x30};
    if (bssid) {
        memcpy(bssid, ap, 6);
        return bssid;
    }

    return ap;
}

String WiFiClass::BSSIDstr() { return mac_string(BSSID()); }

int8_t WiFiClass::RSSI() {
    if (!isConnectedConst()) return 0;
    return (int8_t)(-55 - (int)(rw::random32() % 7));
}

bool WiFiClass::softAP(const char* ssid, const char* passphrase, int, int, int, bool) {
    stub_notice();
    if (!ssid || !*ssid) return false;
    if (passphrase && *passphrase && strlen(passphrase) < 8) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][WiFiAP.cpp:180] softAP(): passphrase too short!", (unsigned long)(rw::uptime_ns() / 1000000ull));
        return false;
    }

    strncpy_s(_apSsid, sizeof _apSsid, ssid, _TRUNCATE);
    _mode = (wifi_mode_t)(_mode | WIFI_MODE_AP);
    setRadio();
    return true;
}

bool WiFiClass::softAPdisconnect(bool wifioff) {
    _apSsid[0] = 0;
    _mode = (wifi_mode_t)(_mode & ~WIFI_MODE_AP);
    if (wifioff) _mode = WIFI_MODE_NULL;
    setRadio();
    return true;
}

int16_t WiFiClass::scanNetworks(bool, bool, bool, uint32_t, uint8_t, const char*, const uint8_t*) {
    stub_notice();
    if (_mode == WIFI_MODE_NULL) {
        _mode = WIFI_MODE_STA;
        setRadio();
    }

    rw::sleep_until(rw::now_ns() + 2000000000ull);  // a real scan takes ~2 s
    _scanCount = 3;
    return _scanCount;
}

String WiFiClass::SSID(uint8_t i) {
    static const char* const names[3] = {"RoboTwin-Lab", "RoboTwin-Guest", "ESP32-Sim"};
    return i < 3 && _scanCount > 0 ? String(names[i]) : String();
}

wifi_auth_mode_t WiFiClass::encryptionType(uint8_t i) { return i == 1 ? WIFI_AUTH_OPEN : WIFI_AUTH_WPA2_PSK; }

int32_t WiFiClass::RSSI(uint8_t i) {
    static const int32_t levels[3] = {-48, -67, -81};
    return i < 3 ? levels[i] : 0;
}

int32_t WiFiClass::channel(uint8_t i) { return i == 0 ? 6 : i == 1 ? 11 : 1; }

int WiFiClass::hostByName(const char*, IPAddress& result) {
    result = IPAddress();
    return 0;
}

/* ---------------------------------------------------------------- network objects */
static void network_notice() {
    rw::log_once(0x710, RW_LOG_WARN, "[%6lu][W][WiFiClient.cpp] connect(): network traffic is not simulated (WiFi is a stub); connect() fails",
                 (unsigned long)(rw::uptime_ns() / 1000000ull));
}

int WiFiClient::connect(IPAddress, uint16_t) {
    network_notice();
    rw::sleep_until(rw::now_ns() + 3000000000ull);  // connection timeout
    return 0;
}

int WiFiClient::connect(const char*, uint16_t) {
    network_notice();
    rw::sleep_until(rw::now_ns() + 3000000000ull);
    return 0;
}

void WiFiServer::begin(uint16_t port) {
    if (port) _port = port;
    network_notice();
}
