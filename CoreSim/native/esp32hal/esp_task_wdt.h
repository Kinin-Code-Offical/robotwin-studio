/* Task watchdog API: no-ops (the simulator has its own wall-clock hang detection). */
#ifndef RW_ESP_TASK_WDT_H
#define RW_ESP_TASK_WDT_H
#include <stdbool.h>
#include "esp_system.h"
#include "freertos/FreeRTOS.h"
typedef struct { uint32_t timeout_ms; uint32_t idle_core_mask; bool trigger_panic; } esp_task_wdt_config_t;
static inline esp_err_t esp_task_wdt_init(const esp_task_wdt_config_t* config) { (void)config; return ESP_OK; }
static inline esp_err_t esp_task_wdt_reconfigure(const esp_task_wdt_config_t* config) { (void)config; return ESP_OK; }
static inline esp_err_t esp_task_wdt_deinit(void) { return ESP_OK; }
static inline esp_err_t esp_task_wdt_add(TaskHandle_t task) { (void)task; return ESP_OK; }
static inline esp_err_t esp_task_wdt_delete(TaskHandle_t task) { (void)task; return ESP_OK; }
static inline esp_err_t esp_task_wdt_reset(void) { return ESP_OK; }
#endif
