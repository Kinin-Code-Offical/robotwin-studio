/* RoboTwin ESP32 simulation - the pulse counter's action and zero-cross types (ESP-IDF hal/pcnt_types.h). */
#pragma once

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    PCNT_CHANNEL_LEVEL_ACTION_KEEP,    /* keep the edge actions */
    PCNT_CHANNEL_LEVEL_ACTION_INVERSE, /* swap increase and decrease */
    PCNT_CHANNEL_LEVEL_ACTION_HOLD,    /* do not count */
} pcnt_channel_level_action_t;

typedef enum {
    PCNT_CHANNEL_EDGE_ACTION_HOLD,
    PCNT_CHANNEL_EDGE_ACTION_INCREASE,
    PCNT_CHANNEL_EDGE_ACTION_DECREASE,
} pcnt_channel_edge_action_t;

typedef enum {
    PCNT_UNIT_ZERO_CROSS_POS_ZERO, /* +N -> 0 */
    PCNT_UNIT_ZERO_CROSS_NEG_ZERO, /* -N -> 0 */
    PCNT_UNIT_ZERO_CROSS_NEG_POS,  /* -N -> +M */
    PCNT_UNIT_ZERO_CROSS_POS_NEG,  /* +N -> -M */
    PCNT_UNIT_ZERO_CROSS_INVALID,
} pcnt_unit_zero_cross_mode_t;

typedef enum {
    PCNT_STEP_FORWARD,
    PCNT_STEP_BACKWARD,
} pcnt_step_direction_t;

#ifdef __cplusplus
}
#endif
