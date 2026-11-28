/*
 * RoboTwin ESP32 simulation - ESP-IDF's MCPWM driver (driver/mcpwm_prelude.h): timers, operators, comparators and
 * generators with their timer and compare actions, dead time and software force, on the classic ESP32's two groups of
 * three timers and three operators. Each generator's pin gets the PWM its actions make (frequency and duty worked out
 * from one period, as the Xtensa emulator's MCPWM does); see rw_mcpwm.cpp. The capture unit (driver/mcpwm_cap.h) latches
 * its timer on a GPIO's edges at their own times (rw_mcpwm_cap.cpp).
 */
#pragma once
#include <stdint.h>
#include <stdbool.h>
#include "Arduino.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct mcpwm_timer_t* mcpwm_timer_handle_t;
typedef struct mcpwm_oper_t* mcpwm_oper_handle_t;
typedef struct mcpwm_cmpr_t* mcpwm_cmpr_handle_t;
typedef struct mcpwm_gen_t* mcpwm_gen_handle_t;
typedef struct mcpwm_fault_t* mcpwm_fault_handle_t;
typedef struct mcpwm_sync_t* mcpwm_sync_handle_t;

typedef enum { MCPWM_TIMER_DIRECTION_UP, MCPWM_TIMER_DIRECTION_DOWN } mcpwm_timer_direction_t;
typedef enum { MCPWM_TIMER_EVENT_EMPTY, MCPWM_TIMER_EVENT_FULL, MCPWM_TIMER_EVENT_INVALID } mcpwm_timer_event_t;
typedef enum {
    MCPWM_TIMER_COUNT_MODE_PAUSE,
    MCPWM_TIMER_COUNT_MODE_UP,
    MCPWM_TIMER_COUNT_MODE_DOWN,
    MCPWM_TIMER_COUNT_MODE_UP_DOWN,
} mcpwm_timer_count_mode_t;
typedef enum {
    MCPWM_TIMER_STOP_EMPTY,
    MCPWM_TIMER_STOP_FULL,
    MCPWM_TIMER_START_NO_STOP,
    MCPWM_TIMER_START_STOP_EMPTY,
    MCPWM_TIMER_START_STOP_FULL,
} mcpwm_timer_start_stop_cmd_t;
typedef enum { MCPWM_GEN_ACTION_KEEP, MCPWM_GEN_ACTION_LOW, MCPWM_GEN_ACTION_HIGH, MCPWM_GEN_ACTION_TOGGLE } mcpwm_generator_action_t;
typedef enum { MCPWM_OPER_BRAKE_MODE_CBC, MCPWM_OPER_BRAKE_MODE_OST, MCPWM_OPER_BRAKE_MODE_INVALID } mcpwm_operator_brake_mode_t;
typedef enum { MCPWM_TIMER_CLK_SRC_PLL160M = 1, MCPWM_TIMER_CLK_SRC_DEFAULT = 1 } mcpwm_timer_clock_source_t;

typedef struct {
    uint32_t count_value;
    mcpwm_timer_direction_t direction;
} mcpwm_timer_event_data_t;
typedef bool (*mcpwm_timer_event_cb_t)(mcpwm_timer_handle_t timer, const mcpwm_timer_event_data_t* edata, void* user_ctx);

typedef struct {
    uint32_t compare_ticks;
    mcpwm_timer_direction_t direction;
} mcpwm_compare_event_data_t;
typedef bool (*mcpwm_compare_event_cb_t)(mcpwm_cmpr_handle_t comparator, const mcpwm_compare_event_data_t* edata, void* user_ctx);

/* ---- timer */
typedef struct {
    mcpwm_timer_event_cb_t on_full;
    mcpwm_timer_event_cb_t on_empty;
    mcpwm_timer_event_cb_t on_stop;
} mcpwm_timer_event_callbacks_t;

typedef struct {
    int group_id;
    mcpwm_timer_clock_source_t clk_src;
    uint32_t resolution_hz;
    mcpwm_timer_count_mode_t count_mode;
    uint32_t period_ticks;   /* ticks in a period; in up-down mode the peak is half of it */
    int intr_priority;
    struct {
        uint32_t update_period_on_empty : 1;
        uint32_t update_period_on_sync : 1;
        uint32_t allow_pd : 1;
    } flags;
} mcpwm_timer_config_t;

esp_err_t mcpwm_new_timer(const mcpwm_timer_config_t* config, mcpwm_timer_handle_t* ret_timer);
esp_err_t mcpwm_del_timer(mcpwm_timer_handle_t timer);
esp_err_t mcpwm_timer_set_period(mcpwm_timer_handle_t timer, uint32_t period_ticks);
esp_err_t mcpwm_timer_enable(mcpwm_timer_handle_t timer);
esp_err_t mcpwm_timer_disable(mcpwm_timer_handle_t timer);
esp_err_t mcpwm_timer_start_stop(mcpwm_timer_handle_t timer, mcpwm_timer_start_stop_cmd_t command);
esp_err_t mcpwm_timer_register_event_callbacks(mcpwm_timer_handle_t timer, const mcpwm_timer_event_callbacks_t* cbs, void* user_data);

/* ---- operator */
typedef struct {
    int group_id;
    int intr_priority;
    struct {
        uint32_t update_gen_action_on_tez : 1;
        uint32_t update_gen_action_on_tep : 1;
        uint32_t update_gen_action_on_sync : 1;
        uint32_t update_dead_time_on_tez : 1;
        uint32_t update_dead_time_on_tep : 1;
        uint32_t update_dead_time_on_sync : 1;
    } flags;
} mcpwm_operator_config_t;

esp_err_t mcpwm_new_operator(const mcpwm_operator_config_t* config, mcpwm_oper_handle_t* ret_oper);
esp_err_t mcpwm_del_operator(mcpwm_oper_handle_t oper);
esp_err_t mcpwm_operator_connect_timer(mcpwm_oper_handle_t oper, mcpwm_timer_handle_t timer);

/* ---- comparator */
typedef struct {
    int intr_priority;
    struct {
        uint32_t update_cmp_on_tez : 1;
        uint32_t update_cmp_on_tep : 1;
        uint32_t update_cmp_on_sync : 1;
    } flags;
} mcpwm_comparator_config_t;

typedef struct {
    mcpwm_compare_event_cb_t on_reach;
} mcpwm_comparator_event_callbacks_t;

esp_err_t mcpwm_new_comparator(mcpwm_oper_handle_t oper, const mcpwm_comparator_config_t* config, mcpwm_cmpr_handle_t* ret_cmpr);
esp_err_t mcpwm_del_comparator(mcpwm_cmpr_handle_t cmpr);
esp_err_t mcpwm_comparator_set_compare_value(mcpwm_cmpr_handle_t cmpr, uint32_t cmp_ticks);
esp_err_t mcpwm_comparator_register_event_callbacks(mcpwm_cmpr_handle_t cmpr, const mcpwm_comparator_event_callbacks_t* cbs, void* user_data);

/* ---- generator */
typedef struct {
    int gen_gpio_num;
    struct {
        uint32_t invert_pwm : 1;
        uint32_t io_loop_back : 1;
        uint32_t io_od_mode : 1;
        uint32_t pull_up : 1;
        uint32_t pull_down : 1;
    } flags;
} mcpwm_generator_config_t;

typedef struct {
    mcpwm_timer_direction_t direction;
    mcpwm_timer_event_t event;
    mcpwm_generator_action_t action;
} mcpwm_gen_timer_event_action_t;

typedef struct {
    mcpwm_timer_direction_t direction;
    mcpwm_cmpr_handle_t comparator;
    mcpwm_generator_action_t action;
} mcpwm_gen_compare_event_action_t;

typedef struct {
    uint32_t posedge_delay_ticks;
    uint32_t negedge_delay_ticks;
    struct {
        uint32_t invert_output : 1;
    } flags;
} mcpwm_dead_time_config_t;

/* The IDF macros are C compound literals; these build the same values in C++ too. */
#ifdef __cplusplus
#define MCPWM_GEN_TIMER_EVENT_ACTION(dir, ev, act) (mcpwm_gen_timer_event_action_t{(dir), (ev), (act)})
#define MCPWM_GEN_TIMER_EVENT_ACTION_END() (mcpwm_gen_timer_event_action_t{MCPWM_TIMER_DIRECTION_UP, MCPWM_TIMER_EVENT_INVALID, MCPWM_GEN_ACTION_KEEP})
#define MCPWM_GEN_COMPARE_EVENT_ACTION(dir, cmp, act) (mcpwm_gen_compare_event_action_t{(dir), (cmp), (act)})
#define MCPWM_GEN_COMPARE_EVENT_ACTION_END() (mcpwm_gen_compare_event_action_t{MCPWM_TIMER_DIRECTION_UP, NULL, MCPWM_GEN_ACTION_KEEP})
#else
#define MCPWM_GEN_TIMER_EVENT_ACTION(dir, ev, act) ((mcpwm_gen_timer_event_action_t){.direction = (dir), .event = (ev), .action = (act)})
#define MCPWM_GEN_TIMER_EVENT_ACTION_END() ((mcpwm_gen_timer_event_action_t){.event = MCPWM_TIMER_EVENT_INVALID})
#define MCPWM_GEN_COMPARE_EVENT_ACTION(dir, cmp, act) ((mcpwm_gen_compare_event_action_t){.direction = (dir), .comparator = (cmp), .action = (act)})
#define MCPWM_GEN_COMPARE_EVENT_ACTION_END() ((mcpwm_gen_compare_event_action_t){.comparator = NULL})
#endif

esp_err_t mcpwm_new_generator(mcpwm_oper_handle_t oper, const mcpwm_generator_config_t* config, mcpwm_gen_handle_t* ret_gen);
esp_err_t mcpwm_del_generator(mcpwm_gen_handle_t gen);
esp_err_t mcpwm_generator_set_force_level(mcpwm_gen_handle_t gen, int level, bool hold_on);
esp_err_t mcpwm_generator_set_action_on_timer_event(mcpwm_gen_handle_t gen, mcpwm_gen_timer_event_action_t ev_act);
esp_err_t mcpwm_generator_set_actions_on_timer_event(mcpwm_gen_handle_t gen, mcpwm_gen_timer_event_action_t ev_act, ...);
esp_err_t mcpwm_generator_set_action_on_compare_event(mcpwm_gen_handle_t gen, mcpwm_gen_compare_event_action_t ev_act);
esp_err_t mcpwm_generator_set_actions_on_compare_event(mcpwm_gen_handle_t gen, mcpwm_gen_compare_event_action_t ev_act, ...);
esp_err_t mcpwm_generator_set_dead_time(mcpwm_gen_handle_t in_generator, mcpwm_gen_handle_t out_generator, const mcpwm_dead_time_config_t* config);

/* ---- fault and brake (driver/mcpwm_fault.h; rw_mcpwm.cpp) */
typedef struct {
    int group_id;
    int intr_priority;
    int gpio_num;
    struct {
        uint32_t active_level : 1;   /* 1: the fault is present while the GPIO is high */
        uint32_t io_loop_back : 1;
        uint32_t pull_up : 1;
        uint32_t pull_down : 1;
    } flags;
} mcpwm_gpio_fault_config_t;
typedef struct {
    int reserved;
} mcpwm_soft_fault_config_t;
typedef struct {
    int reserved;
} mcpwm_fault_event_data_t;
typedef bool (*mcpwm_fault_event_cb_t)(mcpwm_fault_handle_t fault, const mcpwm_fault_event_data_t* edata, void* user_ctx);
typedef struct {
    mcpwm_fault_event_cb_t on_fault_enter;
    mcpwm_fault_event_cb_t on_fault_exit;
} mcpwm_fault_event_callbacks_t;
typedef struct {
    mcpwm_fault_handle_t fault;
    mcpwm_operator_brake_mode_t brake_mode;
    struct {
        uint32_t cbc_recover_on_tez : 1;
        uint32_t cbc_recover_on_tep : 1;
    } flags;
} mcpwm_brake_config_t;
typedef struct {
    int reserved;
} mcpwm_brake_event_data_t;
typedef bool (*mcpwm_brake_event_cb_t)(mcpwm_oper_handle_t oper, const mcpwm_brake_event_data_t* edata, void* user_ctx);
typedef struct {
    mcpwm_brake_event_cb_t on_brake_cbc;
    mcpwm_brake_event_cb_t on_brake_ost;
} mcpwm_operator_event_callbacks_t;
typedef struct {
    mcpwm_timer_direction_t direction;
    mcpwm_operator_brake_mode_t brake_mode;
    mcpwm_generator_action_t action;
} mcpwm_gen_brake_event_action_t;
#define MCPWM_GEN_BRAKE_EVENT_ACTION(dir, mode, act) (mcpwm_gen_brake_event_action_t{(dir), (mode), (act)})
#define MCPWM_GEN_BRAKE_EVENT_ACTION_END() (mcpwm_gen_brake_event_action_t{MCPWM_TIMER_DIRECTION_UP, MCPWM_OPER_BRAKE_MODE_INVALID, MCPWM_GEN_ACTION_KEEP})

esp_err_t mcpwm_new_gpio_fault(const mcpwm_gpio_fault_config_t* config, mcpwm_fault_handle_t* ret_fault);
esp_err_t mcpwm_new_soft_fault(const mcpwm_soft_fault_config_t* config, mcpwm_fault_handle_t* ret_fault);
esp_err_t mcpwm_del_fault(mcpwm_fault_handle_t fault);
esp_err_t mcpwm_soft_fault_activate(mcpwm_fault_handle_t fault);
esp_err_t mcpwm_fault_register_event_callbacks(mcpwm_fault_handle_t fault, const mcpwm_fault_event_callbacks_t* cbs, void* user_data);
esp_err_t mcpwm_operator_set_brake_on_fault(mcpwm_oper_handle_t oper, const mcpwm_brake_config_t* config);
esp_err_t mcpwm_operator_recover_from_fault(mcpwm_oper_handle_t oper, mcpwm_fault_handle_t fault);
esp_err_t mcpwm_operator_register_event_callbacks(mcpwm_oper_handle_t oper, const mcpwm_operator_event_callbacks_t* cbs, void* user_data);
esp_err_t mcpwm_generator_set_action_on_brake_event(mcpwm_gen_handle_t generator, mcpwm_gen_brake_event_action_t ev_act);
esp_err_t mcpwm_generator_set_actions_on_brake_event(mcpwm_gen_handle_t generator, mcpwm_gen_brake_event_action_t ev_act, ...);

/* ---- sync (driver/mcpwm_sync.h; rw_mcpwm.cpp): a sync reloads the timers (and capture timers) that take it with their phase */
typedef struct {
    mcpwm_timer_event_t timer_event;   /* the timer's sync out on its empty or full (INVALID: none) */
    struct {
        uint32_t propagate_input_sync : 1;   /* its own sync input passed on */
    } flags;
} mcpwm_timer_sync_src_config_t;
typedef struct {
    int group_id;
    int gpio_num;
    struct {
        uint32_t active_neg : 1;   /* the falling edge syncs (by default the rising one) */
        uint32_t io_loop_back : 1;
        uint32_t pull_up : 1;
        uint32_t pull_down : 1;
    } flags;
} mcpwm_gpio_sync_src_config_t;
typedef struct {
    int reserved;
} mcpwm_soft_sync_config_t;
typedef struct {
    mcpwm_sync_handle_t sync_src;
    uint32_t count_value;
    mcpwm_timer_direction_t direction;
} mcpwm_timer_sync_phase_config_t;

esp_err_t mcpwm_new_timer_sync_src(mcpwm_timer_handle_t timer, const mcpwm_timer_sync_src_config_t* config, mcpwm_sync_handle_t* ret_sync);
esp_err_t mcpwm_new_gpio_sync_src(const mcpwm_gpio_sync_src_config_t* config, mcpwm_sync_handle_t* ret_sync);
esp_err_t mcpwm_new_soft_sync_src(const mcpwm_soft_sync_config_t* config, mcpwm_sync_handle_t* ret_sync);
esp_err_t mcpwm_del_sync_src(mcpwm_sync_handle_t sync);
esp_err_t mcpwm_soft_sync_activate(mcpwm_sync_handle_t sync);
esp_err_t mcpwm_timer_set_phase_on_sync(mcpwm_timer_handle_t timer, const mcpwm_timer_sync_phase_config_t* config);

/* ---- carrier (rw_mcpwm.cpp): chops an operator's outputs after dead time */
typedef enum { MCPWM_CARRIER_CLK_SRC_PLL160M = 1, MCPWM_CARRIER_CLK_SRC_DEFAULT = 1 } mcpwm_carrier_clock_source_t;
typedef struct {
    mcpwm_carrier_clock_source_t clk_src;
    uint32_t frequency_hz;
    uint32_t first_pulse_duration_us;
    float duty_cycle;
    struct {
        uint32_t invert_before_modulate : 1;
        uint32_t invert_after_modulate : 1;
    } flags;
} mcpwm_carrier_config_t;
esp_err_t mcpwm_operator_apply_carrier(mcpwm_oper_handle_t oper, const mcpwm_carrier_config_t* config);

/* ---- capture (driver/mcpwm_cap.h; rw_mcpwm_cap.cpp) */
typedef struct mcpwm_cap_timer_t* mcpwm_cap_timer_handle_t;
typedef struct mcpwm_cap_channel_t* mcpwm_cap_channel_handle_t;
typedef enum { MCPWM_CAP_EDGE_POS, MCPWM_CAP_EDGE_NEG } mcpwm_capture_edge_t;
typedef enum { MCPWM_CAPTURE_CLK_SRC_APB = 4, MCPWM_CAPTURE_CLK_SRC_DEFAULT = 4 } mcpwm_capture_clock_source_t;

typedef struct {
    int group_id;
    mcpwm_capture_clock_source_t clk_src;
    uint32_t resolution_hz;   /* the classic ESP32's capture timer counts the APB clock (80 MHz) */
    struct {
        uint32_t allow_pd : 1;
    } flags;
} mcpwm_capture_timer_config_t;

typedef struct {
    int gpio_num;
    int intr_priority;
    uint32_t prescale;   /* 0 or 1: every edge; otherwise even: the captured signal toggles every prescale/2 rising edges */
    struct {
        uint32_t pos_edge : 1;
        uint32_t neg_edge : 1;
        uint32_t pull_up : 1;
        uint32_t pull_down : 1;
        uint32_t invert_cap_signal : 1;
        uint32_t io_loop_back : 1;
        uint32_t keep_io_conf_at_exit : 1;
    } flags;
} mcpwm_capture_channel_config_t;

typedef struct {
    uint32_t cap_value;
    mcpwm_capture_edge_t cap_edge;
} mcpwm_capture_event_data_t;
typedef bool (*mcpwm_capture_event_cb_t)(mcpwm_cap_channel_handle_t cap_channel, const mcpwm_capture_event_data_t* edata, void* user_ctx);
typedef struct {
    mcpwm_capture_event_cb_t on_cap;
} mcpwm_capture_event_callbacks_t;

esp_err_t mcpwm_new_capture_timer(const mcpwm_capture_timer_config_t* config, mcpwm_cap_timer_handle_t* ret_cap_timer);
esp_err_t mcpwm_del_capture_timer(mcpwm_cap_timer_handle_t cap_timer);
esp_err_t mcpwm_capture_timer_enable(mcpwm_cap_timer_handle_t cap_timer);
esp_err_t mcpwm_capture_timer_disable(mcpwm_cap_timer_handle_t cap_timer);
esp_err_t mcpwm_capture_timer_start(mcpwm_cap_timer_handle_t cap_timer);
esp_err_t mcpwm_capture_timer_stop(mcpwm_cap_timer_handle_t cap_timer);
esp_err_t mcpwm_capture_timer_get_resolution(mcpwm_cap_timer_handle_t cap_timer, uint32_t* out_resolution);
typedef struct {
    mcpwm_sync_handle_t sync_src;
    uint32_t count_value;
    mcpwm_timer_direction_t direction;   /* the capture timer only counts up */
} mcpwm_capture_timer_sync_phase_config_t;
esp_err_t mcpwm_capture_timer_set_phase_on_sync(mcpwm_cap_timer_handle_t cap_timer, const mcpwm_capture_timer_sync_phase_config_t* config);
esp_err_t mcpwm_new_capture_channel(mcpwm_cap_timer_handle_t cap_timer, const mcpwm_capture_channel_config_t* config, mcpwm_cap_channel_handle_t* ret_cap_channel);
esp_err_t mcpwm_del_capture_channel(mcpwm_cap_channel_handle_t cap_channel);
esp_err_t mcpwm_capture_channel_enable(mcpwm_cap_channel_handle_t cap_channel);
esp_err_t mcpwm_capture_channel_disable(mcpwm_cap_channel_handle_t cap_channel);
esp_err_t mcpwm_capture_channel_register_event_callbacks(mcpwm_cap_channel_handle_t cap_channel, const mcpwm_capture_event_callbacks_t* cbs, void* user_data);
esp_err_t mcpwm_capture_channel_trigger_soft_catch(mcpwm_cap_channel_handle_t cap_channel);

#ifdef __cplusplus
}
#endif
