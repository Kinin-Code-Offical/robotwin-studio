/* [espnow] ESP-NOW over the host's radio medium: peers are kept here as the chip's driver keeps them (20 at most,
   unicast only to a registered peer), frames go out through RW_RADIO_ESPNOW_SEND and come back through a Wi-Fi task
   that delivers them to the receive callback and reports each send's outcome to the send callback, as on the chip. */
#include "Arduino.h"
#include "esp_now.h"
#include "rw_internal.h"

namespace {

struct Peer {
    esp_now_peer_info_t info;
};

bool g_on = false;
bool g_task = false;
Peer g_peers[ESP_NOW_MAX_TOTAL_PEER_NUM];
int g_peer_count = 0;
esp_now_recv_cb_t g_recv = nullptr;
esp_now_recv_cb_v4_t g_recv_v4 = nullptr;
esp_now_send_cb_t g_send = nullptr;
esp_now_send_cb_v55_t g_send_v55 = nullptr;

#pragma pack(push, 1)
struct PollOut {
    uint8_t kind, status;
    int8_t rssi;
    uint8_t pad;
    uint8_t mac[6];
    uint16_t len;
    uint8_t data[ESP_NOW_MAX_DATA_LEN];
};
#pragma pack(pop)

int find_peer(const uint8_t* mac) {
    for (int i = 0; i < g_peer_count; i++)
        if (memcmp(g_peers[i].info.peer_addr, mac, 6) == 0) return i;
    return -1;
}

void deliver(const PollOut& ev) {
    if (ev.kind == 1) {
        uint8_t src[6], dst[6];
        memcpy(src, ev.mac, 6);
        memcpy(dst, rw::radio_mac(), 6);
        wifi_pkt_rx_ctrl_t ctrl = {ev.rssi, 1};
        esp_now_recv_info_t info = {src, dst, &ctrl};
        if (g_recv) g_recv(&info, ev.data, ev.len);
        else if (g_recv_v4) g_recv_v4(src, ev.data, ev.len);
    } else if (ev.kind == 2) {
        uint8_t mac[6];
        memcpy(mac, ev.mac, 6);
        esp_now_send_status_t st = ev.status == 0 ? ESP_NOW_SEND_SUCCESS : ESP_NOW_SEND_FAIL;
        if (g_send) g_send(mac, st);
        else if (g_send_v55) {
            wifi_tx_info_t info = {mac, rw::radio_mac()};
            g_send_v55(&info, st);
        }
    }
}

/* The Wi-Fi task's part for ESP-NOW: every millisecond, what arrived and what was sent. */
void espnow_task(void*) {
    static PollOut ev;
    for (;;) {
        for (int k = 0; k < 16 && g_on; k++) {
            memset(&ev, 0, 12);
            if (rw::radio(RW_RADIO_ESPNOW_POLL, nullptr, 0, &ev, sizeof ev) != 1) break;
            deliver(ev);
        }
        rw::sleep_until(rw::now_ns() + 1000000ull);
    }
}

}  // namespace

esp_err_t esp_now_init(void) {
    if (!rw::radio_available()) return ESP_ERR_ESPNOW_INTERNAL;
    int32_t on = 1;
    if (rw::radio(RW_RADIO_ESPNOW_INIT, &on, sizeof on, nullptr, 0) < 0) return ESP_ERR_ESPNOW_INTERNAL;   // Wi-Fi not started
    g_on = true;
    if (!g_task) {
        g_task = true;
        rw::create_task(espnow_task, "wifi", 3584, nullptr, 23);
    }
    return ESP_OK;
}

esp_err_t esp_now_deinit(void) {
    int32_t off = 0;
    if (g_on) rw::radio(RW_RADIO_ESPNOW_INIT, &off, sizeof off, nullptr, 0);
    g_on = false;
    g_peer_count = 0;
    g_recv = nullptr;
    g_recv_v4 = nullptr;
    g_send = nullptr;
    g_send_v55 = nullptr;
    return ESP_OK;
}

esp_err_t esp_now_get_version(uint32_t* version) {
    if (!version) return ESP_ERR_ESPNOW_ARG;
    *version = 1;
    return ESP_OK;
}

esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_t cb) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    g_recv = cb;
    g_recv_v4 = nullptr;
    return ESP_OK;
}

esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_v4_t cb) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    g_recv_v4 = cb;
    g_recv = nullptr;
    return ESP_OK;
}

esp_err_t esp_now_unregister_recv_cb(void) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    g_recv = nullptr;
    g_recv_v4 = nullptr;
    return ESP_OK;
}

esp_err_t esp_now_register_send_cb(esp_now_send_cb_t cb) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    g_send = cb;
    g_send_v55 = nullptr;
    return ESP_OK;
}

esp_err_t esp_now_register_send_cb(esp_now_send_cb_v55_t cb) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    g_send_v55 = cb;
    g_send = nullptr;
    return ESP_OK;
}

esp_err_t esp_now_unregister_send_cb(void) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    g_send = nullptr;
    g_send_v55 = nullptr;
    return ESP_OK;
}

static esp_err_t send_one(const uint8_t* mac, const uint8_t* data, size_t len) {
    uint8_t buf[6 + ESP_NOW_MAX_DATA_LEN];
    memcpy(buf, mac, 6);
    memcpy(buf + 6, data, len);
    rw::charge(rw::cost(20000.0));   // the driver queues the frame (~20 µs)
    return rw::radio(RW_RADIO_ESPNOW_SEND, buf, (int)(6 + len), nullptr, 0) < 0 ? ESP_ERR_ESPNOW_NOT_INIT : ESP_OK;
}

esp_err_t esp_now_send(const uint8_t* peer_addr, const uint8_t* data, size_t len) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    if (!data || len == 0 || len > ESP_NOW_MAX_DATA_LEN) return ESP_ERR_ESPNOW_ARG;
    if (!peer_addr) {   // every registered (unicast) peer
        if (g_peer_count == 0) return ESP_ERR_ESPNOW_NOT_FOUND;
        for (int i = 0; i < g_peer_count; i++) {
            esp_err_t e = send_one(g_peers[i].info.peer_addr, data, len);
            if (e != ESP_OK) return e;
        }
        return ESP_OK;
    }
    if (find_peer(peer_addr) < 0) return ESP_ERR_ESPNOW_NOT_FOUND;   // the broadcast address is a peer too
    return send_one(peer_addr, data, len);
}

esp_err_t esp_now_add_peer(const esp_now_peer_info_t* peer) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    if (!peer) return ESP_ERR_ESPNOW_ARG;
    if (peer->channel > 14) return ESP_ERR_ESPNOW_ARG;
    if (find_peer(peer->peer_addr) >= 0) return ESP_ERR_ESPNOW_EXIST;
    if (g_peer_count >= ESP_NOW_MAX_TOTAL_PEER_NUM) return ESP_ERR_ESPNOW_FULL;
    g_peers[g_peer_count++].info = *peer;
    return ESP_OK;
}

esp_err_t esp_now_del_peer(const uint8_t* peer_addr) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    if (!peer_addr) return ESP_ERR_ESPNOW_ARG;
    int i = find_peer(peer_addr);
    if (i < 0) return ESP_ERR_ESPNOW_NOT_FOUND;
    g_peers[i] = g_peers[--g_peer_count];
    return ESP_OK;
}

esp_err_t esp_now_mod_peer(const esp_now_peer_info_t* peer) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    if (!peer) return ESP_ERR_ESPNOW_ARG;
    int i = find_peer(peer->peer_addr);
    if (i < 0) return ESP_ERR_ESPNOW_NOT_FOUND;
    g_peers[i].info = *peer;
    return ESP_OK;
}

esp_err_t esp_now_get_peer(const uint8_t* peer_addr, esp_now_peer_info_t* peer) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    if (!peer_addr || !peer) return ESP_ERR_ESPNOW_ARG;
    int i = find_peer(peer_addr);
    if (i < 0) return ESP_ERR_ESPNOW_NOT_FOUND;
    *peer = g_peers[i].info;
    return ESP_OK;
}

bool esp_now_is_peer_exist(const uint8_t* peer_addr) { return g_on && peer_addr && find_peer(peer_addr) >= 0; }

esp_err_t esp_now_get_peer_num(esp_now_peer_num_t* num) {
    if (!g_on) return ESP_ERR_ESPNOW_NOT_INIT;
    if (!num) return ESP_ERR_ESPNOW_ARG;
    num->total_num = g_peer_count;
    num->encrypt_num = 0;
    for (int i = 0; i < g_peer_count; i++)
        if (g_peers[i].info.encrypt) num->encrypt_num++;
    return ESP_OK;
}

esp_err_t esp_now_set_pmk(const uint8_t* pmk) { return pmk ? ESP_OK : ESP_ERR_ESPNOW_ARG; }
