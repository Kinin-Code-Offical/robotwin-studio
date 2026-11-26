/* esp_now.h: ESP-NOW (ESP-IDF 4.x and 5.x callbacks both accepted), carried by the host's radio medium. */
#ifndef RW_ESP_NOW_H
#define RW_ESP_NOW_H
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

#ifndef ESP_OK
typedef int esp_err_t;
#define ESP_OK 0
#define ESP_FAIL -1
#endif

#define ESP_ERR_ESPNOW_BASE (0x3000 + 100)
#define ESP_ERR_ESPNOW_NOT_INIT (ESP_ERR_ESPNOW_BASE + 1)
#define ESP_ERR_ESPNOW_ARG (ESP_ERR_ESPNOW_BASE + 2)
#define ESP_ERR_ESPNOW_NO_MEM (ESP_ERR_ESPNOW_BASE + 3)
#define ESP_ERR_ESPNOW_FULL (ESP_ERR_ESPNOW_BASE + 4)
#define ESP_ERR_ESPNOW_NOT_FOUND (ESP_ERR_ESPNOW_BASE + 5)
#define ESP_ERR_ESPNOW_INTERNAL (ESP_ERR_ESPNOW_BASE + 6)
#define ESP_ERR_ESPNOW_EXIST (ESP_ERR_ESPNOW_BASE + 7)
#define ESP_ERR_ESPNOW_IF (ESP_ERR_ESPNOW_BASE + 8)

#define ESP_NOW_ETH_ALEN 6
#define ESP_NOW_KEY_LEN 16
#define ESP_NOW_MAX_TOTAL_PEER_NUM 20
#define ESP_NOW_MAX_ENCRYPT_PEER_NUM 6
#define ESP_NOW_MAX_DATA_LEN 250

#ifndef RW_WIFI_INTERFACE_T
#define RW_WIFI_INTERFACE_T
typedef enum { WIFI_IF_STA = 0, WIFI_IF_AP = 1 } wifi_interface_t;
#endif

typedef enum { ESP_NOW_SEND_SUCCESS = 0, ESP_NOW_SEND_FAIL } esp_now_send_status_t;

typedef struct esp_now_peer_info {
    uint8_t peer_addr[ESP_NOW_ETH_ALEN];
    uint8_t lmk[ESP_NOW_KEY_LEN];
    uint8_t channel;
    wifi_interface_t ifidx;
    bool encrypt;
    void* priv;
} esp_now_peer_info_t;

typedef struct {
    int rssi;
    unsigned channel;
} wifi_pkt_rx_ctrl_t;

typedef struct esp_now_recv_info {
    uint8_t* src_addr;
    uint8_t* des_addr;
    wifi_pkt_rx_ctrl_t* rx_ctrl;
} esp_now_recv_info_t;

typedef struct {
    const uint8_t* des_addr;
    const uint8_t* src_addr;
} wifi_tx_info_t;

typedef struct {
    int total_num;
    int encrypt_num;
} esp_now_peer_num_t;

typedef void (*esp_now_recv_cb_t)(const esp_now_recv_info_t* info, const uint8_t* data, int len);   /* IDF 5 */
typedef void (*esp_now_recv_cb_v4_t)(const uint8_t* mac, const uint8_t* data, int len);           /* IDF 4 (core 2.x) */
typedef void (*esp_now_send_cb_t)(const uint8_t* mac, esp_now_send_status_t status);
typedef void (*esp_now_send_cb_v55_t)(const wifi_tx_info_t* info, esp_now_send_status_t status);  /* IDF 5.5 (core 3.3) */

esp_err_t esp_now_init(void);
esp_err_t esp_now_deinit(void);
esp_err_t esp_now_get_version(uint32_t* version);
esp_err_t esp_now_unregister_recv_cb(void);
esp_err_t esp_now_unregister_send_cb(void);
esp_err_t esp_now_send(const uint8_t* peer_addr, const uint8_t* data, size_t len);
esp_err_t esp_now_add_peer(const esp_now_peer_info_t* peer);
esp_err_t esp_now_del_peer(const uint8_t* peer_addr);
esp_err_t esp_now_mod_peer(const esp_now_peer_info_t* peer);
esp_err_t esp_now_get_peer(const uint8_t* peer_addr, esp_now_peer_info_t* peer);
bool esp_now_is_peer_exist(const uint8_t* peer_addr);
esp_err_t esp_now_get_peer_num(esp_now_peer_num_t* num);
esp_err_t esp_now_set_pmk(const uint8_t* pmk);

#ifdef __cplusplus
esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_t cb);
esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_v4_t cb);
esp_err_t esp_now_register_send_cb(esp_now_send_cb_t cb);
esp_err_t esp_now_register_send_cb(esp_now_send_cb_v55_t cb);
#endif

#endif /* RW_ESP_NOW_H */
