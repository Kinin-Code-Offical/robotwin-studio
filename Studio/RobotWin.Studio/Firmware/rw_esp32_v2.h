// RobotWin Studio: the Arduino-ESP32 2.x calls a sketch makes, on the 3.x core the real-chip build uses (3.3.13).
// Written for the 2.x core (most examples and tutorials are), a sketch calls ledcSetup / ledcAttachPin / ledcWrite by
// channel, timerBegin(num, divider, countUp) / timerAlarmWrite / timerAlarmEnable and esp_task_wdt_init(seconds, panic),
// which the 3.x core no longer has; the fast mode takes both generations, and with this header the real-chip build does
// too, the same way: ledcWrite and its kin take a pin once a 3.x attach has been made for it, otherwise a channel set up
// with ledcSetup (or given a pin with ledcAttachPin). Added only to the sketch's own file, and only when it makes a 2.x
// call; libraries and the core keep their own names.
#pragma once
#include <Arduino.h>
#include "esp_task_wdt.h"

#if defined(ESP_ARDUINO_VERSION_MAJOR) && ESP_ARDUINO_VERSION_MAJOR >= 3

// ---------------------------------------------------------------------------------------------------------------- LEDC
#define RW2_LEDC_CHANNELS 16   // as the 2.x core counts them on the ESP32: the high and low speed groups, channels 0–15

struct rw2_ledc_channel
{
    bool used;          // set up with ledcSetup (or given a pin with ledcAttachPin)
    uint32_t freq;
    uint8_t bits;
    uint32_t duty;      // written before a pin was given: the pin starts with it
    uint64_t pins;      // the pins attached to it with ledcAttachPin
};

static rw2_ledc_channel rw2_ch[RW2_LEDC_CHANNELS];
static int8_t rw2_pin_ch[SOC_GPIO_PIN_COUNT];   // a pin's 2.x channel, -1 for none
static bool rw2_pin_v3[SOC_GPIO_PIN_COUNT];     // a pin the sketch attached with ledcAttach / ledcAttachChannel
static bool rw2_ledc_ready = false;

static inline void rw2_ledc_init()
{
    if (rw2_ledc_ready) return;
    for (int i = 0; i < SOC_GPIO_PIN_COUNT; i++)
    {
        rw2_pin_ch[i] = -1;
        rw2_pin_v3[i] = false;
    }

    rw2_ledc_ready = true;
}

// What a call's first argument names: 0 a pin attached the 3.x way, 1 a 2.x channel, 2 a pin on a 2.x channel, -1 nothing
static inline int rw2_ledc_kind(uint8_t x)
{
    rw2_ledc_init();
    bool pin = x < SOC_GPIO_PIN_COUNT;
    bool channel = x < RW2_LEDC_CHANNELS && rw2_ch[x].used;
    if (pin && rw2_pin_v3[x]) return 0;
    if (channel) return 1;
    if (pin && rw2_pin_ch[x] >= 0) return 2;
    return -1;
}

static inline uint32_t rw2_ledcSetup(uint8_t channel, uint32_t freq, uint8_t bits)
{
    rw2_ledc_init();
    if (channel >= RW2_LEDC_CHANNELS) return 0;
    rw2_ledc_channel& c = rw2_ch[channel];
    c.used = true;
    c.freq = freq;
    c.bits = bits;
    c.duty = 0;
    // a channel set up again: its pins take the new frequency and resolution, at duty 0
    if (c.pins != 0)
    {
        for (int p = 0; p < SOC_GPIO_PIN_COUNT; p++)
            if (c.pins & (1ULL << p)) ledcChangeFrequency(p, freq, bits);
        ledcWriteChannel(channel, 0);
    }

    return freq;
}

static inline void rw2_ledcAttachPin(uint8_t pin, uint8_t channel)
{
    rw2_ledc_init();
    if (pin >= SOC_GPIO_PIN_COUNT || channel >= RW2_LEDC_CHANNELS) return;
    rw2_ledc_channel& c = rw2_ch[channel];
    if (!c.used)
    {
        // like the 2.x core: a channel's defaults until ledcSetup is called
        c.used = true;
        c.freq = 5000;
        c.bits = 8;
    }

    if (rw2_pin_ch[pin] >= 0 || rw2_pin_v3[pin]) ledcDetach(pin);
    if (!ledcAttachChannel(pin, c.freq, c.bits, channel)) return;
    rw2_pin_ch[pin] = (int8_t)channel;
    rw2_pin_v3[pin] = false;
    c.pins |= 1ULL << pin;
    ledcWrite(pin, c.duty);
}

static inline void rw2_ledcDetachPin(uint8_t pin)
{
    rw2_ledc_init();
    if (pin >= SOC_GPIO_PIN_COUNT) return;
    if (rw2_pin_ch[pin] >= 0) rw2_ch[rw2_pin_ch[pin]].pins &= ~(1ULL << pin);
    rw2_pin_ch[pin] = -1;
    rw2_pin_v3[pin] = false;
    ledcDetach(pin);
}

static inline bool rw2_ledcAttach(uint8_t pin, uint32_t freq, uint8_t bits)
{
    rw2_ledc_init();
    if (pin < SOC_GPIO_PIN_COUNT && rw2_pin_ch[pin] >= 0) rw2_ledcDetachPin(pin);
    bool ok = ledcAttach(pin, freq, bits);
    if (ok && pin < SOC_GPIO_PIN_COUNT) rw2_pin_v3[pin] = true;
    return ok;
}

static inline bool rw2_ledcAttachChannel(uint8_t pin, uint32_t freq, uint8_t bits, uint8_t channel)
{
    rw2_ledc_init();
    if (pin < SOC_GPIO_PIN_COUNT && rw2_pin_ch[pin] >= 0) rw2_ledcDetachPin(pin);
    bool ok = ledcAttachChannel(pin, freq, bits, channel);
    if (ok && pin < SOC_GPIO_PIN_COUNT) rw2_pin_v3[pin] = true;
    return ok;
}

static inline bool rw2_ledcDetach(uint8_t pin)
{
    rw2_ledc_init();
    if (pin < SOC_GPIO_PIN_COUNT && rw2_pin_ch[pin] >= 0)
    {
        rw2_ledcDetachPin(pin);
        return true;
    }

    if (pin < SOC_GPIO_PIN_COUNT) rw2_pin_v3[pin] = false;
    return ledcDetach(pin);
}

// the first pin a 2.x channel drives (they share the channel's timer and duty), -1 for none
static inline int rw2_first_pin(uint8_t channel)
{
    for (int p = 0; p < SOC_GPIO_PIN_COUNT; p++)
        if (rw2_ch[channel].pins & (1ULL << p)) return p;
    return -1;
}

static inline bool rw2_ledcWrite(uint8_t x, uint32_t duty)
{
    switch (rw2_ledc_kind(x))
    {
    case 0:
    case 2:
        return ledcWrite(x, duty);
    case 1:
        rw2_ch[x].duty = duty;
        return rw2_first_pin(x) < 0 || ledcWriteChannel(x, duty);
    default:
        return ledcWrite(x, duty);   // the core's own message
    }
}

static inline uint32_t rw2_ledcRead(uint8_t x)
{
    if (rw2_ledc_kind(x) == 1)
    {
        int p = rw2_first_pin(x);
        return p < 0 ? rw2_ch[x].duty : ledcRead(p);
    }

    return ledcRead(x);
}

static inline uint32_t rw2_ledcReadFreq(uint8_t x)
{
    if (rw2_ledc_kind(x) == 1)
    {
        int p = rw2_first_pin(x);
        return p < 0 ? rw2_ch[x].freq : ledcReadFreq(p);
    }

    return ledcReadFreq(x);
}

static inline uint32_t rw2_ledcWriteTone(uint8_t x, uint32_t freq)
{
    if (rw2_ledc_kind(x) == 1)
    {
        rw2_ledc_channel& c = rw2_ch[x];
        // the 2.x core: a tone sets the channel to 10 bits at that frequency, half duty (0 Hz: duty 0)
        if (freq != 0)
        {
            c.freq = freq;
            c.bits = 10;
        }

        c.duty = freq != 0 ? 0x1FF : 0;
        int p = rw2_first_pin(x);
        if (p < 0) return freq;
        uint32_t done = 0;
        for (int q = 0; q < SOC_GPIO_PIN_COUNT; q++)
            if (c.pins & (1ULL << q)) done = ledcWriteTone(q, freq);
        return done;
    }

    return ledcWriteTone(x, freq);
}

static inline uint32_t rw2_ledcWriteNote(uint8_t x, note_t note, uint8_t octave)
{
    static const uint16_t noteFrequencyBase[12] = {4186, 4435, 4699, 4978, 5274, 5588, 5920, 6272, 6645, 7040, 7459, 7902};
    if (octave > 8 || note >= NOTE_MAX) return 0;
    uint32_t freq = (uint32_t)noteFrequencyBase[note] / (uint32_t)(1 << (8 - octave));
    return rw2_ledcWriteTone(x, freq);
}

static inline uint32_t rw2_ledcChangeFrequency(uint8_t x, uint32_t freq, uint8_t bits)
{
    if (rw2_ledc_kind(x) == 1)
    {
        rw2_ledc_channel& c = rw2_ch[x];
        c.freq = freq;
        c.bits = bits;
        uint32_t done = freq;
        for (int q = 0; q < SOC_GPIO_PIN_COUNT; q++)
            if (c.pins & (1ULL << q)) done = ledcChangeFrequency(q, freq, bits);
        return done;
    }

    return ledcChangeFrequency(x, freq, bits);
}

#define ledcSetup rw2_ledcSetup
#define ledcAttachPin rw2_ledcAttachPin
#define ledcDetachPin rw2_ledcDetachPin
#define ledcAttach rw2_ledcAttach
#define ledcAttachChannel rw2_ledcAttachChannel
#define ledcDetach rw2_ledcDetach
#define ledcWrite rw2_ledcWrite
#define ledcRead rw2_ledcRead
#define ledcReadFreq rw2_ledcReadFreq
#define ledcWriteTone rw2_ledcWriteTone
#define ledcWriteNote rw2_ledcWriteNote
#define ledcChangeFrequency rw2_ledcChangeFrequency

// ------------------------------------------------------------------------------------------------------ hardware timers
// The 2.x calls are other signatures than the 3.x core's, so they are overloads beside them. The alarm is the 3.x one;
// the sketch's handler is called through a gate, so a disabled alarm calls nothing, as on the 2.x core.
struct rw2_timer
{
    hw_timer_t* timer;
    void (*fn)(void);
    uint64_t alarm;
    bool reload;
    bool enabled;
};

static rw2_timer rw2_timers[4];

static inline rw2_timer* rw2_timer_of(hw_timer_t* t)
{
    if (t == nullptr) return nullptr;
    rw2_timer* spare = nullptr;
    for (auto& s : rw2_timers)
    {
        if (s.timer == t) return &s;
        if (s.timer == nullptr && spare == nullptr) spare = &s;
    }

    if (spare != nullptr) *spare = rw2_timer{t, nullptr, 0, false, false};
    return spare;
}

static void ARDUINO_ISR_ATTR rw2_timer_gate(void* arg)
{
    auto* s = (rw2_timer*)arg;
    if (s->enabled && s->fn != nullptr) s->fn();
}

// 2.x: the APB clock (80 MHz) divided; the timer counts from 0 at once (counting down is not in the 3.x core: it counts up)
static inline hw_timer_t* timerBegin(uint8_t num, uint16_t divider, bool countUp)
{
    (void)num;
    (void)countUp;
    if (divider < 2) divider = 2;
    hw_timer_t* t = timerBegin((uint32_t)(80000000UL / divider));
    if (t == nullptr) return nullptr;
    rw2_timer* s = rw2_timer_of(t);
    if (s != nullptr) *s = rw2_timer{t, nullptr, 0, false, false};
    return t;
}

static inline void timerAttachInterrupt(hw_timer_t* t, void (*fn)(void), bool edge)
{
    (void)edge;   // the 2.x core took level interrupts only too
    rw2_timer* s = rw2_timer_of(t);
    if (s == nullptr) return;
    s->fn = fn;
    timerAttachInterruptArg(t, rw2_timer_gate, s);
}

static inline void timerAlarmWrite(hw_timer_t* t, uint64_t value, bool reload)
{
    rw2_timer* s = rw2_timer_of(t);
    if (s == nullptr) return;
    s->alarm = value;
    s->reload = reload;
    if (s->enabled) timerAlarm(t, value, reload, 0);
}

static inline void timerAlarmEnable(hw_timer_t* t)
{
    rw2_timer* s = rw2_timer_of(t);
    if (s == nullptr) return;
    s->enabled = true;
    timerAlarm(t, s->alarm, s->reload, 0);
}

static inline void timerAlarmDisable(hw_timer_t* t)
{
    rw2_timer* s = rw2_timer_of(t);
    if (s == nullptr) return;
    s->enabled = false;
    timerAlarm(t, s->alarm, false, 0);   // the counter runs on without reloading, as on the 2.x core
}

static inline bool timerAlarmEnabled(hw_timer_t* t)
{
    rw2_timer* s = rw2_timer_of(t);
    return s != nullptr && s->enabled;
}

static inline uint64_t timerAlarmRead(hw_timer_t* t)
{
    rw2_timer* s = rw2_timer_of(t);
    return s != nullptr ? s->alarm : 0;
}

// --------------------------------------------------------------------------------------------------- the task watchdog
// IDF 4.4's esp_task_wdt_init(seconds, panic) on a running watchdog reconfigures it; the idle tasks it watches stay.
static inline esp_err_t esp_task_wdt_init(uint32_t timeout_s, bool panic)
{
    esp_task_wdt_config_t c;
    c.timeout_ms = timeout_s * 1000u;
    c.idle_core_mask = 0;
#if defined(CONFIG_ESP_TASK_WDT_CHECK_IDLE_TASK_CPU0)
    c.idle_core_mask |= 1u << 0;
#endif
#if defined(CONFIG_ESP_TASK_WDT_CHECK_IDLE_TASK_CPU1)
    c.idle_core_mask |= 1u << 1;
#endif
    c.trigger_panic = panic;
    esp_err_t e = esp_task_wdt_reconfigure(&c);
    return e == ESP_ERR_INVALID_STATE ? esp_task_wdt_init(&c) : e;
}

#endif
