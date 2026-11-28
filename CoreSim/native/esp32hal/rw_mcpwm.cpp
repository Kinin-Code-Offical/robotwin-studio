/*
 * [motor] ESP-IDF's MCPWM driver in the fast simulation, as the classic ESP32's motor-control PWM works: two groups of
 * three timers (resolution, period, up / down / up-down counting, start and stop commands), three operators each with two
 * comparators and two generators. A generator's actions on its timer's zero and peak and on the comparisons (per counting
 * direction, the chip's priority B > A > peak > zero on a tie) make its wave; dead time delays the rising and falling
 * edges of a generator's wave into another (and inverts it), a software force holds a level. Each generator's pin gets the
 * result through the host's PWM output: frequency and duty worked out from one period (two when a toggle doubles it), the
 * same arithmetic as the Xtensa emulator's MCPWM (Esp32Soc.Mcpwm.cs). The event callbacks (a timer's on_full, on_empty
 * and on_stop, a comparator's on_reach) run in interrupt context at the ticks they happen on: from when the timer started,
 * each period has its zero and its peak and the comparison where the count passes the compare value (twice in up-down
 * mode), as the chip's TEZ, TEP and TEA / TEB interrupts come. The fault handler: a GPIO fault present while its pin is at its
 * active level (or a software one, for an instant) brakes the operators set to it, cycle by cycle (CBC: while it is there)
 * or one shot (OST: until mcpwm_operator_recover_from_fault with the fault gone); meanwhile each generator does its brake
 * action (low, high, toggle or keep) for the counting direction, as the emulator's FH registers do (Esp32Soc.McpwmFault.cs).
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
    // the event callbacks and their timeline
    mcpwm_timer_event_cb_t on_full, on_empty, on_stop;
    void* ctx;
    double origin_ns;      // when the count started (0 counting up, the peak counting down)
    double fired_ticks;    // the last event delivered, in ticks from the origin
    int event_id;
    int stopping;          // 1 at the next empty, 2 at the next full: on_stop then
    // [sync] the source that reloads it with its phase, and its own sync out
    struct mcpwm_sync_t* sync_in;
    uint32_t phase;
    mcpwm_timer_direction_t phase_dir;
    struct mcpwm_sync_t* sync_out;
};

/* A sync source: a timer's sync out (on its empty or full, or its input passed on), a GPIO's edge, or software. */
struct mcpwm_sync_t {
    bool used;
    int kind;              // 0 timer, 1 GPIO, 2 software
    int group;             // -1: a software source not yet given to a timer
    mcpwm_timer_t* timer;  // a timer source's timer
    mcpwm_timer_event_t event;
    bool propagate;
    int gpio;
    bool active_neg;
};

struct mcpwm_cmpr_t {
    mcpwm_oper_t* oper;
    bool used;
    uint32_t value;
    mcpwm_compare_event_cb_t on_reach;
    void* ctx;
};

struct mcpwm_gen_t {
    mcpwm_oper_t* oper;
    bool used;
    int id;
    int gpio;
    bool invert;
    uint32_t actions;      // as GENn_A/B: up zero 1:0, peak 3:2, A 5:4, B 7:6; down from bit 12
    int force;             // -1 none, 0 low, 1 high
    int brake_action[2][2];   // [CBC, OST][up, down]: a mcpwm_generator_action_t (KEEP: the wave goes on)
    bool dead_time;        // this output is another generator's wave through the dead-time module
    mcpwm_gen_t* dt_in;
    double red;            // ticks of the timer's resolution
    double fed;
    bool dt_invert;
};

struct mcpwm_fault_t {
    bool used;
    int group;
    bool soft;
    int gpio;
    bool active_high;
    bool present;          // a GPIO fault at its active level now
    mcpwm_oper_t* bound;   // a software fault serves one operator
    mcpwm_fault_event_cb_t on_enter, on_exit;
    void* ctx;
};

struct mcpwm_oper_t {
    bool used;
    int group;
    mcpwm_timer_t* timer;
    mcpwm_cmpr_t cmprs[2];
    mcpwm_gen_t gens[2];
    // [fault] the brakes set on faults, and the brake now
    struct Brake {
        mcpwm_fault_t* fault;
        mcpwm_operator_brake_mode_t mode;
    } brakes[4];
    int brake_count;
    bool ost;              // tripped one shot
    bool cbc;              // braking cycle by cycle
    int cbc_soft_event;    // a software CBC brake ends with its period
    mcpwm_brake_event_cb_t on_cbc, on_ost;
    void* brake_ctx;
    // [carrier] the chopper after dead time (seconds)
    bool carrier;
    double carrier_period, carrier_duty, carrier_first;
    bool carrier_in_invert, carrier_out_invert;
};

namespace {

mcpwm_timer_t g_timers[2][3];
mcpwm_oper_t g_opers[2][3];
mcpwm_fault_t g_faults[2][4];   // three GPIO faults and a software one per group
mcpwm_sync_t g_syncs[24];       // per group three timer and three GPIO sync sources, and software ones
uint32_t g_group_resolution[2]; // the group clock IDF set for the first timer (0: none yet)

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

Wave flipped(Wave w) { return w.frequency > 0 ? inverted(w) : constant(w.duty < 0.5); }

/* [carrier] A wave chopped by its operator's carrier: the first pulse, then the carrier's duty, as the circuit sees it (the
   carrier's frequency, the duty averaged over the PWM period), the same as Esp32Soc.McpwmSync.cs. */
Wave carried(const mcpwm_oper_t& op, Wave w) {
    if (!op.carrier) return w;
    if (op.carrier_in_invert) w = flipped(w);
    double period = op.carrier_period, duty = op.carrier_duty, first = op.carrier_first;
    Wave c;
    if (w.frequency <= 0) {
        c = w.duty < 0.5 || duty <= 0 ? constant(false) : Wave{1.0 / period, duty};
    } else {
        double high = w.duty / w.frequency;
        if (high <= first) c = w;
        else {
            double rest = high - first, whole = floor(rest / period);
            double h = first + whole * duty * period + fmin(rest - whole * period, duty * period);
            double avg = h * w.frequency;
            c = avg <= 0 ? constant(false) : avg >= 1 ? constant(true) : Wave{1.0 / period, avg};
        }
    }

    return op.carrier_out_invert ? flipped(c) : c;
}

/* What a generator's pin carries: its own wave or another's through dead time, a brake, a force, the GPIO's inversion. */
Wave output_wave(const mcpwm_gen_t& g) {
    Wave w;
    const mcpwm_oper_t& op = *g.oper;
    int brake = op.ost ? 1 : op.cbc ? 0 : -1;
    int brake_act = brake < 0 ? 0 : g.brake_action[brake][op.timer && op.timer->mode == MCPWM_TIMER_COUNT_MODE_DOWN ? 1 : 0];
    if (g.force < 0 && brake_act == MCPWM_GEN_ACTION_LOW) {
        w = constant(false);
    } else if (g.force < 0 && brake_act == MCPWM_GEN_ACTION_HIGH) {
        w = constant(true);
    } else if (g.force >= 0) {
        w = constant(g.force != 0);
    } else if (g.dead_time && g.dt_in) {
        const mcpwm_timer_t* t = g.oper->timer;
        double tick = t && t->resolution ? 1.0 / t->resolution : 0;
        w = generator_wave(*g.dt_in);
        if (g.red) w = delayed(w, g.red * tick, true);
        if (g.fed) w = delayed(w, g.fed * tick, false);
        if (g.dt_invert) w = inverted(w);
        w = carried(op, w);
    } else {
        w = carried(op, generator_wave(g));
    }

    if (g.force < 0 && brake_act == MCPWM_GEN_ACTION_TOGGLE) w = w.frequency > 0 ? inverted(w) : constant(w.duty < 0.5);
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

/* The group clock IDF 5.5 picks for a group's first timer: the group prescale from 2 up, the first that leaves the timer a
   whole prescale of at most 256 for its resolution (1 MHz: 160 MHz / 2 / 80; 100 kHz: / 8 / 200, as the chip's registers
   show under the real driver). Later timers keep it. */
uint32_t group_clock_for(uint32_t resolution) {
    const uint32_t src = 160000000u;
    for (uint32_t g = 2; resolution && g <= 256; g++) {
        uint64_t step = (uint64_t)g * resolution;
        if (src % step == 0 && src / step <= 256) return src / g;
    }
    return src / 2;
}

bool gen_ok(mcpwm_gen_handle_t g) { return g && g->used && g->oper && g->oper->used; }

/* ---- event callbacks: the timer's timeline in ticks from its origin */
/* One period's length in ticks, and where the zero and the peak fall in it. */
void period_events(const mcpwm_timer_t& t, double& length, double& zero, double& peak) {
    length = (double)t.period_ticks;
    switch (t.mode) {
    case MCPWM_TIMER_COUNT_MODE_DOWN: zero = length - 1; peak = 0; break;
    case MCPWM_TIMER_COUNT_MODE_UP_DOWN: zero = 0; peak = length / 2; break;
    default: zero = 0; peak = length - 1; break;
    }
}

/* The first tick after `after` that is `offset` into a period. */
double next_at(double after, double offset, double length) {
    double n = floor((after - offset) / length) + 1;
    double at = n * length + offset;
    if (at <= after + 0.5) at += length;
    return at;
}

double ticks_now(const mcpwm_timer_t& t) { return ((double)now_ns() - t.origin_ns) * t.resolution / 1e9; }

void arm(mcpwm_timer_t& t);
void sync_fire(mcpwm_sync_t* s, double at_ns, int depth);

/* Whether the timer's sync out comes on that event of its own. */
bool syncs_on(const mcpwm_timer_t& t, mcpwm_timer_event_t ev) { return t.sync_out && t.sync_out->used && t.sync_out->event == ev; }

/* The events at one tick, delivered in the chip's order (zero, peak, then the operators' comparisons), then the next. */
void timer_event(void* arg) {
    auto* t = static_cast<mcpwm_timer_t*>(arg);
    t->event_id = 0;
    if (!t->used || !t->enabled) return;
    double length, zero, peak;
    period_events(*t, length, zero, peak);
    double at = t->fired_ticks;
    auto here = [&](double offset) { double r = fmod(at - offset, length); if (r < 0) r += length; return r < 0.5 || r > length - 0.5; };
    uint32_t peak_count = t->mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN ? t->period_ticks / 2 : t->period_ticks - 1;
    if (t->stopping) {
        if ((t->stopping == 1 && here(zero)) || (t->stopping == 2 && here(peak))) {
            int kind = t->stopping;
            t->stopping = 0;
            if (t->on_stop) {
                mcpwm_timer_event_data_t d = {kind == 2 ? peak_count : 0u, MCPWM_TIMER_DIRECTION_UP};
                t->on_stop(t, &d, t->ctx);
            }
        }
        arm(*t);
        return;
    }

    if (!t->running) return;
    if (here(zero) && t->on_empty) {
        mcpwm_timer_event_data_t d = {0u, t->mode == MCPWM_TIMER_COUNT_MODE_DOWN ? MCPWM_TIMER_DIRECTION_DOWN : MCPWM_TIMER_DIRECTION_UP};
        t->on_empty(t, &d, t->ctx);
    }

    if (here(peak) && t->on_full) {
        mcpwm_timer_event_data_t d = {peak_count, t->mode == MCPWM_TIMER_COUNT_MODE_UP ? MCPWM_TIMER_DIRECTION_UP : MCPWM_TIMER_DIRECTION_DOWN};
        t->on_full(t, &d, t->ctx);
    }

    for (auto& op : g_opers[t->group]) {
        if (!op.used || op.timer != t) continue;
        for (auto& c : op.cmprs) {
            if (!c.used || !c.on_reach) continue;
            bool up = false, down = false;
            switch (t->mode) {
            case MCPWM_TIMER_COUNT_MODE_DOWN: down = c.value <= peak_count && here(zero - c.value); break;
            case MCPWM_TIMER_COUNT_MODE_UP_DOWN:
                up = c.value < peak_count && here((double)c.value);
                down = c.value > 0 && c.value < peak_count && here(length - c.value);
                break;
            default: up = c.value <= peak_count && here((double)c.value); break;
            }

            if (up || down) {
                mcpwm_compare_event_data_t d = {c.value, down ? MCPWM_TIMER_DIRECTION_DOWN : MCPWM_TIMER_DIRECTION_UP};
                c.on_reach(&c, &d, c.ctx);
            }
        }
    }

    // [sync] its zero or full as a sync out: the timers that take it reloaded at this tick
    double at_ns = t->origin_ns + at * 1e9 / t->resolution;
    if (here(zero) && syncs_on(*t, MCPWM_TIMER_EVENT_EMPTY)) sync_fire(t->sync_out, at_ns, 0);
    if (here(peak) && syncs_on(*t, MCPWM_TIMER_EVENT_FULL)) sync_fire(t->sync_out, at_ns, 0);
    arm(*t);
}

/* The next tick any callback of this timer is due, scheduled. */
void arm(mcpwm_timer_t& t) {
    if (t.event_id) cancel_event(t.event_id);
    t.event_id = 0;
    if (!t.used || !t.enabled || t.period_ticks < 2 || t.resolution == 0) return;
    double length, zero, peak;
    period_events(t, length, zero, peak);
    double after = t.fired_ticks, best = -1;
    auto consider = [&](double offset) { double at = next_at(after, offset, length); if (best < 0 || at < best) best = at; };
    if (t.stopping) consider(t.stopping == 1 ? zero : peak);
    else if (t.running) {
        if (t.on_empty || syncs_on(t, MCPWM_TIMER_EVENT_EMPTY)) consider(zero);
        if (t.on_full || syncs_on(t, MCPWM_TIMER_EVENT_FULL)) consider(peak);
        uint32_t peak_count = t.mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN ? t.period_ticks / 2 : t.period_ticks - 1;
        for (auto& op : g_opers[t.group]) {
            if (!op.used || op.timer != &t) continue;
            for (auto& c : op.cmprs) {
                if (!c.used || !c.on_reach) continue;
                if (t.mode == MCPWM_TIMER_COUNT_MODE_DOWN) consider(zero - c.value);
                else if (t.mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN) {
                    if (c.value < peak_count) consider((double)c.value);
                    if (c.value > 0 && c.value < peak_count) consider(length - c.value);
                } else consider((double)c.value);
            }
        }
    }

    if (best < 0) return;
    t.fired_ticks = best;
    t.event_id = schedule_event((uint64_t)ceil(t.origin_ns + best * 1e9 / t.resolution), timer_event, &t);
}

/* From now on: the timeline goes on from the present tick (after a change of period, compare value or callbacks). */
void rearm(mcpwm_timer_t* t) {
    if (!t || !t->used) return;
    if (t->running || t->stopping) t->fired_ticks = ticks_now(*t);
    arm(*t);
}

}  // namespace

namespace rw {
/* ---- [legacy] the legacy driver (driver/mcpwm.h, rw_mcpwm_legacy.cpp) on the same model: its timer n drives operator n,
   whose comparators A / B and generators A / B it owns. */
static mcpwm_oper_t& legacy_oper(int unit, int timer) {
    mcpwm_oper_t& op = g_opers[unit][timer];
    if (!op.used) {
        op = mcpwm_oper_t{};
        op.used = true;
        op.group = unit;
        for (int i = 0; i < 2; i++) {
            op.cmprs[i].oper = &op;
            op.cmprs[i].used = true;
            op.gens[i].oper = &op;
            op.gens[i].used = true;
            op.gens[i].id = i;
            op.gens[i].gpio = -1;
            op.gens[i].force = -1;
        }
    }

    op.timer = &g_timers[unit][timer];
    return op;
}

void mcpwm_legacy_config(int unit, int timer, uint32_t resolution, int count_mode, uint32_t period_ticks) {
    mcpwm_timer_t& t = g_timers[unit][timer];
    if (!t.used) {
        t = mcpwm_timer_t{};
        t.used = true;
        t.group = unit;
    }

    t.resolution = resolution;
    t.mode = (mcpwm_timer_count_mode_t)count_mode;
    t.period_ticks = period_ticks;
    t.enabled = true;
    legacy_oper(unit, timer);
    update(unit);
}

uint32_t mcpwm_legacy_period(int unit, int timer) { return g_timers[unit][timer].used ? g_timers[unit][timer].period_ticks : 0; }
int mcpwm_legacy_mode(int unit, int timer) { return g_timers[unit][timer].used ? (int)g_timers[unit][timer].mode : 0; }
uint32_t mcpwm_legacy_resolution(int unit, int timer) { return g_timers[unit][timer].used ? g_timers[unit][timer].resolution : 0; }

void mcpwm_legacy_compare(int unit, int timer, int which, uint32_t value) {
    legacy_oper(unit, timer).cmprs[which].value = value;
    update(unit);
}

uint32_t mcpwm_legacy_compare_value(int unit, int timer, int which) { return g_opers[unit][timer].used ? g_opers[unit][timer].cmprs[which].value : 0; }

void mcpwm_legacy_actions(int unit, int timer, int gen, uint32_t actions) {
    mcpwm_gen_t& g = legacy_oper(unit, timer).gens[gen];
    g.actions = actions;
    g.force = -1;
    update(unit);
}

void mcpwm_legacy_force(int unit, int timer, int gen, int level) {
    legacy_oper(unit, timer).gens[gen].force = level;
    update(unit);
}

void mcpwm_legacy_pin(int unit, int timer, int gen, int gpio) {
    mcpwm_gen_t& g = legacy_oper(unit, timer).gens[gen];
    if (g.gpio >= 0 && g.gpio != gpio) hal_pwm_out(g.gpio, 1000.0, 0.0);
    g.gpio = gpio;
    if (gpio >= 0) pinMode((uint8_t)gpio, OUTPUT);
    update(unit);
}

void mcpwm_legacy_run(int unit, int timer, bool run) {
    mcpwm_timer_t& t = g_timers[unit][timer];
    if (!t.used) return;
    bool was = t.running;
    t.running = run;
    t.stopped_full = false;
    if (run && !was) {
        t.origin_ns = (double)now_ns();
        t.fired_ticks = 0;
    }

    update(unit);
}

/* Dead time between generator A's wave and the two outputs: out 0 = A, 1 = B; from = 0 A's wave, 1 B's; delays in seconds. */
void mcpwm_legacy_dead_time(int unit, int timer, int out, int from, double red_s, double fed_s, bool invert, bool on) {
    mcpwm_oper_t& op = legacy_oper(unit, timer);
    mcpwm_gen_t& g = op.gens[out];
    double res = op.timer ? (double)op.timer->resolution : 1e6;
    g.dead_time = on;
    g.dt_in = &op.gens[from];
    g.red = red_s * res;
    g.fed = fed_s * res;
    g.dt_invert = invert;
    update(unit);
}

void hal_mcpwm_reset() {
    for (auto& row : g_timers)
        for (auto& t : row)
            if (t.event_id) cancel_event(t.event_id);
    for (auto& row : g_opers)
        for (auto& op : row)
            if (op.cbc_soft_event) cancel_event(op.cbc_soft_event);
    memset(g_faults, 0, sizeof g_faults);
    memset(g_timers, 0, sizeof g_timers);
    memset(g_opers, 0, sizeof g_opers);
    memset(g_syncs, 0, sizeof g_syncs);
    memset(g_group_resolution, 0, sizeof g_group_resolution);
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
        if (!g_group_resolution[t.group]) g_group_resolution[t.group] = group_clock_for(config->resolution_hz);
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
    rearm(timer);
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
    if (timer->event_id) cancel_event(timer->event_id);
    timer->event_id = 0;
    update(timer->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_start_stop(mcpwm_timer_handle_t timer, mcpwm_timer_start_stop_cmd_t command) {
    if (!timer || !timer->used) return ESP_ERR_INVALID_ARG;
    if (!timer->enabled) {
        mcpwm_log("mcpwm_timer_start_stop", 286, "timer not enabled yet");
        return ESP_ERR_INVALID_STATE;
    }

    bool was = timer->running;
    switch (command) {
    case MCPWM_TIMER_STOP_EMPTY: timer->running = false; timer->stopped_full = false; break;
    case MCPWM_TIMER_STOP_FULL: timer->running = false; timer->stopped_full = true; break;
    default: timer->running = true; break;   // free running, or one more period (counted as running)
    }

    if (timer->running && !was) {
        // started: the count from 0 (or the peak, counting down) now
        timer->origin_ns = (double)now_ns();
        timer->fired_ticks = 0;   // the count is loaded, not reached: the first zero comes a period on
        timer->stopping = 0;
        arm(*timer);
    } else if (!timer->running && was) {
        timer->fired_ticks = ticks_now(*timer);
        timer->stopping = command == MCPWM_TIMER_STOP_FULL ? 2 : 1;   // on_stop at the next empty / full
        arm(*timer);
    }

    update(timer->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_register_event_callbacks(mcpwm_timer_handle_t timer, const mcpwm_timer_event_callbacks_t* cbs, void* user_data) {
    if (!timer || !timer->used || !cbs) return ESP_ERR_INVALID_ARG;
    if (timer->enabled) {
        mcpwm_log("mcpwm_timer_register_event_callbacks", 336, "timer not in init state");
        return ESP_ERR_INVALID_STATE;
    }

    timer->on_full = cbs->on_full;
    timer->on_empty = cbs->on_empty;
    timer->on_stop = cbs->on_stop;
    timer->ctx = user_data;
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
    if (cmpr->on_reach) rearm(cmpr->oper->timer);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_comparator_register_event_callbacks(mcpwm_cmpr_handle_t cmpr, const mcpwm_comparator_event_callbacks_t* cbs, void* user_data) {
    if (!cmpr || !cmpr->used || !cbs) return ESP_ERR_INVALID_ARG;
    cmpr->on_reach = cbs->on_reach;
    cmpr->ctx = user_data;
    if (cmpr->oper) rearm(cmpr->oper->timer);
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

/* ---------------------------------------------------------------- faults and brakes */
namespace {

mcpwm_fault_t g_soft_faults[8];   // a software fault belongs to no group until an operator brakes on it

void fault_enter(void* arg) {
    auto* f = static_cast<mcpwm_fault_t*>(arg);
    mcpwm_fault_event_data_t d{};
    if (f->used && f->on_enter) f->on_enter(f, &d, f->ctx);
}

void fault_exit(void* arg) {
    auto* f = static_cast<mcpwm_fault_t*>(arg);
    mcpwm_fault_event_data_t d{};
    if (f->used && f->on_exit) f->on_exit(f, &d, f->ctx);
}

void brake_cbc(void* arg) {
    auto* op = static_cast<mcpwm_oper_t*>(arg);
    mcpwm_brake_event_data_t d{};
    if (op->used && op->on_cbc) op->on_cbc(op, &d, op->brake_ctx);
}

void brake_ost(void* arg) {
    auto* op = static_cast<mcpwm_oper_t*>(arg);
    mcpwm_brake_event_data_t d{};
    if (op->used && op->on_ost) op->on_ost(op, &d, op->brake_ctx);
}

/* Every operator of the group: its brake from the faults present now (a one-shot trip latches, CBC follows them). */
void evaluate_brakes(int group) {
    for (auto& op : g_opers[group]) {
        if (!op.used) continue;
        bool cbc = op.cbc_soft_event != 0, ost = false;
        for (int i = 0; i < op.brake_count; i++) {
            const auto& b = op.brakes[i];
            if (!b.fault || !b.fault->used || b.fault->soft || !b.fault->present) continue;
            if (b.mode == MCPWM_OPER_BRAKE_MODE_OST) ost = true;
            else cbc = true;
        }

        if (ost && !op.ost) {
            op.ost = true;
            raise_isr(brake_ost, &op);
        }

        if (cbc && !op.cbc) raise_isr(brake_cbc, &op);
        op.cbc = cbc;
    }

    update(group);
}

void soft_cbc_end(void* arg) {
    auto* op = static_cast<mcpwm_oper_t*>(arg);
    op->cbc_soft_event = 0;
    evaluate_brakes(op->group);
}

mcpwm_oper_t::Brake* brake_of(mcpwm_oper_t& op, mcpwm_fault_t* f) {
    for (int i = 0; i < op.brake_count; i++)
        if (op.brakes[i].fault == f) return &op.brakes[i];
    return nullptr;
}

bool fault_ok(mcpwm_fault_handle_t f) { return f && f->used; }

}  // namespace

namespace rw {
void hal_mcpwm_fault_edge(int gpio, int level) {
    for (int g = 0; g < 2; g++) {
        bool changed = false;
        for (auto& f : g_faults[g]) {
            if (!f.used || f.soft || f.gpio != gpio) continue;
            bool present = (level != 0) == f.active_high;
            if (present == f.present) continue;
            f.present = present;
            raise_isr(present ? fault_enter : fault_exit, &f);
            changed = true;
        }

        if (changed) evaluate_brakes(g);
    }
}
}  // namespace rw

extern "C" esp_err_t mcpwm_new_gpio_fault(const mcpwm_gpio_fault_config_t* config, mcpwm_fault_handle_t* ret_fault) {
    charge(cost(8000.0));
    if (!config || !ret_fault || config->group_id < 0 || config->group_id > 1) {
        mcpwm_log("mcpwm_new_gpio_fault", 104, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    if (!digitalPinIsValid(config->gpio_num)) {
        mcpwm_log("mcpwm_new_gpio_fault", 106, "invalid GPIO number");
        return ESP_ERR_INVALID_ARG;
    }

    for (int i = 0; i < 3; i++) {
        mcpwm_fault_t& f = g_faults[config->group_id][i];
        if (f.used) continue;
        f = mcpwm_fault_t{};
        f.used = true;
        f.group = config->group_id;
        f.gpio = config->gpio_num;
        f.active_high = config->flags.active_level != 0;
        uint8_t mode = INPUT;
        if (config->flags.pull_up) mode |= PULLUP;
        if (config->flags.pull_down) mode |= PULLDOWN;
        if (!config->flags.io_loop_back) pinMode((uint8_t)f.gpio, mode);
        int lv = hal_pin_level(f.gpio);
        if (lv < 0) lv = config->flags.pull_up ? 1 : 0;   // not seen yet: what its pull holds
        f.present = (lv != 0) == f.active_high;
        *ret_fault = &f;
        evaluate_brakes(f.group);
        return ESP_OK;
    }

    mcpwm_log("mcpwm_gpio_fault_register_to_group", 59, "no free gpio fault in group (%d)", config->group_id);
    return ESP_ERR_NOT_FOUND;
}

extern "C" esp_err_t mcpwm_new_soft_fault(const mcpwm_soft_fault_config_t* config, mcpwm_fault_handle_t* ret_fault) {
    if (!config || !ret_fault) return ESP_ERR_INVALID_ARG;
    for (auto& f : g_soft_faults) {
        if (f.used) continue;
        f = mcpwm_fault_t{};
        f.used = true;
        f.group = -1;
        f.soft = true;
        f.gpio = -1;
        *ret_fault = &f;
        return ESP_OK;
    }

    return ESP_ERR_NO_MEM;
}

extern "C" esp_err_t mcpwm_del_fault(mcpwm_fault_handle_t fault) {
    if (!fault_ok(fault)) return ESP_ERR_INVALID_ARG;
    fault->used = false;
    for (int g = 0; g < 2; g++) evaluate_brakes(g);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_fault_register_event_callbacks(mcpwm_fault_handle_t fault, const mcpwm_fault_event_callbacks_t* cbs, void* user_data) {
    if (!fault_ok(fault) || !cbs) return ESP_ERR_INVALID_ARG;
    if (fault->soft) {
        mcpwm_log("mcpwm_fault_register_event_callbacks", 336, "soft fault doesn't support callbacks");
        return ESP_ERR_INVALID_ARG;
    }

    fault->on_enter = cbs->on_fault_enter;
    fault->on_exit = cbs->on_fault_exit;
    fault->ctx = user_data;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_soft_fault_activate(mcpwm_fault_handle_t fault) {
    if (!fault_ok(fault) || !fault->soft) return ESP_ERR_INVALID_ARG;
    for (int g = 0; g < 2; g++) {
        for (auto& op : g_opers[g]) {
            if (!op.used) continue;
            auto* b = brake_of(op, fault);
            if (!b) continue;
            if (b->mode == MCPWM_OPER_BRAKE_MODE_OST) {
                if (!op.ost) {
                    op.ost = true;
                    raise_isr(brake_ost, &op);
                }
            } else {
                // a software CBC brake lasts its period
                double period_s = op.timer && op.timer->resolution ? (double)op.timer->period_ticks / op.timer->resolution : 0;
                if (op.cbc_soft_event) cancel_event(op.cbc_soft_event);
                op.cbc_soft_event = schedule_event(now_ns() + (uint64_t)(period_s * 1e9) + 1, soft_cbc_end, &op);
            }
        }

        evaluate_brakes(g);
    }

    return ESP_OK;
}

extern "C" esp_err_t mcpwm_operator_set_brake_on_fault(mcpwm_oper_handle_t oper, const mcpwm_brake_config_t* config) {
    if (!oper || !oper->used || !config || !fault_ok(config->fault) || config->brake_mode >= MCPWM_OPER_BRAKE_MODE_INVALID) return ESP_ERR_INVALID_ARG;
    if (!config->fault->soft && config->fault->group != oper->group) {
        mcpwm_log("mcpwm_operator_set_brake_on_fault", 268, "fault and operator not in the same group");
        return ESP_ERR_INVALID_ARG;
    }

    if (config->fault->soft) {
        if (config->fault->bound && config->fault->bound != oper) {
            mcpwm_log("mcpwm_operator_set_brake_on_fault", 290, "soft fault already used by another operator");
            return ESP_ERR_INVALID_STATE;
        }

        config->fault->bound = oper;
    }

    auto* b = brake_of(*oper, config->fault);
    if (!b) {
        if (oper->brake_count >= 4) return ESP_ERR_NO_MEM;
        b = &oper->brakes[oper->brake_count++];
        b->fault = config->fault;
    }

    b->mode = config->brake_mode;
    evaluate_brakes(oper->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_operator_recover_from_fault(mcpwm_oper_handle_t oper, mcpwm_fault_handle_t fault) {
    if (!oper || !oper->used || !fault_ok(fault)) return ESP_ERR_INVALID_ARG;
    auto* b = brake_of(*oper, fault);
    if (!b) return ESP_ERR_INVALID_ARG;
    if (!fault->soft && fault->present) {
        mcpwm_log("mcpwm_operator_recover_from_fault", 307, "recovery failed: the fault source is still active");
        return ESP_ERR_INVALID_STATE;
    }

    if (b->mode == MCPWM_OPER_BRAKE_MODE_OST) oper->ost = false;   // the trip ends; another present fault trips it again
    evaluate_brakes(oper->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_operator_register_event_callbacks(mcpwm_oper_handle_t oper, const mcpwm_operator_event_callbacks_t* cbs, void* user_data) {
    if (!oper || !oper->used || !cbs) return ESP_ERR_INVALID_ARG;
    oper->on_cbc = cbs->on_brake_cbc;
    oper->on_ost = cbs->on_brake_ost;
    oper->brake_ctx = user_data;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_generator_set_action_on_brake_event(mcpwm_gen_handle_t gen, mcpwm_gen_brake_event_action_t ev) {
    if (!gen_ok(gen) || ev.brake_mode >= MCPWM_OPER_BRAKE_MODE_INVALID) return ESP_ERR_INVALID_ARG;
    gen->brake_action[ev.brake_mode == MCPWM_OPER_BRAKE_MODE_OST ? 1 : 0][ev.direction == MCPWM_TIMER_DIRECTION_DOWN ? 1 : 0] = (int)ev.action;
    update(gen->oper->group);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_generator_set_actions_on_brake_event(mcpwm_gen_handle_t gen, mcpwm_gen_brake_event_action_t ev, ...) {
    if (!gen_ok(gen)) return ESP_ERR_INVALID_ARG;
    va_list ap;
    va_start(ap, ev);
    esp_err_t rc = ESP_OK;
    for (mcpwm_gen_brake_event_action_t e = ev; e.brake_mode != MCPWM_OPER_BRAKE_MODE_INVALID; e = va_arg(ap, mcpwm_gen_brake_event_action_t)) {
        rc = mcpwm_generator_set_action_on_brake_event(gen, e);
        if (rc != ESP_OK) break;
    }

    va_end(ap);
    return rc;
}

/* ---- [sync] sync sources: a sync reloads the timers that take it with their phase (in their direction), as the chip's
   SYNCI_EN / TIMER_SYNCI_CFG do; a timer's sync out comes on its empty or full, or passes its own input on, and a software
   sync of a timer always gives its sync out too. The circuit sees an output by its frequency and duty, so a sync moves when
   the timer's events come (its callbacks, a capture's count) and the phase between timers of one period. */
namespace {

void reload(mcpwm_timer_t& t, double at_ns) {
    if (!t.used || !t.enabled || !t.running || t.resolution == 0) return;
    double length, zero, peak;
    period_events(t, length, zero, peak);
    double phase = (double)t.phase, pos;
    switch (t.mode) {
    case MCPWM_TIMER_COUNT_MODE_DOWN: pos = zero - fmin(phase, zero); break;
    case MCPWM_TIMER_COUNT_MODE_UP_DOWN: {
        double p = fmin(phase, peak);
        pos = t.phase_dir == MCPWM_TIMER_DIRECTION_DOWN && p > 0 ? length - p : p;
        break;
    }
    default: pos = fmin(phase, peak); break;
    }

    t.origin_ns = at_ns - pos * 1e9 / t.resolution;
    t.fired_ticks = pos;
    arm(t);
}

void sync_fire(mcpwm_sync_t* s, double at_ns, int depth) {
    if (!s || !s->used) return;
    for (auto& row : g_timers)
        for (auto& t : row) {
            if (!t.used || t.sync_in != s) continue;
            reload(t, at_ns);
            if (t.sync_out && t.sync_out->used && t.sync_out->propagate && depth < 3) sync_fire(t.sync_out, at_ns, depth + 1);
        }

    hal_mcpwm_cap_sync(s, at_ns);
}

mcpwm_sync_t* new_sync() {
    for (auto& s : g_syncs)
        if (!s.used) {
            s = mcpwm_sync_t{};
            s.used = true;
            s.gpio = -1;
            s.event = MCPWM_TIMER_EVENT_INVALID;
            return &s;
        }
    return nullptr;
}

bool sync_ok(mcpwm_sync_handle_t s) { return s && s->used; }

int g_sync_level[64];   // each GPIO sync pin as last seen (0 / 1, -1 unknown)
bool g_sync_levels_init;

}  // namespace

namespace rw {
void hal_mcpwm_sync_edge(int gpio, int level, uint64_t time_ns) {
    if (gpio < 0 || gpio >= 64) return;
    if (!g_sync_levels_init) {
        for (auto& l : g_sync_level) l = -1;
        g_sync_levels_init = true;
    }

    int was = g_sync_level[gpio];
    g_sync_level[gpio] = level ? 1 : 0;
    if (was < 0 || was == g_sync_level[gpio]) return;
    for (auto& s : g_syncs)
        if (s.used && s.kind == 1 && s.gpio == gpio && (level != 0) != s.active_neg) sync_fire(&s, (double)time_ns, 0);
}

int hal_mcpwm_sync_group(const void* sync, bool claim, int group) {
    auto* s = (mcpwm_sync_t*)sync;
    if (!s || !s->used) return -2;
    if (s->kind == 2 && claim && s->group < 0) s->group = group;
    return s->group;
}
}  // namespace rw

extern "C" esp_err_t mcpwm_new_timer_sync_src(mcpwm_timer_handle_t timer, const mcpwm_timer_sync_src_config_t* config, mcpwm_sync_handle_t* ret_sync) {
    charge(cost(4000.0));
    if (!timer || !timer->used || !config || !ret_sync) {
        mcpwm_log("mcpwm_new_timer_sync_src", 52, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    if (timer->sync_out) {
        mcpwm_log("mcpwm_new_timer_sync_src", 54, "timer sync_src already exists");
        return ESP_ERR_INVALID_STATE;
    }

    mcpwm_sync_t* s = new_sync();
    if (!s) return ESP_ERR_NO_MEM;
    s->kind = 0;
    s->group = timer->group;
    s->timer = timer;
    s->propagate = config->flags.propagate_input_sync;
    s->event = s->propagate ? MCPWM_TIMER_EVENT_INVALID : config->timer_event;
    timer->sync_out = s;
    rearm(timer);
    *ret_sync = s;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_new_gpio_sync_src(const mcpwm_gpio_sync_src_config_t* config, mcpwm_sync_handle_t* ret_sync) {
    charge(cost(4000.0));
    if (!config || !ret_sync || config->group_id < 0 || config->group_id > 1 || !digitalPinIsValid(config->gpio_num)) {
        mcpwm_log("mcpwm_new_gpio_sync_src", 118, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    int taken = 0;
    for (auto& o : g_syncs)
        if (o.used && o.kind == 1 && o.group == config->group_id) taken++;
    if (taken >= 3) {
        mcpwm_log("mcpwm_gpio_sync_src_register_to_group", 102, "no free gpio sync in group (%d)", config->group_id);
        return ESP_ERR_NOT_FOUND;
    }

    mcpwm_sync_t* s = new_sync();
    if (!s) return ESP_ERR_NO_MEM;
    s->kind = 1;
    s->group = config->group_id;
    s->gpio = config->gpio_num;
    s->active_neg = config->flags.active_neg;
    if (config->flags.pull_up) pinMode(config->gpio_num, INPUT_PULLUP);
    else if (config->flags.pull_down) pinMode(config->gpio_num, INPUT_PULLDOWN);
    else pinMode(config->gpio_num, INPUT);
    *ret_sync = s;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_new_soft_sync_src(const mcpwm_soft_sync_config_t* config, mcpwm_sync_handle_t* ret_sync) {
    charge(cost(2000.0));
    if (!config || !ret_sync) {
        mcpwm_log("mcpwm_new_soft_sync_src", 190, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    mcpwm_sync_t* s = new_sync();
    if (!s) return ESP_ERR_NO_MEM;
    s->kind = 2;
    s->group = -1;
    *ret_sync = s;
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_del_sync_src(mcpwm_sync_handle_t sync) {
    if (!sync_ok(sync)) return ESP_ERR_INVALID_ARG;
    for (auto& row : g_timers)
        for (auto& t : row) {
            if (t.sync_out == sync) {
                t.sync_out = nullptr;
                rearm(&t);
            }
            if (t.sync_in == sync) t.sync_in = nullptr;
        }

    *sync = mcpwm_sync_t{};
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_soft_sync_activate(mcpwm_sync_handle_t sync) {
    charge(cost(400.0));
    if (!sync_ok(sync) || sync->kind != 2) {
        mcpwm_log("mcpwm_soft_sync_activate", 240, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    double at = (double)now_ns();
    for (auto& row : g_timers)
        for (auto& t : row) {
            if (!t.used || t.sync_in != sync) continue;
            reload(t, at);
            if (t.sync_out) sync_fire(t.sync_out, at, 1);   // a software sync of a timer gives its sync out as well
        }

    hal_mcpwm_cap_sync(sync, at);
    return ESP_OK;
}

extern "C" esp_err_t mcpwm_timer_set_phase_on_sync(mcpwm_timer_handle_t timer, const mcpwm_timer_sync_phase_config_t* config) {
    charge(cost(1500.0));
    if (!timer || !timer->used || !config) {
        mcpwm_log("mcpwm_timer_set_phase_on_sync", 348, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    if (!config->sync_src) {
        timer->sync_in = nullptr;
        return ESP_OK;
    }

    if (!sync_ok(config->sync_src)) return ESP_ERR_INVALID_ARG;
    uint32_t peak_ticks = timer->mode == MCPWM_TIMER_COUNT_MODE_UP_DOWN ? timer->period_ticks / 2 : timer->period_ticks;
    if (config->count_value >= peak_ticks) {
        mcpwm_log("mcpwm_timer_set_phase_on_sync", 351, "invalid sync count value");
        return ESP_ERR_INVALID_ARG;
    }

    int group = hal_mcpwm_sync_group(config->sync_src, true, timer->group);
    if (group != timer->group) {
        mcpwm_log("mcpwm_timer_set_phase_on_sync", 357, "timer and sync source are not in the same group");
        return ESP_ERR_INVALID_ARG;
    }

    timer->sync_in = config->sync_src;
    timer->phase = config->count_value;
    timer->phase_dir = config->direction;
    return ESP_OK;
}

/* ---- [carrier] mcpwm_operator_apply_carrier, as IDF works it out from the group clock: a prescale of the group clock over
   8 a carrier period (1-16), the duty in eighths, the first pulse in whole carrier periods (1-16). */
extern "C" esp_err_t mcpwm_operator_apply_carrier(mcpwm_oper_handle_t oper, const mcpwm_carrier_config_t* config) {
    charge(cost(3000.0));
    if (!oper || !oper->used) {
        mcpwm_log("mcpwm_operator_apply_carrier", 278, "invalid argument");
        return ESP_ERR_INVALID_ARG;
    }

    uint32_t real_frequency = 0;
    if (config && config->frequency_hz) {
        uint32_t res = g_group_resolution[oper->group];
        uint8_t pre_scale = (uint8_t)(res / 8 / config->frequency_hz);
        if (pre_scale == 0 || pre_scale > 16) {
            mcpwm_log("mcpwm_operator_apply_carrier", 290, "group clock cannot match the frequency");
            return ESP_ERR_INVALID_STATE;
        }

        real_frequency = res / 8 / pre_scale;
        uint8_t duty = (uint8_t)(config->duty_cycle * 8);
        uint8_t first = (uint8_t)(config->first_pulse_duration_us * real_frequency / 1000000UL);
        if (first == 0 || first > 16) {
            mcpwm_log("mcpwm_operator_apply_carrier", 300, "invalid first pulse duration");
            return ESP_ERR_INVALID_ARG;
        }

        oper->carrier_period = 8.0 * pre_scale / res;
        oper->carrier_duty = (duty & 7) / 8.0;   // a three-bit field
        oper->carrier_first = first * oper->carrier_period;
        oper->carrier_in_invert = config->flags.invert_before_modulate;
        oper->carrier_out_invert = config->flags.invert_after_modulate;
    }

    oper->carrier = real_frequency > 0;
    update(oper->group);
    return ESP_OK;
}
