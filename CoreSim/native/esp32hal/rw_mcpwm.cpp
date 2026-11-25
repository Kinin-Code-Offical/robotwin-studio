/*
 * [motor] ESP-IDF's MCPWM driver in the fast simulation, as the classic ESP32's motor-control PWM works: two groups of
 * three timers (resolution, period, up / down / up-down counting, start and stop commands), three operators each with two
 * comparators and two generators. A generator's actions on its timer's zero and peak and on the comparisons (per counting
 * direction, the chip's priority B > A > peak > zero on a tie) make its wave; dead time delays the rising and falling
 * edges of a generator's wave into another (and inverts it), a software force holds a level. Each generator's pin gets the
 * result through the host's PWM output: frequency and duty worked out from one period (two when a toggle doubles it), the
 * same arithmetic as the Xtensa emulator's MCPWM (Esp32Soc.Mcpwm.cs).
 */
#include "Arduino.h"
#include "driver/mcpwm_prelude.h"
#include "rw_internal.h"
#include <stdarg.h>

using namespace rw;

struct mcpwm_timer_t {
    bool used;
    int group;
    uint32_t resolution;
    mcpwm_timer_count_mode_t mode;
    uint32_t period_ticks;
    bool enabled;
    bool running;
    bool stopped_full;
};

struct mcpwm_cmpr_t {
    mcpwm_oper_t* oper;
    bool used;
    uint32_t value;
};

struct mcpwm_gen_t {
    mcpwm_oper_t* oper;
    bool used;
    int id;
    int gpio;
    bool invert;
    uint32_t actions;      // as GENn_A/B: up zero 1:0, peak 3:2, A 5:4, B 7:6; down from bit 12
    int force;             // -1 none, 0 low, 1 high
    bool dead_time;        // this output is another generator's wave through the dead-time module
    mcpwm_gen_t* dt_in;
    uint32_t red;
    uint32_t fed;
    bool dt_invert;
};

struct mcpwm_oper_t {
    bool used;
    int group;
    mcpwm_timer_t* timer;
    mcpwm_cmpr_t cmprs[2];
    mcpwm_gen_t gens[2];
};

namespace {

mcpwm_timer_t g_timers[2][3];
mcpwm_oper_t g_opers[2][3];

struct Wave {
    double frequency;   // 0: a constant level
    double duty;
};

void mcpwm_log(const char* func, int line, const char* format, ...) {
    char msg[160];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(RW_LOG_ERROR, "E (%lu) mcpwm: %s(%d): %s", (unsigned long)(uptime_ns() / 1000000ull), func, line, msg);
}

Wave constant(bool level) { return Wave{0, level ? 1.0 : 0.0}; }

int apply(int level, int action) { return action == 1 ? 0 : action == 2 ? 1 : action == 3 ? !level : level; }

/* One generator's wave from its actions over its timer's period. */
Wave generator_wave(const mcpwm_gen_t& g) {
    const mcpwm_timer_t* t = g.oper->timer;
    if (!t) return constant(false);
    int mode = t->mode;
    int peak = mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN ? (int)t->period_ticks / 2 : (int)t->period_ticks - 1;
    if (peak <= 0) return constant(false);
    int cmpA = g.oper->cmprs[0].used ? (int)g.oper->cmprs[0].value : 0x7FFFFFFF;
    int cmpB = g.oper->cmprs[1].used ? (int)g.oper->cmprs[1].value : 0x7FFFFFFF;
    auto act = [&](int shift) { return (int)((g.actions >> shift) & 3); };
    struct Ev { double tick; int prio; int action; };
    Ev ev[8];
    int n = 0;
    double length;
    if (mode == MCPWM_TIMER_COUNT_MODE_DOWN) {
        length = peak + 1;
        ev[n++] = {0, 1, act(14)};
        if (cmpA <= peak) ev[n++] = {(double)(peak - cmpA), 2, act(16)};
        if (cmpB <= peak) ev[n++] = {(double)(peak - cmpB), 3, act(18)};
        ev[n++] = {(double)peak, 0, act(12)};
    } else if (mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN) {
        length = 2.0 * peak;
        ev[n++] = {0, 0, act(0) ? act(0) : act(12)};
        if (cmpA < peak) ev[n++] = {(double)cmpA, 2, act(4)};
        if (cmpB < peak) ev[n++] = {(double)cmpB, 3, act(6)};
        ev[n++] = {(double)peak, 1, act(14) ? act(14) : act(2)};
        if (cmpA < peak && cmpA > 0) ev[n++] = {2.0 * peak - cmpA, 2, act(16)};
        if (cmpB < peak && cmpB > 0) ev[n++] = {2.0 * peak - cmpB, 3, act(18)};
    } else {
        length = peak + 1;
        ev[n++] = {0, 0, act(0)};
        if (cmpA <= peak) ev[n++] = {(double)cmpA, 2, act(4)};
        if (cmpB <= peak) ev[n++] = {(double)cmpB, 3, act(6)};
        ev[n++] = {(double)peak, 1, act(2)};
    }

    // By tick, the higher priority last (it wins a tie).
    for (int i = 1; i < n; i++)
        for (int j = i; j > 0 && (ev[j].tick < ev[j - 1].tick || (ev[j].tick == ev[j - 1].tick && ev[j].prio < ev[j - 1].prio)); j--) {
            Ev x = ev[j];
            ev[j] = ev[j - 1];
            ev[j - 1] = x;
        }

    bool running = t->enabled && t->running && mode != MCPWM_TIMER_COUNT_MODE_PAUSE;
    if (!running) {
        int level = 0;
        for (int i = 0; i < n; i++) level = apply(level, ev[i].action);
        double stop_tick = (t->stopped_full ? (mode == MCPWM_TIMER_COUNT_MODE_DOWN ? 0 : peak) : (mode == MCPWM_TIMER_COUNT_MODE_DOWN ? peak : 0));
        for (int i = 0; i < n; i++) {
            level = apply(level, ev[i].action);
            if (ev[i].tick == stop_tick) break;
        }

        return constant(level != 0);
    }

    int lv = 0;
    for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < n; i++) lv = apply(lv, ev[i].action);
    int start = lv, periods = 1;
    double high = 0;
    for (int pass = 0; pass < 2; pass++) {
        double last = 0;
        for (int i = 0; i < n; i++) {
            if (lv) high += ev[i].tick - last;
            last = ev[i].tick;
            lv = apply(lv, ev[i].action);
        }

        if (lv) high += length - last;
        if (pass == 0 && lv == start) break;
        periods = 2;
    }

    double duty = high / (length * periods);
    if (duty <= 1e-9) return constant(false);
    if (duty >= 1 - 1e-9) return constant(true);
    return Wave{(double)t->resolution / (length * periods), duty};
}

Wave delayed(Wave w, double delay_s, bool rising) {
    if (w.frequency <= 0 || delay_s <= 0) return w;
    double shift = delay_s * w.frequency;
    double duty = rising ? w.duty - shift : w.duty + shift;
    if (duty <= 0) return constant(false);
    if (duty >= 1) return constant(true);
    return Wave{w.frequency, duty};
}

Wave inverted(Wave w) { return Wave{w.frequency, 1.0 - w.duty}; }

/* What a generator's pin carries: its own wave or another's through dead time, a force, the GPIO's inversion. */
Wave output_wave(const mcpwm_gen_t& g) {
    Wave w;
    if (g.force >= 0) {
        w = constant(g.force != 0);
    } else if (g.dead_time && g.dt_in) {
        const mcpwm_timer_t* t = g.oper->timer;
        double tick = t && t->resolution ? 1.0 / t->resolution : 0;
        w = generator_wave(*g.dt_in);
        if (g.red) w = delayed(w, g.red * tick, true);
        if (g.fed) w = delayed(w, g.fed * tick, false);
        if (g.dt_invert) w = inverted(w);
    } else {
        w = generator_wave(g);
    }

    return g.invert ? inverted(w) : w;
}

/* Every generator pin of a group gets its wave now. */
void update(int group) {
    for (auto& op : g_opers[group]) {
        if (!op.used) continue;
        for (auto& g : op.gens) {
            if (!g.used || g.gpio < 0) continue;
            Wave w = output_wave(g);
            hal_pwm_out(g.gpio, w.frequency > 0 ? w.frequency : 1000.0, w.duty);
        }
    }
}

bool gen_ok(mcpwm_gen_handle_t g) { return g && g->used && g->oper && g->oper->used; }

}  // namespace

namespace rw {
void hal_mcpwm_reset() {
    memset(g_timers, 0, sizeof g_timers);
    memset(g_opers, 0, sizeof g_opers);
}
}  // namespace rw

/* ---------------------------------------------------------------- timers */
extern "C" esp_err_t mcpwm_new_timer(const mcpwm_timer_config_t* config, mcpwm_timer_handle_t* ret_timer) {
    charge(cost(10000.0));
    if (!config || !ret_timer || config->group_id < 0 || config->group_id > 1) return ESP_ERR_INVALID_ARG;
    if (config->period_ticks < 2 || config->resolution_hz == 0 || config->resolution_hz > 160000000u) {
        mcpwm_log("mcpwm_new_timer", 170, "invalid period ticks or resolution");
        return ESP_ERR_INVALID_ARG;
    }

    uint32_t peak = config->count_mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN ? config->period_ticks / 2 : config->period_ticks;
    if (peak > 65535u) {
        mcpwm_log("mcpwm_new_timer", 175, "invalid period ticks");
        return ESP_ERR_INVALID_ARG;
    }

    for (auto& t : g_timers[config->group_id]) {
        if (t.used) continue;
        t = mcpwm_timer_t{};
        t.used = true;
        t.group = config->group_id;
        t.resolution = config->resolution_hz;
        t.mode = config->count_mode;
        t.period_ticks = config->period_ticks;
        *ret_timer = &t;
        return ESP_OK;
    }

    mcpwm_log("mcpwm_timer_register_to_group", 52, "no free timer in group (%d)", config->group_id);
    return ESP_ERR_NOT_FOUND;
}

extern "C" esp_err_t mcpwm_del_timer(mcpwm_timer_handle_t timer) {
    if (!timer || !timer->used) return ESP_ERR_INVALID_ARG;
    if (timer->enabled) {
        mcpwm_log("mcpwm_del_timer", 213, "timer not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    int group = timer->group;
    *timer = mcpwm_timer_t{};
    update(group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_set_period(mcpwm_timer_handle_t timer, uint32_t period_ticks) {
    if (!timer || !timer->used || period_ticks < 2) return ESP_ERR_INVALID_ARG;
    timer->period_ticks = period_ticks;
    update(timer->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_enable(mcpwm_timer_handle_t timer) {
    if (!timer || !timer->used) return ESP_ERR_INVALID_ARG;
    if (timer->enabled) {
        mcpwm_log("mcpwm_timer_enable", 249, "timer not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    timer->enabled = true;
    update(timer->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_disable(mcpwm_timer_handle_t timer) {
    if (!timer || !timer->used) return ESP_ERR_INVALID_ARG;
    if (!timer->enabled) {
        mcpwm_log("mcpwm_timer_disable", 268, "timer not in enable state");
        return ESP_ERR_INVALID_STATE;
    }

    timer->enabled = false;
    update(timer->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_start_stop(mcpwm_timer_handle_t timer, mcpwm_timer_start_stop_cmd_t command) {
    if (!timer || !timer->used) return ESP_ERR_INVALID_ARG;
    if (!timer->enabled) {
        mcpwm_log("mcpwm_timer_start_stop", 286, "timer not enabled yet");
        return ESP_ERR_INVALID_STATE;
    }

    switch (command) {
    case MCPWM_TIMER_STOP_EMPTY: timer->running = false; timer->stopped_full = false; break;
    case MCPWM_TIMER_STOP_FULL: timer->running = false; timer->stopped_full = true; break;
    default: timer->running = true; break;   // free running, or one more period (counted as running)
    }

    update(timer->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_register_event_callbacks(mcpwm_timer_handle_t timer, const mcpwm_timer_event_callbacks_t*, void*) {
    if (!timer || !timer->used) return ESP_ERR_INVALID_ARG;
    log_once(0xA00, RW_LOG_WARN, "W (%lu) mcpwm: timer event callbacks are not simulated (the PWM outputs are)",
             (unsigned long)(uptime_ns() / 1000000ull));
    return ESP_OK;
}

/* ---------------------------------------------------------------- operators */
extern "C" esp_err_t mcpwm_new_operator(const mcpwm_operator_config_t* config, mcpwm_oper_handle_t* ret_oper) {
    charge(cost(8000.0));
    if (!config || !ret_oper || config->group_id < 0 || config->group_id > 1) return ESP_ERR_INVALID_ARG;
    for (auto& op : g_opers[config->group_id]) {
        if (op.used) continue;
        op = mcpwm_oper_t{};
        op.used = true;
        op.group = config->group_id;
        *ret_oper = &op;
        return ESP_OK;
    }

    mcpwm_log("mcpwm_operator_register_to_group", 52, "no free operator in group (%d)", config->group_id);
    return ESP_ERR_NOT_FOUND;
}

extern "C" esp_err_t mcpwm_del_operator(mcpwm_oper_handle_t oper) {
    if (!oper || !oper->used) return ESP_ERR_INVALID_ARG;
    for (auto& c : oper->cmprs)
        if (c.used) {
            mcpwm_log("mcpwm_del_operator", 170, "comparator still in working");
            return ESP_ERR_INVALID_STATE;
        }
    for (auto& g : oper->gens)
        if (g.used) {
            mcpwm_log("mcpwm_del_operator", 173, "generator still in working");
            return ESP_ERR_INVALID_STATE;
        }
    *oper = mcpwm_oper_t{};
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_operator_connect_timer(mcpwm_oper_handle_t oper, mcpwm_timer_handle_t timer) {
    if (!oper || !oper->used || !timer || !timer->used) return ESP_ERR_INVALID_ARG;
    if (oper->group != timer->group) {
        mcpwm_log("mcpwm_operator_connect_timer", 193, "operator and timer should reside in the same group");
        return ESP_ERR_INVALID_ARG;
    }

    oper->timer = timer;
    update(oper->group);
    return ESP_OK;
}

/* ---------------------------------------------------------------- comparators */
extern "C" esp_err_t mcpwm_new_comparator(mcpwm_oper_handle_t oper, const mcpwm_comparator_config_t*, mcpwm_cmpr_handle_t* ret_cmpr) {
    if (!oper || !oper->used || !ret_cmpr) return ESP_ERR_INVALID_ARG;
    for (auto& c : oper->cmprs) {
        if (c.used) continue;
        c = mcpwm_cmpr_t{};
        c.oper = oper;
        c.used = true;
        *ret_cmpr = &c;
        return ESP_OK;
    }

    mcpwm_log("mcpwm_comparator_register_to_operator", 46, "no free comparator in operator");
    return ESP_ERR_NOT_FOUND;
}

extern "C" esp_err_t mcpwm_del_comparator(mcpwm_cmpr_handle_t cmpr) {
    if (!cmpr || !cmpr->used) return ESP_ERR_INVALID_ARG;
    int group = cmpr->oper->group;
    cmpr->used = false;
    update(group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_comparator_set_compare_value(mcpwm_cmpr_handle_t cmpr, uint32_t cmp_ticks) {
    charge(cost(300.0));
    if (!cmpr || !cmpr->used) return ESP_ERR_INVALID_ARG;
    const mcpwm_timer_t* t = cmpr->oper->timer;
    if (!t) {
        mcpwm_log("mcpwm_comparator_set_compare_value", 135, "timer and operator are not connected");
        return ESP_ERR_INVALID_STATE;
    }

    uint32_t peak = t->mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN ? t->period_ticks / 2 : t->period_ticks;
    if (cmp_ticks > peak) {
        mcpwm_log("mcpwm_comparator_set_compare_value", 137, "compare value out of range");
        return ESP_ERR_INVALID_ARG;
    }

    cmpr->value = cmp_ticks;
    update(cmpr->oper->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_comparator_register_event_callbacks(mcpwm_cmpr_handle_t cmpr, const mcpwm_comparator_event_callbacks_t*, void*) {
    if (!cmpr || !cmpr->used) return ESP_ERR_INVALID_ARG;
    log_once(0xA01, RW_LOG_WARN, "W (%lu) mcpwm: comparator event callbacks are not simulated (the PWM outputs are)",
             (unsigned long)(uptime_ns() / 1000000ull));
    return ESP_OK;
}

/* ---------------------------------------------------------------- generators */
extern "C" esp_err_t mcpwm_new_generator(mcpwm_oper_handle_t oper, const mcpwm_generator_config_t* config, mcpwm_gen_handle_t* ret_gen) {
    charge(cost(8000.0));
    if (!oper || !oper->used || !config || !ret_gen) return ESP_ERR_INVALID_ARG;
    if (config->gen_gpio_num < 0 || config->gen_gpio_num >= 34) {
        mcpwm_log("mcpwm_new_generator", 73, "invalid GPIO number %d", config->gen_gpio_num);
        return ESP_ERR_INVALID_ARG;
    }

    for (int i = 0; i < 2; i++) {
        mcpwm_gen_t& g = oper->gens[i];
        if (g.used) continue;
        g = mcpwm_gen_t{};
        g.oper = oper;
        g.used = true;
        g.id = i;
        g.gpio = config->gen_gpio_num;
        g.invert = config->flags.invert_pwm != 0;
        g.force = -1;
        uint8_t mode = config->flags.io_od_mode ? OUTPUT_OPEN_DRAIN : OUTPUT;
        if (config->flags.pull_up) mode |= PULLUP;
        if (config->flags.pull_down) mode |= PULLDOWN;
        pinMode((uint8_t)g.gpio, mode);
        *ret_gen = &g;
        update(oper->group);
        return ESP_OK;
    }

    mcpwm_log("mcpwm_generator_register_to_operator", 48, "no free generator in operator");
    return ESP_ERR_NOT_FOUND;
}

extern "C" esp_err_t mcpwm_del_generator(mcpwm_gen_handle_t gen) {
    if (!gen_ok(gen)) return ESP_ERR_INVALID_ARG;
    hal_pwm_out(gen->gpio, 1000.0, -1.0);   // the pin leaves the MCPWM
    gen->used = false;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_generator_set_force_level(mcpwm_gen_handle_t gen, int level, bool hold_on) {
    if (!gen_ok(gen) || level < -1 || level > 1) return ESP_ERR_INVALID_ARG;
    // A force that does not hold lasts until the generator's next action: while its timer runs, within a period.
    const mcpwm_timer_t* t = gen->oper->timer;
    bool running = t && t->enabled && t->running;
    gen->force = level < 0 || (!hold_on && running) ? -1 : level;
    update(gen->oper->group);
    return ESP_OK;
}

static esp_err_t timer_action(mcpwm_gen_handle_t gen, mcpwm_gen_timer_event_action_t ev) {
    if (ev.event == MCPWM_TIMER_EVENT_INVALID) return ESP_ERR_INVALID_ARG;
    int shift = (ev.event == MCPWM_TIMER_EVENT_EMPTY ? 0 : 2) + (ev.direction == MCPWM_TIMER_DIRECTION_DOWN ? 12 : 0);
    gen->actions = (gen->actions & ~(3u << shift)) | ((uint32_t)ev.action & 3u) << shift;
    return ESP_OK;
}

static esp_err_t compare_action(mcpwm_gen_handle_t gen, mcpwm_gen_compare_event_action_t ev) {
    if (!ev.comparator || ev.comparator->oper != gen->oper) {
        mcpwm_log("mcpwm_generator_set_action_on_compare_event", 213, "generator and comparator should reside in the same operator");
        return ESP_ERR_INVALID_ARG;
    }

    int cmp = (int)(ev.comparator - gen->oper->cmprs);
    int shift = 4 + 2 * cmp + (ev.direction == MCPWM_TIMER_DIRECTION_DOWN ? 12 : 0);
    gen->actions = (gen->actions & ~(3u << shift)) | ((uint32_t)ev.action & 3u) << shift;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_generator_set_action_on_timer_event(mcpwm_gen_handle_t gen, mcpwm_gen_timer_event_action_t ev_act) {
    if (!gen_ok(gen)) return ESP_ERR_INVALID_ARG;
    esp_err_t rc = timer_action(gen, ev_act);
    update(gen->oper->group);
    return rc;
}

extern "C" esp_err_t mcpwm_generator_set_actions_on_timer_event(mcpwm_gen_handle_t gen, mcpwm_gen_timer_event_action_t ev_act, ...) {
    if (!gen_ok(gen)) return ESP_ERR_INVALID_ARG;
    va_list ap;
    va_start(ap, ev_act);
    for (mcpwm_gen_timer_event_action_t e = ev_act; e.event != MCPWM_TIMER_EVENT_INVALID; e = va_arg(ap, mcpwm_gen_timer_event_action_t))
        timer_action(gen, e);
    va_end(ap);
    update(gen->oper->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_generator_set_action_on_compare_event(mcpwm_gen_handle_t gen, mcpwm_gen_compare_event_action_t ev_act) {
    if (!gen_ok(gen)) return ESP_ERR_INVALID_ARG;
    esp_err_t rc = compare_action(gen, ev_act);
    update(gen->oper->group);
    return rc;
}

extern "C" esp_err_t mcpwm_generator_set_actions_on_compare_event(mcpwm_gen_handle_t gen, mcpwm_gen_compare_event_action_t ev_act, ...) {
    if (!gen_ok(gen)) return ESP_ERR_INVALID_ARG;
    va_list ap;
    va_start(ap, ev_act);
    for (mcpwm_gen_compare_event_action_t e = ev_act; e.comparator != NULL; e = va_arg(ap, mcpwm_gen_compare_event_action_t))
        compare_action(gen, e);
    va_end(ap);
    update(gen->oper->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_generator_set_dead_time(mcpwm_gen_handle_t in_generator, mcpwm_gen_handle_t out_generator, const mcpwm_dead_time_config_t* config) {
    if (!gen_ok(in_generator) || !gen_ok(out_generator) || !config) return ESP_ERR_INVALID_ARG;
    if (in_generator->oper != out_generator->oper) {
        mcpwm_log("mcpwm_generator_set_dead_time", 287, "in/out generator are not derived from the same operator");
        return ESP_ERR_INVALID_ARG;
    }

    bool bypass = !config->posedge_delay_ticks && !config->negedge_delay_ticks;
    out_generator->dead_time = !bypass || config->flags.invert_output;
    out_generator->dt_in = in_generator;
    out_generator->red = config->posedge_delay_ticks;
    out_generator->fed = config->negedge_delay_ticks;
    out_generator->dt_invert = config->flags.invert_output != 0;
    update(out_generator->oper->group);
    return ESP_OK;
}
