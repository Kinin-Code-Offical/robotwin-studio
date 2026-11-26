/*
 * [encoder] ESP-IDF's pulse counter driver (driver/pulse_cnt.h) in the fast simulation, as the classic ESP32's PCNT
 * counts: eight units of two channels. Each channel counts the edges of its GPIO (rising: pos action, falling: neg
 * action) as its level GPIO steers them (keep, inverse, hold), which decodes a quadrature encoder x4. The hardware
 * count returns to zero at the unit's limits (the driver adds the limit when accum_count is set); watch points at the
 * limits, zero and up to two thresholds call on_reach from the interrupt, as on the chip. The edges are the pin changes
 * the simulator delivers (rw_pin_changed), so counting follows the circuit's own signals; the channel pins get their
 * pull-ups, as the driver sets them. [pcnt-filter] The glitch filter (pcnt_unit_set_glitch_filter, up to 1023 APB
 * cycles) works as the hardware's: a signal input's new level counts only once it has held for the filter's width, so
 * a pulse shorter than it is not counted at all; the count follows the edge by that width.
 */
#include "Arduino.h"
#include "driver/pulse_cnt.h"
#include "rw_internal.h"

using namespace rw;

struct pcnt_chan_t {
    pcnt_unit_t* unit;
    int edge_gpio;
    int level_gpio;
    bool invert_edge;
    bool invert_level;
    bool virt_level;
    pcnt_channel_edge_action_t pos;
    pcnt_channel_edge_action_t neg;
    pcnt_channel_level_action_t high;
    pcnt_channel_level_action_t low;
    int stable;              // [pcnt-filter] the signal's level the counter has taken
    bool pending;            // a new level waiting to outlast the filter
    int pending_level;
    uint64_t pending_at;
};

struct pcnt_unit_t {
    bool used;
    bool enabled;      // the driver's FSM: init (false) or enable
    bool running;      // started (the hardware's pause bit clear)
    int low;
    int high;
    bool accum;
    int hw;            // the hardware counter
    int accumulated;   // what the driver added at each limit (accum_count)
    int watch[5];
    int watches;
    pcnt_unit_zero_cross_mode_t zero_mode;
    pcnt_watch_cb_t on_reach;
    void* ctx;
    bool chan_used[2];
    pcnt_chan_t chans[2];
    uint64_t glitch_ns;      // [pcnt-filter] 0: off
};

namespace {

const int kUnits = 8, kMinLimit = -32768, kMaxLimit = 32767;
pcnt_unit_t g_units[kUnits];

struct Reached {
    pcnt_unit_t* unit;
    int value;
    pcnt_unit_zero_cross_mode_t mode;
};

Reached g_reached[64];
unsigned g_head, g_tail;

void pcnt_log(const char* func, int line, const char* format, ...) {
    char msg[192];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(RW_LOG_ERROR, "E (%lu) pcnt: %s(%d): %s", (unsigned long)(uptime_ns() / 1000000ull), func, line, msg);
}

bool watched(const pcnt_unit_t& u, int value) {
    for (int i = 0; i < u.watches; i++)
        if (u.watch[i] == value) return true;
    return false;
}

/* The interrupt: every watch point reached since the last one, to on_reach. */
void deliver(void*) {
    while (g_tail != g_head) {
        Reached r = g_reached[g_tail % 64];
        g_tail++;
        if (!r.unit->used || !r.unit->on_reach) continue;
        pcnt_watch_event_data_t data = {r.value, r.mode};
        r.unit->on_reach(r.unit, &data, r.unit->ctx);
    }
}

void reach(pcnt_unit_t& u, int value) {
    if (!watched(u, value) || !u.on_reach || !u.enabled) return;
    if (g_head - g_tail >= 64) return;   // the interrupt is far behind: the hardware keeps only the latest status too
    g_reached[g_head % 64] = Reached{&u, value, u.zero_mode};
    g_head++;
    raise_isr(deliver, nullptr);
}

/* One count of the hardware counter, with the limit, threshold and zero events it reaches. */
void step(pcnt_unit_t& u, int delta) {
    int was = u.hw, now = was + delta;
    if (now >= u.high) {
        u.hw = 0;
        if (u.accum) u.accumulated += u.high;
        reach(u, u.high);
        return;
    }

    if (now <= u.low) {
        u.hw = 0;
        if (u.accum) u.accumulated += u.low;
        reach(u, u.low);
        return;
    }

    u.hw = now;
    if (now == 0 && was != 0) {
        u.zero_mode = was > 0 ? PCNT_UNIT_ZERO_CROSS_POS_ZERO : PCNT_UNIT_ZERO_CROSS_NEG_ZERO;
        reach(u, 0);
    } else if (now != 0) {
        reach(u, now);
    }
}

void input_pin(int gpio) {
    // The driver sets the channel's pins as inputs with their pull-ups; a pin the sketch drives keeps its output
    // (the PCNT input is appended, as the driver does for a configured output).
    if (gpio < 0 || hal_pin_drives(gpio)) return;
    pinMode((uint8_t)gpio, INPUT_PULLUP);
}

}  // namespace

namespace rw {

void hal_pcnt_reset() {
    memset(g_units, 0, sizeof g_units);
    g_head = g_tail = 0;
}

/* One edge of a channel's signal input, taken by the counter. */
static void count_edge(pcnt_unit_t& u, pcnt_chan_t& ch, int level) {
    ch.stable = level;
    bool rising = (level != 0) != ch.invert_edge;
    pcnt_channel_edge_action_t act = rising ? ch.pos : ch.neg;
    int delta = act == PCNT_CHANNEL_EDGE_ACTION_INCREASE ? 1 : act == PCNT_CHANNEL_EDGE_ACTION_DECREASE ? -1 : 0;
    if (delta == 0) return;
    bool control = ch.virt_level;
    if (ch.level_gpio >= 0) {
        int lv = hal_pin_level(ch.level_gpio);
        control = (lv != 0) != ch.invert_level;   // not seen yet: its pull-up holds it high
    }

    pcnt_channel_level_action_t steer = control ? ch.high : ch.low;
    if (steer == PCNT_CHANNEL_LEVEL_ACTION_HOLD) return;
    if (steer == PCNT_CHANNEL_LEVEL_ACTION_INVERSE) delta = -delta;
    step(u, delta);
}

void hal_pcnt_settle(uint64_t now_ns) {
    for (auto& u : g_units) {
        if (!u.used || !u.running || u.glitch_ns == 0) continue;
        for (int c = 0; c < 2; c++) {
            pcnt_chan_t& ch = u.chans[c];
            if (!u.chan_used[c] || !ch.pending || now_ns < ch.pending_at + u.glitch_ns) continue;
            ch.pending = false;
            count_edge(u, ch, ch.pending_level);
        }
    }
}

void hal_pcnt_edge(int gpio, int level, uint64_t time_ns) {
    hal_pcnt_settle(time_ns);   // what held long enough before this edge counts first
    for (auto& u : g_units) {
        if (!u.used || !u.running) continue;
        for (int c = 0; c < 2; c++) {
            if (!u.chan_used[c] || u.chans[c].edge_gpio != gpio) continue;
            pcnt_chan_t& ch = u.chans[c];
            if (u.glitch_ns > 0) {
                // [pcnt-filter] a level that has not yet held for the filter's width is a glitch when it changes back
                if (ch.pending) {
                    ch.pending = false;
                    if (level == ch.stable) continue;
                }

                if (level == ch.stable) continue;
                ch.pending = true;
                ch.pending_level = level;
                ch.pending_at = time_ns;
                continue;
            }

            count_edge(u, ch, level);
        }
    }
}

}  // namespace rw

extern "C" esp_err_t pcnt_new_unit(const pcnt_unit_config_t* config, pcnt_unit_handle_t* ret_unit) {
    charge(cost(15000.0));
    if (!config || !ret_unit) {
        pcnt_log("pcnt_new_unit", 213, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    if (!(config->low_limit < 0 && config->high_limit > 0 && config->low_limit >= kMinLimit && config->high_limit <= kMaxLimit)) {
        pcnt_log("pcnt_new_unit", 216, "invalid limit range:[%d,%d]", config->low_limit, config->high_limit);
        return ESP_ERR_INVALID_ARG;
    }

    for (auto& u : g_units) {
        if (u.used) continue;
        u = pcnt_unit_t{};
        u.used = true;
        u.low = config->low_limit;
        u.high = config->high_limit;
        u.accum = config->flags.accum_count != 0;
        *ret_unit = &u;
        return ESP_OK;
    }

    pcnt_log("pcnt_register_to_group", 95, "no free unit");
    return ESP_ERR_NOT_FOUND;
}

extern "C" esp_err_t pcnt_del_unit(pcnt_unit_handle_t unit) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    if (unit->enabled) {
        pcnt_log("pcnt_del_unit", 288, "unit not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    for (int c = 0; c < 2; c++)
        if (unit->chan_used[c]) {
            pcnt_log("pcnt_del_unit", 290, "channel %d still in working", c);
            return ESP_ERR_INVALID_STATE;
        }

    *unit = pcnt_unit_t{};
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_set_glitch_filter(pcnt_unit_handle_t unit, const pcnt_glitch_filter_config_t* config) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    if (unit->enabled) {
        pcnt_log("pcnt_unit_set_glitch_filter", 330, "unit not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    if (config && config->max_glitch_ns > 12787u) {   // 1023 APB cycles
        pcnt_log("pcnt_unit_set_glitch_filter", 337, "glitch width out of range");
        return ESP_ERR_INVALID_ARG;
    }

    // [pcnt-filter] the hardware's threshold is whole APB cycles (12.5 ns): the width the driver sets rounds down to them
    unit->glitch_ns = config ? (uint64_t)(config->max_glitch_ns / 12.5) * 125ull / 10ull : 0;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_enable(pcnt_unit_handle_t unit) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    if (unit->enabled) {
        pcnt_log("pcnt_unit_enable", 351, "unit not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    unit->enabled = true;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_disable(pcnt_unit_handle_t unit) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    if (!unit->enabled) {
        pcnt_log("pcnt_unit_disable", 374, "unit not in enable state");
        return ESP_ERR_INVALID_STATE;
    }

    unit->enabled = false;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_start(pcnt_unit_handle_t unit) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    if (!unit->enabled) {
        pcnt_log("pcnt_unit_start", 398, "unit not enabled yet");
        return ESP_ERR_INVALID_STATE;
    }

    unit->running = true;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_stop(pcnt_unit_handle_t unit) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    if (!unit->enabled) {
        pcnt_log("pcnt_unit_stop", 412, "unit not enabled yet");
        return ESP_ERR_INVALID_STATE;
    }

    unit->running = false;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_clear_count(pcnt_unit_handle_t unit) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    unit->hw = 0;
    unit->accumulated = 0;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_get_count(pcnt_unit_handle_t unit, int* value) {
    charge(cost(300.0));
    if (!unit || !unit->used || !value) return ESP_ERR_INVALID_ARG;
    hal_pcnt_settle(rw::now_ns());   // [pcnt-filter] edges that have held by now
    *value = unit->accumulated + unit->hw;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_register_event_callbacks(pcnt_unit_handle_t unit, const pcnt_event_callbacks_t* cbs, void* user_data) {
    if (!unit || !unit->used || !cbs) return ESP_ERR_INVALID_ARG;
    if (unit->enabled) {
        pcnt_log("pcnt_unit_register_event_callbacks", 479, "unit not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    unit->on_reach = cbs->on_reach;
    unit->ctx = user_data;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_add_watch_point(pcnt_unit_handle_t unit, int watch_point) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    if (watch_point > unit->high || watch_point < unit->low) {
        pcnt_log("pcnt_unit_add_watch_point", 518, "watch point out of limit");
        return ESP_ERR_INVALID_ARG;
    }

    if (watched(*unit, watch_point)) {
        pcnt_log("pcnt_unit_add_watch_point", 526, "watch point %d has been added", watch_point);
        return ESP_ERR_INVALID_STATE;
    }

    // The limits and zero have their own comparators; anything else takes one of the two thresholds.
    if (watch_point != unit->high && watch_point != unit->low && watch_point != 0) {
        int thresholds = 0;
        for (int i = 0; i < unit->watches; i++)
            if (unit->watch[i] != unit->high && unit->watch[i] != unit->low && unit->watch[i] != 0) thresholds++;
        if (thresholds >= 2) {
            pcnt_log("pcnt_unit_add_watch_point", 546, "no free threshold");
            return ESP_ERR_NOT_FOUND;
        }
    }

    unit->watch[unit->watches++] = watch_point;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_unit_remove_watch_point(pcnt_unit_handle_t unit, int watch_point) {
    if (!unit || !unit->used) return ESP_ERR_INVALID_ARG;
    for (int i = 0; i < unit->watches; i++)
        if (unit->watch[i] == watch_point) {
            unit->watch[i] = unit->watch[--unit->watches];
            return ESP_OK;
        }

    pcnt_log("pcnt_unit_remove_watch_point", 578, "watch point %d not added yet", watch_point);
    return ESP_ERR_INVALID_STATE;
}

extern "C" esp_err_t pcnt_unit_add_watch_step(pcnt_unit_handle_t, int) {
    pcnt_log("pcnt_unit_add_watch_step", 600, "watch step is not supported by ESP32");
    return ESP_ERR_NOT_SUPPORTED;
}

extern "C" esp_err_t pcnt_unit_remove_all_watch_step(pcnt_unit_handle_t) {
    return ESP_ERR_NOT_SUPPORTED;
}

extern "C" esp_err_t pcnt_new_channel(pcnt_unit_handle_t unit, const pcnt_chan_config_t* config, pcnt_channel_handle_t* ret_chan) {
    charge(cost(8000.0));
    if (!unit || !unit->used || !config || !ret_chan) return ESP_ERR_INVALID_ARG;
    if (unit->enabled) {
        pcnt_log("pcnt_new_channel", 662, "unit not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    for (int c = 0; c < 2; c++) {
        if (unit->chan_used[c]) continue;
        pcnt_chan_t& ch = unit->chans[c];
        ch = pcnt_chan_t{};
        ch.unit = unit;
        ch.edge_gpio = config->edge_gpio_num;
        ch.level_gpio = config->level_gpio_num;
        ch.invert_edge = config->flags.invert_edge_input != 0;
        ch.invert_level = config->flags.invert_level_input != 0;
        ch.virt_level = config->flags.virt_level_io_level != 0;
        unit->chan_used[c] = true;
        input_pin(ch.edge_gpio);
        int start = ch.edge_gpio >= 0 ? hal_pin_level(ch.edge_gpio) : 1;
        ch.stable = start < 0 ? 1 : start;   // [pcnt-filter] the level it starts from (unseen: its pull-up)
        ch.pending = false;
        input_pin(ch.level_gpio);
        *ret_chan = &ch;
        return ESP_OK;
    }

    pcnt_log("pcnt_new_channel", 671, "no free channel in unit (%d)", (int)(unit - g_units));
    return ESP_ERR_NOT_FOUND;
}

extern "C" esp_err_t pcnt_del_channel(pcnt_channel_handle_t chan) {
    if (!chan || !chan->unit) return ESP_ERR_INVALID_ARG;
    pcnt_unit_t* unit = chan->unit;
    if (unit->enabled) {
        pcnt_log("pcnt_del_channel", 731, "unit not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    unit->chan_used[chan - unit->chans] = false;
    *chan = pcnt_chan_t{};
    return ESP_OK;
}

extern "C" esp_err_t pcnt_channel_set_edge_action(pcnt_channel_handle_t chan, pcnt_channel_edge_action_t pos_act, pcnt_channel_edge_action_t neg_act) {
    if (!chan || !chan->unit) return ESP_ERR_INVALID_ARG;
    chan->pos = pos_act;
    chan->neg = neg_act;
    return ESP_OK;
}

extern "C" esp_err_t pcnt_channel_set_level_action(pcnt_channel_handle_t chan, pcnt_channel_level_action_t high_act, pcnt_channel_level_action_t low_act) {
    if (!chan || !chan->unit) return ESP_ERR_INVALID_ARG;
    chan->high = high_act;
    chan->low = low_act;
    return ESP_OK;
}
