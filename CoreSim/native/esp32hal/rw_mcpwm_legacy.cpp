/*
 * [motor] [legacy] The legacy MCPWM driver (driver/mcpwm.h) in the fast simulation, on the new driver's model
 * (rw_mcpwm.cpp, rw_mcpwm_cap.cpp) as the deprecated IDF driver programs the chip: timer n with operator n, a 1 MHz timer
 * (the group at 10 MHz), the period res / f ticks; counting up-down the driver writes res / f − 1 as the peak, so the
 * period is twice that (a 2 kHz request runs at 1002 Hz, as on the chip). Each output's compare is its duty of res / f,
 * the duty modes are actions (mode 0 active high: high at zero, low at
 * the comparison; mode 1 the inverse; forced low / high), dead time from output A's wave (RED / FED in 100 ns), start
 * at init. Capture: the group's capture timer on the APB clock, the callback with the edge (MCPWM_POS_EDGE /
 * MCPWM_NEG_EDGE) and the value. This file sees only the legacy types: the model's are behind rw_internal.h.
 */
#include "Arduino.h"
#include "driver/mcpwm.h"
#include "rw_internal.h"
#include <stdarg.h>

using namespace rw;

namespace {

const uint32_t kTimerHz = 1000000u;   // the legacy driver's default timer resolution

struct Legacy {
    bool init;
    uint32_t resolution;
    uint32_t frequency;
    uint32_t ticks;        // res / f: what the duty is a share of
    mcpwm_counter_type_t counter;
    float duty[2];
    mcpwm_duty_type_t type[2];
};

Legacy g_legacy[2][3];
int g_cap_gpio[2][3] = {{-1, -1, -1}, {-1, -1, -1}};
cap_isr_cb_t g_cap_cb[2][3];
void* g_cap_user[2][3];

void legacy_log(const char* func, int line, const char* format, ...) {
    char msg[160];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(RW_LOG_ERROR, "E (%lu) mcpwm(legacy): %s(%d): %s", (unsigned long)(uptime_ns() / 1000000ull), func, line, msg);
}

bool ok_unit(int unit) { return unit >= 0 && unit < 2; }
bool ok_timer(int unit, int timer) { return ok_unit(unit) && timer >= 0 && timer < 3; }

int model_mode(mcpwm_counter_type_t c) { return c == MCPWM_DOWN_COUNTER ? 2 : c == MCPWM_UP_DOWN_COUNTER ? 3 : c == MCPWM_UP_COUNTER ? 1 : 0; }

/* What the duty is a share of: res / f ticks (the period register plus one, as the driver reads it back). */
uint32_t timer_peak(int unit, int timer) { return g_legacy[unit][timer].ticks; }

/* The model's period for res / f ticks: that many counting up or down; counting up-down twice the peak the driver writes. */
uint32_t model_period(const Legacy& l) { return l.counter == MCPWM_UP_DOWN_COUNTER ? 2 * (l.ticks - 1) : l.ticks; }

/* The generator's actions for a duty mode, in the model's bits: counting up zero 1:0, peak 3:2, A 5:4, B 7:6; down from 12. */
uint32_t actions_for(mcpwm_counter_type_t counter, mcpwm_duty_type_t type, int gen) {
    const uint32_t kLow = 1, kHigh = 2;
    int up = gen == 0 ? 4 : 6, down = gen == 0 ? 16 : 18;
    uint32_t on = type == MCPWM_DUTY_MODE_1 ? kLow : kHigh, off = type == MCPWM_DUTY_MODE_1 ? kHigh : kLow;
    if (type == MCPWM_DUTY_MODE_FORCE_LOW || type == MCPWM_DUTY_MODE_FORCE_HIGH) {
        uint32_t lv = type == MCPWM_DUTY_MODE_FORCE_HIGH ? kHigh : kLow;
        return lv | lv << 2 | lv << up | lv << 12 | lv << 14 | lv << down;
    }

    switch (counter) {
    case MCPWM_DOWN_COUNTER: return off << 14 | on << down;               // low at the peak, high where the count falls past the compare
    case MCPWM_UP_DOWN_COUNTER: return on | off << up | on << down;       // high at zero, low going up, high again coming down
    default: return on | off << up;                                       // high at zero, low at the compare
    }
}

void apply_duty(int unit, int timer, int gen) {
    Legacy& l = g_legacy[unit][timer];
    uint32_t cmp = (uint32_t)(timer_peak(unit, timer) * l.duty[gen] / 100.0f);
    mcpwm_legacy_compare(unit, timer, gen, cmp);
}

}  // namespace

/* [legacy] the capture channels' interrupt: the legacy callback with its edge and value */
static bool legacy_cap(int unit, int ch, uint32_t value, bool negative, void*) {
    if (!g_cap_cb[unit][ch]) return false;
    cap_event_data_t d = {negative ? MCPWM_NEG_EDGE : MCPWM_POS_EDGE, value};
    return g_cap_cb[unit][ch]((mcpwm_unit_t)unit, (mcpwm_capture_channel_id_t)ch, &d, g_cap_user[unit][ch]);
}

namespace rw {
void hal_mcpwm_legacy_reset() {
    memset(g_legacy, 0, sizeof g_legacy);
    for (auto& row : g_cap_gpio)
        for (auto& g : row) g = -1;
    memset(g_cap_cb, 0, sizeof g_cap_cb);
    memset(g_cap_user, 0, sizeof g_cap_user);
}
}  // namespace rw

extern "C" esp_err_t mcpwm_gpio_init(mcpwm_unit_t mcpwm_num, mcpwm_io_signals_t io_signal, int gpio_num) {
    charge(cost(5000.0));
    if (!ok_unit(mcpwm_num)) {
        legacy_log("mcpwm_gpio_init", 117, "MCPWM group num error");
        return ESP_ERR_INVALID_ARG;
    }

    if (!digitalPinIsValid(gpio_num)) {
        legacy_log("mcpwm_gpio_init", 118, "MCPWM GPIO NUM ERROR");
        return ESP_ERR_INVALID_ARG;
    }

    if (io_signal <= MCPWM2B) {
        if (gpio_num >= 34) {
            legacy_log("mcpwm_gpio_init", 121, "MCPWM GPIO NUM ERROR");
            return ESP_ERR_INVALID_ARG;
        }

        mcpwm_legacy_pin(mcpwm_num, io_signal / 2, io_signal % 2, gpio_num);
    } else if (io_signal >= MCPWM_CAP_0 && io_signal <= MCPWM_CAP_2) {
        int ch = io_signal - MCPWM_CAP_0;
        g_cap_gpio[mcpwm_num][ch] = gpio_num;
        mcpwm_legacy_capture_gpio(mcpwm_num, ch, gpio_num);
    } else {
        pinMode((uint8_t)gpio_num, INPUT);   // a sync or fault input: routed, not acted on
    }

    return ESP_OK;
}

extern "C" esp_err_t mcpwm_set_pin(mcpwm_unit_t mcpwm_num, const mcpwm_pin_config_t* p) {
    if (!p) return ESP_ERR_INVALID_ARG;
    const int pins[] = {p->mcpwm0a_out_num, p->mcpwm0b_out_num, p->mcpwm1a_out_num, p->mcpwm1b_out_num, p->mcpwm2a_out_num, p->mcpwm2b_out_num,
                        p->mcpwm_sync0_in_num, p->mcpwm_sync1_in_num, p->mcpwm_sync2_in_num, p->mcpwm_fault0_in_num, p->mcpwm_fault1_in_num, p->mcpwm_fault2_in_num};
    for (int i = 0; i < 12; i++)
        if (pins[i] >= 0) mcpwm_gpio_init(mcpwm_num, (mcpwm_io_signals_t)i, pins[i]);
    const int caps[] = {p->mcpwm_cap0_in_num, p->mcpwm_cap1_in_num, p->mcpwm_cap2_in_num};
    for (int i = 0; i < 3; i++)
        if (caps[i] >= 0) mcpwm_gpio_init(mcpwm_num, (mcpwm_io_signals_t)(MCPWM_CAP_0 + i), caps[i]);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_init(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, const mcpwm_config_t* conf) {
    charge(cost(20000.0));
    if (!ok_timer(mcpwm_num, timer_num) || !conf) {
        legacy_log("mcpwm_init", 263, "MCPWM TIMER ERROR");
        return ESP_ERR_INVALID_ARG;
    }

    Legacy& l = g_legacy[mcpwm_num][timer_num];
    if (!l.resolution) l.resolution = kTimerHz;
    if (conf->frequency == 0 || conf->frequency > l.resolution / 2) {
        legacy_log("mcpwm_init", 271, "invalid frequency");
        return ESP_ERR_INVALID_ARG;
    }

    l.init = true;
    l.frequency = conf->frequency;
    l.counter = conf->counter_mode;
    l.duty[0] = conf->cmpr_a;
    l.duty[1] = conf->cmpr_b;
    l.type[0] = l.type[1] = conf->duty_mode;
    l.ticks = l.resolution / l.frequency;
    mcpwm_legacy_config(mcpwm_num, timer_num, l.resolution, model_mode(l.counter), model_period(l));
    for (int g = 0; g < 2; g++) {
        mcpwm_legacy_actions(mcpwm_num, timer_num, g, actions_for(l.counter, l.type[g], g));
        apply_duty(mcpwm_num, timer_num, g);
    }

    mcpwm_legacy_run(mcpwm_num, timer_num, true);   // the legacy init starts the timer
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_group_set_resolution(mcpwm_unit_t mcpwm_num, unsigned long int resolution) {
    if (!ok_unit(mcpwm_num) || resolution == 0) return ESP_ERR_INVALID_ARG;
    return ESP_OK;   // the timers' own resolution sets their tick
}

extern "C" esp_err_t mcpwm_timer_set_resolution(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, unsigned long int resolution) {
    if (!ok_timer(mcpwm_num, timer_num) || resolution == 0) return ESP_ERR_INVALID_ARG;
    g_legacy[mcpwm_num][timer_num].resolution = (uint32_t)resolution;
    return ESP_OK;
}

static bool need_init(int unit, int timer, const char* func, int line) {
    if (ok_timer(unit, timer) && g_legacy[unit][timer].init) return true;
    legacy_log(func, line, "MCPWM TIMER ERROR");
    return false;
}

extern "C" esp_err_t mcpwm_set_frequency(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, uint32_t frequency) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_set_frequency", 307) || frequency == 0) return ESP_ERR_INVALID_ARG;
    Legacy& l = g_legacy[mcpwm_num][timer_num];
    l.frequency = frequency;
    l.ticks = l.resolution / frequency;
    mcpwm_legacy_config(mcpwm_num, timer_num, l.resolution, model_mode(l.counter), model_period(l));
    for (int g = 0; g < 2; g++) apply_duty(mcpwm_num, timer_num, g);   // the same duty of the new period
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_set_duty(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen, float duty) {
    charge(cost(500.0));
    if (!need_init(mcpwm_num, timer_num, "mcpwm_set_duty", 330) || gen < 0 || gen > 1) return ESP_ERR_INVALID_ARG;
    g_legacy[mcpwm_num][timer_num].duty[gen] = duty < 0 ? 0 : duty > 100 ? 100 : duty;
    apply_duty(mcpwm_num, timer_num, gen);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_set_duty_in_us(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen, uint32_t duty_in_us) {
    charge(cost(500.0));
    if (!need_init(mcpwm_num, timer_num, "mcpwm_set_duty_in_us", 347) || gen < 0 || gen > 1) return ESP_ERR_INVALID_ARG;
    Legacy& l = g_legacy[mcpwm_num][timer_num];
    uint32_t cmp = (uint32_t)((uint64_t)duty_in_us * l.resolution / 1000000u);
    mcpwm_legacy_compare(mcpwm_num, timer_num, gen, cmp);
    uint32_t peak = timer_peak(mcpwm_num, timer_num);
    l.duty[gen] = peak ? 100.0f * cmp / peak : 0;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_set_duty_type(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen, mcpwm_duty_type_t duty_type) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_set_duty_type", 364) || gen < 0 || gen > 1 || duty_type >= MCPWM_DUTY_MODE_MAX) return ESP_ERR_INVALID_ARG;
    Legacy& l = g_legacy[mcpwm_num][timer_num];
    l.type[gen] = duty_type;
    mcpwm_legacy_actions(mcpwm_num, timer_num, gen, actions_for(l.counter, duty_type, gen));
    return ESP_OK;
}

extern "C" uint32_t mcpwm_get_frequency(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_get_frequency", 408)) return 0;
    uint32_t ticks = timer_peak(mcpwm_num, timer_num);
    return ticks ? g_legacy[mcpwm_num][timer_num].resolution / ticks : 0;
}

extern "C" float mcpwm_get_duty(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_operator_t gen) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_get_duty", 419) || gen < 0 || gen > 1) return 0;
    uint32_t peak = timer_peak(mcpwm_num, timer_num);
    return peak ? 100.0f * mcpwm_legacy_compare_value(mcpwm_num, timer_num, gen) / peak : 0;
}

extern "C" uint32_t mcpwm_get_duty_in_us(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_operator_t gen) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_get_duty_in_us", 430) || gen < 0 || gen > 1) return 0;
    return (uint32_t)((uint64_t)mcpwm_legacy_compare_value(mcpwm_num, timer_num, gen) * 1000000u / g_legacy[mcpwm_num][timer_num].resolution);
}

extern "C" esp_err_t mcpwm_set_signal_high(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen) {
    return mcpwm_set_duty_type(mcpwm_num, timer_num, gen, MCPWM_DUTY_MODE_FORCE_HIGH);
}

extern "C" esp_err_t mcpwm_set_signal_low(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen) {
    return mcpwm_set_duty_type(mcpwm_num, timer_num, gen, MCPWM_DUTY_MODE_FORCE_LOW);
}

extern "C" esp_err_t mcpwm_start(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_start", 451)) return ESP_ERR_INVALID_ARG;
    mcpwm_legacy_run(mcpwm_num, timer_num, true);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_stop(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_stop", 462)) return ESP_ERR_INVALID_ARG;
    mcpwm_legacy_run(mcpwm_num, timer_num, false);   // stops at the next empty, its outputs as that event left them
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_deadtime_enable(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_deadtime_type_t dt_mode, uint32_t red, uint32_t fed) {
    if (!need_init(mcpwm_num, timer_num, "mcpwm_deadtime_enable", 548) || dt_mode >= MCPWM_DEADTIME_TYPE_MAX) return ESP_ERR_INVALID_ARG;
    double r = red * 100e-9, f = fed * 100e-9;   // in the 10 MHz group clock's 100 ns
    int u = mcpwm_num, t = timer_num;
    switch (dt_mode) {
    case MCPWM_DEADTIME_BYPASS:
        mcpwm_legacy_dead_time(u, t, 0, 0, 0, 0, false, false);
        mcpwm_legacy_dead_time(u, t, 1, 1, 0, 0, false, false);
        break;
    case MCPWM_BYPASS_RED:   // A as it is, B A's wave with the falling edge delayed
        mcpwm_legacy_dead_time(u, t, 0, 0, 0, 0, false, false);
        mcpwm_legacy_dead_time(u, t, 1, 0, 0, f, false, true);
        break;
    case MCPWM_BYPASS_FED:   // A with the rising edge delayed, B as it is
        mcpwm_legacy_dead_time(u, t, 0, 0, r, 0, false, true);
        mcpwm_legacy_dead_time(u, t, 1, 1, 0, 0, false, false);
        break;
    case MCPWM_ACTIVE_HIGH_MODE:
        mcpwm_legacy_dead_time(u, t, 0, 0, r, 0, false, true);
        mcpwm_legacy_dead_time(u, t, 1, 0, 0, f, false, true);
        break;
    case MCPWM_ACTIVE_LOW_MODE:
        mcpwm_legacy_dead_time(u, t, 0, 0, r, 0, true, true);
        mcpwm_legacy_dead_time(u, t, 1, 0, 0, f, true, true);
        break;
    case MCPWM_ACTIVE_HIGH_COMPLIMENT_MODE:   // a half bridge: A delayed rising, B the inverse delayed falling
        mcpwm_legacy_dead_time(u, t, 0, 0, r, 0, false, true);
        mcpwm_legacy_dead_time(u, t, 1, 0, 0, f, true, true);
        break;
    case MCPWM_ACTIVE_LOW_COMPLIMENT_MODE:
        mcpwm_legacy_dead_time(u, t, 0, 0, r, 0, true, true);
        mcpwm_legacy_dead_time(u, t, 1, 0, 0, f, false, true);
        break;
    case MCPWM_ACTIVE_RED_FED_FROM_PWMXA:   // both outputs: A's wave with both edges delayed
        mcpwm_legacy_dead_time(u, t, 0, 0, r, f, false, true);
        mcpwm_legacy_dead_time(u, t, 1, 0, r, f, false, true);
        break;
    case MCPWM_ACTIVE_RED_FED_FROM_PWMXB:
        mcpwm_legacy_dead_time(u, t, 0, 1, r, f, false, true);
        mcpwm_legacy_dead_time(u, t, 1, 1, r, f, false, true);
        break;
    default:
        break;
    }

    return ESP_OK;
}

extern "C" esp_err_t mcpwm_deadtime_disable(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num) {
    return mcpwm_deadtime_enable(mcpwm_num, timer_num, MCPWM_DEADTIME_BYPASS, 0, 0);
}

/* ---------------------------------------------------------------- capture */
extern "C" esp_err_t mcpwm_capture_enable_channel(mcpwm_unit_t mcpwm_num, mcpwm_capture_channel_id_t cap_channel, const mcpwm_capture_config_t* conf) {
    charge(cost(10000.0));
    if (!ok_unit(mcpwm_num) || cap_channel < 0 || cap_channel > 2 || !conf) {
        legacy_log("mcpwm_capture_enable_channel", 726, "MCPWM CAPTURE ERROR");
        return ESP_ERR_INVALID_ARG;
    }

    g_cap_cb[mcpwm_num][cap_channel] = conf->capture_cb;
    g_cap_user[mcpwm_num][cap_channel] = conf->user_data;
    bool pos = (conf->cap_edge & MCPWM_POS_EDGE) != 0, neg = (conf->cap_edge & MCPWM_NEG_EDGE) != 0;
    return mcpwm_legacy_capture(mcpwm_num, cap_channel, g_cap_gpio[mcpwm_num][cap_channel], pos, neg, conf->cap_prescale, legacy_cap, nullptr)
               ? ESP_OK : ESP_ERR_INVALID_ARG;
}

extern "C" esp_err_t mcpwm_capture_disable_channel(mcpwm_unit_t mcpwm_num, mcpwm_capture_channel_id_t cap_channel) {
    if (!ok_unit(mcpwm_num) || cap_channel < 0 || cap_channel > 2) return ESP_ERR_INVALID_ARG;
    mcpwm_legacy_capture_off(mcpwm_num, cap_channel);
    return ESP_OK;
}

extern "C" uint32_t mcpwm_capture_signal_get_value(mcpwm_unit_t mcpwm_num, mcpwm_capture_signal_t cap_sig) {
    if (!ok_unit(mcpwm_num) || cap_sig < 0 || cap_sig > 2) return 0;
    return mcpwm_legacy_capture_value(mcpwm_num, cap_sig);
}

extern "C" uint32_t mcpwm_capture_get_resolution(mcpwm_unit_t mcpwm_num) { return ok_unit(mcpwm_num) ? 80000000u : 0; }

extern "C" uint32_t mcpwm_capture_signal_get_edge(mcpwm_unit_t mcpwm_num, mcpwm_capture_signal_t cap_sig) {
    if (!ok_unit(mcpwm_num) || cap_sig < 0 || cap_sig > 2) return 0;
    return mcpwm_legacy_capture_negative(mcpwm_num, cap_sig) ? 2u : 1u;   // 1 positive, 2 negative
}
