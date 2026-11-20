/*
 * WiFi - SIMULATOR STUB. There is no radio and no network: begin() reports
 * WL_CONNECTED about one simulated second later, with a fixed IP / MAC / RSSI.
 * The radio state is reported to the host (supply current) and blocks ADC2 like a
 * real ESP32. WiFiClient/WiFiServer/WiFiUDP exist so sketches compile, but every
 * network operation fails (connect() returns 0).
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
    int32_t channel() { return isConnectedConst() ? 6 : 0; }

    bool softAP(const char* ssid, const char* passphrase = nullptr, int channel = 1, int ssid_hidden = 0, int max_connection = 4, bool ftm_responder = false);
    bool softAP(const String& ssid, const String& passphrase = String(), int channel = 1, int ssid_hidden = 0, int max_connection = 4) {
        return softAP(ssid.c_str(), passphrase.c_str(), channel, ssid_hidden, max_connection);
    }
    bool softAPConfig(IPAddress local_ip, IPAddress gateway, IPAddress subnet) { _apIP = local_ip; (void)gateway; (void)subnet; return true; }
    bool softAPdisconnect(bool wifioff = false);
    uint8_t softAPgetStationNum() { return 0; }
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

    static int hostByName(const char* aHostname, IPAddress& aResult);

private:
    bool isConnectedConst() const;
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
    void setRadio();
};

extern WiFiClass WiFi;

/* ---- network objects: present so sketches compile; nothing is transmitted ---- */
class WiFiClient : public Stream {
public:
    WiFiClient() {}
    int connect(IPAddress ip, uint16_t port);
    int connect(const char* host, uint16_t port);
    int connect(IPAddress ip, uint16_t port, int32_t) { return connect(ip, port); }
    int connect(const char* host, uint16_t port, int32_t) { return connect(host, port); }
    size_t write(uint8_t) override { return 0; }
    size_t write(const uint8_t*, size_t) override { return 0; }
    using Print::write;
    int available() override { return 0; }
    int read() override { return -1; }
    int read(uint8_t*, size_t) { return -1; }
    int peek() override { return -1; }
    void flush() override {}
    void stop() {}
    uint8_t connected() { return 0; }
    void setTimeout(uint32_t) {}
    void setNoDelay(bool) {}
    operator bool() { return false; }
    IPAddress remoteIP() const { return IPAddress(); }
    uint16_t remotePort() const { return 0; }
};

class WiFiServer {
public:
    explicit WiFiServer(uint16_t port = 80, uint8_t max_clients = 4) : _port(port) { (void)max_clients; }
    void begin(uint16_t port = 0);
    void end() {}
    void close() {}
    void stop() {}
    WiFiClient available() { return WiFiClient(); }
    WiFiClient accept() { return WiFiClient(); }
    bool hasClient() { return false; }
    void setNoDelay(bool) {}
    operator bool() { return true; }

private:
    uint16_t _port;
};

class WiFiUDP : public Stream {
public:
    uint8_t begin(uint16_t) { return 1; }
    uint8_t begin(IPAddress, uint16_t) { return 1; }
    void stop() {}
    int beginPacket(IPAddress, uint16_t) { return 0; }
    int beginPacket(const char*, uint16_t) { return 0; }
    int endPacket() { return 0; }
    size_t write(uint8_t) override { return 0; }
    size_t write(const uint8_t*, size_t) override { return 0; }
    using Print::write;
    int parsePacket() { return 0; }
    int available() override { return 0; }
    int read() override { return -1; }
    int read(unsigned char*, size_t) { return 0; }
    int read(char*, size_t) { return 0; }
    int peek() override { return -1; }
    void flush() override {}
    IPAddress remoteIP() { return IPAddress(); }
    uint16_t remotePort() { return 0; }
};

#endif
