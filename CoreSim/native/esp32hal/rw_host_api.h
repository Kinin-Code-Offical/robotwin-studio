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

#define RW_ABI_VERSION 1

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
} rw_host_api;

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
