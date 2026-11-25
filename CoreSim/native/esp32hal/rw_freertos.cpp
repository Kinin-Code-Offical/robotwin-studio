/* Minimal FreeRTOS on top of the simulation scheduler (tasks, delays, notifications, semaphores, queues). */
#include "Arduino.h"
#include "rw_internal.h"

using namespace rw;

namespace {

const double kCostRtos = 300.0;

/* FreeRTOS counts its 1 ms tick interrupts: a wait of n ticks ends at the n-th tick boundary from now (n-1 to n ms). */
uint64_t ticks_to_deadline(TickType_t ticks) {
    if (ticks == portMAX_DELAY) return kForever;
    return tick_deadline(ticks);
}

bool can_block(TickType_t ticks) { return ticks != 0 && on_task() && !in_isr(); }

}  // namespace

/* ======================================================================= scheduler */
extern "C" void vPortYield(void) { yield(); }
extern "C" BaseType_t xPortGetCoreID(void) { return 1; }
extern "C" BaseType_t xPortInIsrContext(void) { return in_isr() ? pdTRUE : pdFALSE; }

/* ======================================================================= tasks */
extern "C" BaseType_t xTaskCreatePinnedToCore(TaskFunction_t code, const char* name, uint32_t stack, void* params,
                                              UBaseType_t priority, TaskHandle_t* created, BaseType_t) {
    charge(cost(20000.0));
    if (!code) return pdFAIL;
    if (priority >= configMAX_PRIORITIES) priority = configMAX_PRIORITIES - 1;
    void* h = create_task(code, name, stack, params, (int)priority);
    if (created) *created = h;
    return h ? pdPASS : errCOULD_NOT_ALLOCATE_REQUIRED_MEMORY;
}

extern "C" BaseType_t xTaskCreate(TaskFunction_t code, const char* name, uint32_t stack, void* params,
                                  UBaseType_t priority, TaskHandle_t* created) {
    return xTaskCreatePinnedToCore(code, name, stack, params, priority, created, tskNO_AFFINITY);
}

extern "C" void vTaskDelete(TaskHandle_t task) {
    charge(cost(kCostRtos));
    delete_task(task);
}

extern "C" void vTaskDelay(TickType_t ticks) {
    charge(cost(kCostRtos));
    if (ticks == 0) {
        yield_task();
        return;
    }

    sleep_until(tick_deadline(ticks));
}

extern "C" BaseType_t xTaskDelayUntil(TickType_t* previous, TickType_t increment) {
    charge(cost(kCostRtos));
    if (!previous) return pdFALSE;
    TickType_t target = *previous + increment;
    *previous = target;
    uint64_t at = config.time_origin_ns + (uint64_t)target * 1000000ull;
    if (at <= now_ns()) {
        yield_task();
        return pdFALSE;
    }

    sleep_until(at);
    return pdTRUE;
}

extern "C" void vTaskDelayUntil(TickType_t* previous, TickType_t increment) { xTaskDelayUntil(previous, increment); }

extern "C" TickType_t xTaskGetTickCount(void) {
    charge(cost(50.0));
    return (TickType_t)(uptime_ns() / 1000000ull);
}

extern "C" TickType_t xTaskGetTickCountFromISR(void) { return xTaskGetTickCount(); }

extern "C" TaskHandle_t xTaskGetCurrentTaskHandle(void) { return current_task(); }

extern "C" TaskHandle_t xTaskGetHandle(const char* name) { return name ? find_task(name) : nullptr; }

extern "C" char* pcTaskGetName(TaskHandle_t task) { return const_cast<char*>(task_name(task)); }

extern "C" void vTaskSuspend(TaskHandle_t task) {
    charge(cost(kCostRtos));
    suspend_task(task);
}

extern "C" void vTaskResume(TaskHandle_t task) {
    charge(cost(kCostRtos));
    resume_task(task);
}

extern "C" BaseType_t xTaskResumeFromISR(TaskHandle_t task) {
    resume_task(task);
    return pdTRUE;
}

extern "C" void vTaskSuspendAll(void) {}
extern "C" BaseType_t xTaskResumeAll(void) { return pdFALSE; }

extern "C" UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t) { return 2048; }
extern "C" UBaseType_t uxTaskPriorityGet(TaskHandle_t task) { return (UBaseType_t)task_priority(task); }
extern "C" void vTaskPrioritySet(TaskHandle_t task, UBaseType_t priority) { set_task_priority(task, (int)priority); }
extern "C" UBaseType_t uxTaskGetNumberOfTasks(void) { return (UBaseType_t)task_count(); }
extern "C" BaseType_t xTaskGetAffinity(TaskHandle_t) { return tskNO_AFFINITY; }

extern "C" eTaskState eTaskGetState(TaskHandle_t task) {
    if (!task || task == current_task()) return eRunning;
    return eReady;
}

/* ======================================================================= task notifications */
extern "C" BaseType_t xTaskNotify(TaskHandle_t task, uint32_t value, eNotifyAction action) {
    uint32_t* n = task_notify_value(task);
    if (!n) return pdFAIL;
    bool was_pending = n[1] != 0;
    switch (action) {
    case eSetBits: n[0] |= value; break;
    case eIncrement: n[0]++; break;
    case eSetValueWithOverwrite: n[0] = value; break;
    case eSetValueWithoutOverwrite:
        if (was_pending) return pdFAIL;
        n[0] = value;
        break;
    case eNoAction: break;
    }

    n[1] = 1;
    notify(n);
    return pdPASS;
}

extern "C" BaseType_t xTaskNotifyFromISR(TaskHandle_t task, uint32_t value, eNotifyAction action, BaseType_t* woken) {
    if (woken) *woken = pdTRUE;
    return xTaskNotify(task, value, action);
}

extern "C" BaseType_t xTaskNotifyGive(TaskHandle_t task) { return xTaskNotify(task, 0, eIncrement); }

extern "C" void vTaskNotifyGiveFromISR(TaskHandle_t task, BaseType_t* woken) {
    if (woken) *woken = pdTRUE;
    xTaskNotify(task, 0, eIncrement);
}

extern "C" uint32_t ulTaskNotifyTake(BaseType_t clear, TickType_t ticks) {
    charge(cost(kCostRtos));
    uint32_t* n = task_notify_value(nullptr);
    if (!n) return 0;
    uint64_t deadline = ticks_to_deadline(ticks);
    for (;;) {
        if (n[0] > 0) {
            uint32_t v = n[0];
            if (clear) n[0] = 0;
            else n[0]--;
            n[1] = 0;
            return v;
        }

        if (!can_block(ticks) || now_ns() >= deadline) return 0;
        wait_on(n, deadline);
    }
}

extern "C" BaseType_t xTaskNotifyWait(uint32_t clearOnEntry, uint32_t clearOnExit, uint32_t* value, TickType_t ticks) {
    charge(cost(kCostRtos));
    uint32_t* n = task_notify_value(nullptr);
    if (!n) return pdFALSE;
    if (!n[1]) n[0] &= ~clearOnEntry;
    uint64_t deadline = ticks_to_deadline(ticks);
    for (;;) {
        if (n[1]) {
            if (value) *value = n[0];
            n[0] &= ~clearOnExit;
            n[1] = 0;
            return pdTRUE;
        }

        if (!can_block(ticks) || now_ns() >= deadline) return pdFALSE;
        wait_on(n, deadline);
    }
}

/* ======================================================================= semaphores */
struct rw_semaphore {
    int kind;  // 0 binary, 1 counting, 2 mutex, 3 recursive mutex
    UBaseType_t count;
    UBaseType_t max;
    void* owner;
    UBaseType_t depth;
};

static SemaphoreHandle_t make_semaphore(int kind, UBaseType_t max, UBaseType_t initial) {
    rw_semaphore* s = new rw_semaphore();
    s->kind = kind;
    s->max = max;
    s->count = initial > max ? max : initial;
    s->owner = nullptr;
    s->depth = 0;
    return s;
}

extern "C" SemaphoreHandle_t xSemaphoreCreateBinary(void) { return make_semaphore(0, 1, 0); }
extern "C" SemaphoreHandle_t xSemaphoreCreateMutex(void) { return make_semaphore(2, 1, 1); }
extern "C" SemaphoreHandle_t xSemaphoreCreateRecursiveMutex(void) { return make_semaphore(3, 1, 1); }
extern "C" SemaphoreHandle_t xSemaphoreCreateCounting(UBaseType_t max, UBaseType_t initial) { return make_semaphore(1, max, initial); }

extern "C" void vSemaphoreDelete(SemaphoreHandle_t s) { delete s; }

extern "C" BaseType_t xSemaphoreTake(SemaphoreHandle_t s, TickType_t ticks) {
    charge(cost(kCostRtos));
    if (!s) return pdFALSE;
    uint64_t deadline = ticks_to_deadline(ticks);
    for (;;) {
        if (s->count > 0) {
            s->count--;
            if (s->kind >= 2) s->owner = current_task();
            return pdTRUE;
        }

        if (!can_block(ticks) || now_ns() >= deadline) return pdFALSE;
        wait_on(s, deadline);
    }
}

extern "C" BaseType_t xSemaphoreGive(SemaphoreHandle_t s) {
    charge(cost(kCostRtos));
    if (!s || s->count >= s->max) return pdFALSE;
    s->count++;
    if (s->kind >= 2) s->owner = nullptr;
    notify(s);
    return pdTRUE;
}

extern "C" BaseType_t xSemaphoreTakeRecursive(SemaphoreHandle_t s, TickType_t ticks) {
    if (!s) return pdFALSE;
    if (s->owner && s->owner == current_task()) {
        s->depth++;
        return pdTRUE;
    }

    if (!xSemaphoreTake(s, ticks)) return pdFALSE;
    s->depth = 1;
    return pdTRUE;
}

extern "C" BaseType_t xSemaphoreGiveRecursive(SemaphoreHandle_t s) {
    if (!s || s->owner != current_task()) return pdFALSE;
    if (--s->depth > 0) return pdTRUE;
    return xSemaphoreGive(s);
}

extern "C" BaseType_t xSemaphoreGiveFromISR(SemaphoreHandle_t s, BaseType_t* woken) {
    if (woken) *woken = pdTRUE;
    return xSemaphoreGive(s);
}

extern "C" BaseType_t xSemaphoreTakeFromISR(SemaphoreHandle_t s, BaseType_t* woken) {
    if (woken) *woken = pdFALSE;
    return xSemaphoreTake(s, 0);
}

extern "C" UBaseType_t uxSemaphoreGetCount(SemaphoreHandle_t s) { return s ? s->count : 0; }

/* ======================================================================= queues */
struct rw_queue {
    UBaseType_t length;
    UBaseType_t item_size;
    uint8_t* data;
    UBaseType_t head;
    UBaseType_t count;
};

extern "C" QueueHandle_t xQueueCreate(UBaseType_t length, UBaseType_t item_size) {
    if (length == 0) return nullptr;
    rw_queue* q = new rw_queue();
    q->length = length;
    q->item_size = item_size;
    q->data = item_size ? new uint8_t[(size_t)length * item_size] : nullptr;
    q->head = 0;
    q->count = 0;
    return q;
}

extern "C" void vQueueDelete(QueueHandle_t q) {
    if (!q) return;
    delete[] q->data;
    delete q;
}

static BaseType_t queue_send(QueueHandle_t q, const void* item, TickType_t ticks, bool front, bool overwrite) {
    charge(cost(kCostRtos));
    if (!q) return pdFALSE;
    uint64_t deadline = ticks_to_deadline(ticks);
    for (;;) {
        if (q->count < q->length || overwrite) {
            if (overwrite && q->count >= q->length) {
                q->count = 0;
                q->head = 0;
            }

            UBaseType_t slot;
            if (front) {
                q->head = (q->head + q->length - 1) % q->length;
                slot = q->head;
            } else {
                slot = (q->head + q->count) % q->length;
            }

            if (q->item_size && item) memcpy(q->data + (size_t)slot * q->item_size, item, q->item_size);
            q->count++;
            notify(q);
            return pdTRUE;
        }

        if (!can_block(ticks) || now_ns() >= deadline) return errQUEUE_FULL;
        wait_on(q, deadline);
    }
}

static BaseType_t queue_receive(QueueHandle_t q, void* buffer, TickType_t ticks, bool peek) {
    charge(cost(kCostRtos));
    if (!q) return pdFALSE;
    uint64_t deadline = ticks_to_deadline(ticks);
    for (;;) {
        if (q->count > 0) {
            if (q->item_size && buffer) memcpy(buffer, q->data + (size_t)q->head * q->item_size, q->item_size);
            if (!peek) {
                q->head = (q->head + 1) % q->length;
                q->count--;
                notify(q);
            }

            return pdTRUE;
        }

        if (!can_block(ticks) || now_ns() >= deadline) return errQUEUE_EMPTY;
        wait_on(q, deadline);
    }
}

extern "C" BaseType_t xQueueSend(QueueHandle_t q, const void* item, TickType_t ticks) { return queue_send(q, item, ticks, false, false); }
extern "C" BaseType_t xQueueSendToBack(QueueHandle_t q, const void* item, TickType_t ticks) { return queue_send(q, item, ticks, false, false); }
extern "C" BaseType_t xQueueSendToFront(QueueHandle_t q, const void* item, TickType_t ticks) { return queue_send(q, item, ticks, true, false); }
extern "C" BaseType_t xQueueOverwrite(QueueHandle_t q, const void* item) { return queue_send(q, item, 0, false, true); }
extern "C" BaseType_t xQueueReceive(QueueHandle_t q, void* buffer, TickType_t ticks) { return queue_receive(q, buffer, ticks, false); }
extern "C" BaseType_t xQueuePeek(QueueHandle_t q, void* buffer, TickType_t ticks) { return queue_receive(q, buffer, ticks, true); }

extern "C" BaseType_t xQueueSendFromISR(QueueHandle_t q, const void* item, BaseType_t* woken) {
    if (woken) *woken = pdTRUE;
    return queue_send(q, item, 0, false, false);
}

extern "C" BaseType_t xQueueSendToBackFromISR(QueueHandle_t q, const void* item, BaseType_t* woken) { return xQueueSendFromISR(q, item, woken); }

extern "C" BaseType_t xQueueSendToFrontFromISR(QueueHandle_t q, const void* item, BaseType_t* woken) {
    if (woken) *woken = pdTRUE;
    return queue_send(q, item, 0, true, false);
}

extern "C" BaseType_t xQueueOverwriteFromISR(QueueHandle_t q, const void* item, BaseType_t* woken) {
    if (woken) *woken = pdTRUE;
    return queue_send(q, item, 0, false, true);
}

extern "C" BaseType_t xQueueReceiveFromISR(QueueHandle_t q, void* buffer, BaseType_t* woken) {
    if (woken) *woken = pdFALSE;
    return queue_receive(q, buffer, 0, false);
}

extern "C" UBaseType_t uxQueueMessagesWaiting(QueueHandle_t q) { return q ? q->count : 0; }
extern "C" UBaseType_t uxQueueMessagesWaitingFromISR(QueueHandle_t q) { return uxQueueMessagesWaiting(q); }
extern "C" UBaseType_t uxQueueSpacesAvailable(QueueHandle_t q) { return q ? q->length - q->count : 0; }

extern "C" BaseType_t xQueueReset(QueueHandle_t q) {
    if (!q) return pdFALSE;
    q->head = 0;
    q->count = 0;
    notify(q);
    return pdTRUE;
}

extern "C" BaseType_t xQueueIsQueueEmptyFromISR(QueueHandle_t q) { return q && q->count == 0 ? pdTRUE : pdFALSE; }
extern "C" BaseType_t xQueueIsQueueFullFromISR(QueueHandle_t q) { return q && q->count >= q->length ? pdTRUE : pdFALSE; }
