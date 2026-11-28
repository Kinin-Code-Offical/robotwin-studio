/* ESP-IDF's RTC GPIO calls (driver/rtc_io.h) on the simulation's pins: the RTC-capable pads (GPIO0, 2, 4, 12-15, 25-27,
 * 32-39) set up, driven, read and pulled through the GPIO driver (driver/gpio.h), which the HAL already models; isolating a
 * pad turns its output, input and pulls off. Holding a pad through deep sleep (rtc_gpio_hold_en) is not modelled. */
#ifndef RW_DRIVER_RTC_IO_H
#define RW_DRIVER_RTC_IO_H
#include "driver/gpio.h"

typedef enum {
    RTC_GPIO_MODE_INPUT_ONLY,
    RTC_GPIO_MODE_OUTPUT_ONLY,
    RTC_GPIO_MODE_INPUT_OUTPUT,
    RTC_GPIO_MODE_DISABLED,
    RTC_GPIO_MODE_OUTPUT_OD,
    RTC_GPIO_MODE_INPUT_OUTPUT_OD,
} rtc_gpio_mode_t;

#ifdef __cplusplus
extern "C" {
#endif
bool rtc_gpio_is_valid_gpio(gpio_num_t gpio_num);
esp_err_t rtc_gpio_init(gpio_num_t gpio_num);
esp_err_t rtc_gpio_deinit(gpio_num_t gpio_num);
uint32_t rtc_gpio_get_level(gpio_num_t gpio_num);
esp_err_t rtc_gpio_set_level(gpio_num_t gpio_num, uint32_t level);
esp_err_t rtc_gpio_set_direction(gpio_num_t gpio_num, rtc_gpio_mode_t mode);
esp_err_t rtc_gpio_pullup_en(gpio_num_t gpio_num);
esp_err_t rtc_gpio_pullup_dis(gpio_num_t gpio_num);
esp_err_t rtc_gpio_pulldown_en(gpio_num_t gpio_num);
esp_err_t rtc_gpio_pulldown_dis(gpio_num_t gpio_num);
esp_err_t rtc_gpio_isolate(gpio_num_t gpio_num);
#ifdef __cplusplus
}
#endif
#endif
