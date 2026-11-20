/* Task watchdog timer (TWDT) on simulated time, matching ESP-IDF 5 behaviour and messages.
   Arduino loop watchdog helpers (enableLoopWDT / feedLoopWDT / disableLoopWDT) live here too. */
#include <string.h>
#include "esp_task_wdt.h"
#include "rw_internal.h"

namespace {

struct Entry {
    void* task;       /* nullptr for users */
    bool user;
    char name[32];
    uint64_t last;    /* last reset, simulated ns */
};

const int kMax = 24;
const uint64_t kCheckNs = 10000000ull;   /* deadlines are checked every 10 ms of simulated time */

bool g_init = false;
uint64_t g_timeout = 0;
bool g_panic = false;
Entry g_entries[kMax];
int g_count = 0;
int g_event = 0;
bool g_loop = false;
bool g_triggered = false;

unsigned long now_ms() { return (unsigned long)(rw::uptime_ns() / 1000000ull); }

int find(void* task, bool user, const char* name) {
    for (int i = 0; i < g_count; i++) {
        if (g_entries[i].user != user) continue;
        if (!user && g_entries[i].task == task) return i;
        if (user && g_entries[i].task == (void*)name) return i;
    }

    return -1;
}

void check(void*);

void arm() {
    if (g_event) rw::cancel_event(g_event);
    g_event = g_init ? rw::schedule_event(rw::now_ns() + kCheckNs, check, nullptr) : 0;
}

void report(int late) {
    unsigned long ms = now_ms();
    rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: Task watchdog got triggered. The following tasks/users did not reset the watchdog in time:", ms);
    for (int i = 0; i < g_count; i++) {
        if (rw::now_ns() - g_entries[i].last < g_timeout) continue;
        rw::log(RW_LOG_ERROR, "E (%lu) task_wdt:  - %s (%s)", ms, g_entries[i].name, g_entries[i].user ? "user" : "CPU 1");
    }

    /* The starving task is the one still running on CPU 1: the first late task. */
    const char* running = "IDLE1";
    for (int i = 0; i < g_count; i++)
        if (!g_entries[i].user && rw::now_ns() - g_entries[i].last >= g_timeout) { running = g_entries[i].name; break; }
    rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: Tasks currently running:", ms);
    rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: CPU 0: IDLE0", ms);
    rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: CPU 1: %s", ms, running);
    (void)late;
}

void check(void*) {
    g_event = 0;
    if (!g_init) return;
    uint64_t now = rw::now_ns();
    int late = 0;
    for (int i = 0; i < g_count; i++)
        if (now - g_entries[i].last >= g_timeout) late++;

    if (late > 0) {
        report(late);
        if (g_panic) {
            unsigned long ms = now_ms();
            rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: Aborting.", ms);
            rw::log(RW_LOG_ERROR, "\r\nabort() was called at PC 0x400d2f8b on core 0\r\n\r\nBacktrace: 0x40083b4d:0x3ffbe9e0 0x4008aa25:0x3ffbea00 0x40090a51:0x3ffbea20\r\n\r\nRebooting...");
            g_triggered = true;
            rw::system_restart(ESP_RST_TASK_WDT);
            return;
        }

        /* The IDF timer restarts after the report: it fires again one timeout later if nobody resets. */
        for (int i = 0; i < g_count; i++)
            if (now - g_entries[i].last >= g_timeout) g_entries[i].last = now;
    }

    arm();
}

esp_err_t configure(uint32_t timeout_ms, bool panic) {
    if (timeout_ms == 0) return ESP_ERR_INVALID_ARG;
    g_timeout = (uint64_t)timeout_ms * 1000000ull;
    g_panic = panic;
    uint64_t now = rw::now_ns();
    for (int i = 0; i < g_count; i++) g_entries[i].last = now;
    arm();
    return ESP_OK;
}

esp_err_t add(void* task, bool user, const char* name) {
    if (!g_init) {
        rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: esp_task_wdt_add(%s): TWDT was never initialized", now_ms(), name);
        return ESP_ERR_INVALID_STATE;
    }

    if (find(task, user, name) >= 0) return ESP_ERR_INVALID_ARG;   /* already subscribed */
    if (g_count >= kMax) return ESP_FAIL;
    Entry& e = g_entries[g_count++];
    e.task = user ? (void*)name : task;
    e.user = user;
    strncpy(e.name, name ? name : "?", sizeof e.name - 1);
    e.name[sizeof e.name - 1] = 0;
    e.last = rw::now_ns();
    return ESP_OK;
}

esp_err_t remove_at(int i) {
    if (i < 0) return ESP_ERR_NOT_FOUND;
    for (int k = i; k + 1 < g_count; k++) g_entries[k] = g_entries[k + 1];
    g_count--;
    return ESP_OK;
}

}  // namespace

namespace rw {

/* Chip reset: the bootloader starts the TWDT with the sdkconfig defaults of Arduino-ESP32
   (5 s, no panic, IDLE of CPU 0 subscribed; the idle task always gets to run on CPU 0 here). */
void twdt_reset() {
    g_init = true;
    g_timeout = 5000000000ull;
    g_panic = false;
    g_count = 0;
    g_event = 0;
    g_loop = false;
    g_triggered = false;
}

/* loopTask calls this before every loop() (Arduino: if (loopTaskWDTEnabled) esp_task_wdt_reset()). */
void twdt_loop_iteration() {
    if (g_loop) esp_task_wdt_reset();
}

}  // namespace rw

extern "C" esp_err_t esp_task_wdt_init(const esp_task_wdt_config_t* config) {
    if (!config) return ESP_ERR_INVALID_ARG;
    if (g_init) {
        rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: esp_task_wdt_init(%d): TWDT already initialized", now_ms(), 755);
        return ESP_ERR_INVALID_STATE;
    }

    g_init = true;
    return configure(config->timeout_ms, config->trigger_panic);
}

esp_err_t esp_task_wdt_init(uint32_t timeout_s, bool panic) {
    /* IDF 4.4: init on a running TWDT reconfigures it. */
    g_init = true;
    return configure(timeout_s * 1000u, panic);
}

extern "C" esp_err_t esp_task_wdt_reconfigure(const esp_task_wdt_config_t* config) {
    if (!config) return ESP_ERR_INVALID_ARG;
    if (!g_init) return ESP_ERR_INVALID_STATE;
    return configure(config->timeout_ms, config->trigger_panic);
}

extern "C" esp_err_t esp_task_wdt_deinit(void) {
    if (!g_init) return ESP_ERR_INVALID_STATE;
    if (g_count > 0) {
        rw::log(RW_LOG_ERROR, "E (%lu) task_wdt: esp_task_wdt_deinit(%d): Tasks/users still subscribed", now_ms(), 801);
        return ESP_ERR_INVALID_STATE;
    }

    g_init = false;
    if (g_event) rw::cancel_event(g_event);
    g_event = 0;
    return ESP_OK;
}

extern "C" esp_err_t esp_task_wdt_add(TaskHandle_t task) {
    void* t = task ? (void*)task : rw::current_task();
    return add(t, false, rw::task_name(t));
}

extern "C" esp_err_t esp_task_wdt_add_user(const char* user_name, esp_task_wdt_user_handle_t* user_handle_ret) {
    if (!user_name || !user_handle_ret) return ESP_ERR_INVALID_ARG;
    esp_err_t r = add(nullptr, true, user_name);
    if (r == ESP_OK) *user_handle_ret = (esp_task_wdt_user_handle_t)(void*)user_name;
    return r;
}

extern "C" esp_err_t esp_task_wdt_reset(void) {
    if (!g_init) return ESP_ERR_INVALID_STATE;
    int i = find(rw::current_task(), false, nullptr);
    if (i < 0) {
        rw::log_once(0x900, RW_LOG_ERROR, "E (%lu) task_wdt: esp_task_wdt_reset(%d): task not found", now_ms(), 705);
        return ESP_ERR_NOT_FOUND;
    }

    g_entries[i].last = rw::now_ns();
    rw::charge(rw::cost(2000.0));
    return ESP_OK;
}

extern "C" esp_err_t esp_task_wdt_reset_user(esp_task_wdt_user_handle_t user_handle) {
    if (!g_init) return ESP_ERR_INVALID_STATE;
    int i = find(nullptr, true, (const char*)(void*)user_handle);
    if (i < 0) return ESP_ERR_NOT_FOUND;
    g_entries[i].last = rw::now_ns();
    return ESP_OK;
}

extern "C" esp_err_t esp_task_wdt_delete(TaskHandle_t task) {
    if (!g_init) return ESP_ERR_INVALID_STATE;
    return remove_at(find(task ? (void*)task : rw::current_task(), false, nullptr));
}

extern "C" esp_err_t esp_task_wdt_delete_user(esp_task_wdt_user_handle_t user_handle) {
    if (!g_init) return ESP_ERR_INVALID_STATE;
    return remove_at(find(nullptr, true, (const char*)(void*)user_handle));
}

extern "C" esp_err_t esp_task_wdt_status(TaskHandle_t task) {
    if (!g_init) return ESP_ERR_INVALID_STATE;
    return find(task ? (void*)task : rw::current_task(), false, nullptr) >= 0 ? ESP_OK : ESP_ERR_NOT_FOUND;
}

/* Arduino-ESP32 loop watchdog (esp32-hal-misc.c). */
void enableLoopWDT() {
    void* loop = rw::find_task("loopTask");
    if (find(loop, false, nullptr) < 0 && add(loop, false, "loopTask") != ESP_OK) {
        rw::log(RW_LOG_ERROR, "[%6lu][E][esp32-hal-misc.c] enableLoopWDT(): Failed to add loop task to WDT", now_ms());
        return;
    }

    g_loop = true;
}

void disableLoopWDT() {
    void* loop = rw::find_task("loopTask");
    g_loop = false;
    remove_at(find(loop, false, nullptr));
}

void feedLoopWDT() {
    esp_task_wdt_reset();
}
