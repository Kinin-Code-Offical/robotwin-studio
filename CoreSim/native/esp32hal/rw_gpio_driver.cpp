/*
 * ESP-IDF's GPIO driver calls (driver/gpio.h) on the fast simulation's pins: each pin's mode and pulls become the
 * Arduino pinMode the HAL already models, levels go through digitalWrite/digitalRead, and interrupt types with the ISR
 * service become attachInterruptArg (whose modes are gpio_int_type_t's values: rising 1 ... high level 5). Invalid pins are
 * refused as the driver refuses them (ESP_ERR_INVALID_ARG with its log line).
 */
#include "Arduino.h"
#include "driver/gpio.h"
#include "rw_internal.h"

using namespace rw;

namespace {

struct GpioConf {
    bool output;
    bool input;
    bool open_drain;
    bool pull_up;
    bool pull_down;
    gpio_int_type_t intr;
    gpio_isr_t isr;
    void* arg;
    bool intr_enabled;
};

GpioConf g_conf[40];
bool g_isr_service;

void gpio_log(const char* func, int line, const char* format, ...) {
    char msg[160];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(RW_LOG_ERROR, "E (%lu) gpio: %s(%d): %s", (unsigned long)(uptime_ns() / 1000000ull), func, line, msg);
}

bool valid(int n, const char* func, int line) {
    if (GPIO_IS_VALID_GPIO(n)) return true;
    gpio_log(func, line, "GPIO number error");
    return false;
}

/* The pin's configuration as one Arduino pinMode. */
void apply(int n) {
    const GpioConf& c = g_conf[n];
    uint8_t mode;
    if (c.output) mode = c.open_drain ? OUTPUT_OPEN_DRAIN : OUTPUT;
    else mode = INPUT;
    if (c.pull_up) mode |= PULLUP;
    if (c.pull_down) mode |= PULLDOWN;
    pinMode((uint8_t)n, mode);
}

void attach(int n) {
    const GpioConf& c = g_conf[n];
    if (c.isr && c.intr != GPIO_INTR_DISABLE && c.intr_enabled) attachInterruptArg((uint8_t)n, c.isr, c.arg, (int)c.intr);
    else detachInterrupt((uint8_t)n);
}

}  // namespace

extern "C" esp_err_t gpio_config(const gpio_config_t* config) {
    if (!config || config->pin_bit_mask == 0 || config->pin_bit_mask >= (1ull << 40)) {
        gpio_log("gpio_config", 788, "GPIO_PIN mask error");
        return ESP_ERR_INVALID_ARG;
    }

    for (int n = 0; n < 40; n++) {
        if ((config->pin_bit_mask >> n & 1) == 0) continue;
        if (!GPIO_IS_VALID_GPIO(n)) {
            gpio_log("gpio_config", 795, "GPIO_PIN mask error");
            return ESP_ERR_INVALID_ARG;
        }

        bool output = (config->mode & 2) != 0;
        if (output && n >= 34) {
            gpio_log("gpio_config", 800, "io%d can only be input", n);
            return ESP_ERR_INVALID_ARG;
        }

        GpioConf& c = g_conf[n];
        c.output = output;
        c.input = (config->mode & 1) != 0;
        c.open_drain = (config->mode & 4) != 0;
        c.pull_up = config->pull_up_en == GPIO_PULLUP_ENABLE;
        c.pull_down = config->pull_down_en == GPIO_PULLDOWN_ENABLE;
        c.intr = config->intr_type;
        c.intr_enabled = config->intr_type != GPIO_INTR_DISABLE;
        apply(n);
        if (c.isr) attach(n);
    }

    return ESP_OK;
}

extern "C" esp_err_t gpio_reset_pin(gpio_num_t gpio_num) {
    if (!valid(gpio_num, "gpio_reset_pin", 830)) return ESP_ERR_INVALID_ARG;
    detachInterrupt((uint8_t)gpio_num);
    g_conf[gpio_num] = GpioConf{};
    g_conf[gpio_num].pull_up = true;   // the reset state: input disabled, output disabled, pull-up on
    apply(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_set_level(gpio_num_t gpio_num, uint32_t level) {
    if (!GPIO_IS_VALID_OUTPUT_GPIO(gpio_num)) {
        gpio_log("gpio_set_level", 231, "GPIO output gpio_num error");
        return ESP_ERR_INVALID_ARG;
    }

    digitalWrite((uint8_t)gpio_num, level ? HIGH : LOW);
    return ESP_OK;
}

extern "C" int gpio_get_level(gpio_num_t gpio_num) {
    if (!GPIO_IS_VALID_GPIO(gpio_num)) return 0;
    return digitalRead((uint8_t)gpio_num);
}

extern "C" esp_err_t gpio_set_direction(gpio_num_t gpio_num, gpio_mode_t mode) {
    if (!valid(gpio_num, "gpio_set_direction", 388)) return ESP_ERR_INVALID_ARG;
    if ((mode & 2) != 0 && gpio_num >= 34) {
        gpio_log("gpio_set_direction", 390, "io%d can't be set to output mode", (int)gpio_num);
        return ESP_ERR_INVALID_ARG;
    }

    GpioConf& c = g_conf[gpio_num];
    c.output = (mode & 2) != 0;
    c.input = (mode & 1) != 0;
    c.open_drain = (mode & 4) != 0;
    apply(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_set_pull_mode(gpio_num_t gpio_num, gpio_pull_mode_t pull) {
    if (!valid(gpio_num, "gpio_set_pull_mode", 437)) return ESP_ERR_INVALID_ARG;
    GpioConf& c = g_conf[gpio_num];
    c.pull_up = pull == GPIO_PULLUP_ONLY || pull == GPIO_PULLUP_PULLDOWN;
    c.pull_down = pull == GPIO_PULLDOWN_ONLY || pull == GPIO_PULLUP_PULLDOWN;
    apply(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_pullup_en(gpio_num_t gpio_num) {
    if (!valid(gpio_num, "gpio_pullup_en", 80)) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].pull_up = true;
    apply(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_pullup_dis(gpio_num_t gpio_num) {
    if (!valid(gpio_num, "gpio_pullup_dis", 95)) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].pull_up = false;
    apply(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_pulldown_en(gpio_num_t gpio_num) {
    if (!valid(gpio_num, "gpio_pulldown_en", 110)) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].pull_down = true;
    apply(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_pulldown_dis(gpio_num_t gpio_num) {
    if (!valid(gpio_num, "gpio_pulldown_dis", 125)) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].pull_down = false;
    apply(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_set_drive_capability(gpio_num_t gpio_num, gpio_drive_cap_t) {
    return valid(gpio_num, "gpio_set_drive_capability", 900) ? ESP_OK : ESP_ERR_INVALID_ARG;
}

extern "C" esp_err_t gpio_set_intr_type(gpio_num_t gpio_num, gpio_int_type_t intr_type) {
    if (!valid(gpio_num, "gpio_set_intr_type", 151)) return ESP_ERR_INVALID_ARG;
    if (intr_type >= GPIO_INTR_MAX) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].intr = intr_type;
    attach(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_intr_enable(gpio_num_t gpio_num) {
    if (!valid(gpio_num, "gpio_intr_enable", 180)) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].intr_enabled = true;
    attach(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_intr_disable(gpio_num_t gpio_num) {
    if (!valid(gpio_num, "gpio_intr_disable", 200)) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].intr_enabled = false;
    attach(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_install_isr_service(int) {
    if (g_isr_service) {
        gpio_log("gpio_install_isr_service", 494, "GPIO isr service already installed");
        return ESP_ERR_INVALID_STATE;
    }

    g_isr_service = true;
    return ESP_OK;
}

extern "C" void gpio_uninstall_isr_service(void) {
    for (int n = 0; n < 40; n++)
        if (g_conf[n].isr) {
            g_conf[n].isr = nullptr;
            detachInterrupt((uint8_t)n);
        }
    g_isr_service = false;
}

extern "C" esp_err_t gpio_isr_handler_add(gpio_num_t gpio_num, gpio_isr_t isr_handler, void* args) {
    if (!g_isr_service) {
        gpio_log("gpio_isr_handler_add", 452, "GPIO isr service is not installed, call gpio_install_isr_service() first");
        return ESP_ERR_INVALID_STATE;
    }

    if (!valid(gpio_num, "gpio_isr_handler_add", 453)) return ESP_ERR_INVALID_ARG;
    GpioConf& c = g_conf[gpio_num];
    c.isr = isr_handler;
    c.arg = args;
    if (c.intr != GPIO_INTR_DISABLE) c.intr_enabled = true;
    attach(gpio_num);
    return ESP_OK;
}

extern "C" esp_err_t gpio_isr_handler_remove(gpio_num_t gpio_num) {
    if (!g_isr_service) return ESP_ERR_INVALID_STATE;
    if (!valid(gpio_num, "gpio_isr_handler_remove", 470)) return ESP_ERR_INVALID_ARG;
    g_conf[gpio_num].isr = nullptr;
    detachInterrupt((uint8_t)gpio_num);
    return ESP_OK;
}

namespace rw {
void hal_gpio_driver_reset() {
    memset(g_conf, 0, sizeof g_conf);
    g_isr_service = false;
}
}  // namespace rw
