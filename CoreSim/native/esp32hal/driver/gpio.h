/*
 * RoboTwin ESP32 simulation - ESP-IDF's GPIO driver (driver/gpio.h), the calls sketches use: gpio_config, levels,
 * directions and pulls, interrupt types with the ISR service, and the light-sleep wake levels. They act on the same pins
 * as Arduino's pinMode/digitalWrite/attachInterrupt (rw_gpio_driver.cpp).
 */
#pragma once
#include <stdint.h>
#include "Arduino.h"
#include "esp_sleep.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    GPIO_MODE_DISABLE = 0,
    GPIO_MODE_INPUT = 1,
    GPIO_MODE_OUTPUT = 2,
    GPIO_MODE_OUTPUT_OD = 6,
    GPIO_MODE_INPUT_OUTPUT_OD = 7,
    GPIO_MODE_INPUT_OUTPUT = 3,
} gpio_mode_t;

typedef enum { GPIO_PULLUP_DISABLE = 0, GPIO_PULLUP_ENABLE = 1 } gpio_pullup_t;
typedef enum { GPIO_PULLDOWN_DISABLE = 0, GPIO_PULLDOWN_ENABLE = 1 } gpio_pulldown_t;
typedef enum { GPIO_PULLUP_ONLY, GPIO_PULLDOWN_ONLY, GPIO_PULLUP_PULLDOWN, GPIO_FLOATING } gpio_pull_mode_t;
typedef enum { GPIO_DRIVE_CAP_0, GPIO_DRIVE_CAP_1, GPIO_DRIVE_CAP_2, GPIO_DRIVE_CAP_DEFAULT = 2, GPIO_DRIVE_CAP_3 } gpio_drive_cap_t;

typedef struct {
    uint64_t pin_bit_mask;
    gpio_mode_t mode;
    gpio_pullup_t pull_up_en;
    gpio_pulldown_t pull_down_en;
    gpio_int_type_t intr_type;
} gpio_config_t;

typedef void (*gpio_isr_t)(void* arg);

#define GPIO_IS_VALID_GPIO(n) ((n) >= 0 && (n) < 40 && (n) != 20 && (n) != 24 && !((n) >= 28 && (n) <= 31))
#define GPIO_IS_VALID_OUTPUT_GPIO(n) (GPIO_IS_VALID_GPIO(n) && (n) < 34)
#define ESP_INTR_FLAG_LEVEL1 (1 << 1)
#define ESP_INTR_FLAG_IRAM (1 << 10)
#define ESP_INTR_FLAG_DEFAULT 0

esp_err_t gpio_config(const gpio_config_t* config);
esp_err_t gpio_reset_pin(gpio_num_t gpio_num);
esp_err_t gpio_set_level(gpio_num_t gpio_num, uint32_t level);
int gpio_get_level(gpio_num_t gpio_num);
esp_err_t gpio_set_direction(gpio_num_t gpio_num, gpio_mode_t mode);
esp_err_t gpio_set_pull_mode(gpio_num_t gpio_num, gpio_pull_mode_t pull);
esp_err_t gpio_pullup_en(gpio_num_t gpio_num);
esp_err_t gpio_pullup_dis(gpio_num_t gpio_num);
esp_err_t gpio_pulldown_en(gpio_num_t gpio_num);
esp_err_t gpio_pulldown_dis(gpio_num_t gpio_num);
esp_err_t gpio_set_drive_capability(gpio_num_t gpio_num, gpio_drive_cap_t strength);
esp_err_t gpio_set_intr_type(gpio_num_t gpio_num, gpio_int_type_t intr_type);
esp_err_t gpio_intr_enable(gpio_num_t gpio_num);
esp_err_t gpio_intr_disable(gpio_num_t gpio_num);
esp_err_t gpio_install_isr_service(int intr_alloc_flags);
void gpio_uninstall_isr_service(void);
esp_err_t gpio_isr_handler_add(gpio_num_t gpio_num, gpio_isr_t isr_handler, void* args);
esp_err_t gpio_isr_handler_remove(gpio_num_t gpio_num);

#ifdef __cplusplus
}
#endif
