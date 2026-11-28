/*
 * [motor] The MCPWM capture unit in the fast simulation (driver/mcpwm_cap.h), as the classic ESP32 has it: per group one
 * capture timer counting the APB clock (80 MHz), three channels that latch it on their GPIO's rising and / or falling
 * edges and hand the value and the edge to on_cap in interrupt context, as IDF's driver does. The edges are the circuit's,
 * at their own times (hal_pin_changed); a pin the chip drives with PWM itself (LEDC, MCPWM) gives its edges from the PWM's
 * timing, rising at k/f and falling at (k + duty)/f from when it started, as on the chip the GPIO matrix feeds the output
 * back to the capture input. A prescale p (even) makes the captured signal toggle every p/2 rising edges of the input.
 * The same unit at register level is in the Xtensa emulator (Esp32Soc.Mcpwm.cs).
 */
#include "Arduino.h"
#include "driver/mcpwm_prelude.h"
#include "rw_internal.h"
#include <stdarg.h>
#include <math.h>

using namespace rw;

static const uint32_t kApbHz = 80000000u;

struct mcpwm_cap_timer_t {
    bool used;
    int group;
    uint32_t resolution;
    bool enabled;
    bool running;
    double origin_ns;   // when the count was 0 (running)
    uint32_t frozen;    // the count while stopped
};

struct mcpwm_cap_channel_t {
    bool used;
    mcpwm_cap_timer_t* timer;
    int id;
    int gpio;
    bool pos, neg, invert;
    uint32_t prescale;          // 0 / 1: every edge
    uint32_t rises;             // rising input edges seen (for the prescaler)
    int prescaled;              // the prescaled signal's level
    int level;                  // the input's level as last seen, -1 unknown
    bool enabled;
    mcpwm_capture_event_cb_t on_cap;
    void* ctx;
    int pwm_event;              // the next edge of the chip's own PWM on the pin, scheduled
    double pwm_freq, pwm_duty;  // that PWM
    double pwm_since_ns;
    long long pwm_edge;         // index of the next of its edges: even rising, odd falling
    uint32_t last_value;        // as CAP_CHn holds it
    bool last_negative;         // as CAP_STATUS holds it
    rw::legacy_cap_fn legacy;   // [legacy] the legacy driver's callback instead of on_cap
    void* legacy_user;
};

namespace {

mcpwm_cap_timer_t g_cap_timers[2];
mcpwm_cap_channel_t g_cap_chans[2][3];

struct Caught {
    mcpwm_cap_channel_t* chan;
    uint32_t value;
    mcpwm_capture_edge_t edge;
};

Caught g_caught[64];
unsigned g_head, g_tail;

void cap_log(const char* func, int line, const char* format, ...) {
    char msg[160];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(RW_LOG_ERROR, "E (%lu) mcpwm: %s(%d): %s", (unsigned long)(uptime_ns() / 1000000ull), func, line, msg);
}

uint32_t count_at(const mcpwm_cap_timer_t& t, double at_ns) {
    if (!t.running) return t.frozen;
    double ticks = (at_ns - t.origin_ns) * t.resolution / 1e9;
    return (uint32_t)(uint64_t)(long long)floor(ticks);
}

/* The interrupt: each capture since the last one, to on_cap. */
void deliver(void*) {
    while (g_tail != g_head) {
        Caught c = g_caught[g_tail % 64];
        g_tail++;
        if (!c.chan->used || !c.chan->enabled) continue;
        if (c.chan->legacy) {
            c.chan->legacy(c.chan->timer->group, c.chan->id, c.value, c.edge == MCPWM_CAP_EDGE_NEG, c.chan->legacy_user);
            continue;
        }

        if (!c.chan->on_cap) continue;
        mcpwm_capture_event_data_t data = {c.value, c.edge};
        c.chan->on_cap(c.chan, &data, c.chan->ctx);
    }
}

void catch_at(mcpwm_cap_channel_t& ch, mcpwm_capture_edge_t edge, double at_ns) {
    if (!ch.timer || !ch.timer->enabled) return;
    uint32_t value = count_at(*ch.timer, at_ns);
    ch.last_value = value;
    ch.last_negative = edge == MCPWM_CAP_EDGE_NEG;
    if (g_head - g_tail >= 64) return;   // the interrupt is far behind: the hardware keeps only the latest value too
    g_caught[g_head % 64] = Caught{&ch, value, edge};
    g_head++;
    raise_isr(deliver, nullptr);
}

/* One edge of the input, after the inversion: through the prescaler, captured when its edge is selected. */
void input_edge(mcpwm_cap_channel_t& ch, int level, double at_ns) {
    if (!ch.enabled) return;
    bool rising = (level != 0) != ch.invert;
    int edge;   // the captured signal's edge: 1 rising, 0 falling, -1 none
    if (ch.prescale <= 1) edge = rising ? 1 : 0;
    else {
        if (!rising) return;
        uint32_t half = ch.prescale / 2;
        if (ch.rises++ % half != 0) return;
        ch.prescaled = !ch.prescaled;
        edge = ch.prescaled;
    }

    if ((edge == 1 && ch.pos) || (edge == 0 && ch.neg)) catch_at(ch, edge ? MCPWM_CAP_EDGE_POS : MCPWM_CAP_EDGE_NEG, at_ns);
}

void schedule_pwm_edge(mcpwm_cap_channel_t& ch);

void pwm_edge(void* arg) {
    auto* ch = static_cast<mcpwm_cap_channel_t*>(arg);
    ch->pwm_event = 0;
    if (!ch->used || ch->pwm_freq <= 0) return;
    long long k = ch->pwm_edge;
    double at = ch->pwm_since_ns + ((double)(k / 2) + (k % 2 ? ch->pwm_duty : 0.0)) / ch->pwm_freq * 1e9;
    ch->pwm_edge++;
    input_edge(*ch, k % 2 ? 0 : 1, at);
    schedule_pwm_edge(*ch);
}

/* The next edge of the chip's own PWM on the channel's pin, at its own time. */
void schedule_pwm_edge(mcpwm_cap_channel_t& ch) {
    if (ch.pwm_event) cancel_event(ch.pwm_event);
    ch.pwm_event = 0;
    if (!ch.enabled || ch.pwm_freq <= 0 || ch.pwm_duty <= 0 || ch.pwm_duty >= 1) return;
    long long k = ch.pwm_edge;
    double at = ch.pwm_since_ns + ((double)(k / 2) + (k % 2 ? ch.pwm_duty : 0.0)) / ch.pwm_freq * 1e9;
    ch.pwm_event = schedule_event((uint64_t)ceil(at), pwm_edge, &ch);
}

/* The pin's own PWM as it is now: its edges from here, the first rising one now (as the output starts its period). */
void follow_pwm(mcpwm_cap_channel_t& ch, double frequency, double duty, uint64_t now) {
    ch.pwm_freq = frequency;
    ch.pwm_duty = duty;
    ch.pwm_since_ns = (double)now;
    ch.pwm_edge = 0;
    schedule_pwm_edge(ch);
}

bool chan_ok(mcpwm_cap_channel_handle_t c) { return c && c->used && c->timer && c->timer->used; }

}  // namespace

namespace rw {

/* PWM a pin drives now, for a capture channel added later (the host is told each change; hal_mcpwm_cap_pwm). */
static double g_pin_pwm_freq[40], g_pin_pwm_duty[40];

void hal_mcpwm_cap_reset() {
    for (auto& row : g_cap_chans)
        for (auto& c : row)
            if (c.pwm_event) cancel_event(c.pwm_event);
    memset(g_cap_timers, 0, sizeof g_cap_timers);
    memset(g_cap_chans, 0, sizeof g_cap_chans);
    memset(g_pin_pwm_freq, 0, sizeof g_pin_pwm_freq);
    memset(g_pin_pwm_duty, 0, sizeof g_pin_pwm_duty);
    g_head = g_tail = 0;
}

void hal_mcpwm_cap_edge(int gpio, int level, uint64_t time_ns) {
    for (auto& row : g_cap_chans)
        for (auto& c : row) {
            if (!c.used || c.gpio != gpio) continue;
            c.level = level;
            if (c.pwm_freq > 0) continue;   // the chip's own PWM: its edges come from its timing
            input_edge(c, level, (double)time_ns);
        }
}

void hal_mcpwm_cap_pwm(int gpio, double frequency, double duty, uint64_t now) {
    if (gpio < 0 || gpio >= 40) return;
    bool pwm = frequency > 0 && duty > 0 && duty < 1;
    g_pin_pwm_freq[gpio] = pwm ? frequency : 0;
    g_pin_pwm_duty[gpio] = pwm ? duty : 0;
    for (auto& row : g_cap_chans)
        for (auto& c : row)
            if (c.used && c.gpio == gpio) {
                if (pwm) follow_pwm(c, frequency, duty, now);
                else {
                    c.pwm_freq = 0;
                    schedule_pwm_edge(c);   // cancels
                    int lv = duty >= 0.5 ? 1 : 0;   // a constant level: one edge when it changes
                    if (c.level != lv) {
                        c.level = lv;
                        input_edge(c, lv, (double)now);
                    }
                }
            }
}

static void adopt_pin_pwm(mcpwm_cap_channel_t& c) {
    if (c.gpio < 0 || c.gpio >= 40 || g_pin_pwm_freq[c.gpio] <= 0) return;
    follow_pwm(c, g_pin_pwm_freq[c.gpio], g_pin_pwm_duty[c.gpio], now_ns());
}

/* [legacy] channel n of the group's capture timer, which the legacy driver starts with its first channel */
bool mcpwm_legacy_capture(int unit, int channel, int gpio, bool pos, bool neg, uint32_t prescale, legacy_cap_fn fn, void* user) {
    if (unit < 0 || unit > 1 || channel < 0 || channel > 2) return false;
    if (prescale > 1 && prescale % 2 != 0) return false;
    mcpwm_cap_timer_t& t = g_cap_timers[unit];
    if (!t.used) {
        t = mcpwm_cap_timer_t{};
        t.used = true;
        t.group = unit;
        t.resolution = kApbHz;
    }

    if (!t.running) {
        t.enabled = true;
        t.running = true;
        t.origin_ns = (double)now_ns();
    }

    mcpwm_cap_channel_t& c = g_cap_chans[unit][channel];
    if (c.pwm_event) cancel_event(c.pwm_event);
    c = mcpwm_cap_channel_t{};
    c.used = true;
    c.timer = &t;
    c.id = channel;
    c.gpio = gpio;
    c.pos = pos;
    c.neg = neg;
    c.prescale = prescale;
    c.level = gpio >= 0 ? hal_pin_level(gpio) : -1;
    c.legacy = fn;
    c.legacy_user = user;
    c.enabled = true;
    adopt_pin_pwm(c);
    return true;
}

void mcpwm_legacy_capture_off(int unit, int channel) {
    mcpwm_cap_channel_t& c = g_cap_chans[unit][channel];
    if (c.pwm_event) cancel_event(c.pwm_event);
    c.pwm_event = 0;
    c.enabled = false;
}

void mcpwm_legacy_capture_gpio(int unit, int channel, int gpio) {
    mcpwm_cap_channel_t& c = g_cap_chans[unit][channel];
    if (gpio >= 0 && (gpio >= 40 || g_pin_pwm_freq[gpio] <= 0)) pinMode((uint8_t)gpio, INPUT);
    if (!c.used) return;
    c.gpio = gpio;
    c.level = hal_pin_level(gpio);
    if (c.enabled) adopt_pin_pwm(c);
}

uint32_t mcpwm_legacy_capture_value(int unit, int channel) { return g_cap_chans[unit][channel].last_value; }
bool mcpwm_legacy_capture_negative(int unit, int channel) { return g_cap_chans[unit][channel].last_negative; }

}  // namespace rw

/* ---------------------------------------------------------------- capture timer */
extern "C" esp_err_t mcpwm_new_capture_timer(const mcpwm_capture_timer_config_t* config, mcpwm_cap_timer_handle_t* ret_cap_timer) {
    charge(cost(10000.0));
    if (!config || !ret_cap_timer) {
        cap_log("mcpwm_new_capture_timer", 90, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    if (config->group_id < 0 || config->group_id > 1) {
        cap_log("mcpwm_new_capture_timer", 92, "invalid group ID:%d", config->group_id);
        return ESP_ERR_INVALID_ARG;
    }

    mcpwm_cap_timer_t& t = g_cap_timers[config->group_id];
    if (t.used) {
        cap_log("mcpwm_capture_timer_register_to_group", 54, "no free cap timer in group (%d)", config->group_id);
        return ESP_ERR_NOT_FOUND;
    }

    t = mcpwm_cap_timer_t{};
    t.used = true;
    t.group = config->group_id;
    t.resolution = kApbHz;   // the classic ESP32's capture timer runs on the APB clock itself
    *ret_cap_timer = &t;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_del_capture_timer(mcpwm_cap_timer_handle_t cap_timer) {
    if (!cap_timer || !cap_timer->used) return ESP_ERR_INVALID_ARG;
    for (auto& c : g_cap_chans[cap_timer->group])
        if (c.used) {
            cap_log("mcpwm_del_capture_timer", 160, "cap channel still in working");
            return ESP_ERR_INVALID_STATE;
        }
    cap_timer->used = false;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_timer_enable(mcpwm_cap_timer_handle_t cap_timer) {
    if (!cap_timer || !cap_timer->used) return ESP_ERR_INVALID_ARG;
    if (cap_timer->enabled) {
        cap_log("mcpwm_capture_timer_enable", 178, "timer already enabled");
        return ESP_ERR_INVALID_STATE;
    }

    cap_timer->enabled = true;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_timer_disable(mcpwm_cap_timer_handle_t cap_timer) {
    if (!cap_timer || !cap_timer->used) return ESP_ERR_INVALID_ARG;
    if (!cap_timer->enabled) {
        cap_log("mcpwm_capture_timer_disable", 193, "timer not in enable state");
        return ESP_ERR_INVALID_STATE;
    }

    cap_timer->enabled = false;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_timer_start(mcpwm_cap_timer_handle_t cap_timer) {
    if (!cap_timer || !cap_timer->used) return ESP_ERR_INVALID_ARG;
    if (!cap_timer->enabled) {
        cap_log("mcpwm_capture_timer_start", 209, "timer not enabled yet");
        return ESP_ERR_INVALID_STATE;
    }

    if (!cap_timer->running) {
        cap_timer->origin_ns = (double)now_ns() - (double)cap_timer->frozen * 1e9 / cap_timer->resolution;
        cap_timer->running = true;
    }

    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_timer_stop(mcpwm_cap_timer_handle_t cap_timer) {
    if (!cap_timer || !cap_timer->used) return ESP_ERR_INVALID_ARG;
    if (cap_timer->running) {
        cap_timer->frozen = count_at(*cap_timer, (double)now_ns());
        cap_timer->running = false;
    }

    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_timer_get_resolution(mcpwm_cap_timer_handle_t cap_timer, uint32_t* out_resolution) {
    if (!cap_timer || !cap_timer->used || !out_resolution) return ESP_ERR_INVALID_ARG;
    *out_resolution = cap_timer->resolution;
    return ESP_OK;
}

/* ---------------------------------------------------------------- capture channels */
extern "C" esp_err_t mcpwm_new_capture_channel(mcpwm_cap_timer_handle_t cap_timer, const mcpwm_capture_channel_config_t* config,
                                              mcpwm_cap_channel_handle_t* ret_cap_channel) {
    charge(cost(10000.0));
    if (!cap_timer || !cap_timer->used || !config || !ret_cap_channel) {
        cap_log("mcpwm_new_capture_channel", 276, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    if (config->prescale > 1 && (config->prescale % 2 != 0 || config->prescale > 256)) {
        cap_log("mcpwm_new_capture_channel", 279, "invalid prescale");
        return ESP_ERR_INVALID_ARG;
    }

    if (!digitalPinIsValid(config->gpio_num)) {
        cap_log("mcpwm_new_capture_channel", 283, "invalid GPIO number");
        return ESP_ERR_INVALID_ARG;
    }

    mcpwm_cap_channel_t* chan = nullptr;
    int id = 0;
    for (; id < 3; id++)
        if (!g_cap_chans[cap_timer->group][id].used) {
            chan = &g_cap_chans[cap_timer->group][id];
            break;
        }
    if (!chan) {
        cap_log("mcpwm_capture_channel_register_to_timer", 241, "no free channel in the timer (%d)", cap_timer->group);
        return ESP_ERR_NOT_FOUND;
    }

    *chan = mcpwm_cap_channel_t{};
    chan->used = true;
    chan->timer = cap_timer;
    chan->id = id;
    chan->gpio = config->gpio_num;
    chan->pos = config->flags.pos_edge;
    chan->neg = config->flags.neg_edge;
    chan->invert = config->flags.invert_cap_signal;
    chan->prescale = config->prescale;
    chan->level = -1;
    // the pad as an input with its pulls; a pin the chip drives itself (io_loop_back, its own PWM) keeps its output
    int lv = hal_pin_level(chan->gpio);
    if (lv >= 0) chan->level = lv;
    if (!config->flags.io_loop_back && rw::g_pin_pwm_freq[chan->gpio] <= 0) {
        uint8_t mode = INPUT;
        if (config->flags.pull_up) mode |= PULLUP;
        if (config->flags.pull_down) mode |= PULLDOWN;
        pinMode((uint8_t)chan->gpio, mode);
    }

    *ret_cap_channel = chan;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_del_capture_channel(mcpwm_cap_channel_handle_t cap_channel) {
    if (!chan_ok(cap_channel)) return ESP_ERR_INVALID_ARG;
    if (cap_channel->pwm_event) cancel_event(cap_channel->pwm_event);
    cap_channel->used = false;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_channel_enable(mcpwm_cap_channel_handle_t cap_channel) {
    if (!chan_ok(cap_channel)) return ESP_ERR_INVALID_ARG;
    if (cap_channel->enabled) {
        cap_log("mcpwm_capture_channel_enable", 359, "channel already enabled");
        return ESP_ERR_INVALID_STATE;
    }

    cap_channel->enabled = true;
    cap_channel->rises = 0;
    cap_channel->prescaled = 0;
    rw::adopt_pin_pwm(*cap_channel);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_channel_disable(mcpwm_cap_channel_handle_t cap_channel) {
    if (!chan_ok(cap_channel)) return ESP_ERR_INVALID_ARG;
    if (!cap_channel->enabled) {
        cap_log("mcpwm_capture_channel_disable", 378, "channel not in enable state");
        return ESP_ERR_INVALID_STATE;
    }

    cap_channel->enabled = false;
    if (cap_channel->pwm_event) cancel_event(cap_channel->pwm_event);
    cap_channel->pwm_event = 0;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_channel_register_event_callbacks(mcpwm_cap_channel_handle_t cap_channel, const mcpwm_capture_event_callbacks_t* cbs, void* user_data) {
    if (!chan_ok(cap_channel) || !cbs) return ESP_ERR_INVALID_ARG;
    if (cap_channel->enabled) {
        cap_log("mcpwm_capture_channel_register_event_callbacks", 400, "channel is enabled already");
        return ESP_ERR_INVALID_STATE;
    }

    cap_channel->on_cap = cbs->on_cap;
    cap_channel->ctx = user_data;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_capture_channel_trigger_soft_catch(mcpwm_cap_channel_handle_t cap_channel) {
    if (!chan_ok(cap_channel)) return ESP_ERR_INVALID_ARG;
    if (!cap_channel->enabled) {
        cap_log("mcpwm_capture_channel_trigger_soft_catch", 436, "channel not enabled yet");
        return ESP_ERR_INVALID_STATE;
    }

    // a software capture reports the edge the hardware's status last held: positive
    catch_at(*cap_channel, MCPWM_CAP_EDGE_POS, (double)now_ns());
    return ESP_OK;
}
