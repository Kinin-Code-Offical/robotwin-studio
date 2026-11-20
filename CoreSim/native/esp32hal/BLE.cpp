/* BLE subset over the radio medium (see rw_ble.h). */
#include "rw_ble.h"
#include "rw_internal.h"

namespace {

#pragma pack(push, 1)
struct AdvIn {
    int32_t on;
    int32_t interval_ms;
    char name[32];
    char uuid[40];
    uint8_t mfr_len;
    uint8_t mfr[31];
};
struct ScanIn {
    uint64_t from_ns;
    uint64_t to_ns;
};
struct CharIn {
    char svc[40];
    char chr[40];
    uint32_t props;
};
struct FindIn {
    int32_t conn;
    char svc[40];
    char chr[40];
};
struct FindOut {
    int32_t handle;
    uint32_t props;
};
struct PollOut {
    int32_t kind;
    int32_t conn;
    int32_t handle;
    uint8_t addr[6];
    uint16_t len;
    uint8_t data[512];
};
#pragma pack(pop)

const int kMaxChars = 24;
const int kMaxRemote = 24;
const int kMaxClients = 4;

bool g_init = false;
bool g_task = false;
String* g_name = nullptr;
uint8_t g_bt_mac[6] = {0x24, 0x0A, 0xC4, 0x5A, 0x7E, 0x13};
BLEScan* g_scan = nullptr;
BLEServer* g_server = nullptr;
BLEAdvertising* g_adv = nullptr;
BLECharacteristic* g_chars[kMaxChars];
int g_char_count = 0;
BLERemoteCharacteristic* g_remote[kMaxRemote];
int g_remote_count = 0;
BLEClient* g_clients[kMaxClients];
int g_client_count = 0;
BLEService* g_last_service = nullptr;

void copy_str(char* dst, size_t size, const String& s) {
    memset(dst, 0, size);
    strncpy_s(dst, size, s.c_str(), _TRUNCATE);
}

void dispatch(const PollOut& ev) {
    switch (ev.kind) {
        case 1: /* notify -> client */
            for (int i = 0; i < g_remote_count; i++) {
                BLERemoteCharacteristic* r = g_remote[i];
                if (r && r->_client && r->_client->_conn == ev.conn && r->_handle == ev.handle && r->_cb) {
                    r->_cb(r, const_cast<uint8_t*>(ev.data), ev.len, true);
                }
            }
            break;
        case 2: /* write -> server characteristic */
            for (int i = 0; i < g_char_count; i++) {
                BLECharacteristic* c = g_chars[i];
                if (c && c->_handle == ev.handle) {
                    size_t n = ev.len < sizeof c->_value ? ev.len : sizeof c->_value;
                    memcpy(c->_value, ev.data, n);
                    c->_len = n;
                    if (c->_cb) c->_cb->onWrite(c);
                }
            }
            break;
        case 3: /* a client connected to our server */
            if (g_server) {
                g_server->_connected++;
                if (g_server->_cb) g_server->_cb->onConnect(g_server);
            }
            break;
        case 4: { /* disconnect: server side or client side */
            bool client = false;
            for (int i = 0; i < g_client_count; i++) {
                BLEClient* c = g_clients[i];
                if (c && c->_conn == ev.conn) {
                    c->_conn = 0;
                    client = true;
                    if (c->_cb) c->_cb->onDisconnect(c);
                }
            }
            if (!client && g_server) {
                if (g_server->_connected > 0) g_server->_connected--;
                if (g_server->_cb) g_server->_cb->onDisconnect(g_server);
            }
            break;
        }
    }
}

/* The Bluetooth controller task: delivers notifications, writes and connection events to the callbacks. */
void bt_task(void*) {
    static PollOut ev;
    for (;;) {
        for (int k = 0; k < 16; k++) {
            memset(&ev, 0, 24);
            if (rw::radio(RW_RADIO_BLE_POLL, nullptr, 0, &ev, sizeof ev) != 1) break;
            dispatch(ev);
        }
        rw::sleep_until(rw::now_ns() + 2000000ull);
    }
}

void ensure_task() {
    if (g_task || !rw::radio_available()) return;
    g_task = true;
    rw::create_task(bt_task, "btController", 4096, nullptr, 22);
}

void push_adv() {
    if (!g_adv) return;
    AdvIn in;
    memset(&in, 0, sizeof in);
    in.on = g_adv->_on ? 1 : 0;
    in.interval_ms = (int32_t)(g_adv->_intervalMs + 0.5);
    copy_str(in.name, sizeof in.name, g_adv->_name.length() ? g_adv->_name : (g_name ? *g_name : String()));
    copy_str(in.uuid, sizeof in.uuid, g_adv->_uuid);
    in.mfr_len = (uint8_t)(g_adv->_mfr.length() < 31 ? g_adv->_mfr.length() : 31);
    memcpy(in.mfr, g_adv->_mfr.c_str(), in.mfr_len);
    rw::radio(RW_RADIO_BLE_ADV, &in, sizeof in, nullptr, 0);
}

int hexval(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }

}  // namespace

namespace rw {
void hal_ble_reset() {
    /* Objects allocated by the previous run lived in the sketch heap, which is gone. */
    g_init = false;
    g_task = false;
    g_name = nullptr;
    g_scan = nullptr;
    g_server = nullptr;
    g_adv = nullptr;
    g_char_count = 0;
    g_remote_count = 0;
    g_client_count = 0;
    g_last_service = nullptr;
}
}  // namespace rw

/* ---------------------------------------------------------------- UUID / address */
BLEUUID::BLEUUID(uint16_t uuid16) {
    char buf[40];
    snprintf(buf, sizeof buf, "0000%04x-0000-1000-8000-00805f9b34fb", uuid16);
    _s = buf;
}

BLEUUID::BLEUUID(uint32_t uuid32) {
    char buf[40];
    snprintf(buf, sizeof buf, "%08x-0000-1000-8000-00805f9b34fb", (unsigned)uuid32);
    _s = buf;
}

BLEAddress::BLEAddress(const char* text) {
    memset(_a, 0, 6);
    if (!text) return;
    int i = 0;
    for (const char* p = text; *p && i < 6;) {
        int h = hexval(p[0]), l = p[1] ? hexval(p[1]) : -1;
        if (h < 0 || l < 0) {
            p++;
            continue;
        }
        _a[i++] = (uint8_t)(h * 16 + l);
        p += 2;
    }
}

String BLEAddress::toString() const {
    char buf[18];
    snprintf(buf, sizeof buf, "%02x:%02x:%02x:%02x:%02x:%02x", _a[0], _a[1], _a[2], _a[3], _a[4], _a[5]);
    return String(buf);
}

String BLEAdvertisedDevice::toString() const {
    String s = "Name: " + _name + ", Address: " + _addr.toString();
    if (_uuid.length()) s += ", serviceUUID: " + _uuid;
    s += ", rssi: " + String(_rssi);
    return s;
}

/* ---------------------------------------------------------------- device */
void BLEDevice::init(const String& deviceName) {
    rw::charge(rw::cost(50000000.0));  // controller + host stack start ≈ 50 ms
    if (!g_name) g_name = new String();
    *g_name = deviceName;
    g_init = true;
    if (!rw::radio_available()) {
        rw::log_once(0x900, RW_LOG_INFO, "BLE on a lone board: no other radios exist (place the board in a RobotWin world)");
        return;
    }

    uint8_t mac[6];
    if (rw::radio(RW_RADIO_BLE_INIT, deviceName.c_str(), (int)deviceName.length() + 1, mac, 6) >= 6) memcpy(g_bt_mac, mac, 6);
    ensure_task();
}

void BLEDevice::deinit(bool) {
    if (g_adv) {
        g_adv->_on = false;
        push_adv();
    }
    g_init = false;
}

bool BLEDevice::getInitialized() { return g_init; }

BLEScan* BLEDevice::getScan() {
    if (!g_scan) g_scan = new BLEScan();
    return g_scan;
}

BLEServer* BLEDevice::createServer() {
    if (!g_server) g_server = new BLEServer();
    ensure_task();
    return g_server;
}

BLEClient* BLEDevice::createClient() {
    BLEClient* c = new BLEClient();
    if (g_client_count < kMaxClients) g_clients[g_client_count++] = c;
    ensure_task();
    return c;
}

BLEAdvertising* BLEDevice::getAdvertising() {
    if (!g_adv) g_adv = new BLEAdvertising();
    return g_adv;
}

void BLEDevice::startAdvertising() { getAdvertising()->start(); }
void BLEDevice::stopAdvertising() { getAdvertising()->stop(); }

BLEAddress BLEDevice::getAddress() { return BLEAddress(g_bt_mac); }

String BLEDevice::toString() { return "BLEDevice " + getAddress().toString(); }

/* ---------------------------------------------------------------- scanning */
BLEScanResults BLEScan::start(uint32_t durationSeconds, bool is_continue) {
    if (!is_continue) _results._count = 0;
    uint64_t from = rw::now_ns();
    uint64_t to = from + (uint64_t)durationSeconds * 1000000000ull;
    rw::sleep_until(to);  // start() blocks for the scan duration, like the real library
    if (rw::radio_available()) {
        static uint8_t buf[4 + RW_BLE_MAX_RESULTS * sizeof(rw_ble_adv)];
        ScanIn in = {from, to};
        int n = rw::radio(RW_RADIO_BLE_SCAN, &in, sizeof in, buf, sizeof buf);
        for (int i = 0; i < n && i < RW_BLE_MAX_RESULTS; i++) {
            const rw_ble_adv* r = reinterpret_cast<const rw_ble_adv*>(buf + 4 + i * sizeof(rw_ble_adv));
            BLEAdvertisedDevice d;
            char name[33];
            memcpy(name, r->name, 32);
            name[32] = 0;
            char uuid[41];
            memcpy(uuid, r->uuid, 40);
            uuid[40] = 0;
            d._name = name;
            d._uuid = uuid;
            d._mfr = String(r->mfr, r->mfr_len);
            d._addr = BLEAddress(r->addr);
            d._rssi = r->rssi;
            d._tx = r->tx_power;
            int slot = _results._count;
            for (int k = 0; k < _results._count; k++) {
                if (_results._devices[k]._addr == d._addr) slot = k;
            }
            if (slot >= RW_BLE_MAX_RESULTS) continue;
            _results._devices[slot] = d;
            if (slot == _results._count) _results._count++;
            if (_cb) _cb->onResult(d);
        }
    }

    _results._self = &_results;
    return _results;
}

bool BLEScan::start(uint32_t durationSeconds, void (*scanCompleteCB)(BLEScanResults), bool is_continue) {
    BLEScanResults r = start(durationSeconds, is_continue);
    if (scanCompleteCB) scanCompleteCB(r);
    return true;
}

/* ---------------------------------------------------------------- server */
void BLECharacteristic::registerWithMedium() {
    if (_handle >= 0 || !rw::radio_available()) return;
    CharIn in;
    memset(&in, 0, sizeof in);
    copy_str(in.svc, sizeof in.svc, _service ? _service->getUUID().toString() : String());
    copy_str(in.chr, sizeof in.chr, _uuid.toString());
    in.props = _props;
    _handle = rw::radio(RW_RADIO_BLE_ADD_CHAR, &in, sizeof in, nullptr, 0);
}

void BLECharacteristic::setValue(const uint8_t* data, size_t length) {
    if (length > sizeof _value) length = sizeof _value;
    if (data && length) memcpy(_value, data, length);
    _len = length;
    if (_handle < 0) return;
    static uint8_t frame[8 + 512];
    int32_t h = _handle, notify = 0;
    memcpy(frame, &h, 4);
    memcpy(frame + 4, &notify, 4);
    memcpy(frame + 8, _value, _len);
    rw::radio(RW_RADIO_BLE_SET_VALUE, frame, (int)(8 + _len), nullptr, 0);
}

void BLECharacteristic::notify(bool) {
    rw::charge(rw::cost(30000.0));
    if (_handle < 0) return;
    static uint8_t frame[8 + 512];
    int32_t h = _handle, notify = 1;
    memcpy(frame, &h, 4);
    memcpy(frame + 4, &notify, 4);
    memcpy(frame + 8, _value, _len);
    rw::radio(RW_RADIO_BLE_SET_VALUE, frame, (int)(8 + _len), nullptr, 0);
    if (_cb) _cb->onNotify(this);
}

BLECharacteristic* BLEService::createCharacteristic(const BLEUUID& uuid, uint32_t properties) {
    BLECharacteristic* c = new BLECharacteristic(uuid, properties);
    addCharacteristic(c);
    return c;
}

void BLEService::addCharacteristic(BLECharacteristic* c) {
    if (!c) return;
    c->_service = this;
    c->registerWithMedium();
    if (g_char_count < kMaxChars) g_chars[g_char_count++] = c;
}

BLEService* BLEServer::createService(const BLEUUID& uuid) {
    g_last_service = new BLEService(uuid, this);
    return g_last_service;
}

BLEAdvertising* BLEServer::getAdvertising() { return BLEDevice::getAdvertising(); }

void BLEServer::startAdvertising() { BLEDevice::startAdvertising(); }

void BLEServer::disconnect(uint16_t connId) {
    int32_t c = connId;
    rw::radio(RW_RADIO_BLE_DISCONNECT, &c, sizeof c, nullptr, 0);
}

void BLEAdvertising::setAdvertisementData(BLEAdvertisementData& d) {
    if (d._name.length()) _name = d._name;
    if (d._mfr.length()) _mfr = d._mfr;
    if (d._uuid.length()) _uuid = d._uuid;
    if (_on) push_adv();
}

void BLEAdvertising::start() {
    _on = true;
    rw::charge(rw::cost(2000000.0));
    push_adv();
}

void BLEAdvertising::stop() {
    _on = false;
    push_adv();
}

/* ---------------------------------------------------------------- client */
bool BLEClient::connect(BLEAddress address, int, uint32_t) {
    if (!rw::radio_available()) {
        rw::sleep_until(rw::now_ns() + 3000000000ull);
        return false;
    }

    _peer = address;
    int c = rw::radio(RW_RADIO_BLE_CONNECT, address.getNative(), 6, nullptr, 0);
    rw::sleep_until(rw::now_ns() + (c > 0 ? 60000000ull : 3000000000ull));  // connection setup / supervision timeout
    if (c <= 0) return false;
    _conn = c;
    if (_cb) _cb->onConnect(this);
    return true;
}

void BLEClient::disconnect() {
    if (_conn <= 0) return;
    int32_t c = _conn;
    rw::radio(RW_RADIO_BLE_DISCONNECT, &c, sizeof c, nullptr, 0);
}

BLERemoteService* BLEClient::getService(const BLEUUID& uuid) {
    if (_conn <= 0) return nullptr;
    rw::sleep_until(rw::now_ns() + 30000000ull);  // service discovery
    BLERemoteService* s = new BLERemoteService();
    s->_uuid = uuid;
    s->_client = this;
    return s;
}

BLERemoteCharacteristic* BLERemoteService::getCharacteristic(const BLEUUID& uuid) {
    if (!_client || _client->_conn <= 0) return nullptr;
    FindIn in;
    memset(&in, 0, sizeof in);
    in.conn = _client->_conn;
    copy_str(in.svc, sizeof in.svc, _uuid.toString());
    copy_str(in.chr, sizeof in.chr, uuid.toString());
    FindOut out = {-1, 0};
    int h = rw::radio(RW_RADIO_BLE_FIND_CHAR, &in, sizeof in, &out, sizeof out);
    rw::sleep_until(rw::now_ns() + 15000000ull);
    if (h < 0) return nullptr;
    BLERemoteCharacteristic* r = new BLERemoteCharacteristic();
    r->_uuid = uuid;
    r->_client = _client;
    r->_handle = out.handle;
    r->_props = out.props;
    if (g_remote_count < kMaxRemote) g_remote[g_remote_count++] = r;
    return r;
}

String BLERemoteCharacteristic::readValue() {
    if (!_client || _client->_conn <= 0) return String();
    int32_t in[2] = {_client->_conn, _handle};
    static uint8_t buf[512];
    rw::sleep_until(rw::now_ns() + 15000000ull);  // one connection interval round trip
    int n = rw::radio(RW_RADIO_BLE_READ, in, sizeof in, buf, sizeof buf);
    return n > 0 ? String(buf, (unsigned int)n) : String();
}

uint8_t BLERemoteCharacteristic::readUInt8() {
    String v = readValue();
    return v.length() >= 1 ? (uint8_t)v.c_str()[0] : 0;
}

uint16_t BLERemoteCharacteristic::readUInt16() {
    String v = readValue();
    uint16_t x = 0;
    if (v.length() >= 2) memcpy(&x, v.c_str(), 2);
    return x;
}

uint32_t BLERemoteCharacteristic::readUInt32() {
    String v = readValue();
    uint32_t x = 0;
    if (v.length() >= 4) memcpy(&x, v.c_str(), 4);
    return x;
}

float BLERemoteCharacteristic::readFloat() {
    String v = readValue();
    float x = 0;
    if (v.length() >= 4) memcpy(&x, v.c_str(), 4);
    return x;
}

void BLERemoteCharacteristic::writeValue(const uint8_t* data, size_t length, bool response) {
    if (!_client || _client->_conn <= 0) return;
    static uint8_t frame[8 + 512];
    if (length > 512) length = 512;
    int32_t c = _client->_conn, h = _handle;
    memcpy(frame, &c, 4);
    memcpy(frame + 4, &h, 4);
    if (data && length) memcpy(frame + 8, data, length);
    rw::radio(RW_RADIO_BLE_WRITE, frame, (int)(8 + length), nullptr, 0);
    if (response) rw::sleep_until(rw::now_ns() + 15000000ull);
}

void BLERemoteCharacteristic::registerForNotify(notify_callback cb, bool notifications, bool) {
    _cb = cb;
    if (!_client || _client->_conn <= 0) return;
    int32_t in[3] = {_client->_conn, _handle, (cb && notifications) ? 1 : 0};
    rw::radio(RW_RADIO_BLE_SUBSCRIBE, in, sizeof in, nullptr, 0);
}
