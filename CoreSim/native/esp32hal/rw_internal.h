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

/* Accounts execution time of an API call. A sketch task that passes the end of the
   host's slice blocks here until the host advances; may also switch to the ISR/system
   task or round-robin to another task. No-op off the sketch threads. */
void charge(uint64_t ns);
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

/* ---- implemented by the HAL files, called by the runtime ---- */
void hal_reset();                                          /* rw_init */
void hal_pin_changed(int gpio, int level, uint64_t time_ns);   /* rw_pin_changed (host thread) */
void hal_slice_start();                                    /* start of every rw_run_until */
int serial_rx_push(int port, const uint8_t* data, int length);
void run_serial_events();                                  /* after each loop() */
void hal_wifi_reset();

}  // namespace rw

#endif
