/* esp_timer.h subset: the microsecond time base. */
#ifndef RW_ESP_TIMER_H
#define RW_ESP_TIMER_H
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
/* Microseconds since the chip reset (64-bit, never wraps in practice). */
int64_t esp_timer_get_time(void);
#ifdef __cplusplus
}
#endif
#endif
