/* Radio medium access (ABI 2): thin wrapper around rw_host_api.radio_request. */
#include "rw_internal.h"
#include <string.h>

namespace rw {

namespace {
bool g_radio = false;
uint8_t g_mac[6] = {0x24, 0x0A, 0xC4, 0x5A, 0x7E, 0x11};
}  // namespace

int radio(int op, const void* in, int in_len, void* out, int out_cap) {
    if (!has_api || !api.radio_request) return RW_RADIO_UNSUPPORTED;
    ApiScope scope;
    return api.radio_request(api.user, op, in, in_len, out, out_cap);
}

bool radio_available() { return g_radio; }

const uint8_t* radio_mac() { return g_mac; }

/* Called from hal_wifi_reset() at every chip reset (host thread, inside rw_init). */
void radio_reset() {
    static const uint8_t kDefault[6] = {0x24, 0x0A, 0xC4, 0x5A, 0x7E, 0x11};
    memcpy(g_mac, kDefault, 6);
    g_radio = radio(RW_RADIO_RESET, nullptr, 0, nullptr, 0) >= 0;
    if (g_radio) {
        uint8_t mac[6];
        if (radio(RW_RADIO_INFO, nullptr, 0, mac, 6) >= 6) memcpy(g_mac, mac, 6);
    }

    hal_ble_reset();
    hal_bp32_reset();
}

}  // namespace rw
