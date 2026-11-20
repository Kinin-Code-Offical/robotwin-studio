/*
 * Bluepad32 (Arduino API) for the RoboTwin ESP32 HAL model.
 *
 * Game controllers in the RobotWin world (a "Kablosuz kumanda" object fed by a real
 * DualSense / DualShock 4 / Xbox pad on the PC or by the on-screen virtual pad) pair
 * with this ESP32 over the simulated Bluetooth link: put the controller into pairing
 * mode, call BP32.setup() + BP32.update(), and onConnected() fires with a ControllerPtr.
 * Bonded controllers reconnect by themselves; walking out of radio range drops the link
 * after the supervision timeout (onDisconnected()). Values use Bluepad32's ranges:
 * sticks -511..512 (up = negative Y), triggers 0..1023.
 */
#ifndef RW_BLUEPAD32_H
#define RW_BLUEPAD32_H
#include "Arduino.h"

#define BP32_MAX_GAMEPADS 4
#define BP32_MAX_DEVICES 4

enum {
    BUTTON_A = 1 << 0,
    BUTTON_B = 1 << 1,
    BUTTON_X = 1 << 2,
    BUTTON_Y = 1 << 3,
    BUTTON_SHOULDER_L = 1 << 4,
    BUTTON_SHOULDER_R = 1 << 5,
    BUTTON_TRIGGER_L = 1 << 6,
    BUTTON_TRIGGER_R = 1 << 7,
    BUTTON_THUMB_L = 1 << 8,
    BUTTON_THUMB_R = 1 << 9,
};

enum {
    DPAD_UP = 1 << 0,
    DPAD_DOWN = 1 << 1,
    DPAD_RIGHT = 1 << 2,
    DPAD_LEFT = 1 << 3,
};

enum {
    MISC_BUTTON_SYSTEM = 1 << 0,  /* PS / Xbox */
    MISC_BUTTON_SELECT = 1 << 1,  /* Create / Share / View */
    MISC_BUTTON_START = 1 << 2,   /* Options / Menu */
    MISC_BUTTON_CAPTURE = 1 << 3, /* touchpad click */
};

typedef enum {
    CONTROLLER_TYPE_UnknownSteamController = 1,
    CONTROLLER_TYPE_XBoxOneController = 31,
    CONTROLLER_TYPE_PS4Controller = 32,
    CONTROLLER_TYPE_PS5Controller = 34,
} controller_type_t;

struct ControllerProperties {
    uint8_t btaddr[6];
    uint16_t type;
    uint16_t vendor_id;
    uint16_t product_id;
    uint16_t flags;
};

class Controller {
public:
    bool isConnected() const { return _connected; }
    bool hasData() const { return _connected; }
    bool isGamepad() const { return true; }
    bool isMouse() const { return false; }
    bool isKeyboard() const { return false; }
    bool isBalanceBoard() const { return false; }
    bool isAnyController() const { return true; }
    int index() const { return _index; }
    String getModelName() const { return String(_name); }
    ControllerProperties getProperties() const;
    int getClass() const { return 1; }

    int32_t axisX() const { return _r.axis_x; }
    int32_t axisY() const { return _r.axis_y; }
    int32_t axisRX() const { return _r.axis_rx; }
    int32_t axisRY() const { return _r.axis_ry; }
    int32_t brake() const { return _r.brake; }
    int32_t throttle() const { return _r.throttle; }
    uint16_t buttons() const { return _r.buttons; }
    uint8_t dpad() const { return _r.dpad; }
    uint16_t miscButtons() const { return _r.misc; }
    int32_t gyroX() const { return _r.gyro[0]; }
    int32_t gyroY() const { return _r.gyro[1]; }
    int32_t gyroZ() const { return _r.gyro[2]; }
    int32_t accelX() const { return _r.accel[0]; }
    int32_t accelY() const { return _r.accel[1]; }
    int32_t accelZ() const { return _r.accel[2]; }
    uint8_t battery() const { return _r.battery; }

    bool a() const { return (_r.buttons & BUTTON_A) != 0; }
    bool b() const { return (_r.buttons & BUTTON_B) != 0; }
    bool x() const { return (_r.buttons & BUTTON_X) != 0; }
    bool y() const { return (_r.buttons & BUTTON_Y) != 0; }
    bool l1() const { return (_r.buttons & BUTTON_SHOULDER_L) != 0; }
    bool r1() const { return (_r.buttons & BUTTON_SHOULDER_R) != 0; }
    bool l2() const { return (_r.buttons & BUTTON_TRIGGER_L) != 0; }
    bool r2() const { return (_r.buttons & BUTTON_TRIGGER_R) != 0; }
    bool thumbL() const { return (_r.buttons & BUTTON_THUMB_L) != 0; }
    bool thumbR() const { return (_r.buttons & BUTTON_THUMB_R) != 0; }
    bool miscSystem() const { return (_r.misc & MISC_BUTTON_SYSTEM) != 0; }
    bool miscSelect() const { return (_r.misc & MISC_BUTTON_SELECT) != 0; }
    bool miscStart() const { return (_r.misc & MISC_BUTTON_START) != 0; }
    bool miscCapture() const { return (_r.misc & MISC_BUTTON_CAPTURE) != 0; }
    bool miscHome() const { return miscSystem(); }
    bool miscBack() const { return miscSelect(); }

    void setColorLED(uint8_t red, uint8_t green, uint8_t blue);
    void setPlayerLEDs(uint8_t led);
    void playDualRumble(uint16_t delayedStartMs, uint16_t durationMs, uint8_t weakMagnitude, uint8_t strongMagnitude);
    void setRumble(uint8_t force, uint8_t duration) { playDualRumble(0, (uint16_t)(duration * 4), force, force); }
    void disconnect();

    /* internal */
    int _index = 0;
    bool _connected = false;
    char _name[24] = {0};
    struct Raw {
        int32_t axis_x, axis_y, axis_rx, axis_ry, brake, throttle;
        uint16_t buttons;
        uint8_t dpad, misc, battery, model;
        int32_t gyro[3], accel[3];
        uint8_t addr[6];
        int32_t rssi;
    } _r = {};
};

typedef Controller* ControllerPtr;
typedef void (*GamepadCallback)(ControllerPtr);
typedef GamepadCallback ControllerCallback;

class Bluepad32 {
public:
    void setup(ControllerCallback onConnect, ControllerCallback onDisconnect, bool startScanning = true);
    bool update();
    void forgetBluetoothKeys();
    void enableNewBluetoothConnections(bool enabled);
    void enableVirtualDevice(bool) {}
    void enableBLEService(bool) {}
    const uint8_t* localBdAddress();
    const char* firmwareVersion() const { return "Bluepad32 for Arduino (RoboTwin simulated) v4.1"; }

private:
    ControllerCallback _onConnect = nullptr;
    ControllerCallback _onDisconnect = nullptr;
    bool _started = false;
};

extern Bluepad32 BP32;

#endif
