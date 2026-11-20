/* Task watchdog (ESP-IDF 5 API, as in Arduino-ESP32 3.x): real per-task deadlines in simulated time.
   Like on the chip, the TWDT is already initialised at boot (5 s, no panic, IDLE of CPU 0 watched);
   esp_task_wdt_init() then returns ESP_ERR_INVALID_STATE - use esp_task_wdt_reconfigure() or
   deinit + init. A subscribed task that does not call esp_task_wdt_reset() in time triggers the
   IDF "task_wdt" report; with trigger_panic the chip aborts and reboots (reset reason TASK_WDT). */
#ifndef RW_ESP_TASK_WDT_H
#define RW_ESP_TASK_WDT_H
#include <stdbool.h>
#include "esp_system.h"
#include "freertos/FreeRTOS.h"

#ifndef ESP_ERR_INVALID_ARG
#define ESP_ERR_INVALID_ARG 0x102
#endif
#ifndef ESP_ERR_INVALID_STATE
#define ESP_ERR_INVALID_STATE 0x103
#endif
#ifndef ESP_ERR_NOT_FOUND
#define ESP_ERR_NOT_FOUND 0x105
#endif

typedef struct { uint32_t timeout_ms; uint32_t idle_core_mask; bool trigger_panic; } esp_task_wdt_config_t;
typedef struct esp_task_wdt_user_handle_s* esp_task_wdt_user_handle_t;

#ifdef __cplusplus
extern "C" {
#endif
esp_err_t esp_task_wdt_init(const esp_task_wdt_config_t* config);
esp_err_t esp_task_wdt_reconfigure(const esp_task_wdt_config_t* config);
esp_err_t esp_task_wdt_deinit(void);
esp_err_t esp_task_wdt_add(TaskHandle_t task);
esp_err_t esp_task_wdt_add_user(const char* user_name, esp_task_wdt_user_handle_t* user_handle_ret);
esp_err_t esp_task_wdt_reset(void);
esp_err_t esp_task_wdt_reset_user(esp_task_wdt_user_handle_t user_handle);
esp_err_t esp_task_wdt_delete(TaskHandle_t task);
esp_err_t esp_task_wdt_delete_user(esp_task_wdt_user_handle_t user_handle);
esp_err_t esp_task_wdt_status(TaskHandle_t task);
#ifdef __cplusplus
}
#endif

#ifdef __cplusplus
/* Arduino-ESP32 2.x / ESP-IDF 4.4 signature: (re)configures the running watchdog. */
esp_err_t esp_task_wdt_init(uint32_t timeout_s, bool panic);
#endif
#endif
