/*
 * esp32-hal implementation for the RoboTwin simulation: GPIO, interrupts, ADC,
 * DAC, LEDC/analogWrite/tone, touch, hardware timers, time, sleep, ESP class,
 * random/map and other Arduino helpers. All electrical effects are delegated to
 * the host through rw::api; this file keeps the Arduino-level state and rules
 * (valid pins, input-only pads, ADC2 vs Wi-Fi, LEDC channels...).
 */
#include "Arduino.h"
#include "rw_internal.h"

using namespace rw;

namespace {

/* ---------------------------------------------------------------- per-call execution time (ns) */
const double kCostPinMode = 1000.0;
const double kCostDigitalWrite = 100.0;
const double kCostDigitalRead = 100.0;
const double kCostAnalogRead = 10000.0;
const double kCostLedc = 1000.0;
const double kCostLedcSetup = 10000.0;
const double kCostDac = 2000.0;
const double kCostTouch = 500000.0;
const double kCostTime = 50.0;
const double kCostYield = 100.0;
const double kCostMisc = 100.0;

struct Edge {
    uint64_t time;
    uint8_t level;
};

struct PinState {
    uint8_t mode;
    uint8_t out;
    uint8_t in;
    bool in_known;
    int8_t ledc;
    bool ledc_v3;
    bool dac;
    bool tone;
    int tone_event;
    uint8_t atten;
    voidFuncPtr isr0;
    voidFuncPtrArg isr1;
    void* isr_arg;
    int isr_mode;
    bool isr_enabled;
    uint32_t edge_count;
    Edge edges[16];
};

struct Channel {
    bool used;
    double freq;
    uint8_t res;
    uint32_t duty;
};

PinState g_pins[40];
Channel g_ch[16];
uint8_t g_read_bits = 12;
uint8_t g_write_bits = 8;
double g_write_freq = 1000.0;
uint32_t g_write_freq_pin[40];
uint8_t g_write_bits_pin[40];
uint32_t g_cpu_mhz = 240;
uint64_t g_sleep_timer_us = 0;
bool g_sleep_timer = false;
int g_ext0_gpio = -1;
int g_ext0_level = 0;
uint32_t g_min_free_heap = 0xFFFFFFFFu;

inline bool valid_pin(int p) { return p >= 0 && p < 40 && p != 20 && p != 24 && !(p >= 28 && p <= 31); }
inline bool output_pin(int p) { return valid_pin(p) && p < 34; }
inline bool flash_pin(int p) { return p >= 6 && p <= 11; }
inline unsigned long now_ms() { return (unsigned long)(uptime_ns() / 1000000ull); }

/* Arduino-ESP32 3.x log format: [  1234][E][file:line] func(): message */
void core_log(int level, const char* file, int line, const char* func, const char* format, ...) {
    char msg[384];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(level, "[%6lu][%c][%s:%d] %s(): %s", now_ms(), level == RW_LOG_ERROR ? 'E' : level == RW_LOG_WARN ? 'W' : 'I',
            file, line, func, msg);
}

int adc_unit(int p) {
    if (p >= 32 && p <= 39) return 1;
    switch (p) {
    case 0: case 2: case 4: case 12: case 13: case 14: case 15: case 25: case 26: case 27: return 2;
    default: return 0;
    }
}

int adc_channel(int p) {
    switch (p) {
    case 36: return 0; case 37: return 1; case 38: return 2; case 39: return 3;
    case 32: return 4; case 33: return 5; case 34: return 6; case 35: return 7;
    case 4: return 0; case 0: return 1; case 2: return 2; case 15: return 3; case 13: return 4;
    case 12: return 5; case 14: return 6; case 27: return 7; case 25: return 8; case 26: return 9;
    default: return -1;
    }
}

int touch_channel(int p) {
    switch (p) {
    case 4: return 0; case 0: return 1; case 2: return 2; case 15: return 3; case 13: return 4;
    case 12: return 5; case 14: return 6; case 27: return 7; case 33: return 8; case 32: return 9;
    default: return -1;
    }
}

bool g_wifi_on = false;

/* ---------------------------------------------------------------- host helpers */
void host_pin_mode(int pin, int mode) {
    if (!has_api || !api.pin_mode) return;
    ApiScope s;
    api.pin_mode(api.user, pin, mode);
}

void host_write(int pin, int level) {
    if (!has_api || !api.digital_write) return;
    ApiScope s;
    api.digital_write(api.user, pin, level);
}

int host_read(int pin) {
    if (!has_api || !api.digital_read) return 0;
    ApiScope s;
    return api.digital_read(api.user, pin) ? 1 : 0;
}

void host_pwm(int pin, double freq, double duty) {
    if (!has_api || !api.pwm_write) return;
    ApiScope s;
    api.pwm_write(api.user, pin, freq, duty);
}

void host_dac(int pin, double volts) {
    if (!has_api || !api.dac_write) return;
    ApiScope s;
    api.dac_write(api.user, pin, volts);
}

/* ---------------------------------------------------------------- LEDC */
double channel_duty_fraction(const Channel& c) {
    uint64_t full = 1ull << c.res;
    uint64_t duty = c.duty;
    uint64_t max_duty = full - 1;
    if (duty == max_duty && max_duty != 1) duty = full;  // "all bits set" means fully on (core behaviour)
    if (duty >= full) return 1.0;
    return (double)duty / (double)full;
}

void ledc_apply(int channel) {
    const Channel& c = g_ch[channel];
    double frac = channel_duty_fraction(c);
    for (int p = 0; p < 40; p++) {
        if (g_pins[p].ledc == channel) host_pwm(p, c.freq, frac);
    }
}

void ledc_detach_pin(int pin, bool release_channel) {
    PinState& s = g_pins[pin];
    if (s.ledc < 0) return;
    int c = s.ledc;
    s.ledc = -1;
    s.ledc_v3 = false;
    s.tone = false;
    host_pwm(pin, 0.0, -1.0);
    if (release_channel) {
        bool still = false;
        for (int p = 0; p < 40; p++) if (g_pins[p].ledc == c) still = true;
        if (!still) g_ch[c].used = false;
    }
}

void dac_disable_pin(int pin) {
    if (!g_pins[pin].dac) return;
    g_pins[pin].dac = false;
    host_dac(pin, -1.0);
}

bool ledc_config_ok(double freq, uint8_t res) {
    if (res < 1 || res > 20) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 60, "ledcSetup", "No more LEDC channels available! (resolution %u bits is out of range 1-20)", res);
        return false;
    }

    if (!(freq > 0.0) || freq * (double)(1ull << res) > 80000000.0) {
        rw::log(RW_LOG_ERROR, "E (%lu) ledc: requested frequency %.0f Hz and duty resolution %u bits can not be achieved, try reducing freq_hz or duty_resolution",
                now_ms(), freq, res);
        return false;
    }

    return true;
}

int ledc_free_channel() {
    for (int c = 0; c < 16; c++) if (!g_ch[c].used) return c;
    return -1;
}

int ledc_resolve(uint8_t x) {
    if (x < 40 && g_pins[x].ledc >= 0 && (g_pins[x].ledc_v3 || !(x < 16 && g_ch[x].used))) return g_pins[x].ledc;
    if (x < 16 && g_ch[x].used) return x;
    if (x < 40 && g_pins[x].ledc >= 0) return g_pins[x].ledc;
    return -1;
}

bool ledc_attach_v3(uint8_t pin, double freq, uint8_t res, int channel) {
    if (!output_pin(pin)) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 75, "ledcAttachChannel", "Pin %u is not a valid output pin", pin);
        return false;
    }

    if (!ledc_config_ok(freq, res)) return false;
    if (channel < 0) channel = ledc_free_channel();
    if (channel < 0 || channel > 15) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 80, "ledcAttach", "No more LEDC channels available!");
        return false;
    }

    if (g_pins[pin].ledc >= 0 && g_pins[pin].ledc != channel) ledc_detach_pin(pin, true);
    dac_disable_pin(pin);
    g_ch[channel].used = true;
    g_ch[channel].freq = freq;
    g_ch[channel].res = res;
    g_ch[channel].duty = 0;
    g_pins[pin].ledc = (int8_t)channel;
    g_pins[pin].ledc_v3 = true;
    ledc_apply(channel);
    return true;
}

/* ---------------------------------------------------------------- interrupts */
void call_plain_isr(void* fn) { reinterpret_cast<voidFuncPtr>(fn)(); }

void fire_isr(int pin) {
    PinState& s = g_pins[pin];
    if (s.isr1) raise_isr(reinterpret_cast<isr_fn>(s.isr1), s.isr_arg);
    else if (s.isr0) raise_isr(call_plain_isr, reinterpret_cast<void*>(s.isr0));
}

/* ---------------------------------------------------------------- tone */
void tone_stop_event(void* arg) {
    int pin = (int)(intptr_t)arg;
    g_pins[pin].tone_event = 0;
    noTone((uint8_t)pin);
}

/* ---------------------------------------------------------------- ADC model */
uint32_t adc_raw12(double v, int atten) {
    static const double kFullScale[4] = {1.1, 1.5, 2.2, 3.3};
    if (!(v == v)) v = 0.0;
    double fs = kFullScale[atten & 3];
    double code;
    if (config.adc_model == 1) {
        // Typical ESP32 transfer curve (11 dB shape, scaled to the attenuation): dead zone near 0 V,
        // compression and saturation near full scale.
        static const double kV[] = {0.0, 0.13, 1.0, 2.0, 2.5, 2.8, 3.0, 3.13, 3.3};
        static const double kC[] = {0.0, 0.0, 1088.0, 2370.0, 2985.0, 3400.0, 3790.0, 4095.0, 4095.0};
        double u = v / fs * 3.3;
        if (u <= 0.0) code = 0.0;
        else if (u >= kV[8]) code = 4095.0;
        else {
            int i = 0;
            while (i < 7 && u > kV[i + 1]) i++;
            double t = (u - kV[i]) / (kV[i + 1] - kV[i]);
            code = kC[i] + t * (kC[i + 1] - kC[i]);
        }
    } else {
        code = v / fs * 4095.0;
    }

    if (code < 0.0) code = 0.0;
    if (code > 4095.0) code = 4095.0;
    return (uint32_t)(code + 0.5);
}

bool adc_prepare(uint8_t pin, const char* func) {
    int unit = adc_unit(pin);
    if (!valid_pin(pin) || unit == 0) {
        core_log(RW_LOG_ERROR, "esp32-hal-adc.c", 254, func, "Pin %u is not ADC pin!", pin);
        return false;
    }

    if (unit == 2 && g_wifi_on) {
        log_once(0x2000 + pin, RW_LOG_ERROR, "[%6lu][E][esp32-hal-adc.c:279] %s(): GPIO%u: ADC2 is in use by Wi-Fi (ESP_ERR_TIMEOUT); use an ADC1 pin (GPIO32-39)",
                 now_ms(), func, pin);
        return false;
    }

    if (g_pins[pin].mode != ANALOG) {
        // analogRead() attaches the pad to the ADC: it becomes an analog input (pulls off).
        if (g_pins[pin].ledc >= 0) ledc_detach_pin(pin, true);
        dac_disable_pin(pin);
        g_pins[pin].mode = ANALOG;
        host_pin_mode(pin, ANALOG);
    }

    return true;
}

double adc_volts(uint8_t pin) {
    if (!has_api || !api.analog_read) return 0.0;
    ApiScope s;
    return api.analog_read(api.user, pin);
}

/* ---------------------------------------------------------------- hardware timers */
struct TimerState {
    bool used;
    uint32_t freq;
    bool running;
    uint64_t base_count;
    uint64_t base_time;
    uint64_t alarm;
    bool autoreload;
    uint64_t reload_count;
    uint64_t fired;
    bool alarm_enabled;
    voidFuncPtr fn0;
    voidFuncPtrArg fn1;
    void* arg;
    int event_id;
    uint64_t event_at;
};

}  // namespace

struct rw_hw_timer {
    TimerState s;
};

namespace {

rw_hw_timer g_timers[4];

uint64_t timer_count(const TimerState& t) {
    if (!t.running || t.freq == 0) return t.base_count;
    uint64_t dt = now_ns() > t.base_time ? now_ns() - t.base_time : 0;
    return t.base_count + (uint64_t)((double)dt * (double)t.freq / 1e9);
}

void timer_fire(void* arg);

void timer_schedule(rw_hw_timer* timer) {
    TimerState& t = timer->s;
    if (t.event_id) cancel_event(t.event_id);
    t.event_id = 0;
    if (!t.running || !t.alarm_enabled || t.freq == 0) return;
    uint64_t count = timer_count(t);
    uint64_t remaining = t.alarm > count ? t.alarm - count : 0;
    uint64_t at = now_ns() + (uint64_t)((double)remaining * 1e9 / (double)t.freq);
    t.event_at = at;
    t.event_id = schedule_event(at, timer_fire, timer);
}

void timer_fire(void* arg) {
    rw_hw_timer* timer = static_cast<rw_hw_timer*>(arg);
    TimerState& t = timer->s;
    t.event_id = 0;
    uint64_t at = t.event_at;
    t.fired++;
    if (t.autoreload) {
        t.base_count = 0;
        t.base_time = at;
        bool more = t.reload_count == 0 || t.fired < t.reload_count;
        if (more && t.alarm > 0) {
            t.event_at = at + (uint64_t)((double)t.alarm * 1e9 / (double)t.freq);
            t.event_id = schedule_event(t.event_at, timer_fire, timer);
        } else {
            t.alarm_enabled = false;
        }
    } else {
        t.alarm_enabled = false;
    }

    if (t.fn1) t.fn1(t.arg);
    else if (t.fn0) t.fn0();
}

rw_hw_timer* timer_alloc(int index, uint32_t freq) {
    if (index < 0) {
        for (int i = 0; i < 4; i++) {
            if (!g_timers[i].s.used) { index = i; break; }
        }
    }

    if (index < 0 || index > 3) {
        core_log(RW_LOG_ERROR, "esp32-hal-timer.c", 100, "timerBegin", "No free hardware timer (the ESP32 has 4)");
        return nullptr;
    }

    rw_hw_timer* timer = &g_timers[index];
    if (timer->s.event_id) cancel_event(timer->s.event_id);
    memset(&timer->s, 0, sizeof timer->s);
    timer->s.used = true;
    timer->s.freq = freq ? freq : 1000000;
    timer->s.running = true;
    timer->s.base_time = now_ns();
    return timer;
}

}  // namespace

/* ======================================================================= rw:: hooks */
namespace rw {

void hal_reset() {
    twdt_reset();
    memset(g_pins, 0, sizeof g_pins);
    for (int p = 0; p < 40; p++) {
        g_pins[p].ledc = -1;
        g_pins[p].atten = ADC_11db;
    }

    memset(g_ch, 0, sizeof g_ch);
    memset(g_write_freq_pin, 0, sizeof g_write_freq_pin);
    memset(g_write_bits_pin, 0, sizeof g_write_bits_pin);
    memset(g_timers, 0, sizeof g_timers);
    g_read_bits = 12;
    g_write_bits = 8;
    g_write_freq = 1000.0;
    g_cpu_mhz = 240;
    g_sleep_timer = false;
    g_sleep_timer_us = 0;
    g_ext0_gpio = -1;
    g_wifi_on = false;
    g_min_free_heap = 0xFFFFFFFFu;
    hal_wifi_reset();
}

void hal_pin_changed(int gpio, int level, uint64_t time_ns) {
    if (!valid_pin(gpio)) return;
    PinState& s = g_pins[gpio];
    uint8_t lv = level ? 1 : 0;
    if (s.in_known && s.in == lv) return;
    bool first = !s.in_known;
    s.in = lv;
    s.in_known = true;
    if (first) return;
    Edge& e = s.edges[s.edge_count % 16];
    e.time = time_ns;
    e.level = lv;
    s.edge_count++;
    if (!s.isr_enabled || (!s.isr0 && !s.isr1)) return;
    int m = s.isr_mode;
    if ((m == RISING && lv) || (m == FALLING && !lv) || m == CHANGE) fire_isr(gpio);
}

void hal_slice_start() {
    for (int p = 0; p < 40; p++) {
        PinState& s = g_pins[p];
        if (!s.isr_enabled || (!s.isr0 && !s.isr1) || !s.in_known) continue;
        int m = s.isr_mode;
        if (((m == ONLOW || m == ONLOW_WE) && !s.in) || ((m == ONHIGH || m == ONHIGH_WE) && s.in)) fire_isr(p);
    }
}

}  // namespace rw

/* Wi-Fi radio state, set by WiFi.cpp (ADC2 conflict, supply current). */
void rw_hal_set_wifi(bool on) {
    if (g_wifi_on == on) return;
    g_wifi_on = on;
    if (has_api && api.system_event) {
        ApiScope s;
        api.system_event(api.user, RW_EVENT_WIFI, on ? 1 : 0);
    }
}

/* Internal pin configuration used by peripherals (Wire, Serial): no Arduino-level checks. */
void rw_hal_pin_function(int pin, int mode, int level) {
    if (!output_pin(pin)) return;
    if (g_pins[pin].ledc >= 0) ledc_detach_pin(pin, true);
    dac_disable_pin(pin);
    g_pins[pin].mode = (uint8_t)mode;
    g_pins[pin].out = (uint8_t)(level ? 1 : 0);
    host_write(pin, level ? 1 : 0);
    host_pin_mode(pin, mode);
}

/* ======================================================================= GPIO */
extern "C" void pinMode(uint8_t pin, uint8_t mode) {
    charge(cost(kCostPinMode));
    if (!valid_pin(pin)) {
        core_log(RW_LOG_ERROR, "esp32-hal-gpio.c", 102, "__pinMode", "Invalid IO %u selected", pin);
        return;
    }

    if (flash_pin(pin)) {
        log_once(0x100 + pin, RW_LOG_WARN, "[%6lu][W][esp32-hal-gpio.c:110] __pinMode(): GPIO%u is wired to the SPI flash on ESP32 modules; "
                 "using it resets a real board (ignored in the simulation)", now_ms(), pin);
        return;
    }

    if ((mode & 0x02) && !output_pin(pin)) {
        rw::log(RW_LOG_ERROR, "E (%lu) gpio: gpio_config(%u): GPIO%u can only be used as input mode (GPIO34-39 have no output driver)",
                now_ms(), 340u, pin);
        return;
    }

    if ((mode & (PULLUP | PULLDOWN)) && pin >= 34) {
        log_once(0x200 + pin, RW_LOG_WARN, "W (%lu) gpio: GPIO%u has no internal pull-up/pull-down resistors (GPIO34-39); add an external resistor",
                 now_ms(), pin);
        mode = (uint8_t)(mode & ~(PULLUP | PULLDOWN));
    }

    PinState& s = g_pins[pin];
    if (s.ledc >= 0) ledc_detach_pin(pin, true);
    dac_disable_pin(pin);
    s.mode = mode;
    host_pin_mode(pin, mode);
}

extern "C" void digitalWrite(uint8_t pin, uint8_t val) {
    charge(cost(kCostDigitalWrite));
    if (!valid_pin(pin)) {
        core_log(RW_LOG_ERROR, "esp32-hal-gpio.c", 145, "__digitalWrite", "Invalid IO %u selected", pin);
        return;
    }

    if (!output_pin(pin)) {
        log_once(0x300 + pin, RW_LOG_ERROR, "E (%lu) gpio: gpio_set_level(%u): GPIO output gpio_num error (GPIO%u is input only)", now_ms(), 226u, pin);
        return;
    }

    uint8_t level = val ? 1 : 0;
    PinState& s = g_pins[pin];
    if (s.out == level) return;
    s.out = level;
    host_write(pin, level);
}

extern "C" int digitalRead(uint8_t pin) {
    charge(cost(kCostDigitalRead));
    if (!valid_pin(pin)) return 0;
    const PinState& s = g_pins[pin];
    // Push-pull outputs read back the level they drive (the input buffer sees the pad);
    // returning the register avoids a one-slice lag for digitalWrite(p, !digitalRead(p)).
    if ((s.mode & 0x02) && !(s.mode & OPEN_DRAIN) && s.ledc < 0 && !s.dac) return s.out;
    if ((s.mode & OPEN_DRAIN) && !s.out) return 0;
    return host_read(pin);
}

extern "C" void attachInterruptArg(uint8_t pin, voidFuncPtrArg handler, void* arg, int mode) {
    charge(cost(kCostPinMode));
    if (!valid_pin(pin)) {
        core_log(RW_LOG_ERROR, "esp32-hal-gpio.c", 210, "__attachInterruptFunctionalArg", "Invalid IO %u selected", pin);
        return;
    }

    PinState& s = g_pins[pin];
    s.isr0 = nullptr;
    s.isr1 = handler;
    s.isr_arg = arg;
    s.isr_mode = mode == 0 ? ONLOW : mode;  // LOW is accepted as "while low"
    s.isr_enabled = handler != nullptr;
}

extern "C" void attachInterrupt(uint8_t pin, voidFuncPtr handler, int mode) {
    attachInterruptArg(pin, nullptr, nullptr, mode);
    if (valid_pin(pin)) {
        g_pins[pin].isr0 = handler;
        g_pins[pin].isr_enabled = handler != nullptr;
    }
}

extern "C" void detachInterrupt(uint8_t pin) {
    charge(cost(kCostPinMode));
    if (!valid_pin(pin)) return;
    g_pins[pin].isr0 = nullptr;
    g_pins[pin].isr1 = nullptr;
    g_pins[pin].isr_enabled = false;
}

extern "C" void enableInterrupt(uint8_t pin) {
    if (valid_pin(pin)) g_pins[pin].isr_enabled = g_pins[pin].isr0 || g_pins[pin].isr1;
}

extern "C" void disableInterrupt(uint8_t pin) {
    if (valid_pin(pin)) g_pins[pin].isr_enabled = false;
}

extern "C" void rw_set_interrupts(int enabled) { set_interrupts_enabled(enabled != 0); }

extern "C" int8_t rw_digital_pin_to_analog_channel(uint8_t pin) {
    int unit = adc_unit(pin);
    if (unit == 0) return -1;
    return (int8_t)(unit == 1 ? adc_channel(pin) : 10 + adc_channel(pin));
}

extern "C" int8_t rw_digital_pin_to_touch_channel(uint8_t pin) { return (int8_t)touch_channel(pin); }

/* ======================================================================= time */
extern "C" unsigned long millis(void) {
    charge(cost(kCostTime));
    return (unsigned long)(uptime_ns() / 1000000ull);
}

extern "C" unsigned long micros(void) {
    charge(cost(kCostTime));
    return (unsigned long)(uptime_ns() / 1000ull);
}

extern "C" int64_t esp_timer_get_time(void) {
    charge(cost(kCostTime));
    return (int64_t)(uptime_ns() / 1000ull);
}

extern "C" void delay(uint32_t ms) {
    charge(cost(kCostTime));
    sleep_until(now_ns() + ms_to_ns(ms));
}

extern "C" void delayMicroseconds(uint32_t us) {
    // Busy wait on a real ESP32: the time is charged to the running task (no task switch).
    charge((uint64_t)us * 1000ull);
}

extern "C" void yield(void) {
    charge(cost(kCostYield));
    yield_task();
}

extern "C" uint32_t getCpuFrequencyMhz(void) { return g_cpu_mhz; }

extern "C" bool setCpuFrequencyMhz(uint32_t mhz) {
    if (mhz != 240 && mhz != 160 && mhz != 80 && mhz != 40 && mhz != 20 && mhz != 10) return false;
    g_cpu_mhz = mhz;
    return true;
}

extern "C" uint32_t getXtalFrequencyMhz(void) { return 40; }
extern "C" uint32_t getApbFrequency(void) { return 80000000u; }

/* ======================================================================= ADC */
extern "C" uint16_t analogReadRaw(uint8_t pin) {
    charge(cost(kCostAnalogRead));
    if (!adc_prepare(pin, "__analogReadRaw")) return 0;
    return (uint16_t)adc_raw12(adc_volts(pin), g_pins[pin].atten);
}

extern "C" uint16_t analogRead(uint8_t pin) {
    charge(cost(kCostAnalogRead));
    if (!adc_prepare(pin, "__analogRead")) return 0;
    uint32_t raw = adc_raw12(adc_volts(pin), g_pins[pin].atten);
    if (g_read_bits < 12) return (uint16_t)(raw >> (12 - g_read_bits));
    if (g_read_bits > 12) return (uint16_t)(raw << (g_read_bits - 12));
    return (uint16_t)raw;
}

extern "C" uint32_t analogReadMilliVolts(uint8_t pin) {
    static const double kFullScale[4] = {1.1, 1.5, 2.2, 3.3};
    charge(cost(kCostAnalogRead));
    if (!adc_prepare(pin, "__analogReadMilliVolts")) return 0;
    double v = adc_volts(pin);
    if (!(v == v)) v = 0.0;
    double fs = kFullScale[g_pins[pin].atten & 3];
    double lo = 0.0, hi = fs;
    if (config.adc_model == 1) {
        lo = 0.075 * fs / 3.3;
        hi = 3.13 * fs / 3.3;
    }

    if (v < lo) v = lo;
    if (v > hi) v = hi;
    return (uint32_t)(v * 1000.0 + 0.5);
}

extern "C" void analogReadResolution(uint8_t bits) {
    if (bits < 1 || bits > 16) {
        core_log(RW_LOG_ERROR, "esp32-hal-adc.c", 180, "__analogReadResolution", "Invalid resolution %u (1-16)", bits);
        return;
    }

    g_read_bits = bits;
}

extern "C" void analogSetWidth(uint8_t bits) {
    if (bits >= 9 && bits <= 12) g_read_bits = bits;
}

extern "C" void analogSetAttenuation(adc_attenuation_t attenuation) {
    for (int p = 0; p < 40; p++) g_pins[p].atten = (uint8_t)(attenuation & 3);
}

extern "C" void analogSetPinAttenuation(uint8_t pin, adc_attenuation_t attenuation) {
    if (pin < 40) g_pins[pin].atten = (uint8_t)(attenuation & 3);
}

void analogSetAttenuation(int attenuation) { analogSetAttenuation((adc_attenuation_t)(attenuation & 3)); }
void analogSetPinAttenuation(uint8_t pin, int attenuation) { analogSetPinAttenuation(pin, (adc_attenuation_t)(attenuation & 3)); }

extern "C" void analogSetClockDiv(uint8_t) {}
extern "C" void analogSetVRefPin(uint8_t) {}

extern "C" bool adcAttachPin(uint8_t pin) {
    charge(cost(kCostPinMode));
    return adc_prepare(pin, "adcAttachPin");
}

/* ======================================================================= DAC */
extern "C" void dacWrite(uint8_t pin, uint8_t value) {
    charge(cost(kCostDac));
    if (pin != 25 && pin != 26) {
        core_log(RW_LOG_ERROR, "esp32-hal-dac.c", 50, "__dacWrite", "DAC is only available on GPIO25 and GPIO26 (pin %u)", pin);
        return;
    }

    PinState& s = g_pins[pin];
    if (s.ledc >= 0) ledc_detach_pin(pin, true);
    if (s.mode != ANALOG) {
        s.mode = ANALOG;
        host_pin_mode(pin, ANALOG);
    }

    s.dac = true;
    host_dac(pin, 3.3 * value / 255.0);
}

extern "C" void dacDisable(uint8_t pin) {
    if (pin == 25 || pin == 26) dac_disable_pin(pin);
}

/* ======================================================================= touch */
extern "C" touch_value_t touchRead(uint8_t pin) {
    charge(cost(kCostTouch));
    if (touch_channel(pin) < 0) {
        core_log(RW_LOG_ERROR, "esp32-hal-touch.c", 240, "__touchRead", "No touch pad on selected pin %u!", pin);
        return 0;
    }

    if (!has_api || !api.touch_read) return 0;
    ApiScope s;
    int v = api.touch_read(api.user, pin);
    return v < 0 ? 0 : (touch_value_t)v;
}

extern "C" void touchAttachInterrupt(uint8_t pin, voidFuncPtr, touch_value_t) {
    log_once(0x400 + pin, RW_LOG_WARN, "[%6lu][W][esp32-hal-touch.c:200] __touchAttachInterrupt(): touch interrupts are not simulated; poll touchRead() instead",
             now_ms());
}

extern "C" void touchDetachInterrupt(uint8_t) {}
extern "C" void touchSetCycles(uint16_t, uint16_t) {}

extern "C" float temperatureRead(void) { return 53.3f; }
extern "C" int hallRead(void) { return 0; }

/* ======================================================================= analogWrite */
void analogWrite(uint8_t pin, int value) {
    charge(cost(kCostLedc));
    if (!output_pin(pin)) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 250, "analogWrite", "Pin %u is not a valid output pin", pin);
        return;
    }

    PinState& s = g_pins[pin];
    uint8_t bits = g_write_bits_pin[pin] ? g_write_bits_pin[pin] : g_write_bits;
    double freq = g_write_freq_pin[pin] ? (double)g_write_freq_pin[pin] : g_write_freq;
    if (s.ledc < 0) {
        if (!ledc_attach_v3(pin, freq, bits, -1)) return;
    }

    int c = s.ledc;
    uint32_t maxv = (1u << g_ch[c].res) - 1u;
    if (value < 0) value = 0;
    g_ch[c].duty = (uint32_t)value > maxv ? maxv : (uint32_t)value;
    ledc_apply(c);
}

void analogWriteResolution(uint8_t bits) {
    if (bits >= 1 && bits <= 20) g_write_bits = bits;
}

void analogWriteResolution(uint8_t pin, uint8_t bits) {
    if (pin < 40 && bits >= 1 && bits <= 20) {
        g_write_bits_pin[pin] = bits;
        if (g_pins[pin].ledc >= 0) ledcChangeFrequency(pin, g_ch[g_pins[pin].ledc].freq, bits);
    }
}

void analogWriteFrequency(double freq) {
    if (freq > 0.0) g_write_freq = freq;
}

void analogWriteFrequency(uint8_t pin, uint32_t freq) {
    if (pin < 40 && freq > 0) {
        g_write_freq_pin[pin] = freq;
        if (g_pins[pin].ledc >= 0) ledcChangeFrequency(pin, freq, g_ch[g_pins[pin].ledc].res);
    }
}

/* ======================================================================= LEDC */
double ledcSetup(uint8_t channel, double freq, uint8_t res) {
    charge(cost(kCostLedcSetup));
    if (channel > 15) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 60, "ledcSetup", "No more LEDC channels available! (channel %u > 15)", channel);
        return 0.0;
    }

    if (!ledc_config_ok(freq, res)) return 0.0;
    Channel& c = g_ch[channel];
    c.used = true;
    c.freq = freq;
    c.res = res;
    c.duty = 0;
    ledc_apply(channel);
    return freq;
}

void ledcAttachPin(uint8_t pin, uint8_t channel) {
    charge(cost(kCostLedc));
    if (!output_pin(pin)) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 90, "ledcAttachPin", "Pin %u is not a valid output pin", pin);
        return;
    }

    if (channel > 15) return;
    if (!g_ch[channel].used) {
        // Like the 2.x core this works with the channel's defaults until ledcSetup() is called.
        g_ch[channel].used = true;
        g_ch[channel].freq = 5000.0;
        g_ch[channel].res = 8;
    }

    dac_disable_pin(pin);
    g_pins[pin].ledc = (int8_t)channel;
    g_pins[pin].ledc_v3 = false;
    const Channel& c = g_ch[channel];
    host_pwm(pin, c.freq, channel_duty_fraction(c));
}

void ledcDetachPin(uint8_t pin) {
    charge(cost(kCostLedc));
    if (pin < 40) ledc_detach_pin(pin, false);
}

bool ledcAttach(uint8_t pin, uint32_t freq, uint8_t resolution) {
    charge(cost(kCostLedcSetup));
    return ledc_attach_v3(pin, freq, resolution, -1);
}

bool ledcAttachChannel(uint8_t pin, uint32_t freq, uint8_t resolution, uint8_t channel) {
    charge(cost(kCostLedcSetup));
    if (channel > 15) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 70, "ledcAttachChannel", "Channel %u is not available (0-15)", channel);
        return false;
    }

    return ledc_attach_v3(pin, freq, resolution, channel);
}

bool ledcDetach(uint8_t pin) {
    charge(cost(kCostLedc));
    if (pin >= 40 || g_pins[pin].ledc < 0) return false;
    ledc_detach_pin(pin, true);
    return true;
}

bool ledcWrite(uint8_t chanOrPin, uint32_t duty) {
    charge(cost(kCostLedc));
    int c = ledc_resolve(chanOrPin);
    if (c < 0) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 120, "ledcWrite", "Pin/channel %u is not attached to LEDC. Call ledcAttach() (or ledcSetup() + ledcAttachPin()) first", chanOrPin);
        return false;
    }

    uint32_t full = (uint32_t)(1ull << g_ch[c].res);
    g_ch[c].duty = duty > full ? full : duty;
    ledc_apply(c);
    return true;
}

double ledcWriteTone(uint8_t chanOrPin, double freq) {
    charge(cost(kCostLedc));
    int c = ledc_resolve(chanOrPin);
    if (c < 0) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 150, "ledcWriteTone", "Pin/channel %u is not attached to LEDC", chanOrPin);
        return 0.0;
    }

    Channel& ch = g_ch[c];
    if (!(freq > 0.0)) {
        ch.duty = 0;
        ledc_apply(c);
        return 0.0;
    }

    if (!ledc_config_ok(freq, 10)) return 0.0;
    ch.freq = freq;
    ch.res = 10;
    ch.duty = 0x1FF;
    ledc_apply(c);
    return freq;
}

double ledcWriteNote(uint8_t chanOrPin, note_t note, uint8_t octave) {
    static const uint16_t kNotes[12] = {4186, 4435, 4699, 4978, 5274, 5588, 5920, 6272, 6645, 7040, 7459, 7902};
    if (octave > 8 || note >= NOTE_MAX) return 0.0;
    double freq = (double)kNotes[note] / (double)(1u << (8 - octave));
    return ledcWriteTone(chanOrPin, freq);
}

uint32_t ledcRead(uint8_t chanOrPin) {
    int c = ledc_resolve(chanOrPin);
    return c < 0 ? 0 : g_ch[c].duty;
}

double ledcReadFreq(uint8_t chanOrPin) {
    int c = ledc_resolve(chanOrPin);
    return c < 0 || g_ch[c].duty == 0 ? 0.0 : g_ch[c].freq;
}

double ledcChangeFrequency(uint8_t chanOrPin, double freq, uint8_t resolution) {
    charge(cost(kCostLedcSetup));
    int c = ledc_resolve(chanOrPin);
    if (c < 0) {
        core_log(RW_LOG_ERROR, "esp32-hal-ledc.c", 170, "ledcChangeFrequency", "Pin/channel %u is not attached to LEDC", chanOrPin);
        return 0.0;
    }

    if (!ledc_config_ok(freq, resolution)) return 0.0;
    Channel& ch = g_ch[c];
    if (resolution != ch.res) {
        ch.duty = (uint32_t)((uint64_t)ch.duty * (1ull << resolution) / (1ull << ch.res));
    }

    ch.freq = freq;
    ch.res = resolution;
    ledc_apply(c);
    return freq;
}

bool ledcFade(uint8_t pin, uint32_t, uint32_t target_duty, int) {
    // Hardware fades are not stepped: the target duty is applied immediately.
    return ledcWrite(pin, target_duty);
}

bool ledcOutputInvert(uint8_t, bool) { return true; }

/* ======================================================================= tone */
void tone(uint8_t pin, unsigned int frequency, unsigned long duration) {
    charge(cost(kCostLedc));
    if (!output_pin(pin)) {
        core_log(RW_LOG_ERROR, "Tone.cpp", 120, "tone", "Pin %u is not a valid output pin", pin);
        return;
    }

    PinState& s = g_pins[pin];
    if (s.tone_event) {
        cancel_event(s.tone_event);
        s.tone_event = 0;
    }

    if (frequency == 0) {
        noTone(pin);
        return;
    }

    if (s.ledc < 0) {
        if (!ledc_attach_v3(pin, frequency, 10, -1)) return;
        s.tone = true;
    }

    ledcWriteTone(pin, frequency);
    if (duration > 0) s.tone_event = schedule_event(now_ns() + ms_to_ns(duration), tone_stop_event, (void*)(intptr_t)pin);
}

void noTone(uint8_t pin) {
    if (pin >= 40) return;
    PinState& s = g_pins[pin];
    if (s.tone_event) {
        cancel_event(s.tone_event);
        s.tone_event = 0;
    }

    if (s.ledc >= 0) {
        g_ch[s.ledc].duty = 0;
        ledc_apply(s.ledc);
        ledc_detach_pin(pin, true);
    }
}

/* ======================================================================= pulseIn */
static unsigned long pulse_in(uint8_t pin, uint8_t state, unsigned long timeout_us) {
    charge(cost(kCostDigitalRead * 10));
    if (!valid_pin(pin)) return 0;
    PinState& s = g_pins[pin];
    uint8_t want = state ? 1 : 0;
    uint64_t deadline = now_ns() + (uint64_t)timeout_us * 1000ull;
    uint32_t seen = s.edge_count;
    bool have_start = false;
    uint64_t start = 0;
    for (;;) {
        // Examine the edges reported by the host since the call (they carry exact times).
        while (seen < s.edge_count) {
            if (s.edge_count - seen > 16) seen = s.edge_count - 16;
            const Edge& e = s.edges[seen % 16];
            seen++;
            if (!have_start) {
                if (e.level == want) {
                    have_start = true;
                    start = e.time;
                }
            } else if (e.level != want) {
                uint64_t width = e.time > start ? e.time - start : 0;
                return (unsigned long)(width / 1000ull);
            }
        }

        if (now_ns() >= deadline) return 0;
        wait_slice(deadline);
    }
}

unsigned long pulseIn(uint8_t pin, uint8_t state, unsigned long timeout) { return pulse_in(pin, state, timeout); }
unsigned long pulseInLong(uint8_t pin, uint8_t state, unsigned long timeout) { return pulse_in(pin, state, timeout); }

/* ======================================================================= hardware timers */
hw_timer_t* timerBegin(uint32_t frequency) {
    charge(cost(kCostLedcSetup));
    return timer_alloc(-1, frequency);
}

hw_timer_t* timerBegin(uint8_t num, uint16_t divider, bool) {
    charge(cost(kCostLedcSetup));
    if (divider < 2) divider = 2;
    return timer_alloc(num, 80000000u / divider);
}

void timerEnd(hw_timer_t* timer) {
    if (!timer) return;
    if (timer->s.event_id) cancel_event(timer->s.event_id);
    memset(&timer->s, 0, sizeof timer->s);
}

void timerStart(hw_timer_t* timer) {
    if (!timer || timer->s.running) return;
    timer->s.running = true;
    timer->s.base_time = now_ns();
    timer_schedule(timer);
}

void timerStop(hw_timer_t* timer) {
    if (!timer || !timer->s.running) return;
    timer->s.base_count = timer_count(timer->s);
    timer->s.running = false;
    timer_schedule(timer);
}

void timerRestart(hw_timer_t* timer) {
    if (!timer) return;
    timer->s.base_count = 0;
    timer->s.base_time = now_ns();
    timer_schedule(timer);
}

void timerWrite(hw_timer_t* timer, uint64_t val) {
    if (!timer) return;
    timer->s.base_count = val;
    timer->s.base_time = now_ns();
    timer_schedule(timer);
}

uint64_t timerRead(hw_timer_t* timer) { return timer ? timer_count(timer->s) : 0; }

uint64_t timerReadMicros(hw_timer_t* timer) {
    return timer && timer->s.freq ? (uint64_t)((double)timer_count(timer->s) * 1e6 / timer->s.freq) : 0;
}

uint64_t timerReadMilis(hw_timer_t* timer) {
    return timer && timer->s.freq ? (uint64_t)((double)timer_count(timer->s) * 1e3 / timer->s.freq) : 0;
}

double timerReadSeconds(hw_timer_t* timer) {
    return timer && timer->s.freq ? (double)timer_count(timer->s) / timer->s.freq : 0.0;
}

uint32_t timerGetFrequency(hw_timer_t* timer) { return timer ? timer->s.freq : 0; }

void timerAttachInterrupt(hw_timer_t* timer, void (*userFunc)(void), bool) {
    if (!timer) return;
    timer->s.fn0 = userFunc;
    timer->s.fn1 = nullptr;
}

void timerAttachInterruptArg(hw_timer_t* timer, void (*userFunc)(void*), void* arg) {
    if (!timer) return;
    timer->s.fn0 = nullptr;
    timer->s.fn1 = userFunc;
    timer->s.arg = arg;
}

void timerDetachInterrupt(hw_timer_t* timer) {
    if (!timer) return;
    timer->s.fn0 = nullptr;
    timer->s.fn1 = nullptr;
}

void timerAlarm(hw_timer_t* timer, uint64_t alarm_value, bool autoreload, uint64_t reload_count) {
    if (!timer) return;
    timer->s.alarm = alarm_value;
    timer->s.autoreload = autoreload;
    timer->s.reload_count = reload_count;
    timer->s.fired = 0;
    timer->s.alarm_enabled = true;
    timer_schedule(timer);
}

void timerAlarmWrite(hw_timer_t* timer, uint64_t alarm_value, bool autoreload) {
    if (!timer) return;
    timer->s.alarm = alarm_value;
    timer->s.autoreload = autoreload;
    timer->s.reload_count = 0;
    timer_schedule(timer);
}

void timerAlarmEnable(hw_timer_t* timer) {
    if (!timer) return;
    timer->s.alarm_enabled = true;
    timer->s.fired = 0;
    timer_schedule(timer);
}

void timerAlarmDisable(hw_timer_t* timer) {
    if (!timer) return;
    timer->s.alarm_enabled = false;
    timer_schedule(timer);
}

bool timerAlarmEnabled(hw_timer_t* timer) { return timer && timer->s.alarm_enabled; }

/* ======================================================================= math / helpers */
long random(long howbig) {
    charge(cost(kCostMisc));
    if (howbig <= 0) return 0;
    return (long)(((uint64_t)random32() * (uint64_t)howbig) >> 32);
}

long random(long howsmall, long howbig) {
    if (howsmall >= howbig) return howsmall;
    return random(howbig - howsmall) + howsmall;
}

long random(void) { return (long)(random32() & 0x7FFFFFFFu); }

void randomSeed(unsigned long seed) {
    if (seed != 0) seed_random(seed);
}

long map(long x, long in_min, long in_max, long out_min, long out_max) {
    const long run = in_max - in_min;
    if (run == 0) {
        core_log(RW_LOG_ERROR, "WMath.cpp", 70, "map", "Invalid input range, min == max");
        return -1;
    }

    const long rise = out_max - out_min;
    const long delta = x - in_min;
    return (delta * rise) / run + out_min;
}

uint16_t makeWord(uint16_t w) { return w; }
uint16_t makeWord(uint8_t h, uint8_t l) { return (uint16_t)((h << 8) | l); }

void shiftOut(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder, uint8_t val) {
    for (uint8_t i = 0; i < 8; i++) {
        if (bitOrder == LSBFIRST) digitalWrite(dataPin, !!(val & (1 << i)));
        else digitalWrite(dataPin, !!(val & (1 << (7 - i))));
        digitalWrite(clockPin, HIGH);
        digitalWrite(clockPin, LOW);
    }
}

uint8_t shiftIn(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder) {
    uint8_t value = 0;
    for (uint8_t i = 0; i < 8; ++i) {
        digitalWrite(clockPin, HIGH);
        if (bitOrder == LSBFIRST) value |= (uint8_t)(digitalRead(dataPin) << i);
        else value |= (uint8_t)(digitalRead(dataPin) << (7 - i));
        digitalWrite(clockPin, LOW);
    }

    return value;
}

/* ======================================================================= ESP / esp_system / sleep */
EspClass ESP;

static const uint8_t kMac[6] = {0x24, 0x0A, 0xC4, 0x5A, 0x7E, 0x11};
static const uint32_t kHeapSize = 327680u;
static const uint32_t kHeapFreeAtBoot = 296000u;

uint32_t EspClass::getHeapSize() { return kHeapSize; }

uint32_t EspClass::getFreeHeap() {
    size_t used = heap_in_use();
    uint32_t free_heap = used >= kHeapFreeAtBoot ? 0u : kHeapFreeAtBoot - (uint32_t)used;
    if (free_heap < g_min_free_heap) g_min_free_heap = free_heap;
    return free_heap;
}

uint32_t EspClass::getMinFreeHeap() {
    getFreeHeap();
    return g_min_free_heap;
}

uint32_t EspClass::getMaxAllocHeap() {
    uint32_t f = getFreeHeap();
    return f < 110592u ? f : 110592u;
}

uint32_t EspClass::getCycleCount() { return (uint32_t)(uptime_ns() * g_cpu_mhz / 1000ull); }

uint64_t EspClass::getEfuseMac() {
    uint64_t v = 0;
    for (int i = 5; i >= 0; i--) v = (v << 8) | kMac[i];
    return v;
}

void EspClass::restart() { esp_restart(); }

void EspClass::deepSleep(uint64_t time_us) { esp_deep_sleep(time_us); }

extern "C" uint32_t esp_random(void) {
    charge(cost(kCostMisc));
    return random32();
}

extern "C" void esp_fill_random(void* buf, size_t len) {
    uint8_t* b = static_cast<uint8_t*>(buf);
    for (size_t i = 0; i < len; i++) b[i] = (uint8_t)random32();
}

namespace rw {
void system_restart(int reset_reason) {
    if (has_api && api.system_event) {
        ApiScope s;
        api.system_event(api.user, RW_EVENT_RESTART, reset_reason);
    }

    block_forever();
}
}  // namespace rw

extern "C" void esp_restart(void) {
    rw::log(RW_LOG_INFO, "esp_restart() requested by the sketch");
    if (has_api && api.system_event) {
        ApiScope s;
        api.system_event(api.user, RW_EVENT_RESTART, 0);
    }

    block_forever();
}

extern "C" esp_reset_reason_t esp_reset_reason(void) { return (esp_reset_reason_t)config.reset_reason; }

extern "C" uint32_t esp_get_free_heap_size(void) { return ESP.getFreeHeap(); }
extern "C" uint32_t esp_get_minimum_free_heap_size(void) { return ESP.getMinFreeHeap(); }

extern "C" esp_err_t esp_efuse_mac_get_default(uint8_t* mac) {
    if (!mac) return ESP_ERR_INVALID_ARG;
    memcpy(mac, kMac, 6);
    return ESP_OK;
}

extern "C" esp_err_t esp_read_mac(uint8_t* mac, esp_mac_type_t type) {
    if (!mac) return ESP_ERR_INVALID_ARG;
    memcpy(mac, kMac, 6);
    mac[5] = (uint8_t)(mac[5] + (int)type);
    return ESP_OK;
}

extern "C" const char* esp_get_idf_version(void) { return "v5.1.4-robotwin-sim"; }

extern "C" const char* esp_err_to_name(esp_err_t code) {
    switch (code) {
    case ESP_OK: return "ESP_OK";
    case ESP_FAIL: return "ESP_FAIL";
    case ESP_ERR_INVALID_ARG: return "ESP_ERR_INVALID_ARG";
    case ESP_ERR_INVALID_STATE: return "ESP_ERR_INVALID_STATE";
    case ESP_ERR_TIMEOUT: return "ESP_ERR_TIMEOUT";
    default: return "UNKNOWN ERROR";
    }
}

extern "C" esp_err_t esp_sleep_enable_timer_wakeup(uint64_t time_in_us) {
    g_sleep_timer = true;
    g_sleep_timer_us = time_in_us;
    return ESP_OK;
}

extern "C" esp_err_t esp_sleep_enable_ext0_wakeup(gpio_num_t gpio_num, int level) {
    if (!valid_pin(gpio_num)) return ESP_ERR_INVALID_ARG;
    g_ext0_gpio = gpio_num;
    g_ext0_level = level ? 1 : 0;
    return ESP_OK;
}

extern "C" esp_err_t esp_sleep_disable_wakeup_source(esp_sleep_source_t source) {
    if (source == ESP_SLEEP_WAKEUP_TIMER || source == ESP_SLEEP_WAKEUP_ALL) g_sleep_timer = false;
    if (source == ESP_SLEEP_WAKEUP_EXT0 || source == ESP_SLEEP_WAKEUP_ALL) g_ext0_gpio = -1;
    return ESP_OK;
}

extern "C" void esp_deep_sleep_start(void) {
    if (has_api && api.system_event) {
        ApiScope s;
        if (g_ext0_gpio >= 0) api.system_event(api.user, RW_EVENT_EXT0_WAKEUP, g_ext0_gpio | (g_ext0_level << 8));
        api.system_event(api.user, RW_EVENT_DEEP_SLEEP, g_sleep_timer ? (int64_t)g_sleep_timer_us : -1);
    }

    block_forever();
}

extern "C" void esp_deep_sleep(uint64_t time_in_us) {
    esp_sleep_enable_timer_wakeup(time_in_us);
    esp_deep_sleep_start();
}

extern "C" esp_err_t esp_light_sleep_start(void) {
    if (!g_sleep_timer) {
        log_once(0x500, RW_LOG_WARN, "W (%lu) sleep: light sleep without a timer wake-up source returns immediately in the simulation", now_ms());
        return ESP_OK;
    }

    sleep_until(now_ns() + g_sleep_timer_us * 1000ull);
    return ESP_OK;
}

extern "C" esp_sleep_wakeup_cause_t esp_sleep_get_wakeup_cause(void) { return (esp_sleep_wakeup_cause_t)config.wakeup_cause; }

/* ======================================================================= sketch logging */
extern "C" void rw_sketch_log(int level, const char* format, ...) {
    char msg[384];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(level, "[%6lu][%c][sketch] %s", now_ms(), level == RW_LOG_ERROR ? 'E' : 'W', msg);
}

extern "C" void rw_sketch_log_tag(int level, const char* tag, const char* format, ...) {
    char msg[384];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(level, "%c (%lu) %s: %s", level == RW_LOG_ERROR ? 'E' : 'W', now_ms(), tag ? tag : "", msg);
}
