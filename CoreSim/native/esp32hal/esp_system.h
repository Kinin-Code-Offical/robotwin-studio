/* esp_system.h / esp_random.h / esp_mac.h subset. */
#ifndef RW_ESP_SYSTEM_H
#define RW_ESP_SYSTEM_H
#include <stdint.h>
#include <stddef.h>

#ifndef ESP_OK
typedef int esp_err_t;
#define ESP_OK 0
#define ESP_FAIL -1
#endif

typedef enum {
    ESP_RST_UNKNOWN = 0,
    ESP_RST_POWERON,
    ESP_RST_EXT,
    ESP_RST_SW,
    ESP_RST_PANIC,
    ESP_RST_INT_WDT,
    ESP_RST_TASK_WDT,
    ESP_RST_WDT,
    ESP_RST_DEEPSLEEP,
    ESP_RST_BROWNOUT,
    ESP_RST_SDIO,
} esp_reset_reason_t;

typedef enum {
    ESP_MAC_WIFI_STA,
    ESP_MAC_WIFI_SOFTAP,
    ESP_MAC_BT,
    ESP_MAC_ETH,
} esp_mac_type_t;

#ifdef __cplusplus
extern "C" {
#endif
uint32_t esp_random(void);
void esp_fill_random(void* buf, size_t len);
void esp_restart(void);
esp_reset_reason_t esp_reset_reason(void);
uint32_t esp_get_free_heap_size(void);
uint32_t esp_get_minimum_free_heap_size(void);
esp_err_t esp_efuse_mac_get_default(uint8_t* mac);
esp_err_t esp_read_mac(uint8_t* mac, esp_mac_type_t type);
const char* esp_get_idf_version(void);
const char* esp_err_to_name(esp_err_t code);
#ifdef __cplusplus
}
#endif

#ifndef ESP_ERR_NO_MEM
#define ESP_ERR_NO_MEM 0x101
#endif
#ifndef ESP_ERR_INVALID_ARG
#define ESP_ERR_INVALID_ARG 0x102
#endif
#ifndef ESP_ERR_INVALID_STATE
#define ESP_ERR_INVALID_STATE 0x103
#endif
#ifndef ESP_ERR_INVALID_SIZE
#define ESP_ERR_INVALID_SIZE 0x104
#endif
#ifndef ESP_ERR_NOT_FOUND
#define ESP_ERR_NOT_FOUND 0x105
#endif
#ifndef ESP_ERR_NOT_SUPPORTED
#define ESP_ERR_NOT_SUPPORTED 0x106
#endif
#ifndef ESP_ERR_TIMEOUT
#define ESP_ERR_TIMEOUT 0x107
#endif

#ifdef __cplusplus
extern "C" {
#endif
/* ESP_ERROR_CHECK's failure: the chip's report (code, file, line, function, expression), abort(), a PANIC restart. */
void rw_esp_error_check_failed(esp_err_t rc, const char* file, int line, const char* function, const char* expression);
void rw_esp_error_check_report(esp_err_t rc, const char* file, int line, const char* function, const char* expression);
#ifdef __cplusplus
}
#endif

#define ESP_ERROR_CHECK(x) do { esp_err_t rc_ = (x); if (rc_ != ESP_OK) rw_esp_error_check_failed(rc_, __FILE__, __LINE__, __FUNCTION__, #x); } while (0)
#define ESP_ERROR_CHECK_WITHOUT_ABORT(x) ([&]() { esp_err_t rc_ = (x); if (rc_ != ESP_OK) rw_esp_error_check_report(rc_, __FILE__, __LINE__, __FUNCTION__, #x); return rc_; }())
#endif
