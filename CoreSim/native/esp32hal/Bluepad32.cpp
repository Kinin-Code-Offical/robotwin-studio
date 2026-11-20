/* Bluepad32 over the radio medium (see Bluepad32.h). */
#include "Bluepad32.h"
#include "rw_internal.h"

Bluepad32 BP32;

namespace {
Controller g_ctl[BP32_MAX_GAMEPADS];
uint32_t g_seq[BP32_MAX_GAMEPADS];
uint8_t g_bt[6] = {0x24, 0x0A, 0xC4, 0x5A, 0x7E, 0x13};

#pragma pack(push, 1)
struct OutputIn {
    int32_t index;
    int32_t what;
    uint8_t r, g, b, leds, weak, strong;
    uint16_t pad;
    uint32_t duration_ms;
};
#pragma pack(pop)

void send_output(int index, int what, uint8_t r, uint8_t g, uint8_t b, uint8_t leds, uint8_t weak, uint8_t strong, uint32_t ms) {
    OutputIn in;
    memset(&in, 0, sizeof in);
    in.index = index;
    in.what = what;
    in.r = r;
    in.g = g;
    in.b = b;
    in.leds = leds;
    in.weak = weak;
    in.strong = strong;
    in.duration_ms = ms;
    rw::charge(rw::cost(20000.0));
    rw::radio(RW_RADIO_PAD_OUTPUT, &in, sizeof in, nullptr, 0);
}
}  // namespace

namespace rw {
void hal_bp32_reset() {
    BP32 = Bluepad32();
    for (int i = 0; i < BP32_MAX_GAMEPADS; i++) {
        g_ctl[i] = Controller();
        g_ctl[i]._index = i;
        g_seq[i] = 0;
    }

    memcpy(g_bt, radio_mac(), 6);
    g_bt[5] = (uint8_t)(g_bt[5] + 2);
}
}  // namespace rw

ControllerProperties Controller::getProperties() const {
    ControllerProperties p;
    memset(&p, 0, sizeof p);
    memcpy(p.btaddr, _r.addr, 6);
    switch (_r.model) {
        case 0: p.type = CONTROLLER_TYPE_PS5Controller; p.vendor_id = 0x054C; p.product_id = 0x0CE6; break;
        case 1: p.type = CONTROLLER_TYPE_PS4Controller; p.vendor_id = 0x054C; p.product_id = 0x09CC; break;
        case 2: p.type = CONTROLLER_TYPE_XBoxOneController; p.vendor_id = 0x045E; p.product_id = 0x0B13; break;
        default: p.type = CONTROLLER_TYPE_UnknownSteamController; break;
    }
    return p;
}

void Controller::setColorLED(uint8_t red, uint8_t green, uint8_t blue) {
    if (_connected) send_output(_index, 1, red, green, blue, 0, 0, 0, 0);
}

void Controller::setPlayerLEDs(uint8_t led) {
    if (_connected) send_output(_index, 2, 0, 0, 0, led, 0, 0, 0);
}

void Controller::playDualRumble(uint16_t delayedStartMs, uint16_t durationMs, uint8_t weakMagnitude, uint8_t strongMagnitude) {
    (void)delayedStartMs;
    if (_connected) send_output(_index, 4, 0, 0, 0, 0, weakMagnitude, strongMagnitude, durationMs);
}

void Controller::disconnect() {
    if (!_connected) return;
    int32_t i = _index;
    rw::radio(RW_RADIO_PAD_DISCONNECT, &i, sizeof i, nullptr, 0);
}

void Bluepad32::setup(ControllerCallback onConnect, ControllerCallback onDisconnect, bool startScanning) {
    _onConnect = onConnect;
    _onDisconnect = onDisconnect;
    _started = true;
    rw::charge(rw::cost(80000000.0));  // BTstack + controller start ≈ 80 ms
    rw::log(RW_LOG_INFO, "Bluepad32 (simulated): %s", rw::radio_available()
        ? "waiting for controllers - put a 'Kablosuz kumanda' in pairing mode"
        : "lone board, no controllers exist (place the board in a RobotWin world)");
    if (!rw::radio_available()) return;
    int32_t allow = startScanning ? 1 : 0;
    rw::radio(RW_RADIO_PAD_ENABLE, &allow, sizeof allow, nullptr, 0);
}

void Bluepad32::enableNewBluetoothConnections(bool enabled) {
    if (!rw::radio_available()) return;
    int32_t allow = enabled ? 1 : 0;
    rw::radio(RW_RADIO_PAD_ENABLE, &allow, sizeof allow, nullptr, 0);
}

void Bluepad32::forgetBluetoothKeys() {
    if (rw::radio_available()) rw::radio(RW_RADIO_PAD_FORGET, nullptr, 0, nullptr, 0);
}

const uint8_t* Bluepad32::localBdAddress() { return g_bt; }

bool Bluepad32::update() {
    rw::charge(rw::cost(40000.0));
    if (!_started || !rw::radio_available()) return false;
    static rw_pad_record recs[BP32_MAX_GAMEPADS];
    memset(recs, 0, sizeof recs);
    int n = rw::radio(RW_RADIO_PAD_POLL, nullptr, 0, recs, sizeof recs);
    if (n <= 0) return false;
    bool updated = false;
    for (int i = 0; i < n && i < BP32_MAX_GAMEPADS; i++) {
        const rw_pad_record& r = recs[i];
        Controller& c = g_ctl[i];
        c._index = i;
        if (r.connected) {
            c._r.axis_x = r.axis_x;
            c._r.axis_y = r.axis_y;
            c._r.axis_rx = r.axis_rx;
            c._r.axis_ry = r.axis_ry;
            c._r.brake = r.brake;
            c._r.throttle = r.throttle;
            c._r.buttons = r.buttons;
            c._r.dpad = r.dpad;
            c._r.misc = r.misc;
            c._r.battery = r.battery;
            c._r.model = r.model;
            memcpy(c._r.gyro, r.gyro, sizeof r.gyro);
            memcpy(c._r.accel, r.accel, sizeof r.accel);
            memcpy(c._r.addr, r.addr, 6);
            c._r.rssi = r.rssi;
            memcpy(c._name, r.name, sizeof c._name);
            c._name[sizeof c._name - 1] = 0;
            if (!c._connected) {
                c._connected = true;
                rw::log(RW_LOG_INFO, "Bluepad32: device connected, idx=%d %s %02X:%02X:%02X:%02X:%02X:%02X", i, c._name,
                        r.addr[0], r.addr[1], r.addr[2], r.addr[3], r.addr[4], r.addr[5]);
                if (_onConnect) _onConnect(&c);
            }
        } else if (c._connected) {
            if (_onDisconnect) _onDisconnect(&c);
            c._connected = false;
            c._r = Controller::Raw();
        }

        if (r.seq != g_seq[i]) {
            g_seq[i] = r.seq;
            updated = true;
        }
    }

    return updated;
}
