/*
 * RoboTwin ESP32 simulation runtime: exported C ABI, simulated-time scheduler,
 * cooperative tasks, interrupt/timer dispatch, watchdog, crash capture, heap.
 *
 * Execution model
 * ---------------
 * Every sketch task (loopTask running setup()/loop(), FreeRTOS tasks and the
 * system task that runs interrupt handlers and timer callbacks) is a Win32
 * thread, but only one of them - or the host - runs at any moment: control is
 * handed over like a baton under one critical section. Simulated time g_now is
 * shared (one CPU). The host calls rw_run_until(t); tasks then run until all of
 * them are blocked past t (delay, waits) or their charged execution time passes
 * t, at which point the host gets the baton back and steps the circuit.
 *
 * A task that never calls into the API cannot be preempted; if it keeps the
 * baton for longer than rw_config.watchdog_ms of wall-clock time,
 * rw_run_until() reports RW_STATUS_WATCHDOG and rw_stop() removes the thread.
 */
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <process.h>
#include <malloc.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <exception>
#include <new>
#include <vector>
#include "rw_internal.h"

#define RW_EXPORT extern "C" __declspec(dllexport)

void setup(void);
void loop(void);

namespace rw {

rw_host_api api;
rw_config config;
bool has_api = false;

namespace {

const uint64_t kTickNs = 1000000ull;  // FreeRTOS tick: round-robin time slice

struct AbortSketch {};
struct TaskExit {};

struct Task {
    HANDLE thread = nullptr;
    CONDITION_VARIABLE cv;
    bool go = false;
    bool finished = false;
    bool kill = false;
    bool suspended = false;
    bool is_sys = false;
    bool signaled = false;
    uint64_t wake = 0;
    const void* wait_obj = nullptr;
    int prio = 1;
    uint32_t stack = 0;   // bytes of stack the sketch asked for: on the chip they come from the heap
    uint64_t seq = 0;
    uint64_t slice_start = 0;
    void (*fn)(void*) = nullptr;
    void* arg = nullptr;
    char name[32] = {0};
    volatile LONG in_api = 0;
    uint32_t notify[2] = {0, 0};  // FreeRTOS task notification: value, pending
};

INIT_ONCE g_init_once = INIT_ONCE_STATIC_INIT;
CRITICAL_SECTION g_cs;
CONDITION_VARIABLE g_host_cv;

BOOL CALLBACK init_sched(PINIT_ONCE, PVOID, PVOID*) {
    InitializeCriticalSection(&g_cs);
    InitializeConditionVariable(&g_host_cv);
    return TRUE;
}

void sched_init() { InitOnceExecuteOnce(&g_init_once, init_sched, nullptr, nullptr); }

struct Lock {
    Lock() { sched_init(); EnterCriticalSection(&g_cs); }
    ~Lock() { LeaveCriticalSection(&g_cs); }
    Lock(const Lock&) = delete;
    Lock& operator=(const Lock&) = delete;
};

std::vector<Task*> g_tasks;
Task* g_sys = nullptr;
Task* g_loop = nullptr;
Task* g_running = nullptr;
bool g_host_turn = true;
bool g_started = false;
bool g_stopping = false;
volatile bool g_stopping_flag = false;
bool g_crashed = false;
bool g_hung = false;
uint64_t g_now = 0;
uint64_t g_limit = 0;
uint64_t g_seq = 0;
uint64_t g_preempt_at = UINT64_MAX;
bool g_preempt_hint = false;
volatile LONGLONG g_last_api_tick = 0;
int g_status = RW_STATUS_OK;
char g_status_msg[2048];
thread_local Task* t_self = nullptr;
thread_local int t_isr_depth = 0;

struct IsrItem { isr_fn fn; void* arg; };
struct TimedItem { uint64_t at; int id; isr_fn fn; void* arg; };
std::vector<IsrItem> g_isr_queue;
std::vector<TimedItem> g_timed;
int g_next_event_id = 1;
bool g_irq_enabled = true;
uint64_t g_sys_due = UINT64_MAX;

inline void touch_watchdog() { InterlockedExchange64(&g_last_api_tick, (LONGLONG)GetTickCount64()); }

void update_sys_due() {
    uint64_t due = UINT64_MAX;
    if (g_irq_enabled && !g_isr_queue.empty()) {
        due = 0;
    } else {
        for (const TimedItem& e : g_timed) if (e.at < due) due = e.at;
    }
    g_sys_due = due;
}

inline bool eligible(const Task* t) { return !t->finished && !t->suspended; }

void compute_preempt_at(const Task* cur) {
    uint64_t at = UINT64_MAX;
    for (const Task* u : g_tasks) {
        if (u == cur || u->is_sys || !eligible(u) || u->prio <= cur->prio) continue;
        if (u->wake < at) at = u->wake;
    }
    g_preempt_at = at;
}

Task* pick_next() {
    if (g_crashed || g_stopping) return nullptr;
    if (g_sys) g_sys->wake = g_sys_due;
    uint64_t earliest = UINT64_MAX;
    for (const Task* t : g_tasks) {
        if (eligible(t) && t->wake < earliest) earliest = t->wake;
    }
    if (earliest == UINT64_MAX) return nullptr;
    uint64_t at = earliest > g_now ? earliest : g_now;
    if (at > g_limit) return nullptr;
    Task* best = nullptr;
    for (Task* t : g_tasks) {
        if (!eligible(t) || t->wake > at) continue;
        if (!best) { best = t; continue; }
        if (t->is_sys != best->is_sys) { if (t->is_sys) best = t; continue; }
        if (t->prio != best->prio) { if (t->prio > best->prio) best = t; continue; }
        if (t->seq < best->seq) best = t;
    }
    return best;
}

void hand_to(Task* next) {
    if (!next) {
        g_running = nullptr;
        g_host_turn = true;
        WakeConditionVariable(&g_host_cv);
        return;
    }

    if (next->wake > g_now) g_now = next->wake;
    next->seq = ++g_seq;
    next->slice_start = g_now;
    compute_preempt_at(next);
    g_running = next;
    next->go = true;
    WakeConditionVariable(&next->cv);
}

void wait_turn(Task* t) {
    while (!t->go && !g_stopping) SleepConditionVariableCS(&t->cv, &g_cs, INFINITE);
    t->go = false;
    if (g_stopping) throw AbortSketch();
    touch_watchdog();
    if (t->kill) throw TaskExit();
}

/* The calling task gives up the CPU (its wake time is already set). Lock held. */
void switch_away(Task* t) {
    g_preempt_hint = false;
    Task* next = pick_next();
    if (next == t) {
        if (t->wake > g_now) g_now = t->wake;
        t->slice_start = g_now;
        compute_preempt_at(t);
        return;
    }

    hand_to(next);
    wait_turn(t);
}

bool has_ready_peer(const Task* t) {
    for (const Task* u : g_tasks) {
        if (u != t && !u->is_sys && eligible(u) && u->prio >= t->prio && u->wake <= g_now) return true;
    }
    return false;
}

void report(int status, const char* format, ...) {
    va_list ap;
    va_start(ap, format);
    vsnprintf(g_status_msg, sizeof g_status_msg, format, ap);
    va_end(ap);
    g_status = status;
}

void report_crash(const Task* t, const char* what) {
    g_crashed = true;
    report(RW_STATUS_CRASHED, "%s\r\n(simulator: in task %s at %.6f s)", what, t ? t->name : "?",
           (double)(g_now - config.time_origin_ns) * 1e-9);
}

int seh_filter(Task* t, EXCEPTION_POINTERS* ep) {
    DWORD code = ep->ExceptionRecord->ExceptionCode;
    if (code == 0xE06D7363) return EXCEPTION_CONTINUE_SEARCH;  // C++ exception: handled by catch blocks
    char what[512];
    switch (code) {
    case EXCEPTION_ACCESS_VIOLATION: {
        ULONG_PTR kind = ep->ExceptionRecord->NumberParameters > 1 ? ep->ExceptionRecord->ExceptionInformation[0] : 0;
        ULONG_PTR addr = ep->ExceptionRecord->NumberParameters > 1 ? ep->ExceptionRecord->ExceptionInformation[1] : 0;
        snprintf(what, sizeof what,
                 "Guru Meditation Error: Core  1 panic'ed (%s). Exception was unhandled.\r\nEXCVADDR: 0x%08llx",
                 kind == 1 ? "StoreProhibited" : kind == 8 ? "InstrFetchProhibited" : "LoadProhibited",
                 (unsigned long long)addr);
        break;
    }
    case EXCEPTION_INT_DIVIDE_BY_ZERO:
        snprintf(what, sizeof what, "Guru Meditation Error: Core  1 panic'ed (IntegerDivideByZero). Exception was unhandled.");
        break;
    case EXCEPTION_STACK_OVERFLOW:
        snprintf(what, sizeof what, "***ERROR*** A stack overflow in task %s has been detected.", t ? t->name : "?");
        break;
    case EXCEPTION_ILLEGAL_INSTRUCTION:
    case EXCEPTION_PRIV_INSTRUCTION:
        snprintf(what, sizeof what, "Guru Meditation Error: Core  1 panic'ed (IllegalInstruction). Exception was unhandled.");
        break;
    default:
        snprintf(what, sizeof what, "Guru Meditation Error: Core  1 panic'ed (Unhandled exception 0x%08lX).", (unsigned long)code);
        break;
    }

    report_crash(t, what);
    return EXCEPTION_EXECUTE_HANDLER;
}

void run_task_cpp(Task* t) {
    try {
        {
            Lock l;
            wait_turn(t);
        }
        t->fn(t->arg);
    } catch (AbortSketch&) {
    } catch (TaskExit&) {
    } catch (std::exception& e) {
        char what[512];
        snprintf(what, sizeof what, "abort() was called: unhandled C++ exception: %s", e.what());
        report_crash(t, what);
    } catch (...) {
        report_crash(t, "abort() was called: unhandled C++ exception");
    }
}

void run_task_seh(Task* t) {
    __try {
        run_task_cpp(t);
    } __except (seh_filter(t, GetExceptionInformation())) {
        if (GetExceptionCode() == EXCEPTION_STACK_OVERFLOW) _resetstkoflw();
    }
}

unsigned __stdcall task_entry(void* p) {
    Task* t = static_cast<Task*>(p);
    t_self = t;
    run_task_seh(t);
    Lock l;
    t->finished = true;
    if (g_running == t && !g_stopping) {
        if (g_crashed) {
            g_running = nullptr;
            g_host_turn = true;
            WakeConditionVariable(&g_host_cv);
        } else {
            hand_to(pick_next());
        }
    }

    return 0;
}

void hijacked_exit() { _endthreadex(0xDEAD); }

/* Removes a thread that does not stop cooperatively (it is spinning in sketch code). */
void force_exit(Task* t) {
    for (int attempt = 0; attempt < 100; attempt++) {
        if (WaitForSingleObject(t->thread, 0) == WAIT_OBJECT_0) return;
        if (SuspendThread(t->thread) == (DWORD)-1) break;
        if (t->in_api == 0) {
            CONTEXT ctx;
            memset(&ctx, 0, sizeof ctx);
            ctx.ContextFlags = CONTEXT_CONTROL;
            if (GetThreadContext(t->thread, &ctx)) {
                // Redirect it to _endthreadex so the CRT and the CLR see a normal thread exit.
                DWORD64 sp = ((ctx.Rsp - 1024) & ~(DWORD64)15) - 8;
                ctx.Rsp = sp;
                ctx.Rip = (DWORD64)(void*)&hijacked_exit;
                if (SetThreadContext(t->thread, &ctx)) {
                    ResumeThread(t->thread);
                    if (WaitForSingleObject(t->thread, 2000) == WAIT_OBJECT_0) return;
                    SuspendThread(t->thread);
                }
            }

            TerminateThread(t->thread, 0xDEAD);
            WaitForSingleObject(t->thread, 2000);
            return;
        }

        ResumeThread(t->thread);
        WaitForSingleObject(t->thread, 20);
    }

    TerminateThread(t->thread, 0xDEAD);
    WaitForSingleObject(t->thread, 2000);
}

Task* new_task(void (*fn)(void*), const char* name, int prio, bool is_sys) {
    Task* t = new Task();
    InitializeConditionVariable(&t->cv);
    t->fn = fn;
    t->prio = prio;
    t->is_sys = is_sys;
    t->wake = g_now;
    strncpy_s(t->name, sizeof t->name, name ? name : "task", _TRUNCATE);
    g_tasks.push_back(t);
    uintptr_t h = _beginthreadex(nullptr, 1u << 20, task_entry, t, STACK_SIZE_PARAM_IS_A_RESERVATION, nullptr);
    t->thread = reinterpret_cast<HANDLE>(h);
    if (!t->thread) {
        t->finished = true;
    }

    return t;
}

void loop_task_main(void*) {
    setup();
    for (;;) {
        twdt_loop_iteration();
        loop();
        run_serial_events();
        charge(cost(1000.0));  // loopTask bookkeeping per iteration (keeps an empty loop() advancing time)
    }
}

void sys_task_main(void*) {
    for (;;) {
        for (;;) {
            if (g_irq_enabled && !g_isr_queue.empty()) {
                IsrItem item = g_isr_queue.front();
                g_isr_queue.erase(g_isr_queue.begin());
                update_sys_due();
                t_isr_depth++;
                item.fn(item.arg);
                t_isr_depth--;
                continue;
            }

            int best = -1;
            for (int i = 0; i < (int)g_timed.size(); i++) {
                if (g_timed[i].at <= g_now && (best < 0 || g_timed[i].at < g_timed[best].at)) best = i;
            }

            if (best >= 0) {
                TimedItem e = g_timed[best];
                g_timed.erase(g_timed.begin() + best);
                update_sys_due();
                t_isr_depth++;
                e.fn(e.arg);
                t_isr_depth--;
                continue;
            }

            break;
        }

        Lock l;
        update_sys_due();
        g_sys->wake = g_sys_due;
        switch_away(g_sys);
    }
}

std::vector<int> g_log_once_keys;

/* ----------------------------------------------------------------- private heap */
HANDLE g_heap = nullptr;
bool g_heap_dead = false;
volatile LONG64 g_heap_bytes = 0;

void destroy_heap() {
    g_heap_dead = true;
    if (g_heap) HeapDestroy(g_heap);
    g_heap = nullptr;
}

HANDLE heap() {
    if (!g_heap && !g_heap_dead) {
        HANDLE h = HeapCreate(0, 0, 0);
        if (InterlockedCompareExchangePointer(&g_heap, h, nullptr) != nullptr) {
            HeapDestroy(h);
        } else {
            atexit(destroy_heap);
        }
    }

    return g_heap;
}

uint64_t g_rng = 0x9E3779B97F4A7C15ull;

}  // namespace

/* ======================================================================= public (rw::) */

uint64_t now_ns() { return g_now; }

uint64_t uptime_ns() { return g_now > config.time_origin_ns ? g_now - config.time_origin_ns : 0; }

uint64_t tick_deadline(uint32_t ticks) {
    uint64_t tick = uptime_ns() / 1000000ull;   // ticks since the scheduler started (the app's start)
    return config.time_origin_ns + (tick + ticks) * 1000000ull;
}

uint64_t ms_to_ns(uint64_t ms) { return ms * 1000000ull; }

uint64_t cost(double ns) {
    double scale = config.cost_scale > 0.0 ? config.cost_scale : 1.0;
    double v = ns * scale;
    return v <= 0.0 ? 0 : (uint64_t)(v + 0.5);
}

bool g_port_registers_used = false;

void charge(uint64_t ns) {
    Task* t = t_self;
    if (!t) return;
    if (g_port_registers_used) hal_port_settle();   // what a library wrote through a port register pointer, onto the pins
    touch_watchdog();
    g_now += ns;
    if (t->is_sys) return;  // interrupt handlers never switch away
    if (g_stopping_flag) throw AbortSketch();
    if (g_now <= g_limit && g_now < g_sys_due && g_now < g_preempt_at && !g_preempt_hint) {
        if (g_now - t->slice_start < kTickNs) return;
        Lock l;
        if (!has_ready_peer(t)) {
            t->slice_start = g_now;
            return;
        }

        t->wake = g_now;
        switch_away(t);
        return;
    }

    Lock l;
    t->wake = g_now;
    switch_away(t);
}

void sleep_until(uint64_t at) {
    Task* t = t_self;
    if (!t) return;
    touch_watchdog();
    if (t->is_sys) {  // delay() inside an interrupt handler: busy wait
        if (at > g_now) g_now = at;
        return;
    }

    Lock l;
    t->wake = at > g_now ? at : g_now;
    switch_away(t);
}

void yield_task() {
    Task* t = t_self;
    if (!t || t->is_sys) return;
    touch_watchdog();
    Lock l;
    t->wake = g_now;
    switch_away(t);
}

void wait_slice(uint64_t deadline) {
    uint64_t next = g_limit + 1;
    sleep_until(deadline < next ? deadline : next);
}

bool wait_on(const void* obj, uint64_t deadline) {
    Task* t = t_self;
    if (!t || t->is_sys) return false;
    touch_watchdog();
    Lock l;
    t->wait_obj = obj;
    t->signaled = false;
    t->wake = deadline;
    switch_away(t);
    t->wait_obj = nullptr;
    return t->signaled;
}

void notify(const void* obj) {
    Lock l;
    for (Task* u : g_tasks) {
        if (u->wait_obj != obj || u->finished) continue;
        u->wait_obj = nullptr;
        u->signaled = true;
        u->wake = g_now;
        if (g_running && !g_running->is_sys && u->prio > g_running->prio) g_preempt_hint = true;
    }
}

bool on_task() { return t_self != nullptr; }

bool in_isr() { return t_isr_depth > 0; }

void* current_task() { return t_self ? t_self : g_loop; }

ApiScope::ApiScope() {
    Task* t = t_self;
    if (t) InterlockedIncrement(&t->in_api);
    touch_watchdog();
}

ApiScope::~ApiScope() {
    Task* t = t_self;
    if (t) InterlockedDecrement(&t->in_api);
    touch_watchdog();
}

static void vlog(int level, const char* format, va_list ap) {
    if (!has_api || !api.log) return;
    char buf[512];
    vsnprintf(buf, sizeof buf, format, ap);
    ApiScope scope;
    api.log(api.user, level, buf);
}

void log(int level, const char* format, ...) {
    va_list ap;
    va_start(ap, format);
    vlog(level, format, ap);
    va_end(ap);
}

void log_once(int key, int level, const char* format, ...) {
    for (int k : g_log_once_keys) if (k == key) return;
    g_log_once_keys.push_back(key);
    va_list ap;
    va_start(ap, format);
    vlog(level, format, ap);
    va_end(ap);
}

void raise_isr(isr_fn fn, void* arg) {
    g_isr_queue.push_back(IsrItem{fn, arg});
    update_sys_due();
}

int schedule_event(uint64_t at, isr_fn fn, void* arg) {
    int id = g_next_event_id++;
    g_timed.push_back(TimedItem{at, id, fn, arg});
    update_sys_due();
    return id;
}

void cancel_event(int id) {
    for (size_t i = 0; i < g_timed.size(); i++) {
        if (g_timed[i].id == id) {
            g_timed.erase(g_timed.begin() + i);
            break;
        }
    }

    update_sys_due();
}

void set_interrupts_enabled(bool enabled) {
    g_irq_enabled = enabled;
    update_sys_due();
}

void* create_task(void (*fn)(void*), const char* name, uint32_t stack, void* arg, int priority) {
    Lock l;
    Task* t = new_task(fn, name, priority, false);
    t->arg = arg;
    t->stack = stack;
    t->wake = g_now;
    if (g_running && !g_running->is_sys && priority > g_running->prio) g_preempt_hint = true;
    return t;
}

void delete_task(void* task) {
    Task* t = static_cast<Task*>(task);
    if (!t || t == t_self) {
        if (t_self && !t_self->is_sys) throw TaskExit();
        return;
    }

    Lock l;
    t->kill = true;
    t->suspended = false;
    t->wait_obj = nullptr;
    t->wake = g_now;
}

void suspend_task(void* task) {
    Task* t = task ? static_cast<Task*>(task) : t_self;
    if (!t || t->is_sys) return;
    Lock l;
    t->suspended = true;
    if (t == t_self) switch_away(t);
}

void resume_task(void* task) {
    Task* t = static_cast<Task*>(task);
    if (!t) return;
    Lock l;
    if (!t->suspended) return;
    t->suspended = false;
    if (t->wake < g_now || t->wait_obj == nullptr) t->wake = g_now;
    if (g_running && !g_running->is_sys && t->prio > g_running->prio) g_preempt_hint = true;
}

const char* task_name(void* task) {
    Task* t = task ? static_cast<Task*>(task) : t_self;
    return t ? t->name : "";
}

int task_priority(void* task) {
    Task* t = task ? static_cast<Task*>(task) : t_self;
    return t ? t->prio : 0;
}

void set_task_priority(void* task, int priority) {
    Task* t = task ? static_cast<Task*>(task) : t_self;
    if (!t || t->is_sys) return;
    Lock l;
    t->prio = priority;
    g_preempt_hint = true;
}

int task_count() {
    int n = 0;
    for (const Task* t : g_tasks) if (!t->finished) n++;
    return n + 2;  // + IDLE0/IDLE1
}

void* find_task(const char* name) {
    for (Task* t : g_tasks) if (!t->finished && strcmp(t->name, name) == 0) return t;
    return nullptr;
}

uint32_t* task_notify_value(void* task) {
    Task* t = task ? static_cast<Task*>(task) : t_self;
    return t ? t->notify : nullptr;
}

void block_forever() {
    Task* t = t_self;
    if (!t || t->is_sys) return;
    Lock l;
    t->wake = UINT64_MAX;
    switch_away(t);
}

uint32_t random32() {
    uint64_t x = g_rng;
    x ^= x >> 12;
    x ^= x << 25;
    x ^= x >> 27;
    g_rng = x;
    return (uint32_t)((x * 0x2545F4914F6CDD1Dull) >> 32);
}

void seed_random(uint64_t seed) { g_rng = seed ? seed : 0x9E3779B97F4A7C15ull; }

void* heap_alloc(size_t n) {
    HANDLE h = heap();
    if (!h) return (malloc)(n ? n : 1);
    void* p = HeapAlloc(h, 0, n ? n : 1);
    if (p) InterlockedAdd64(&g_heap_bytes, (LONG64)HeapSize(h, 0, p));
    return p;
}

void* heap_realloc(void* p, size_t n) {
    if (!p) return heap_alloc(n);
    HANDLE h = heap();
    if (!h) return nullptr;
    LONG64 before = (LONG64)HeapSize(h, 0, p);
    void* q = HeapReAlloc(h, 0, p, n ? n : 1);
    if (q) InterlockedAdd64(&g_heap_bytes, (LONG64)HeapSize(h, 0, q) - before);
    return q;
}

void heap_free(void* p) {
    if (!p || g_heap_dead || !g_heap) return;
    InterlockedAdd64(&g_heap_bytes, -(LONG64)HeapSize(g_heap, 0, p));
    HeapFree(g_heap, 0, p);
}

size_t heap_in_use() { return (size_t)(g_heap_bytes > 0 ? g_heap_bytes : 0); }

/* The stacks of the sketch's own running tasks: xTaskCreate takes them from the heap on the chip. */
size_t task_stack_bytes() {
    Lock l;   // a critical section: taking it again is fine
    size_t n = 0;
    for (const Task* t : g_tasks)
        if (!t->finished && !t->is_sys) n += t->stack;
    return n;
}

}  // namespace rw

/* ======================================================================= C allocation -> the chip's heap
   [mem-budget] The sketch's malloc, calloc, realloc and free (Arduino.h names them so) come from the same private
   heap as new, so what it holds is counted against the chip's heap. Each block carries a tag before it: a pointer
   that did not come from here (strdup and other CRT functions allocate with the CRT) goes back to the CRT. */
namespace {
const uint64_t kBlockTag = 0x52574D454D424C4Bull;   // "RWMEMBLK"
struct BlockHead {
    uint64_t tag;
    uint64_t size;
};

inline BlockHead* head_of(void* p) { return reinterpret_cast<BlockHead*>(static_cast<char*>(p) - sizeof(BlockHead)); }

inline bool ours(void* p) { return p && head_of(p)->tag == kBlockTag; }
}  // namespace

extern "C" void* rw_malloc(size_t n) {
    auto* h = static_cast<BlockHead*>(rw::heap_alloc(n + sizeof(BlockHead)));
    if (!h) return nullptr;
    h->tag = kBlockTag;
    h->size = n;
    return h + 1;
}

extern "C" void* rw_calloc(size_t count, size_t size) {
    if (size && count > (size_t)-1 / size) return nullptr;
    void* p = rw_malloc(count * size);
    if (p) memset(p, 0, count * size);
    return p;
}

extern "C" void rw_free(void* p) {
    if (!p) return;
    if (!ours(p)) {
        (free)(p);
        return;
    }

    BlockHead* h = head_of(p);
    h->tag = 0;
    rw::heap_free(h);
}

extern "C" void* rw_realloc(void* p, size_t n) {
    if (!p) return rw_malloc(n);
    if (!ours(p)) return (realloc)(p, n);
    if (n == 0) {
        rw_free(p);
        return nullptr;
    }

    auto* h = static_cast<BlockHead*>(rw::heap_realloc(head_of(p), n + sizeof(BlockHead)));
    if (!h) return nullptr;
    h->size = n;
    return h + 1;
}

/* ======================================================================= C++ allocation -> private heap
   A watchdog-killed thread may die while holding a heap lock; keeping the sketch's
   allocations in a private heap that disappears with the DLL protects the host. */
void* operator new(size_t n) {
    void* p = rw::heap_alloc(n);
    if (!p) throw std::bad_alloc();
    return p;
}
void* operator new[](size_t n) {
    void* p = rw::heap_alloc(n);
    if (!p) throw std::bad_alloc();
    return p;
}
void* operator new(size_t n, const std::nothrow_t&) noexcept { return rw::heap_alloc(n); }
void* operator new[](size_t n, const std::nothrow_t&) noexcept { return rw::heap_alloc(n); }
void operator delete(void* p) noexcept { rw::heap_free(p); }
void operator delete[](void* p) noexcept { rw::heap_free(p); }
void operator delete(void* p, size_t) noexcept { rw::heap_free(p); }
void operator delete[](void* p, size_t) noexcept { rw::heap_free(p); }
void operator delete(void* p, const std::nothrow_t&) noexcept { rw::heap_free(p); }
void operator delete[](void* p, const std::nothrow_t&) noexcept { rw::heap_free(p); }

/* ======================================================================= exported C ABI */
using namespace rw;

RW_EXPORT int rw_abi_version(void) { return RW_ABI_VERSION; }

RW_EXPORT int rw_init(const rw_host_api* host, const rw_config* cfg) {
    if (!host || host->abi_version != RW_ABI_VERSION || host->struct_size < sizeof(rw_host_api)) return -1;
    if (g_started) return -2;
    sched_init();
    api = *host;
    has_api = true;
    memset(&config, 0, sizeof config);
    if (cfg) memcpy(&config, cfg, cfg->struct_size < sizeof config ? cfg->struct_size : sizeof config);
    config.struct_size = sizeof config;
    if (config.watchdog_ms == 0) config.watchdog_ms = 2000;
    if (!(config.cost_scale > 0.0)) config.cost_scale = 1.0;
    g_now = g_limit = config.time_origin_ns;
    seed_random(config.random_seed);
    g_log_once_keys.clear();
    g_status = RW_STATUS_OK;
    g_status_msg[0] = 0;
    hal_reset();
    return 0;
}

RW_EXPORT int rw_start(uint64_t start_ns) {
    if (!has_api || g_started) return -1;
    Lock l;
    g_stopping = false;
    g_stopping_flag = false;
    g_crashed = false;
    g_hung = false;
    g_irq_enabled = true;
    g_status = RW_STATUS_OK;
    g_status_msg[0] = 0;
    g_running = nullptr;
    g_host_turn = true;
    if (start_ns < g_now) start_ns = g_now;
    g_sys = new_task(sys_task_main, "sys_evt", 100, true);
    g_sys->wake = UINT64_MAX;
    g_loop = new_task(loop_task_main, "loopTask", 1, false);
    g_loop->wake = start_ns;
    update_sys_due();
    g_started = true;
    return 0;
}

RW_EXPORT int rw_run_until(uint64_t time_ns) {
    if (!g_started) return RW_STATUS_NOT_RUNNING;
    if (g_crashed) return RW_STATUS_CRASHED;
    if (g_hung) return RW_STATUS_WATCHDOG;
    Lock l;
    if (time_ns <= g_limit) return RW_STATUS_OK;
    uint64_t slice_start = g_limit;
    rw::hal_pcnt_settle(slice_start);   // [pcnt-filter] edges that have outlasted the glitch filter by now
    g_limit = time_ns;
    if (g_now < slice_start) g_now = slice_start;  // the CPU idled until the start of this slice
    hal_slice_start();
    Task* next = pick_next();
    if (!next) return RW_STATUS_OK;

    g_host_turn = false;
    touch_watchdog();
    hand_to(next);
    while (!g_host_turn) {
        SleepConditionVariableCS(&g_host_cv, &g_cs, 50);
        if (g_host_turn) break;
        ULONGLONG idle = GetTickCount64() - (ULONGLONG)g_last_api_tick;
        if (idle > config.watchdog_ms) {
            g_hung = true;
            Task* t = g_running;
            unsigned long ms = (unsigned long)(uptime_ns() / 1000000ull);
            if (t && t->is_sys) {
                report(RW_STATUS_WATCHDOG,
                       "Guru Meditation Error: Core  1 panic'ed (Interrupt wdt timeout on CPU1).\r\n"
                       "(simulator: an interrupt handler ran for more than %u ms of real time without returning)",
                       config.watchdog_ms);
            } else {
                const char* name = t ? t->name : "loopTask";
                report(RW_STATUS_WATCHDOG,
                       "E (%lu) task_wdt: Task watchdog got triggered. The following tasks did not reset the watchdog in time:\r\n"
                       "E (%lu) task_wdt:  - %s (CPU 1)\r\n"
                       "E (%lu) task_wdt: Tasks currently running:\r\n"
                       "E (%lu) task_wdt: CPU 0: IDLE\r\n"
                       "E (%lu) task_wdt: CPU 1: %s\r\n"
                       "(simulator: %s ran for more than %u ms of real time without calling the Arduino API, so simulated time could not advance)",
                       ms, ms, name, ms, ms, ms, name, name, config.watchdog_ms);
            }

            return RW_STATUS_WATCHDOG;
        }
    }

    if (g_crashed) return RW_STATUS_CRASHED;
    return RW_STATUS_OK;
}

RW_EXPORT void rw_stop(void) {
    if (!g_started) return;
    Task* hung = g_hung ? g_running : nullptr;
    {
        Lock l;
        g_stopping = true;
        g_stopping_flag = true;
        for (Task* t : g_tasks) WakeConditionVariable(&t->cv);
    }

    for (Task* t : g_tasks) {
        if (t == hung || !t->thread) continue;
        if (WaitForSingleObject(t->thread, 3000) != WAIT_OBJECT_0) force_exit(t);
    }

    if (hung && hung->thread) force_exit(hung);

    Lock l;
    for (Task* t : g_tasks) {
        if (t->thread) CloseHandle(t->thread);
        delete t;
    }

    g_tasks.clear();
    g_sys = nullptr;
    g_loop = nullptr;
    g_running = nullptr;
    g_host_turn = true;
    g_isr_queue.clear();
    g_timed.clear();
    g_irq_enabled = true;
    update_sys_due();
    g_started = false;
    g_stopping = false;
    g_stopping_flag = false;
    g_hung = false;
    g_crashed = false;
}

RW_EXPORT void rw_pin_changed(int gpio, int level, uint64_t time_ns) {
    if (!has_api) return;
    hal_pin_changed(gpio, level, time_ns);
}

RW_EXPORT int rw_serial_rx(int port, const uint8_t* data, int length) {
    if (!has_api || !data || length <= 0) return 0;
    return serial_rx_push(port, data, length);
}

RW_EXPORT uint64_t rw_now_ns(void) { return g_now; }

RW_EXPORT int rw_status_message(char* buffer, int capacity) {
    if (!buffer || capacity <= 0) return (int)strlen(g_status_msg);
    strncpy_s(buffer, (size_t)capacity, g_status_msg, _TRUNCATE);
    return (int)strlen(buffer);
}

/* [mem-budget] What the sketch holds of the chip's memory: heap allocations and its tasks' stacks (bytes). */
RW_EXPORT void rw_memory(uint64_t* heap, uint64_t* stacks) {
    if (heap) *heap = (uint64_t)rw::heap_in_use();
    if (stacks) *stacks = (uint64_t)rw::task_stack_bytes();
}

RW_EXPORT int rw_task_count(void) {
    int n = 0;
    for (const Task* t : g_tasks) if (!t->finished) n++;
    return n;
}
