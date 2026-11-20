/*
 * WiFi for the RoboTwin ESP32 HAL model.
 *
 * Inside a RobotWin world (host with a radio medium, ABI 2) the station really
 * associates with an access point that exists in the world (a router object or another
 * ESP32 in soft-AP mode; without any router an implicit "virtual home network" accepts
 * any SSID). RSSI, scan results, association loss when out of range, IP addresses and
 * UDP / TCP-like traffic between ESP32s in the same world go through the medium.
 * There is no Internet and no DNS: hostByName() only parses dotted addresses.
 *
 * Without a medium (a lone board in a test bench) it is the old stub: begin() reports
 * WL_CONNECTED about one simulated second later with a fixed IP / MAC / RSSI, and every
 * network operation fails.
 */
#ifndef RW_WIFI_H
#define RW_WIFI_H
#include "Arduino.h"
#include "IPAddress.h"

typedef enum {
    WL_NO_SHIELD = 255,
    WL_STOPPED = 254,
    WL_IDLE_STATUS = 0,
    WL_NO_SSID_AVAIL = 1,
    WL_SCAN_COMPLETED = 2,
    WL_CONNECTED = 3,
    WL_CONNECT_FAILED = 4,
    WL_CONNECTION_LOST = 5,
    WL_DISCONNECTED = 6
} wl_status_t;

typedef enum { WIFI_MODE_NULL = 0, WIFI_MODE_STA, WIFI_MODE_AP, WIFI_MODE_APSTA, WIFI_MODE_MAX } wifi_mode_t;
#define WIFI_OFF WIFI_MODE_NULL
#define WIFI_STA WIFI_MODE_STA
#define WIFI_AP WIFI_MODE_AP
#define WIFI_AP_STA WIFI_MODE_APSTA

typedef enum {
    WIFI_AUTH_OPEN = 0,
    WIFI_AUTH_WEP,
    WIFI_AUTH_WPA_PSK,
    WIFI_AUTH_WPA2_PSK,
    WIFI_AUTH_WPA_WPA2_PSK,
    WIFI_AUTH_WPA2_ENTERPRISE,
    WIFI_AUTH_WPA3_PSK,
    WIFI_AUTH_MAX
} wifi_auth_mode_t;

typedef enum {
    WIFI_POWER_19_5dBm = 78,
    WIFI_POWER_19dBm = 76,
    WIFI_POWER_18_5dBm = 74,
    WIFI_POWER_17dBm = 68,
    WIFI_POWER_15dBm = 60,
    WIFI_POWER_13dBm = 52,
    WIFI_POWER_11dBm = 44,
    WIFI_POWER_8_5dBm = 34,
    WIFI_POWER_7dBm = 28,
    WIFI_POWER_5dBm = 20,
    WIFI_POWER_2dBm = 8,
    WIFI_POWER_MINUS_1dBm = -4
} wifi_power_t;

#define WIFI_SCAN_RUNNING (-1)
#define WIFI_SCAN_FAILED (-2)
#define RW_WIFI_MAX_SCAN 20

class WiFiClass {
public:
    wl_status_t begin(const char* ssid, const char* passphrase = nullptr, int32_t channel = 0, const uint8_t* bssid = nullptr, bool connect = true);
    wl_status_t begin(const String& ssid, const String& passphrase = String()) { return begin(ssid.c_str(), passphrase.c_str()); }
    wl_status_t begin();
    wl_status_t status();
    bool isConnected() { return status() == WL_CONNECTED; }
    bool disconnect(bool wifioff = false, bool eraseap = false);
    bool reconnect();
    uint8_t waitForConnectResult(unsigned long timeoutLength = 60000);

    bool mode(wifi_mode_t m);
    wifi_mode_t getMode() { return _mode; }
    bool enableSTA(bool enable) { return mode(enable ? (wifi_mode_t)(_mode | WIFI_MODE_STA) : (wifi_mode_t)(_mode & ~WIFI_MODE_STA)); }
    bool enableAP(bool enable) { return mode(enable ? (wifi_mode_t)(_mode | WIFI_MODE_AP) : (wifi_mode_t)(_mode & ~WIFI_MODE_AP)); }

    bool config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1 = (uint32_t)0, IPAddress dns2 = (uint32_t)0);
    bool setHostname(const char* hostname);
    const char* getHostname() { return _hostname; }
    bool setAutoReconnect(bool) { return true; }
    bool getAutoReconnect() { return true; }
    bool setAutoConnect(bool) { return true; }
    bool setSleep(bool) { return true; }
    bool getSleep() { return true; }
    bool setTxPower(wifi_power_t) { return true; }
    wifi_power_t getTxPower() { return WIFI_POWER_19_5dBm; }
    void persistent(bool) {}
    void useStaticBuffers(bool) {}

    IPAddress localIP();
    IPAddress subnetMask();
    IPAddress gatewayIP();
    IPAddress dnsIP(uint8_t dns_no = 0);
    IPAddress broadcastIP();
    IPAddress networkID();
    uint8_t subnetCIDR() { return 24; }
    uint8_t* macAddress(uint8_t* mac);
    String macAddress();
    String SSID() const;
    String psk() const;
    uint8_t* BSSID(uint8_t* bssid = nullptr);
    String BSSIDstr();
    int8_t RSSI();
    int32_t channel();

    bool softAP(const char* ssid, const char* passphrase = nullptr, int channel = 1, int ssid_hidden = 0, int max_connection = 4, bool ftm_responder = false);
    bool softAP(const String& ssid, const String& passphrase = String(), int channel = 1, int ssid_hidden = 0, int max_connection = 4) {
        return softAP(ssid.c_str(), passphrase.c_str(), channel, ssid_hidden, max_connection);
    }
    bool softAPConfig(IPAddress local_ip, IPAddress gateway, IPAddress subnet) { _apIP = local_ip; (void)gateway; (void)subnet; return true; }
    bool softAPdisconnect(bool wifioff = false);
    uint8_t softAPgetStationNum();
    IPAddress softAPIP() { return _apIP; }
    IPAddress softAPBroadcastIP() { return IPAddress(_apIP[0], _apIP[1], _apIP[2], 255); }
    String softAPmacAddress();
    String softAPSSID() const { return String(_apSsid); }

    int16_t scanNetworks(bool async = false, bool show_hidden = false, bool passive = false, uint32_t max_ms_per_chan = 300, uint8_t channel = 0, const char* ssid = nullptr, const uint8_t* bssid = nullptr);
    int16_t scanComplete() { return _scanCount; }
    void scanDelete() { _scanCount = WIFI_SCAN_FAILED; }
    String SSID(uint8_t networkItem);
    wifi_auth_mode_t encryptionType(uint8_t networkItem);
    int32_t RSSI(uint8_t networkItem);
    int32_t channel(uint8_t networkItem);
    uint8_t* BSSID(uint8_t networkItem, uint8_t* bssid = nullptr);
    String BSSIDstr(uint8_t networkItem);

    static int hostByName(const char* aHostname, IPAddress& aResult);

    /* Simulator: true when this board runs inside a RobotWin world radio medium. */
    static bool simulatedMedium();

private:
    bool isConnectedConst() const;
    void refresh();
    wifi_mode_t _mode = WIFI_MODE_NULL;
    bool _began = false;
    uint64_t _connectAtNs = 0;
    char _ssid[33] = {0};
    char _psk[65] = {0};
    char _apSsid[33] = {0};
    char _hostname[33] = "esp32-robotwin";
    IPAddress _localIP = IPAddress(192, 168, 1, 77);
    IPAddress _gateway = IPAddress(192, 168, 1, 1);
    IPAddress _subnet = IPAddress(255, 255, 255, 0);
    IPAddress _dns = IPAddress(192, 168, 1, 1);
    IPAddress _apIP = IPAddress(192, 168, 4, 1);
    int16_t _scanCount = WIFI_SCAN_FAILED;
    /* medium state (refreshed by status()) */
    int _medStatus = WL_IDLE_STATUS;
    int _medRssi = 0;
    uint32_t _medIp = 0;
    uint32_t _medGw = 0;
    int _medChannel = 0;
    uint8_t _medBssid[6] = {0};
    struct ScanItem {
        char ssid[33];
        int8_t rssi;
        uint8_t channel;
        uint8_t auth;
        uint8_t bssid[6];
    } _scan[RW_WIFI_MAX_SCAN];
    void setRadio();
};

extern WiFiClass WiFi;

/* ---- TCP-like stream between ESP32s of the same world (reliable, ordered, with link latency) ---- */
class WiFiClient : public Stream {
public:
    WiFiClient() {}
    explicit WiFiClient(int id) : _id(id) {}
    int connect(IPAddress ip, uint16_t port);
    int connect(const char* host, uint16_t port);
    int connect(IPAddress ip, uint16_t port, int32_t) { return connect(ip, port); }
    int connect(const char* host, uint16_t port, int32_t) { return connect(host, port); }
    size_t write(uint8_t b) override { return write(&b, 1); }
    size_t write(const uint8_t* buf, size_t size) override;
    using Print::write;
    int available() override;
    int read() override;
    int read(uint8_t* buf, size_t size);
    int peek() override;
    void flush() override {}
    void stop();
    uint8_t connected();
    void setNoDelay(bool) {}
    operator bool() { return _id > 0 && connected(); }
    IPAddress remoteIP();
    uint16_t remotePort();
    int fd() const { return _id; }

private:
    int fill();
    int _id = 0;
    uint8_t _rx[256];
    int _rxLen = 0;
    int _rxPos = 0;
};

class WiFiServer {
public:
    explicit WiFiServer(uint16_t port = 80, uint8_t max_clients = 4) : _port(port) { (void)max_clients; }
    void begin(uint16_t port = 0);
    void end();
    void close() { end(); }
    void stop() { end(); }
    WiFiClient available() { return accept(); }
    WiFiClient accept();
    bool hasClient();
    void setNoDelay(bool) {}
    operator bool() { return _listening; }

private:
    uint16_t _port;
    bool _listening = false;
    int _pending = 0;
};

class WiFiUDP : public Stream {
public:
    uint8_t begin(uint16_t port);
    uint8_t begin(IPAddress, uint16_t port) { return begin(port); }
    uint8_t beginMulticast(IPAddress, uint16_t port) { return begin(port); }
    void stop();
    int beginPacket(IPAddress ip, uint16_t port);
    int beginPacket(const char* host, uint16_t port);
    int beginPacket() { return beginPacket(_remoteIP, _remotePort); }
    int endPacket();
    size_t write(uint8_t b) override { return write(&b, 1); }
    size_t write(const uint8_t* buf, size_t size) override;
    using Print::write;
    int parsePacket();
    int available() override { return _rxLen - _rxPos; }
    int read() override { return _rxPos < _rxLen ? _rx[_rxPos++] : -1; }
    int read(unsigned char* buf, size_t len);
    int read(char* buf, size_t len) { return read(reinterpret_cast<unsigned char*>(buf), len); }
    int peek() override { return _rxPos < _rxLen ? _rx[_rxPos] : -1; }
    void flush() override { _rxPos = _rxLen; }
    IPAddress remoteIP() { return _remoteIP; }
    uint16_t remotePort() { return _remotePort; }

private:
    uint16_t _port = 0;
    uint32_t _txIp = 0;
    uint16_t _txPort = 0;
    int _txLen = -1;
    uint8_t _tx[1472];
    uint8_t _rx[1472];
    int _rxLen = 0;
    int _rxPos = 0;
    IPAddress _remoteIP;
    uint16_t _remotePort = 0;
};

#endif
