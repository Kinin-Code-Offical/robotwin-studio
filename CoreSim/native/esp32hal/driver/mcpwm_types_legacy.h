/* RoboTwin ESP32 simulation - the legacy MCPWM driver's types (driver/mcpwm_types_legacy.h, IDF 4.x and the
 * deprecated driver of IDF 5): see driver/mcpwm.h. */
#pragma once
#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    MCPWM0A = 0,
    MCPWM0B,
    MCPWM1A,
    MCPWM1B,
    MCPWM2A,
    MCPWM2B,
    MCPWM_SYNC_0,
    MCPWM_SYNC_1,
    MCPWM_SYNC_2,
    MCPWM_FAULT_0,
    MCPWM_FAULT_1,
    MCPWM_FAULT_2,
    MCPWM_CAP_0 = 84,
    MCPWM_CAP_1,
    MCPWM_CAP_2,
} mcpwm_io_signals_t;

typedef struct {
    int mcpwm0a_out_num;
    int mcpwm0b_out_num;
    int mcpwm1a_out_num;
    int mcpwm1b_out_num;
    int mcpwm2a_out_num;
    int mcpwm2b_out_num;
    int mcpwm_sync0_in_num;
    int mcpwm_sync1_in_num;
    int mcpwm_sync2_in_num;
    int mcpwm_fault0_in_num;
    int mcpwm_fault1_in_num;
    int mcpwm_fault2_in_num;
    int mcpwm_cap0_in_num;
    int mcpwm_cap1_in_num;
    int mcpwm_cap2_in_num;
} mcpwm_pin_config_t;

typedef enum { MCPWM_UNIT_0, MCPWM_UNIT_1, MCPWM_UNIT_MAX } mcpwm_unit_t;
typedef enum { MCPWM_TIMER_0, MCPWM_TIMER_1, MCPWM_TIMER_2, MCPWM_TIMER_MAX } mcpwm_timer_t;
typedef enum { MCPWM_GEN_A, MCPWM_GEN_B, MCPWM_GEN_MAX } mcpwm_generator_t;
#define MCPWM_OPR_A MCPWM_GEN_A
#define MCPWM_OPR_B MCPWM_GEN_B
#define MCPWM_OPR_MAX MCPWM_GEN_MAX
typedef mcpwm_generator_t mcpwm_operator_t;

typedef enum { MCPWM_CARRIER_OUT_IVT_DIS, MCPWM_CARRIER_OUT_IVT_EN } mcpwm_carrier_out_ivt_t;
typedef enum { MCPWM_SELECT_F0, MCPWM_SELECT_F1, MCPWM_SELECT_F2 } mcpwm_fault_signal_t;
typedef enum {
    MCPWM_SELECT_NO_INPUT,
    MCPWM_SELECT_TIMER0_SYNC,
    MCPWM_SELECT_TIMER1_SYNC,
    MCPWM_SELECT_TIMER2_SYNC,
    MCPWM_SELECT_GPIO_SYNC0,
    MCPWM_SELECT_GPIO_SYNC1,
    MCPWM_SELECT_GPIO_SYNC2,
} mcpwm_sync_signal_t;
#define MCPWM_SELECT_SYNC0 MCPWM_SELECT_GPIO_SYNC0
#define MCPWM_SELECT_SYNC1 MCPWM_SELECT_GPIO_SYNC1
#define MCPWM_SELECT_SYNC2 MCPWM_SELECT_GPIO_SYNC2
typedef enum { MCPWM_SWSYNC_SOURCE_SYNCIN, MCPWM_SWSYNC_SOURCE_TEZ, MCPWM_SWSYNC_SOURCE_TEP, MCPWM_SWSYNC_SOURCE_DISABLED } mcpwm_timer_sync_trigger_t;
typedef enum { MCPWM_LOW_LEVEL_TGR, MCPWM_HIGH_LEVEL_TGR } mcpwm_fault_input_level_t;
typedef enum { MCPWM_NEG_EDGE = 1, MCPWM_POS_EDGE = 2, MCPWM_BOTH_EDGE = 3 } mcpwm_capture_on_edge_t;
typedef enum { MCPWM_FREEZE_COUNTER, MCPWM_UP_COUNTER, MCPWM_DOWN_COUNTER, MCPWM_UP_DOWN_COUNTER, MCPWM_COUNTER_MAX } mcpwm_counter_type_t;
typedef enum { MCPWM_DUTY_MODE_0 = 0, MCPWM_DUTY_MODE_1, MCPWM_DUTY_MODE_FORCE_LOW, MCPWM_DUTY_MODE_FORCE_HIGH, MCPWM_DUTY_MODE_MAX } mcpwm_duty_type_t;
#define MCPWM_HAL_GENERATOR_MODE_FORCE_LOW MCPWM_DUTY_MODE_FORCE_LOW
#define MCPWM_HAL_GENERATOR_MODE_FORCE_HIGH MCPWM_DUTY_MODE_FORCE_HIGH
typedef enum {
    MCPWM_DEADTIME_BYPASS = 0,
    MCPWM_BYPASS_RED,
    MCPWM_BYPASS_FED,
    MCPWM_ACTIVE_HIGH_MODE,
    MCPWM_ACTIVE_LOW_MODE,
    MCPWM_ACTIVE_HIGH_COMPLIMENT_MODE,
    MCPWM_ACTIVE_LOW_COMPLIMENT_MODE,
    MCPWM_ACTIVE_RED_FED_FROM_PWMXA,
    MCPWM_ACTIVE_RED_FED_FROM_PWMXB,
    MCPWM_DEADTIME_TYPE_MAX,
} mcpwm_deadtime_type_t;
typedef enum { MCPWM_ACTION_NO_CHANGE = 0, MCPWM_ACTION_FORCE_LOW, MCPWM_ACTION_FORCE_HIGH, MCPWM_ACTION_TOGGLE } mcpwm_output_action_t;
typedef mcpwm_output_action_t mcpwm_action_on_pwmxa_t;
typedef mcpwm_output_action_t mcpwm_action_on_pwmxb_t;
#define MCPWM_NO_CHANGE_IN_MCPWMXA MCPWM_ACTION_NO_CHANGE
#define MCPWM_FORCE_MCPWMXA_LOW MCPWM_ACTION_FORCE_LOW
#define MCPWM_FORCE_MCPWMXA_HIGH MCPWM_ACTION_FORCE_HIGH
#define MCPWM_TOG_MCPWMXA MCPWM_ACTION_TOGGLE
#define MCPWM_NO_CHANGE_IN_MCPWMXB MCPWM_ACTION_NO_CHANGE
#define MCPWM_FORCE_MCPWMXB_LOW MCPWM_ACTION_FORCE_LOW
#define MCPWM_FORCE_MCPWMXB_HIGH MCPWM_ACTION_FORCE_HIGH
#define MCPWM_TOG_MCPWMXB MCPWM_ACTION_TOGGLE
typedef enum { MCPWM_SELECT_CAP0, MCPWM_SELECT_CAP1, MCPWM_SELECT_CAP2 } mcpwm_capture_signal_t;
typedef mcpwm_capture_signal_t mcpwm_capture_channel_id_t;

typedef struct {
    mcpwm_capture_on_edge_t cap_edge;
    uint32_t cap_value;
} cap_event_data_t;
typedef bool (*cap_isr_cb_t)(mcpwm_unit_t mcpwm, mcpwm_capture_channel_id_t cap_channel, const cap_event_data_t* edata, void* user_data);

typedef struct {
    uint32_t frequency;   /* Hz */
    float cmpr_a;         /* duty of output A, % */
    float cmpr_b;
    mcpwm_duty_type_t duty_mode;
    mcpwm_counter_type_t counter_mode;
} mcpwm_config_t;

typedef struct {
    uint8_t carrier_period;
    uint8_t carrier_duty;
    uint8_t pulse_width_in_os;
    mcpwm_carrier_out_ivt_t carrier_ivt_mode;
} mcpwm_carrier_config_t;

typedef struct {
    mcpwm_capture_on_edge_t cap_edge;
    uint32_t cap_prescale;   /* 1: every edge */
    cap_isr_cb_t capture_cb;
    void* user_data;
} mcpwm_capture_config_t;

#ifdef __cplusplus
}
#endif
