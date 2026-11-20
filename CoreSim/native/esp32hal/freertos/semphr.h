/* FreeRTOS semaphores and mutexes (binary, counting, mutex, recursive mutex). */
#ifndef RW_FREERTOS_SEMPHR_H
#define RW_FREERTOS_SEMPHR_H
#include "FreeRTOS.h"
#include "queue.h"

typedef struct rw_semaphore* SemaphoreHandle_t;
typedef SemaphoreHandle_t xSemaphoreHandle;

#ifdef __cplusplus
extern "C" {
#endif
SemaphoreHandle_t xSemaphoreCreateBinary(void);
SemaphoreHandle_t xSemaphoreCreateMutex(void);
SemaphoreHandle_t xSemaphoreCreateRecursiveMutex(void);
SemaphoreHandle_t xSemaphoreCreateCounting(UBaseType_t uxMaxCount, UBaseType_t uxInitialCount);
void vSemaphoreDelete(SemaphoreHandle_t xSemaphore);
BaseType_t xSemaphoreTake(SemaphoreHandle_t xSemaphore, TickType_t xBlockTime);
BaseType_t xSemaphoreGive(SemaphoreHandle_t xSemaphore);
BaseType_t xSemaphoreTakeRecursive(SemaphoreHandle_t xMutex, TickType_t xBlockTime);
BaseType_t xSemaphoreGiveRecursive(SemaphoreHandle_t xMutex);
BaseType_t xSemaphoreGiveFromISR(SemaphoreHandle_t xSemaphore, BaseType_t* pxHigherPriorityTaskWoken);
BaseType_t xSemaphoreTakeFromISR(SemaphoreHandle_t xSemaphore, BaseType_t* pxHigherPriorityTaskWoken);
UBaseType_t uxSemaphoreGetCount(SemaphoreHandle_t xSemaphore);
#ifdef __cplusplus
}
#endif

#define vSemaphoreCreateBinary(xSemaphore) do { (xSemaphore) = xSemaphoreCreateBinary(); if (xSemaphore) xSemaphoreGive(xSemaphore); } while (0)
#endif
