/* FreeRTOS task API subset (see FreeRTOS.h for the scheduling model). */
#ifndef RW_FREERTOS_TASK_H
#define RW_FREERTOS_TASK_H
#include "FreeRTOS.h"

typedef enum { eNoAction = 0, eSetBits, eIncrement, eSetValueWithOverwrite, eSetValueWithoutOverwrite } eNotifyAction;
typedef enum { eRunning = 0, eReady, eBlocked, eSuspended, eDeleted, eInvalid } eTaskState;

#ifdef __cplusplus
extern "C" {
#endif
BaseType_t xTaskCreate(TaskFunction_t pxTaskCode, const char* pcName, uint32_t usStackDepth, void* pvParameters,
                       UBaseType_t uxPriority, TaskHandle_t* pxCreatedTask);
BaseType_t xTaskCreatePinnedToCore(TaskFunction_t pxTaskCode, const char* pcName, uint32_t usStackDepth, void* pvParameters,
                                   UBaseType_t uxPriority, TaskHandle_t* pxCreatedTask, BaseType_t xCoreID);
void vTaskDelete(TaskHandle_t xTaskToDelete);
void vTaskDelay(TickType_t xTicksToDelay);
void vTaskDelayUntil(TickType_t* pxPreviousWakeTime, TickType_t xTimeIncrement);
BaseType_t xTaskDelayUntil(TickType_t* pxPreviousWakeTime, TickType_t xTimeIncrement);
TickType_t xTaskGetTickCount(void);
TickType_t xTaskGetTickCountFromISR(void);
TaskHandle_t xTaskGetCurrentTaskHandle(void);
TaskHandle_t xTaskGetHandle(const char* pcNameToQuery);
char* pcTaskGetName(TaskHandle_t xTaskToQuery);
void vTaskSuspend(TaskHandle_t xTaskToSuspend);
void vTaskResume(TaskHandle_t xTaskToResume);
BaseType_t xTaskResumeFromISR(TaskHandle_t xTaskToResume);
void vTaskSuspendAll(void);
BaseType_t xTaskResumeAll(void);
UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t xTask);
UBaseType_t uxTaskPriorityGet(TaskHandle_t xTask);
void vTaskPrioritySet(TaskHandle_t xTask, UBaseType_t uxNewPriority);
UBaseType_t uxTaskGetNumberOfTasks(void);
eTaskState eTaskGetState(TaskHandle_t xTask);
BaseType_t xTaskGetAffinity(TaskHandle_t xTask);

BaseType_t xTaskNotify(TaskHandle_t xTaskToNotify, uint32_t ulValue, eNotifyAction eAction);
BaseType_t xTaskNotifyFromISR(TaskHandle_t xTaskToNotify, uint32_t ulValue, eNotifyAction eAction, BaseType_t* pxHigherPriorityTaskWoken);
BaseType_t xTaskNotifyGive(TaskHandle_t xTaskToNotify);
void vTaskNotifyGiveFromISR(TaskHandle_t xTaskToNotify, BaseType_t* pxHigherPriorityTaskWoken);
uint32_t ulTaskNotifyTake(BaseType_t xClearCountOnExit, TickType_t xTicksToWait);
BaseType_t xTaskNotifyWait(uint32_t ulBitsToClearOnEntry, uint32_t ulBitsToClearOnExit, uint32_t* pulNotificationValue, TickType_t xTicksToWait);
#ifdef __cplusplus
}
#endif

#define taskYIELD() vPortYield()
#define pcTaskGetTaskName pcTaskGetName
#define xTaskHandle TaskHandle_t
#define pdTASK_CODE TaskFunction_t
#define vTaskStartScheduler() ((void)0)

#endif
