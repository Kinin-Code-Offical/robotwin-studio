/*
 * RoboTwin ESP32 simulation - ESP-IDF's pulse counter driver (driver/pulse_cnt.h) as on the classic ESP32: eight
 * units of two channels counting the edges of their GPIOs (rotary encoders, flow meters, tachometers). The edges are
 * the pin changes the simulator delivers; see rw_pcnt.cpp.
 */
#pragma once
#include <stdint.h>
#include <stdbool.h>
#include "Arduino.h"
#include "hal/pcnt_types.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct pcnt_unit_t* pcnt_unit_handle_t;
typedef struct pcnt_chan_t* pcnt_channel_handle_t;

typedef struct {
    int watch_point_value;
    pcnt_unit_zero_cross_mode_t zero_cross_mode;
} pcnt_watch_event_data_t;

typedef bool (*pcnt_watch_cb_t)(pcnt_unit_handle_t unit, const pcnt_watch_event_data_t* edata, void* user_ctx);

typedef struct {
    pcnt_watch_cb_t on_reach;
} pcnt_event_callbacks_t;

typedef struct {
    int low_limit;      /* below 0, down to -32768 */
    int high_limit;     /* above 0, up to 32767 */
    int intr_priority;
    struct {
        uint32_t accum_count : 1; /* keep counting past the limits (the driver adds the limit at each overflow) */
    } flags;
} pcnt_unit_config_t;

typedef struct {
    int edge_gpio_num;  /* -1: a virtual level (virt_edge_io_level) */
    int level_gpio_num; /* -1: a virtual level (virt_level_io_level) */
    struct {
        uint32_t invert_edge_input : 1;
        uint32_t invert_level_input : 1;
        uint32_t virt_edge_io_level : 1;
        uint32_t virt_level_io_level : 1;
        uint32_t io_loop_back : 1;
    } flags;
} pcnt_chan_config_t;

typedef struct {
    uint32_t max_glitch_ns; /* up to 1023 APB cycles (12787 ns) */
} pcnt_glitch_filter_config_t;

esp_err_t pcnt_new_unit(const pcnt_unit_config_t* config, pcnt_unit_handle_t* ret_unit);
esp_err_t pcnt_del_unit(pcnt_unit_handle_t unit);
esp_err_t pcnt_unit_set_glitch_filter(pcnt_unit_handle_t unit, const pcnt_glitch_filter_config_t* config);
esp_err_t pcnt_unit_enable(pcnt_unit_handle_t unit);
esp_err_t pcnt_unit_disable(pcnt_unit_handle_t unit);
esp_err_t pcnt_unit_start(pcnt_unit_handle_t unit);
esp_err_t pcnt_unit_stop(pcnt_unit_handle_t unit);
esp_err_t pcnt_unit_clear_count(pcnt_unit_handle_t unit);
esp_err_t pcnt_unit_get_count(pcnt_unit_handle_t unit, int* value);
esp_err_t pcnt_unit_register_event_callbacks(pcnt_unit_handle_t unit, const pcnt_event_callbacks_t* cbs, void* user_data);
esp_err_t pcnt_unit_add_watch_point(pcnt_unit_handle_t unit, int watch_point);
esp_err_t pcnt_unit_remove_watch_point(pcnt_unit_handle_t unit, int watch_point);
esp_err_t pcnt_unit_add_watch_step(pcnt_unit_handle_t unit, int step_interval);   /* not on the classic ESP32 */
esp_err_t pcnt_unit_remove_all_watch_step(pcnt_unit_handle_t unit);
esp_err_t pcnt_new_channel(pcnt_unit_handle_t unit, const pcnt_chan_config_t* config, pcnt_channel_handle_t* ret_chan);
esp_err_t pcnt_del_channel(pcnt_channel_handle_t chan);
esp_err_t pcnt_channel_set_edge_action(pcnt_channel_handle_t chan, pcnt_channel_edge_action_t pos_act, pcnt_channel_edge_action_t neg_act);
esp_err_t pcnt_channel_set_level_action(pcnt_channel_handle_t chan, pcnt_channel_level_action_t high_act, pcnt_channel_level_action_t low_act);

#ifdef __cplusplus
}
#endif
