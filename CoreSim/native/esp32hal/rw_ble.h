/*
 * BLE subset of the ESP32 Arduino BLE library (BLEDevice / BLEScan / BLEServer /
 * BLEClient), routed through the RobotWin world's radio medium: advertisements reach
 * other ESP32s with distance- and obstacle-dependent RSSI, scans list what was heard,
 * a GATT server's notify() reaches subscribed clients one connection interval later.
 * Not simulated: security/bonding, MTU negotiation, descriptors other than 0x2902,
 * extended advertising. On a lone board (no medium) scans find nothing.
 */
#ifndef RW_BLE_H
#define RW_BLE_H
#include "Arduino.h"

class BLEUUID {
public:
    BLEUUID() {}
    BLEUUID(const char* uuid) : _s(uuid ? uuid : "") {}
    BLEUUID(const String& uuid) : _s(uuid) {}
    BLEUUID(uint16_t uuid16);
    BLEUUID(uint32_t uuid32);
    String toString() const { return _s; }
    bool equals(const BLEUUID& o) const { return _s.equalsIgnoreCase(o._s); }
    bool operator==(const BLEUUID& o) const { return equals(o); }
    bool operator!=(const BLEUUID& o) const { return !equals(o); }

private:
    String _s;
};

class BLEAddress {
public:
    BLEAddress() { memset(_a, 0, 6); }
    explicit BLEAddress(const uint8_t* a) { memcpy(_a, a, 6); }
    BLEAddress(const char* text);
    BLEAddress(const String& text) : BLEAddress(text.c_str()) {}
    String toString() const;
    const uint8_t* getNative() const { return _a; }
    bool equals(const BLEAddress& o) const { return memcmp(_a, o._a, 6) == 0; }
    bool operator==(const BLEAddress& o) const { return equals(o); }

private:
    uint8_t _a[6];
};

class BLEAdvertisedDevice {
public:
    String getName() const { return _name; }
    bool haveName() const { return _name.length() > 0; }
    BLEAddress getAddress() const { return _addr; }
    int getRSSI() const { return _rssi; }
    bool haveRSSI() const { return true; }
    BLEUUID getServiceUUID() const { return BLEUUID(_uuid); }
    bool haveServiceUUID() const { return _uuid.length() > 0; }
    bool isAdvertisingService(const BLEUUID& u) const { return haveServiceUUID() && BLEUUID(_uuid).equals(u); }
    String getManufacturerData() const { return _mfr; }
    bool haveManufacturerData() const { return _mfr.length() > 0; }
    int8_t getTXPower() const { return _tx; }
    bool haveTXPower() const { return true; }
    String toString() const;

    String _name;
    String _uuid;
    String _mfr;
    BLEAddress _addr;
    int _rssi = 0;
    int8_t _tx = 0;
};

class BLEAdvertisedDeviceCallbacks {
public:
    virtual ~BLEAdvertisedDeviceCallbacks() {}
    virtual void onResult(BLEAdvertisedDevice advertisedDevice) = 0;
};

#define RW_BLE_MAX_RESULTS 32

class BLEScanResults {
public:
    int getCount() { return _count; }
    BLEAdvertisedDevice getDevice(uint32_t i) { return i < (uint32_t)_count ? _devices[i] : BLEAdvertisedDevice(); }
    /* both the core 2.x (value) and core 3.x (pointer) styles compile */
    BLEScanResults* operator->() { return this; }
    operator BLEScanResults*() { return _self ? _self : this; }

    int _count = 0;
    BLEAdvertisedDevice _devices[RW_BLE_MAX_RESULTS];
    BLEScanResults* _self = nullptr;
};

class BLEScan {
public:
    void setAdvertisedDeviceCallbacks(BLEAdvertisedDeviceCallbacks* cb, bool wantDuplicates = false, bool shouldParse = true) {
        _cb = cb;
        (void)wantDuplicates;
        (void)shouldParse;
    }
    void setActiveScan(bool active) { _active = active; }
    void setInterval(uint16_t ms) { _interval = ms; }
    void setWindow(uint16_t ms) { _window = ms; }
    BLEScanResults start(uint32_t durationSeconds, bool is_continue = false);
    bool start(uint32_t durationSeconds, void (*scanCompleteCB)(BLEScanResults), bool is_continue = false);
    void stop() {}
    void clearResults() { _results._count = 0; }
    BLEScanResults getResults() { return _results; }

private:
    BLEAdvertisedDeviceCallbacks* _cb = nullptr;
    bool _active = false;
    uint16_t _interval = 100;
    uint16_t _window = 99;
    BLEScanResults _results;
};

class BLEDescriptor {
public:
    explicit BLEDescriptor(const BLEUUID& uuid) : _uuid(uuid) {}
    virtual ~BLEDescriptor() {}
    BLEUUID getUUID() { return _uuid; }

private:
    BLEUUID _uuid;
};

class BLE2902 : public BLEDescriptor {
public:
    BLE2902() : BLEDescriptor(BLEUUID((uint16_t)0x2902)) {}
    void setNotifications(bool on) { _notify = on; }
    bool getNotifications() { return _notify; }
    void setIndications(bool on) { _indicate = on; }

private:
    bool _notify = false;
    bool _indicate = false;
};

class BLECharacteristic;
class BLECharacteristicCallbacks {
public:
    virtual ~BLECharacteristicCallbacks() {}
    virtual void onRead(BLECharacteristic* c) { (void)c; }
    virtual void onWrite(BLECharacteristic* c) { (void)c; }
    virtual void onNotify(BLECharacteristic* c) { (void)c; }
};

class BLEService;
class BLECharacteristic {
public:
    static const uint32_t PROPERTY_READ = 1 << 0;
    static const uint32_t PROPERTY_WRITE = 1 << 1;
    static const uint32_t PROPERTY_NOTIFY = 1 << 2;
    static const uint32_t PROPERTY_BROADCAST = 1 << 3;
    static const uint32_t PROPERTY_INDICATE = 1 << 4;
    static const uint32_t PROPERTY_WRITE_NR = 1 << 5;

    BLECharacteristic(const BLEUUID& uuid, uint32_t properties = PROPERTY_READ | PROPERTY_WRITE) : _uuid(uuid), _props(properties) {}
    void setValue(const uint8_t* data, size_t length);
    void setValue(const String& value) { setValue(reinterpret_cast<const uint8_t*>(value.c_str()), value.length()); }
    void setValue(const char* value) { setValue(reinterpret_cast<const uint8_t*>(value), strlen(value)); }
    void setValue(uint16_t v) { setValue(reinterpret_cast<const uint8_t*>(&v), 2); }
    void setValue(uint32_t v) { setValue(reinterpret_cast<const uint8_t*>(&v), 4); }
    void setValue(int v) { setValue(reinterpret_cast<const uint8_t*>(&v), 4); }
    void setValue(float v) { setValue(reinterpret_cast<const uint8_t*>(&v), 4); }
    void setValue(double v) { setValue(reinterpret_cast<const uint8_t*>(&v), 8); }
    String getValue() { return String(_value, (unsigned int)_len); }
    uint8_t* getData() { return _value; }
    size_t getLength() { return _len; }
    void notify(bool is_notification = true);
    void indicate() { notify(false); }
    void addDescriptor(BLEDescriptor* d) { (void)d; }
    void setCallbacks(BLECharacteristicCallbacks* cb) { _cb = cb; }
    BLEUUID getUUID() { return _uuid; }
    int handle() const { return _handle; }

    /* internal */
    BLEUUID _uuid;
    uint32_t _props;
    BLEService* _service = nullptr;
    int _handle = -1;
    BLECharacteristicCallbacks* _cb = nullptr;
    uint8_t _value[512];
    size_t _len = 0;
    void registerWithMedium();
};

class BLEServer;
class BLEService {
public:
    BLEService(const BLEUUID& uuid, BLEServer* server) : _uuid(uuid), _server(server) {}
    BLECharacteristic* createCharacteristic(const char* uuid, uint32_t properties) { return createCharacteristic(BLEUUID(uuid), properties); }
    BLECharacteristic* createCharacteristic(const BLEUUID& uuid, uint32_t properties);
    void addCharacteristic(BLECharacteristic* c);
    void start() {}
    void stop() {}
    BLEUUID getUUID() { return _uuid; }
    BLEServer* getServer() { return _server; }

private:
    BLEUUID _uuid;
    BLEServer* _server;
};

class BLEServerCallbacks {
public:
    virtual ~BLEServerCallbacks() {}
    virtual void onConnect(BLEServer* server) { (void)server; }
    virtual void onDisconnect(BLEServer* server) { (void)server; }
};

class BLEAdvertising;
class BLEServer {
public:
    BLEService* createService(const char* uuid) { return createService(BLEUUID(uuid)); }
    BLEService* createService(const BLEUUID& uuid);
    void setCallbacks(BLEServerCallbacks* cb) { _cb = cb; }
    uint32_t getConnectedCount() { return _connected; }
    BLEAdvertising* getAdvertising();
    void startAdvertising();
    void disconnect(uint16_t connId);

    BLEServerCallbacks* _cb = nullptr;
    uint32_t _connected = 0;
};

class BLEAdvertisementData {
public:
    void setName(const String& name) { _name = name; }
    void setManufacturerData(const String& data) { _mfr = data; }
    void setCompleteServices(const BLEUUID& uuid) { _uuid = uuid.toString(); }
    void setFlags(uint8_t) {}
    String _name;
    String _mfr;
    String _uuid;
};

class BLEAdvertising {
public:
    void addServiceUUID(const BLEUUID& uuid) { _uuid = uuid.toString(); }
    void addServiceUUID(const char* uuid) { _uuid = uuid; }
    void setScanResponse(bool) {}
    void setMinPreferred(uint16_t) {}
    void setMaxPreferred(uint16_t) {}
    void setMinInterval(uint16_t units) { _intervalMs = units * 0.625; }
    void setMaxInterval(uint16_t) {}
    void setAdvertisementData(BLEAdvertisementData& d);
    void setScanResponseData(BLEAdvertisementData& d) { setAdvertisementData(d); }
    void setAppearance(uint16_t) {}
    void start();
    void stop();

    String _uuid;
    String _name;
    String _mfr;
    double _intervalMs = 100.0;
    bool _on = false;
};

class BLERemoteCharacteristic;
typedef void (*notify_callback)(BLERemoteCharacteristic* pBLERemoteCharacteristic, uint8_t* pData, size_t length, bool isNotify);

class BLEClient;
class BLERemoteCharacteristic {
public:
    bool canNotify() { return (_props & BLECharacteristic::PROPERTY_NOTIFY) != 0; }
    bool canIndicate() { return (_props & BLECharacteristic::PROPERTY_INDICATE) != 0; }
    bool canRead() { return (_props & BLECharacteristic::PROPERTY_READ) != 0; }
    bool canWrite() { return (_props & (BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_WRITE_NR)) != 0; }
    String readValue();
    uint8_t readUInt8();
    uint16_t readUInt16();
    uint32_t readUInt32();
    float readFloat();
    void writeValue(const uint8_t* data, size_t length, bool response = false);
    void writeValue(const String& value, bool response = false) { writeValue(reinterpret_cast<const uint8_t*>(value.c_str()), value.length(), response); }
    void writeValue(uint8_t v, bool response = false) { writeValue(&v, 1, response); }
    void registerForNotify(notify_callback cb, bool notifications = true, bool descriptorRequiresRegistration = true);
    BLEUUID getUUID() { return _uuid; }

    BLEUUID _uuid;
    BLEClient* _client = nullptr;
    int _handle = -1;
    uint32_t _props = 0;
    notify_callback _cb = nullptr;
};

class BLERemoteService {
public:
    BLERemoteCharacteristic* getCharacteristic(const char* uuid) { return getCharacteristic(BLEUUID(uuid)); }
    BLERemoteCharacteristic* getCharacteristic(const BLEUUID& uuid);
    BLEUUID getUUID() { return _uuid; }

    BLEUUID _uuid;
    BLEClient* _client = nullptr;
};

class BLEClientCallbacks {
public:
    virtual ~BLEClientCallbacks() {}
    virtual void onConnect(BLEClient* client) { (void)client; }
    virtual void onDisconnect(BLEClient* client) { (void)client; }
};

class BLEClient {
public:
    bool connect(BLEAdvertisedDevice* device) { return device && connect(device->getAddress()); }
    bool connect(BLEAddress address, int type = 0, uint32_t timeoutMs = 0);
    void disconnect();
    bool isConnected() { return _conn > 0; }
    void setClientCallbacks(BLEClientCallbacks* cb) { _cb = cb; }
    BLERemoteService* getService(const char* uuid) { return getService(BLEUUID(uuid)); }
    BLERemoteService* getService(const BLEUUID& uuid);
    int getRssi() { return _rssi; }
    BLEAddress getPeerAddress() { return _peer; }
    bool setMTU(uint16_t) { return true; }

    int _conn = 0;
    int _rssi = 0;
    BLEAddress _peer;
    BLEClientCallbacks* _cb = nullptr;
};

typedef enum { ESP_PWR_LVL_N12 = 0, ESP_PWR_LVL_N9, ESP_PWR_LVL_N6, ESP_PWR_LVL_N3, ESP_PWR_LVL_N0, ESP_PWR_LVL_P3, ESP_PWR_LVL_P6, ESP_PWR_LVL_P9 } esp_power_level_t;

class BLEDevice {
public:
    static void init(const String& deviceName);
    static void init(const char* deviceName) { init(String(deviceName ? deviceName : "")); }
    static void deinit(bool release_memory = false);
    static bool getInitialized();
    static BLEScan* getScan();
    static BLEServer* createServer();
    static BLEClient* createClient();
    static BLEAdvertising* getAdvertising();
    static void startAdvertising();
    static void stopAdvertising();
    static BLEAddress getAddress();
    static void setPower(esp_power_level_t level, int type = 0) { (void)level; (void)type; }
    static void setMTU(uint16_t) {}
    static String toString();
};

#endif
