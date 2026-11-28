/*
 * RoboTwin ESP32 simulation - the legacy MCPWM driver (driver/mcpwm.h): what sketches and libraries written for IDF 4.x /
 * Arduino-ESP32 2.x call, and IDF 5 keeps as a deprecated driver (the real-chip build uses that one). Its timer n drives
 * operator n; output A and B take their duty in % (or µs) of the period, the frequency on a 1 MHz timer, the duty mode
 * (active high / low, forced), dead time, start and stop; the capture channels hand their value and edge to the callback.
 * The same model as the new driver's (rw_mcpwm.cpp, rw_mcpwm_cap.cpp), see rw_mcpwm_legacy.cpp. Carrier, fault and sync
 * are not there. As on the chip, a sketch cannot use this driver and the new one (driver/mcpwm_prelude.h) together.
 */
#pragma once
#include <stdint.h>
#include <stdbool.h>
#include "Arduino.h"
#include "driver/mcpwm_types_legacy.h"

#ifdef __cplusplus
extern "C" {
#endif

esp_err_t mcpwm_gpio_init(mcpwm_unit_t mcpwm_num, mcpwm_io_signals_t io_signal, int gpio_num);
esp_err_t mcpwm_set_pin(mcpwm_unit_t mcpwm_num, const mcpwm_pin_config_t* mcpwm_pin);
esp_err_t mcpwm_init(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, const mcpwm_config_t* mcpwm_conf);
esp_err_t mcpwm_group_set_resolution(mcpwm_unit_t mcpwm_num, unsigned long int resolution);
esp_err_t mcpwm_timer_set_resolution(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, unsigned long int resolution);
esp_err_t mcpwm_set_frequency(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, uint32_t frequency);
esp_err_t mcpwm_set_duty(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen, float duty);
esp_err_t mcpwm_set_duty_in_us(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen, uint32_t duty_in_us);
esp_err_t mcpwm_set_duty_type(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen, mcpwm_duty_type_t duty_type);
uint32_t mcpwm_get_frequency(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num);
float mcpwm_get_duty(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_operator_t gen);
uint32_t mcpwm_get_duty_in_us(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_operator_t gen);
esp_err_t mcpwm_set_signal_high(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen);
esp_err_t mcpwm_set_signal_low(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_generator_t gen);
esp_err_t mcpwm_start(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num);
esp_err_t mcpwm_stop(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num);
esp_err_t mcpwm_deadtime_enable(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num, mcpwm_deadtime_type_t dt_mode, uint32_t red, uint32_t fed);
esp_err_t mcpwm_deadtime_disable(mcpwm_unit_t mcpwm_num, mcpwm_timer_t timer_num);
esp_err_t mcpwm_capture_enable_channel(mcpwm_unit_t mcpwm_num, mcpwm_capture_channel_id_t cap_channel, const mcpwm_capture_config_t* cap_conf);
esp_err_t mcpwm_capture_disable_channel(mcpwm_unit_t mcpwm_num, mcpwm_capture_channel_id_t cap_channel);
uint32_t mcpwm_capture_signal_get_value(mcpwm_unit_t mcpwm_num, mcpwm_capture_signal_t cap_sig);
uint32_t mcpwm_capture_get_resolution(mcpwm_unit_t mcpwm_num);
uint32_t mcpwm_capture_signal_get_edge(mcpwm_unit_t mcpwm_num, mcpwm_capture_signal_t cap_sig);

#ifdef __cplusplus
}
#endif
