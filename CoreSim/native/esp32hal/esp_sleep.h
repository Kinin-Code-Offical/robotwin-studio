/* esp_sleep.h subset: deep sleep with timer / ext0 wake-up; light sleep until the timer, EXT0 or a GPIO level wakes it. */
#ifndef RW_ESP_SLEEP_H
#define RW_ESP_SLEEP_H
#include <stdint.h>

#ifndef ESP_OK
typedef int esp_err_t;
#define ESP_OK 0
#define ESP_FAIL -1
#endif

typedef enum {
    GPIO_NUM_NC = -1,
    GPIO_NUM_0 = 0, GPIO_NUM_1, GPIO_NUM_2, GPIO_NUM_3, GPIO_NUM_4, GPIO_NUM_5, GPIO_NUM_6, GPIO_NUM_7,
    GPIO_NUM_8, GPIO_NUM_9, GPIO_NUM_10, GPIO_NUM_11, GPIO_NUM_12, GPIO_NUM_13, GPIO_NUM_14, GPIO_NUM_15,
    GPIO_NUM_16, GPIO_NUM_17, GPIO_NUM_18, GPIO_NUM_19, GPIO_NUM_20, GPIO_NUM_21, GPIO_NUM_22, GPIO_NUM_23,
    GPIO_NUM_25 = 25, GPIO_NUM_26, GPIO_NUM_27, GPIO_NUM_28, GPIO_NUM_29, GPIO_NUM_30, GPIO_NUM_31,
    GPIO_NUM_32, GPIO_NUM_33, GPIO_NUM_34, GPIO_NUM_35, GPIO_NUM_36, GPIO_NUM_37, GPIO_NUM_38, GPIO_NUM_39,
    GPIO_NUM_MAX
} gpio_num_t;

typedef enum {
    ESP_SLEEP_WAKEUP_UNDEFINED = 0,
    ESP_SLEEP_WAKEUP_ALL,
    ESP_SLEEP_WAKEUP_EXT0,
    ESP_SLEEP_WAKEUP_EXT1,
    ESP_SLEEP_WAKEUP_TIMER,
    ESP_SLEEP_WAKEUP_TOUCHPAD,
    ESP_SLEEP_WAKEUP_ULP,
    ESP_SLEEP_WAKEUP_GPIO,
    ESP_SLEEP_WAKEUP_UART,
} esp_sleep_source_t;
typedef esp_sleep_source_t esp_sleep_wakeup_cause_t;

#ifndef RW_GPIO_INT_TYPE
#define RW_GPIO_INT_TYPE
typedef enum {
    GPIO_INTR_DISABLE = 0,
    GPIO_INTR_POSEDGE = 1,
    GPIO_INTR_NEGEDGE = 2,
    GPIO_INTR_ANYEDGE = 3,
    GPIO_INTR_LOW_LEVEL = 4,
    GPIO_INTR_HIGH_LEVEL = 5,
    GPIO_INTR_MAX,
} gpio_int_type_t;
#endif

#ifdef __cplusplus
extern "C" {
#endif
esp_err_t esp_sleep_enable_timer_wakeup(uint64_t time_in_us);
esp_err_t esp_sleep_enable_ext0_wakeup(gpio_num_t gpio_num, int level);
esp_err_t esp_sleep_disable_wakeup_source(esp_sleep_source_t source);
void esp_deep_sleep_start(void);
void esp_deep_sleep(uint64_t time_in_us);
esp_err_t esp_light_sleep_start(void);
esp_err_t esp_sleep_enable_gpio_wakeup(void);
/* driver/gpio.h's light-sleep wake levels (GPIO_INTR_LOW_LEVEL / GPIO_INTR_HIGH_LEVEL only). */
esp_err_t gpio_wakeup_enable(gpio_num_t gpio_num, gpio_int_type_t intr_type);
esp_err_t gpio_wakeup_disable(gpio_num_t gpio_num);
esp_sleep_wakeup_cause_t esp_sleep_get_wakeup_cause(void);
#ifdef __cplusplus
}
#endif
#endif
