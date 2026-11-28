/*
 * RoboTwin ESP32 HAL-level simulation: the C ABI between the simulator host
 * (C#, RobotTwin.CoreSim.Firmware.Esp32) and a sketch compiled natively for
 * Windows x64 against this Arduino-ESP32 API shim.
 *
 * The sketch DLL never touches hardware. Every I/O call goes through the
 * rw_host_api callback table handed to rw_init(). Simulated time is owned by
 * the host: the sketch only runs inside rw_run_until(), and blocks whenever
 * its charged time passes the end of the host's current slice.
 *
 * Keep this header plain C and in sync with Esp32Native.cs.
 */
#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ABI history: 1 = initial; 2 = radio_request (Wi-Fi / BLE / Bluepad32 through the host's radio medium). */
#define RW_ABI_VERSION 2

/* rw_run_until() results */
#define RW_STATUS_OK        0  /* sketch is blocked at/after the requested time */
#define RW_STATUS_WATCHDOG  1  /* a task ran longer than the watchdog limit without calling the API */
#define RW_STATUS_CRASHED   2  /* unhandled exception / access violation in sketch code (Guru Meditation) */
#define RW_STATUS_NOT_RUNNING 3

/* rw_host_api.system_event kinds */
#define RW_EVENT_RESTART        1  /* ESP.restart() / esp_restart(); arg unused */
#define RW_EVENT_DEEP_SLEEP     2  /* esp_deep_sleep_start(); arg = timer wake-up in us, or -1 */
#define RW_EVENT_WIFI           3  /* arg = 1 radio on, 0 radio off */
#define RW_EVENT_EXT0_WAKEUP    4  /* esp_sleep_enable_ext0_wakeup(); arg = gpio | (level << 8) */
#define RW_EVENT_LIGHT_SLEEP    5  /* esp_light_sleep_start(); arg = 1 asleep, 0 awake (the chip draws its sleep current) */

/* rw_host_api.log levels (ESP_LOG style) */
#define RW_LOG_ERROR   1
#define RW_LOG_WARN    2
#define RW_LOG_INFO    3

typedef struct rw_host_api {
    uint32_t abi_version;           /* RW_ABI_VERSION */
    uint32_t struct_size;           /* sizeof(rw_host_api) */
    void* user;

    /* One line of ESP-IDF style log output (goes to UART0 on a real board). */
    void (*log)(void* user, int level, const char* message);

    /* GPIO. mode uses the Arduino-ESP32 constants (INPUT 0x01, OUTPUT 0x03, PULLUP 0x04,
       INPUT_PULLUP 0x05, PULLDOWN 0x08, INPUT_PULLDOWN 0x09, OPEN_DRAIN 0x10,
       OUTPUT_OPEN_DRAIN 0x13, ANALOG 0xC0); 0 = pin released (high impedance). */
    void (*pin_mode)(void* user, int gpio, int mode);
    void (*digital_write)(void* user, int gpio, int level);
    int (*digital_read)(void* user, int gpio);

    /* Voltage at the pad in volts (the shim applies attenuation, resolution and the ADC model). */
    double (*analog_read)(void* user, int gpio);

    /* LEDC / analogWrite / tone output: frequency in Hz, duty 0..1. duty < 0 detaches the pin from LEDC. */
    void (*pwm_write)(void* user, int gpio, double frequency_hz, double duty);

    /* DAC (GPIO25/26): output voltage in volts; volts < 0 disables the DAC. */
    void (*dac_write)(void* user, int gpio, double volts);

    int (*touch_read)(void* user, int gpio);

    /* UARTs: port 0 = Serial, 1 = Serial1, 2 = Serial2. */
    void (*serial_begin)(void* user, int port, uint32_t baud);
    void (*serial_write)(void* user, int port, const uint8_t* data, int length);

    /* I2C master, byte level (maps 1:1 to RobotTwin.CoreSim.Firmware.I2cBus). bus 0 = Wire, 1 = Wire1. */
    int (*i2c_start)(void* user, int bus, int address7, int read);   /* returns 1 on ACK */
    int (*i2c_write)(void* user, int bus, int value);                /* returns 1 on ACK */
    int (*i2c_read)(void* user, int bus, int ack);
    void (*i2c_stop)(void* user, int bus);

    void (*system_event)(void* user, int kind, int64_t arg);

    /* ABI 2: one request to the host's radio medium (see RW_RADIO_* below). Returns >= 0 on
       success (a count or byte length), RW_RADIO_UNSUPPORTED when the host has no medium
       (the HAL then behaves like a lone board: Wi-Fi stub, nothing to scan or pair).
       Payloads are packed little-endian structs; the host fills at most out_cap bytes. */
    int (*radio_request)(void* user, int op, const void* in, int in_len, void* out, int out_cap);
} rw_host_api;

/* radio_request op codes (keep in sync with Esp32Radio.cs / Esp32RadioOp) */
#define RW_RADIO_UNSUPPORTED     (-1)
#define RW_RADIO_RESET           0x01  /* chip reset: the medium forgets this node's radio state */
#define RW_RADIO_INFO            0x02  /* out: u8 mac[6] (base MAC; AP = +1, BT = +2) */
#define RW_RADIO_WIFI_MODE       0x10  /* in: i32 mode */
#define RW_RADIO_WIFI_BEGIN      0x11  /* in: char ssid[33], char pass[65] */
#define RW_RADIO_WIFI_STATUS     0x12  /* out: i32 status, i32 rssi, u32 ip, u32 gw, i32 channel, u8 bssid[6] */
#define RW_RADIO_WIFI_DISCONNECT 0x13
#define RW_RADIO_WIFI_SCAN       0x14  /* out: i32 n, n x rw_wifi_net */
#define RW_RADIO_WIFI_SOFTAP     0x15  /* in: char ssid[33], char pass[65], i32 channel, i32 max_conn */
#define RW_RADIO_WIFI_SOFTAP_STOP 0x16
#define RW_RADIO_WIFI_SOFTAP_INFO 0x17 /* out: i32 stations */
#define RW_RADIO_UDP_BIND        0x20  /* in: u16 port, u16 on */
#define RW_RADIO_UDP_SEND        0x21  /* in: u32 ip, u16 dport, u16 sport, payload; ret: receivers */
#define RW_RADIO_UDP_RECV        0x22  /* in: u16 port; out: u32 ip, u16 sport, u16 len, payload; ret: bytes (0 none) */
#define RW_RADIO_TCP_LISTEN      0x23  /* in: u16 port, u16 on */
#define RW_RADIO_TCP_CONNECT     0x24  /* in: u32 ip, u16 port; ret: id > 0, -2 unreachable, -3 refused */
#define RW_RADIO_TCP_ACCEPT      0x25  /* in: u16 port; out: i32 id, u32 ip, u16 port; ret: id or 0 */
#define RW_RADIO_TCP_SEND        0x26  /* in: i32 id, payload; ret: bytes or -1 */
#define RW_RADIO_TCP_RECV        0x27  /* in: i32 id; out: payload; ret: bytes, 0, -1 closed */
#define RW_RADIO_TCP_CLOSE       0x28  /* in: i32 id */
#define RW_RADIO_TCP_STATE       0x29  /* in: i32 id; out: i32 connected, i32 available, u32 ip, u16 port */
#define RW_RADIO_BLE_INIT        0x30  /* in: device name; out: u8 bt_mac[6] */
#define RW_RADIO_BLE_ADV         0x31  /* in: i32 on, i32 interval_ms, char name[32], char uuid[40], u8 mfr_len, u8 mfr[31] */
#define RW_RADIO_BLE_SCAN        0x32  /* in: u64 from_ns, u64 to_ns; out: i32 n, n x rw_ble_adv */
#define RW_RADIO_BLE_ADD_CHAR    0x33  /* in: char svc[40], char chr[40], u32 props; ret: handle */
#define RW_RADIO_BLE_SET_VALUE   0x34  /* in: i32 handle, i32 notify, payload */
#define RW_RADIO_BLE_CONNECT     0x35  /* in: u8 addr[6]; ret: connection id or -1 */
#define RW_RADIO_BLE_FIND_CHAR   0x36  /* in: i32 conn, char svc[40], char chr[40]; out: i32 handle, u32 props; ret: handle or -1 */
#define RW_RADIO_BLE_SUBSCRIBE   0x37  /* in: i32 conn, i32 handle, i32 on */
#define RW_RADIO_BLE_READ        0x38  /* in: i32 conn, i32 handle; out: payload; ret: bytes or < 0 */
#define RW_RADIO_BLE_WRITE       0x39  /* in: i32 conn, i32 handle, payload */
#define RW_RADIO_BLE_DISCONNECT  0x3A  /* in: i32 conn */
#define RW_RADIO_BLE_POLL        0x3B  /* out: i32 kind (1 notify, 2 write, 3 connect, 4 disconnect), i32 conn, i32 handle, u8 addr[6], u16 len, payload */
#define RW_RADIO_PAD_ENABLE      0x40  /* in: i32 allow_new */
#define RW_RADIO_PAD_POLL        0x41  /* out: 4 x rw_pad_record; ret: slots */
#define RW_RADIO_PAD_OUTPUT      0x42  /* in: i32 index, i32 what (1 color, 2 player LEDs, 4 rumble), u8 r, g, b, leds, weak, strong, u16 pad, u32 duration_ms */
#define RW_RADIO_PAD_FORGET      0x43
#define RW_RADIO_PAD_DISCONNECT  0x44  /* in: i32 index */
#define RW_RADIO_ESPNOW_INIT     0x50  /* in: i32 on; ret: 0, -2 while Wi-Fi is off */
#define RW_RADIO_ESPNOW_SEND     0x51  /* in: u8 mac[6], payload (<= 250); ret: 0, -2 not initialised */
#define RW_RADIO_MDNS_BEGIN      0x70  /* in: char host[64]; ret: 0, -1 */
#define RW_RADIO_MDNS_END        0x71
#define RW_RADIO_MDNS_ADD_SERVICE 0x72 /* in: char type[32], char proto[8], u16 port; ret: 0, -1 */
#define RW_RADIO_MDNS_QUERY_HOST 0x73  /* in: char host[64]; out: u32 ip; ret: 1 found / 0 */
#define RW_RADIO_MDNS_QUERY_SVC  0x74  /* in: char type[32], char proto[8]; out: n x (char host[64], u32 ip, u16 port, u16 0); ret: n */
#define RW_RADIO_ESPNOW_POLL     0x52  /* out: u8 kind (1 rx, 2 sent), u8 status, i8 rssi, u8 0, u8 mac[6], u16 len, payload; ret: 1 / 0 */

#pragma pack(push, 1)
typedef struct rw_wifi_net {
    char ssid[33];
    int8_t rssi;
    uint8_t channel;
    uint8_t auth;
    uint8_t bssid[6];
} rw_wifi_net;                         /* 42 bytes */

typedef struct rw_ble_adv {
    uint8_t addr[6];
    int8_t rssi;
    int8_t tx_power;
    char name[32];
    char uuid[40];
    uint8_t mfr_len;
    uint8_t mfr[31];
} rw_ble_adv;                          /* 112 bytes */

typedef struct rw_pad_record {
    int32_t connected;
    uint8_t addr[6];
    uint8_t model;                     /* 0 DualSense, 1 DualShock 4, 2 Xbox, 3 generic */
    uint8_t battery;
    int32_t axis_x, axis_y, axis_rx, axis_ry;   /* -511..512 */
    int32_t brake, throttle;                    /* 0..1023 */
    uint16_t buttons;
    uint8_t dpad;
    uint8_t misc;
    int32_t gyro[3];
    int32_t accel[3];
    uint32_t seq;
    int32_t rssi;
    char name[24];
} rw_pad_record;                       /* 96 bytes */
#pragma pack(pop)

typedef struct rw_config {
    uint32_t struct_size;           /* sizeof(rw_config) */
    uint32_t watchdog_ms;           /* wall-clock ms a task may run without an API call (0 = 2000) */
    uint64_t time_origin_ns;        /* host time of the chip reset: millis() counts from here */
    uint64_t random_seed;           /* esp_random()/random() seed (deterministic runs) */
    int32_t adc_model;              /* 0 = ideal linear, 1 = typical ESP32 non-linearity */
    int32_t reset_reason;           /* esp_reset_reason_t reported to the sketch (1 = power-on) */
    int32_t wakeup_cause;           /* esp_sleep_wakeup_cause_t reported to the sketch (0 = undefined) */
    int32_t reserved0;
    double cost_scale;              /* multiplier for the per-call time charges (0 = 1.0) */
} rw_config;

#ifdef __cplusplus
}
#endif
