/* esp32-camera subset ([sense]): frames from the world's camera through radio_request ops 0x50-0x54. */
#include "rw_internal.h"
#include "esp_camera.h"
#include <string.h>

namespace {

const int kOpInit = 0x50, kOpGrab = 0x51, kOpRead = 0x52, kOpSet = 0x53, kOpDeinit = 0x54;

bool g_init = false;
pixformat_t g_format = PIXFORMAT_RGB565;
sensor_t g_sensor;

/* esp32-camera pixformat_t -> host CameraPixelFormat (Rgb565 0, Yuv422 1, Grayscale 2, Jpeg 3, Rgb888 4). */
int host_format(pixformat_t f) {
    switch (f) {
        case PIXFORMAT_YUV422: return 1;
        case PIXFORMAT_GRAYSCALE: return 2;
        case PIXFORMAT_JPEG: return 3;
        case PIXFORMAT_RGB888: return 4;
        default: return 0;
    }
}

void put32(uint8_t* b, int v) {
    b[0] = (uint8_t)v;
    b[1] = (uint8_t)(v >> 8);
    b[2] = (uint8_t)(v >> 16);
    b[3] = (uint8_t)(v >> 24);
}

int get32(const uint8_t* b) { return b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24); }

int set(int what, int value) {
    uint8_t in[8];
    put32(in, what);
    put32(in + 4, value);
    return rw::radio(kOpSet, in, 8, nullptr, 0) >= 0 ? 0 : -1;
}

int s_framesize(sensor_t* s, framesize_t f) {
    if ((int)f < 0 || f >= FRAMESIZE_INVALID) return -1;
    s->status.framesize = f;
    return set(1, (int)f);
}

int s_quality(sensor_t* s, int q) {
    s->status.quality = (uint8_t)q;
    return set(2, q);
}

int s_pixformat(sensor_t* s, pixformat_t p) {
    s->pixformat = p;
    g_format = p;
    return set(3, host_format(p));
}

int s_ignore(sensor_t*, int) { return 0; }
int s_none(sensor_t*) { return 0; }

void setup_sensor(const camera_config_t* c) {
    memset(&g_sensor, 0, sizeof(g_sensor));
    g_sensor.id.PID = OV2640_PID;
    g_sensor.id.MIDH = 0x7F;
    g_sensor.id.MIDL = 0xA2;
    g_sensor.slv_addr = 0x30;
    g_sensor.pixformat = c->pixel_format;
    g_sensor.xclk_freq_hz = c->xclk_freq_hz;
    g_sensor.status.framesize = c->frame_size;
    g_sensor.status.quality = (uint8_t)c->jpeg_quality;
    g_sensor.status.aec = 1;
    g_sensor.status.agc = 1;
    g_sensor.status.awb = 1;
    g_sensor.init_status = s_none;
    g_sensor.reset = s_none;
    g_sensor.set_pixformat = s_pixformat;
    g_sensor.set_framesize = s_framesize;
    g_sensor.set_quality = s_quality;
    int (**ignore[])(sensor_t*, int) = {
        &g_sensor.set_contrast, &g_sensor.set_brightness, &g_sensor.set_saturation, &g_sensor.set_sharpness, &g_sensor.set_denoise,
        &g_sensor.set_colorbar, &g_sensor.set_whitebal, &g_sensor.set_gain_ctrl, &g_sensor.set_exposure_ctrl,
        &g_sensor.set_hmirror, &g_sensor.set_vflip, &g_sensor.set_aec2, &g_sensor.set_awb_gain, &g_sensor.set_agc_gain, &g_sensor.set_aec_value,
        &g_sensor.set_special_effect, &g_sensor.set_wb_mode, &g_sensor.set_ae_level, &g_sensor.set_dcw, &g_sensor.set_bpc, &g_sensor.set_wpc,
        &g_sensor.set_raw_gma, &g_sensor.set_lenc};
    for (auto p : ignore) *p = s_ignore;
    g_sensor.set_gainceiling = [](sensor_t*, int) { return 0; };
}

}  // namespace

extern "C" esp_err_t esp_camera_init(const camera_config_t* config) {
    if (config == nullptr) return ESP_FAIL;
    uint8_t in[20];
    put32(in, (int)config->frame_size);
    put32(in + 4, host_format(config->pixel_format));
    put32(in + 8, config->jpeg_quality);
    put32(in + 12, (int)config->fb_count);
    put32(in + 16, config->xclk_freq_hz);
    rw::charge(rw::cost(150e6));   // SCCB probe + register setup takes ~150 ms on a real board
    int rc = rw::radio(kOpInit, in, 20, nullptr, 0);
    if (rc < 0) {
        rw::log(RW_LOG_ERROR, "E (%u) camera: Camera probe failed with error 0x105(ESP_ERR_NOT_FOUND)", 0u);
        return ESP_ERR_CAMERA_NOT_DETECTED;
    }

    g_format = config->pixel_format;
    setup_sensor(config);
    g_init = true;
    return ESP_OK;
}

extern "C" esp_err_t esp_camera_deinit(void) {
    if (g_init) rw::radio(kOpDeinit, nullptr, 0, nullptr, 0);
    g_init = false;
    return ESP_OK;
}

extern "C" camera_fb_t* esp_camera_fb_get(void) {
    if (!g_init) return nullptr;
    uint8_t hdr[24];
    // The driver waits up to 4 s for a frame (FB_GET_TIMEOUT).
    uint64_t deadline = rw::now_ns() + 4000000000ull;
    int len = 0;
    for (;;) {
        len = rw::radio(kOpGrab, nullptr, 0, hdr, 24);
        if (len > 0) break;
        if (len < 0 || rw::now_ns() >= deadline) {
            rw::log(RW_LOG_ERROR, "E camera: Failed to get the frame on time!");
            return nullptr;
        }
        rw::sleep_until(rw::now_ns() + 1000000ull);   // next frame (VSYNC)
    }

    auto* fb = (camera_fb_t*)rw::heap_alloc(sizeof(camera_fb_t));
    if (fb == nullptr) return nullptr;
    fb->width = (size_t)get32(hdr);
    fb->height = (size_t)get32(hdr + 4);
    fb->len = (size_t)len;
    fb->format = g_format;
    uint64_t us = (uint64_t)(uint32_t)get32(hdr + 16) | ((uint64_t)(uint32_t)get32(hdr + 20) << 32);
    fb->timestamp.tv_sec = (long)(us / 1000000ull);
    fb->timestamp.tv_usec = (long)(us % 1000000ull);
    fb->buf = (uint8_t*)rw::heap_alloc((size_t)len);
    if (fb->buf == nullptr) {
        rw::heap_free(fb);
        rw::log(RW_LOG_ERROR, "E camera: frame buffer malloc failed (%d bytes)", len);
        return nullptr;
    }

    int off = 0;
    while (off < len) {
        uint8_t in[4];
        put32(in, off);
        int n = rw::radio(kOpRead, in, 4, fb->buf + off, len - off < 8192 ? len - off : 8192);
        if (n <= 0) break;
        off += n;
    }

    return fb;
}

extern "C" void esp_camera_fb_return(camera_fb_t* fb) {
    if (fb == nullptr) return;
    rw::heap_free(fb->buf);
    rw::heap_free(fb);
}

extern "C" sensor_t* esp_camera_sensor_get(void) { return g_init ? &g_sensor : nullptr; }
