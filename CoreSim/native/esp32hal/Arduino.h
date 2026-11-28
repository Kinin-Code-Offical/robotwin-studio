/*
 * RoboTwin ESP32 simulation - Arduino-ESP32 API shim (HAL level).
 *
 * The sketch is compiled natively (MSVC, Windows x64) against these headers and
 * runs in lockstep with the circuit simulator. Every hardware access goes to the
 * host through rw_host_api (see rw_host_api.h). Values and names follow the
 * Arduino-ESP32 core (2.x and 3.x APIs are both accepted where they differ).
 */
#ifndef RW_ARDUINO_H
#define RW_ARDUINO_H

#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif
#ifndef _USE_MATH_DEFINES
#define _USE_MATH_DEFINES
#endif

#include <stdint.h>
#include <stddef.h>
#include <stdlib.h>
#include <stdbool.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>
#include <math.h>
#include <inttypes.h>
#include <ctype.h>
#include <limits.h>
#include <float.h>
#ifdef __cplusplus
/* Standard C++ headers are pulled in before the Arduino macros below are defined. */
#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <cstdio>
#endif

#define ARDUINO 10819
#define ARDUINO_ARCH_ESP32 1
#define ESP32 1
#define ARDUINO_ESP32_DEV 1
#define ARDUINO_BOARD "ESP32_DEV"
#define ARDUINO_VARIANT "esp32"
#define CONFIG_IDF_TARGET_ESP32 1
#define CONFIG_IDF_TARGET "esp32"
#define CONFIG_FREERTOS_HZ 1000
#define CONFIG_ARDUINO_RUNNING_CORE 1
#define ARDUINO_RUNNING_CORE 1
#define ARDUINO_EVENT_RUNNING_CORE 1
#define ROBOTWIN_SIM 1
#define ROBOTWIN_ESP32_HAL 1

#define ESP_ARDUINO_VERSION_MAJOR 3
#define ESP_ARDUINO_VERSION_MINOR 3
#define ESP_ARDUINO_VERSION_PATCH 13
#define ESP_ARDUINO_VERSION_VAL(major, minor, patch) (((major) << 16) | ((minor) << 8) | (patch))
#define ESP_ARDUINO_VERSION ESP_ARDUINO_VERSION_VAL(ESP_ARDUINO_VERSION_MAJOR, ESP_ARDUINO_VERSION_MINOR, ESP_ARDUINO_VERSION_PATCH)
#define ESP_ARDUINO_VERSION_STR "3.3.13-robotwin-sim"
#define ESP_IDF_VERSION_MAJOR 5
#define ESP_IDF_VERSION_MINOR 5
#define ESP_IDF_VERSION_PATCH 6
#define ESP_IDF_VERSION_VAL(major, minor, patch) (((major) << 16) | ((minor) << 8) | (patch))
#define ESP_IDF_VERSION ESP_IDF_VERSION_VAL(ESP_IDF_VERSION_MAJOR, ESP_IDF_VERSION_MINOR, ESP_IDF_VERSION_PATCH)

#ifndef F_CPU
#define F_CPU 240000000L
#endif

/* GCC / Xtensa attributes that have no meaning here. */
#ifndef __attribute__
#define __attribute__(x)
#endif

/* GCC's bit built-ins, which libraries call directly (FastLED's bitsets, CRC and fixed-point code ...): MSVC's own
   intrinsics, the same results (a zero argument to clz / ctz gives the width, as the Xtensa build's NSAU does). */
#if defined(_MSC_VER) && !defined(__clang__)
#include <intrin.h>
#ifdef __cplusplus
static inline int __builtin_popcount(unsigned int x) { return (int)__popcnt(x); }
static inline int __builtin_popcountl(unsigned long x) { return (int)__popcnt((unsigned int)x); }
static inline int __builtin_popcountll(unsigned long long x) { return (int)__popcnt64(x); }
static inline int __builtin_parity(unsigned int x) { return (int)(__popcnt(x) & 1); }
static inline int __builtin_parityll(unsigned long long x) { return (int)(__popcnt64(x) & 1); }
static inline int __builtin_clz(unsigned int x) { unsigned long i; return _BitScanReverse(&i, x) ? 31 - (int)i : 32; }
static inline int __builtin_clzl(unsigned long x) { return __builtin_clz((unsigned int)x); }
static inline int __builtin_clzll(unsigned long long x) { unsigned long i; return _BitScanReverse64(&i, x) ? 63 - (int)i : 64; }
static inline int __builtin_ctz(unsigned int x) { unsigned long i; return _BitScanForward(&i, x) ? (int)i : 32; }
static inline int __builtin_ctzl(unsigned long x) { return __builtin_ctz((unsigned int)x); }
static inline int __builtin_ctzll(unsigned long long x) { unsigned long i; return _BitScanForward64(&i, x) ? (int)i : 64; }
static inline int __builtin_ffs(int x) { unsigned long i; return _BitScanForward(&i, (unsigned int)x) ? (int)i + 1 : 0; }
static inline int __builtin_ffsl(long x) { return __builtin_ffs((int)x); }
static inline int __builtin_ffsll(long long x) { unsigned long i; return _BitScanForward64(&i, (unsigned long long)x) ? (int)i + 1 : 0; }
static inline unsigned short __builtin_bswap16(unsigned short x) { return _byteswap_ushort(x); }
static inline unsigned int __builtin_bswap32(unsigned int x) { return (unsigned int)_byteswap_ulong(x); }
static inline unsigned long long __builtin_bswap64(unsigned long long x) { return _byteswap_uint64(x); }
#endif
#ifndef __builtin_expect
#define __builtin_expect(x, expected) (x)
#endif
#ifndef __builtin_unreachable
#define __builtin_unreachable() __assume(0)
#endif
#ifndef __builtin_constant_p
#define __builtin_constant_p(x) 0   /* "not known while compiling": the library takes its general path */
#endif
#endif
#define IRAM_ATTR
#define DRAM_ATTR
#define EXT_RAM_ATTR
#define RTC_DATA_ATTR
#define RTC_RODATA_ATTR
#define RTC_NOINIT_ATTR
#define RTC_IRAM_ATTR
#define ARDUINO_ISR_ATTR
#define WORD_ALIGNED_ATTR
#define NOINLINE_ATTR
#define ICACHE_RAM_ATTR
#define PROGMEM
#define PGM_P const char*
#define PGM_VOID_P const void*
#define PSTR(s) (s)
#ifndef __STRINGIFY
#define __STRINGIFY(a) #a
#endif
#define NOP() do { } while (0)

typedef bool boolean;
typedef uint8_t byte;
typedef uint16_t word;
typedef int esp_err_t;
#define ESP_OK 0
#define ESP_FAIL -1
#define ESP_ERR_INVALID_ARG 0x102
#define ESP_ERR_INVALID_STATE 0x103
#define ESP_ERR_TIMEOUT 0x107

/* ---------------------------------------------------------------- constants */
#define HIGH 0x1
#define LOW 0x0

#define INPUT 0x01
#define OUTPUT 0x03
#define PULLUP 0x04
#define INPUT_PULLUP 0x05
#define PULLDOWN 0x08
#define INPUT_PULLDOWN 0x09
#define OPEN_DRAIN 0x10
#define OUTPUT_OPEN_DRAIN 0x13
#define ANALOG 0xC0

/* Interrupt modes (esp32-hal-gpio.h). LOW (0) is accepted as ONLOW by this simulator. */
#define DISABLED 0x00
#define RISING 0x01
#define FALLING 0x02
#define CHANGE 0x03
#define ONLOW 0x04
#define ONHIGH 0x05
#define ONLOW_WE 0x0C
#define ONHIGH_WE 0x0D

#define PI 3.1415926535897932384626433832795
#define HALF_PI 1.5707963267948966192313216916398
#define TWO_PI 6.283185307179586476925286766559
#define DEG_TO_RAD 0.017453292519943295769236907684886
#define RAD_TO_DEG 57.295779513082320876798154814105
#define EULER 2.718281828459045235360287471352

#define SERIAL 0x0
#define DISPLAY 0x1
#define LSBFIRST 0
#define MSBFIRST 1

#define DEC 10
#define HEX 16
#define OCT 8
#define BIN 2

#define NOT_A_PIN -1
#define NOT_A_PORT -1
#define NOT_AN_INTERRUPT -1
#define NOT_ON_TIMER 0

#define SOC_GPIO_PIN_COUNT 40
#define GPIO_PIN_COUNT 40
#define digitalPinIsValid(pin) ((pin) >= 0 && (pin) < 40 && (pin) != 20 && (pin) != 24 && ((pin) < 28 || (pin) > 31))
#define digitalPinCanOutput(pin) (digitalPinIsValid(pin) && (pin) < 34)
#define digitalPinToInterrupt(p) ((((uint8_t)(p)) < 40) ? (p) : NOT_AN_INTERRUPT)
#define digitalPinHasPWM(p) digitalPinCanOutput(p)
#define analogInputToDigitalPin(p) (p)
#define digitalPinToAnalogChannel(pin) rw_digital_pin_to_analog_channel(pin)
#define digitalPinToTouchChannel(pin) rw_digital_pin_to_touch_channel(pin)
#define digitalPinToDacChannel(pin) (((pin) == 25) ? 0 : ((pin) == 26) ? 1 : -1)
#define digitalPinToGPIONumber(pin) (pin)

/* Port registers, as the core gives them (GPIO0-31 port 0, GPIO32-39 port 1): a library that switches pins through a
   pointer to the output, input or enable register (Adafruit BusIO's software SPI, display drivers ...) gets a copy the HAL
   keeps. What it writes there goes to the pins at its next HAL call (each call settles them first); the input copy is
   refreshed then, for the input pins of that port. kind: 0 output, 1 input, 2 enable. */
#ifdef __cplusplus
extern "C" {
#endif
volatile uint32_t* rw_port_register(int kind, int port);
#ifdef __cplusplus
}
#endif
#define digitalPinToPort(pin) (((pin) > 31) ? 1 : 0)
#define digitalPinToBitMask(pin) (1UL << (digitalPinToGPIONumber(pin) % 32))
#define digitalPinToTimer(pin) (0)
#define portOutputRegister(port) rw_port_register(0, (port))
#define portInputRegister(port) rw_port_register(1, (port))
#define portModeRegister(port) rw_port_register(2, (port))

/* ---------------------------------------------------------------- macros */
#define _min(a, b) ((a) < (b) ? (a) : (b))
#define _max(a, b) ((a) > (b) ? (a) : (b))
#define _abs(x) ((x) > 0 ? (x) : -(x))
#define constrain(amt, low, high) ((amt) < (low) ? (low) : ((amt) > (high) ? (high) : (amt)))
#define radians(deg) ((deg) * DEG_TO_RAD)
#define degrees(rad) ((rad) * RAD_TO_DEG)
#define sq(x) ((x) * (x))

#define lowByte(w) ((uint8_t)((w) & 0xff))
#define highByte(w) ((uint8_t)((w) >> 8))
#define bitRead(value, bit) (((value) >> (bit)) & 0x01)
#define bitSet(value, bit) ((value) |= (1UL << (bit)))
#define bitClear(value, bit) ((value) &= ~(1UL << (bit)))
#define bitToggle(value, bit) ((value) ^= (1UL << (bit)))
#define bitWrite(value, bit, bitvalue) ((bitvalue) ? bitSet(value, bit) : bitClear(value, bit))
#ifndef __cplusplus
#define bit(b) (1UL << (b))
#endif
#ifndef _BV
#define _BV(b) (1UL << (b))
#endif

#define clockCyclesPerMicrosecond() ((long int)getCpuFrequencyMhz())
#define clockCyclesToMicroseconds(a) ((a) / clockCyclesPerMicrosecond())
#define microsecondsToClockCycles(a) ((a) * clockCyclesPerMicrosecond())

#define interrupts() rw_set_interrupts(1)
#define noInterrupts() rw_set_interrupts(0)
#define sei() rw_set_interrupts(1)
#define cli() rw_set_interrupts(0)

/* printf() on an ESP32 goes to UART0 (the Serial monitor), even without Serial.begin(). */
#ifdef __cplusplus
extern "C" int rw_printf(const char* format, ...);
#else
int rw_printf(const char* format, ...);
#endif
#define printf(...) rw_printf(__VA_ARGS__)
#define ets_printf(...) rw_printf(__VA_ARGS__)

#include "pgmspace.h"
#include "binary.h"
#include "esp32-hal.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "esp_sleep.h"
#include "pins_arduino.h"

#ifdef __cplusplus
using std::abs;
using std::isinf;
using std::isnan;
using std::max;
using std::min;
using ::round;

static inline constexpr unsigned long bit(unsigned int b) { return 1UL << b; }

#include "WString.h"
#include "Printable.h"
#include "Print.h"
#include "Stream.h"
#include "HardwareSerial.h"
#include "Esp.h"

void setup(void);
void loop(void);

uint16_t makeWord(uint16_t w);
uint16_t makeWord(uint8_t h, uint8_t l);
#define word(...) makeWord(__VA_ARGS__)

unsigned long pulseIn(uint8_t pin, uint8_t state, unsigned long timeout = 1000000L);
unsigned long pulseInLong(uint8_t pin, uint8_t state, unsigned long timeout = 1000000L);

long random(long howbig);
long random(long howsmall, long howbig);
long random(void);
void randomSeed(unsigned long seed);
long map(long x, long in_min, long in_max, long out_min, long out_max);

void tone(uint8_t pin, unsigned int frequency, unsigned long duration = 0);
void noTone(uint8_t pin);

void shiftOut(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder, uint8_t val);
uint8_t shiftIn(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder);

#endif /* __cplusplus */

/* [mem-budget] The sketch's C allocations come from the simulated chip's heap (counted against its ~296 KB), as new
   does. Function-like names only, so declarations such as "using ::malloc;" are left alone. */
#ifndef RW_NO_MALLOC_REDIRECT
#ifdef __cplusplus
extern "C" {
#endif
void* rw_malloc(size_t n);
void* rw_calloc(size_t count, size_t size);
void* rw_realloc(void* p, size_t n);
void rw_free(void* p);
#ifdef __cplusplus
}
namespace std {   /* std::malloc(n) reads std::rw_malloc(n) after the names below */
using ::rw_malloc;
using ::rw_calloc;
using ::rw_realloc;
using ::rw_free;
}
#endif
#define malloc(n) rw_malloc(n)
#define calloc(c, s) rw_calloc(c, s)
#define realloc(p, n) rw_realloc(p, n)
#define free(p) rw_free(p)
#endif

#endif /* RW_ARDUINO_H */
