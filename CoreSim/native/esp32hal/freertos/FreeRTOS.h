/*
 * Minimal FreeRTOS for the RoboTwin ESP32 simulation. Tasks are real threads that
 * the simulator runs cooperatively (one at a time, like a single core) in simulated
 * time: a task switch happens when a task blocks (vTaskDelay, a semaphore/queue wait),
 * yields, or after a 1 ms tick of charged execution time.
 */
#ifndef RW_FREERTOS_H
#define RW_FREERTOS_H
#include <stdint.h>
#include <stddef.h>

typedef int BaseType_t;
typedef unsigned int UBaseType_t;
typedef uint32_t TickType_t;
typedef uint32_t StackType_t;
typedef void* TaskHandle_t;
typedef void (*TaskFunction_t)(void*);

#define pdFALSE ((BaseType_t)0)
#define pdTRUE ((BaseType_t)1)
#define pdFAIL pdFALSE
#define pdPASS pdTRUE
#define errQUEUE_EMPTY ((BaseType_t)0)
#define errQUEUE_FULL ((BaseType_t)0)
#define errCOULD_NOT_ALLOCATE_REQUIRED_MEMORY (-1)

#define configTICK_RATE_HZ 1000
#define configMAX_PRIORITIES 25
#define configMINIMAL_STACK_SIZE 768
#define configASSERT(x) ((void)0)
#define portMAX_DELAY ((TickType_t)0xffffffffUL)
#define portTICK_PERIOD_MS ((TickType_t)1000 / configTICK_RATE_HZ)
#define portTICK_RATE_MS portTICK_PERIOD_MS
#define pdMS_TO_TICKS(xTimeInMs) ((TickType_t)(((uint64_t)(xTimeInMs) * (TickType_t)configTICK_RATE_HZ) / (TickType_t)1000U))
#define pdTICKS_TO_MS(xTicks) ((TickType_t)(((uint64_t)(xTicks) * (TickType_t)1000U) / (TickType_t)configTICK_RATE_HZ))
#define tskIDLE_PRIORITY ((UBaseType_t)0U)
#define tskNO_AFFINITY ((BaseType_t)0x7FFFFFFF)
#define portNUM_PROCESSORS 2

/* Critical sections: the simulated CPU runs one task at a time, so these are no-ops. */
typedef struct { uint32_t owner; uint32_t count; } portMUX_TYPE;
#define portMUX_INITIALIZER_UNLOCKED { 0xB33FFFFFu, 0 }
#define portMUX_FREE_VAL 0xB33FFFFFu
#define spinlock_initialize(m) ((void)(m))
#define vPortCPUInitializeMutex(m) ((void)(m))
#define portENTER_CRITICAL(m) ((void)(m))
#define portEXIT_CRITICAL(m) ((void)(m))
#define portENTER_CRITICAL_ISR(m) ((void)(m))
#define portEXIT_CRITICAL_ISR(m) ((void)(m))
#define portENTER_CRITICAL_SAFE(m) ((void)(m))
#define portEXIT_CRITICAL_SAFE(m) ((void)(m))
#define taskENTER_CRITICAL(m) ((void)(m))
#define taskEXIT_CRITICAL(m) ((void)(m))
#define taskENTER_CRITICAL_ISR(m) ((void)(m))
#define taskEXIT_CRITICAL_ISR(m) ((void)(m))
#define taskDISABLE_INTERRUPTS() ((void)0)
#define taskENABLE_INTERRUPTS() ((void)0)
#define portDISABLE_INTERRUPTS() ((void)0)
#define portENABLE_INTERRUPTS() ((void)0)
#define portYIELD_FROM_ISR(...) ((void)0)
#define portEND_SWITCHING_ISR(x) ((void)(x))
#define portYIELD() vPortYield()

#ifdef __cplusplus
extern "C" {
#endif
void vPortYield(void);
BaseType_t xPortGetCoreID(void);
BaseType_t xPortInIsrContext(void);
#ifdef __cplusplus
}
#endif

#endif
