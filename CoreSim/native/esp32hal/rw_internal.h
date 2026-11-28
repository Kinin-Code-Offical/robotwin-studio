/* Internal interface between the scheduler (rw_runtime.cpp) and the HAL implementation files.
   Not for sketches. Must not include <windows.h> (its macros clash with Arduino names). */
#ifndef RW_INTERNAL_H
#define RW_INTERNAL_H
#include <stdint.h>
#include <stddef.h>
#include "rw_host_api.h"

namespace rw {

extern rw_host_api api;
extern rw_config config;
extern bool has_api;

const uint64_t kForever = UINT64_MAX;

/* ---- simulated time (absolute host nanoseconds) ---- */
uint64_t now_ns();
uint64_t uptime_ns();                 /* since the chip reset: millis()/micros() base */
uint64_t ms_to_ns(uint64_t ms);
uint64_t cost(double nanoseconds);    /* scaled by rw_config.cost_scale */
/* The n-th FreeRTOS tick boundary (1 ms) from now, absolute: where vTaskDelay(n) and tick timeouts end. */
uint64_t tick_deadline(uint32_t ticks);

/* Accounts execution time of an API call. A sketch task that passes the end of the
   host's slice blocks here until the host advances; may also switch to the ISR/system
   task or round-robin to another task. No-op off the sketch threads. */
void charge(uint64_t ns);
extern bool g_port_registers_used;   /* a library holds a port register pointer: charge() settles the copies (rw_hal.cpp) */
void hal_port_settle();
/* delay()/vTaskDelay(): block the calling task until the given absolute time. */
void sleep_until(uint64_t at_ns);
/* yield(): let other ready tasks run. */
void yield_task();
/* Block until the host starts its next slice (fresh inputs) or the deadline. */
void wait_slice(uint64_t deadline_ns);
/* Block until notify(obj) or the deadline. Returns true when notified. */
bool wait_on(const void* obj, uint64_t deadline_ns);
void notify(const void* obj);

bool on_task();        /* calling thread is a sketch task (incl. the ISR task) */
bool in_isr();         /* running an interrupt handler / timer callback */
void* current_task();

/* Host-call bracket: marks the thread as inside the API (watchdog / safe-stop bookkeeping). */
struct ApiScope {
    ApiScope();
    ~ApiScope();
};

void log(int level, const char* format, ...);
void log_once(int key, int level, const char* format, ...);

/* ---- interrupts and timed events, run on the system (ISR) task ---- */
typedef void (*isr_fn)(void* arg);
void raise_isr(isr_fn fn, void* arg);
int schedule_event(uint64_t at_ns, isr_fn fn, void* arg);   /* returns an id > 0 */
void cancel_event(int id);
void set_interrupts_enabled(bool enabled);

/* ---- tasks ---- */
void* create_task(void (*fn)(void*), const char* name, uint32_t stack, void* arg, int priority);
void delete_task(void* task);          /* nullptr = calling task (does not return) */
void suspend_task(void* task);
void resume_task(void* task);
const char* task_name(void* task);
int task_priority(void* task);
void set_task_priority(void* task, int priority);
int task_count();
void* find_task(const char* name);
uint32_t* task_notify_value(void* task);
void block_forever();                  /* calling task never runs again (restart / deep sleep) */

/* ---- misc ---- */
uint32_t random32();
void seed_random(uint64_t seed);
void* heap_alloc(size_t n);
void* heap_realloc(void* p, size_t n);
void heap_free(void* p);
size_t heap_in_use();
size_t task_stack_bytes();

/* ---- implemented by the HAL files, called by the runtime ---- */
void hal_reset();
void twdt_reset();                                         /* chip reset: TWDT back to boot defaults (rw_task_wdt.cpp) */
void twdt_loop_iteration();                                /* before every loop(): feeds the loop WDT when enabled */
void system_restart(int reset_reason);                     /* reboot the chip with an esp_reset_reason_t (rw_hal.cpp) */                                          /* rw_init */
void hal_pin_changed(int gpio, int level, uint64_t time_ns);   /* rw_pin_changed (host thread) */
void hal_slice_start();                                    /* start of every rw_run_until */
int serial_rx_push(int port, const uint8_t* data, int length);
void run_serial_events();                                  /* after each loop() */
void hal_wifi_reset();
void hal_rmt_reset();
void hal_pcnt_reset();
void hal_gpio_driver_reset();
void hal_mcpwm_reset();
void hal_mcpwm_cap_reset();                                /* every capture timer and channel released (rw_mcpwm_cap.cpp) */
void hal_mcpwm_cap_edge(int gpio, int level, uint64_t time_ns);   /* an input pin's edge, for the capture channels on it */
void hal_mcpwm_fault_edge(int gpio, int level);            /* and for the MCPWM's GPIO faults on it (rw_mcpwm.cpp) */
void hal_mcpwm_sync_edge(int gpio, int level, uint64_t time_ns);   /* and for its GPIO sync sources (rw_mcpwm.cpp) */
int hal_mcpwm_sync_group(const void* sync, bool claim, int group);   /* a sync source's group (-2: none); a software one takes `group` when claimed */
void hal_mcpwm_cap_sync(const void* sync, double at_ns);   /* a sync event, for the capture timers that take it (rw_mcpwm_cap.cpp) */
void hal_mcpwm_cap_pwm(int gpio, double frequency, double duty, uint64_t now_ns);   /* a pin's own PWM changed */
/* [legacy] the legacy MCPWM driver (rw_mcpwm_legacy.cpp) on the new driver's model, in plain types (its own types clash
   with the new driver's): timer n with operator n (rw_mcpwm.cpp), the group's capture (rw_mcpwm_cap.cpp) */
void hal_mcpwm_legacy_reset();
void mcpwm_legacy_config(int unit, int timer, uint32_t resolution, int count_mode, uint32_t period_ticks);
uint32_t mcpwm_legacy_period(int unit, int timer);
int mcpwm_legacy_mode(int unit, int timer);
uint32_t mcpwm_legacy_resolution(int unit, int timer);
void mcpwm_legacy_compare(int unit, int timer, int which, uint32_t value);
uint32_t mcpwm_legacy_compare_value(int unit, int timer, int which);
void mcpwm_legacy_actions(int unit, int timer, int gen, uint32_t actions);
void mcpwm_legacy_force(int unit, int timer, int gen, int level);
void mcpwm_legacy_pin(int unit, int timer, int gen, int gpio);
void mcpwm_legacy_run(int unit, int timer, bool run);
void mcpwm_legacy_dead_time(int unit, int timer, int out, int from, double red_s, double fed_s, bool invert, bool on);
typedef bool (*legacy_cap_fn)(int unit, int channel, uint32_t value, bool negative, void* user);
bool mcpwm_legacy_capture(int unit, int channel, int gpio, bool pos, bool neg, uint32_t prescale, legacy_cap_fn fn, void* user);
void mcpwm_legacy_capture_off(int unit, int channel);
void mcpwm_legacy_capture_gpio(int unit, int channel, int gpio);
uint32_t mcpwm_legacy_capture_value(int unit, int channel);
bool mcpwm_legacy_capture_negative(int unit, int channel);                                    /* chip reset: every MCPWM timer and operator released (rw_mcpwm.cpp) */
void hal_pwm_out(int gpio, double frequency, double duty);  /* a PWM on a pin through the host (duty < 0: released) */                               /* chip reset: driver/gpio.h's pin configurations (rw_gpio_driver.cpp) */                                     /* chip reset: every pulse counter unit released (rw_pcnt.cpp) */
void hal_pcnt_edge(int gpio, int level, uint64_t time_ns);
void hal_pcnt_settle(uint64_t now_ns);
void hal_pcnt_pwm(int gpio, double frequency, double duty, uint64_t now_ns);   /* [pcnt-pwm] a pin's own PWM (LEDC, MCPWM) changed */                       /* [pcnt-filter] edges that outlasted the glitch filter count */                   /* an input pin changed level (host thread, from hal_pin_changed) */
int hal_pin_level(int gpio);                               /* an input pin's level as last seen, or -1 (rw_hal.cpp) */
bool hal_pin_drives(int gpio);                             /* the sketch drives the pin (output, LEDC, DAC, tone) */                                      /* chip reset: every RMT channel released (rw_rmt.cpp) */

/* ---- radio medium (rw_radio.cpp): RW_RADIO_UNSUPPORTED when the host has none ---- */
int radio(int op, const void* in, int in_len, void* out, int out_cap);
bool radio_available();
const uint8_t* radio_mac();   /* base MAC (from the host, or the stub default) */
void hal_ble_reset();         /* BLE.cpp */
void hal_bp32_reset();        /* Bluepad32.cpp */

}  // namespace rw

#endif
