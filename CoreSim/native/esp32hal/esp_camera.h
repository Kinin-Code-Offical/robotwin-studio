/*
 * RoboTwin ESP32 HAL: esp32-camera driver subset ([sense]).
 * Frames come from a camera object in the simulated world (its own render of the world at
 * the configured frame size, with exposure, noise, motion blur and rolling shutter), through
 * the host's radio_request channel (ops 0x50-0x54, see CameraUnit.cs / Esp32CameraOp).
 * Supported: esp_camera_init/deinit, esp_camera_fb_get/fb_return, esp_camera_sensor_get with
 * set_framesize / set_quality / set_pixformat (other sensor controls are accepted and ignored).
 * Pixel formats: RGB565 (big-endian like the OV sensors), YUV422, GRAYSCALE, JPEG, RGB888.
 */
#pragma once
#include <stdint.h>
#include <stddef.h>
#include "esp_system.h"

#ifdef __cplusplus
extern "C" {
#endif

#define ESP_ERR_CAMERA_BASE 0x20000
#define ESP_ERR_CAMERA_NOT_DETECTED (ESP_ERR_CAMERA_BASE + 1)
#define ESP_ERR_CAMERA_FAILED_TO_SET_FRAME_SIZE (ESP_ERR_CAMERA_BASE + 2)
#define ESP_ERR_CAMERA_FAILED_TO_SET_OUT_FORMAT (ESP_ERR_CAMERA_BASE + 3)
#define ESP_ERR_CAMERA_NOT_SUPPORTED (ESP_ERR_CAMERA_BASE + 4)

typedef enum {
    PIXFORMAT_RGB565, PIXFORMAT_YUV422, PIXFORMAT_YUV420, PIXFORMAT_GRAYSCALE, PIXFORMAT_JPEG,
    PIXFORMAT_RGB888, PIXFORMAT_RAW, PIXFORMAT_RGB444, PIXFORMAT_RGB555
} pixformat_t;

typedef enum {
    FRAMESIZE_96X96, FRAMESIZE_QQVGA, FRAMESIZE_QCIF, FRAMESIZE_HQVGA, FRAMESIZE_240X240, FRAMESIZE_QVGA,
    FRAMESIZE_CIF, FRAMESIZE_HVGA, FRAMESIZE_VGA, FRAMESIZE_SVGA, FRAMESIZE_XGA, FRAMESIZE_HD, FRAMESIZE_SXGA,
    FRAMESIZE_UXGA, FRAMESIZE_FHD, FRAMESIZE_INVALID
} framesize_t;

typedef enum { CAMERA_GRAB_WHEN_EMPTY, CAMERA_GRAB_LATEST } camera_grab_mode_t;
typedef enum { CAMERA_FB_IN_PSRAM, CAMERA_FB_IN_DRAM } camera_fb_location_t;
typedef enum { LEDC_TIMER_0, LEDC_TIMER_1, LEDC_TIMER_2, LEDC_TIMER_3 } rw_ledc_timer_t;
typedef enum { LEDC_CHANNEL_0, LEDC_CHANNEL_1, LEDC_CHANNEL_2, LEDC_CHANNEL_3, LEDC_CHANNEL_4, LEDC_CHANNEL_5, LEDC_CHANNEL_6, LEDC_CHANNEL_7 } rw_ledc_channel_t;

typedef struct {
    int pin_pwdn;
    int pin_reset;
    int pin_xclk;
    union { int pin_sccb_sda; int pin_sscb_sda; };
    union { int pin_sccb_scl; int pin_sscb_scl; };
    int pin_d7, pin_d6, pin_d5, pin_d4, pin_d3, pin_d2, pin_d1, pin_d0;
    int pin_vsync;
    int pin_href;
    int pin_pclk;
    int xclk_freq_hz;
    int ledc_timer;
    int ledc_channel;
    pixformat_t pixel_format;
    framesize_t frame_size;
    int jpeg_quality;
    size_t fb_count;
    camera_fb_location_t fb_location;
    camera_grab_mode_t grab_mode;
    int sccb_i2c_port;
} camera_config_t;

typedef struct {
    long tv_sec;
    long tv_usec;
} rw_cam_timeval;

typedef struct {
    uint8_t* buf;
    size_t len;
    size_t width;
    size_t height;
    pixformat_t format;
    rw_cam_timeval timestamp;
} camera_fb_t;

#define OV7670_PID 0x76
#define OV2640_PID 0x26
#define OV3660_PID 0x3660
#define OV5640_PID 0x5640

typedef struct {
    uint8_t MIDH, MIDL;
    uint16_t PID;
    uint8_t VER;
} sensor_id_t;

typedef struct {
    framesize_t framesize;
    uint8_t quality;
    int8_t brightness, contrast, saturation, sharpness;
    uint8_t denoise, special_effect, wb_mode, awb, awb_gain, aec, aec2;
    int8_t ae_level;
    uint16_t aec_value;
    uint8_t agc;
    uint8_t agc_gain, gainceiling, bpc, wpc, raw_gma, lenc, hmirror, vflip, dcw, colorbar;
} camera_status_t;

typedef struct _sensor sensor_t;
struct _sensor {
    sensor_id_t id;
    uint8_t slv_addr;
    pixformat_t pixformat;
    camera_status_t status;
    int xclk_freq_hz;
    int (*init_status)(sensor_t* sensor);
    int (*reset)(sensor_t* sensor);
    int (*set_pixformat)(sensor_t* sensor, pixformat_t pixformat);
    int (*set_framesize)(sensor_t* sensor, framesize_t framesize);
    int (*set_contrast)(sensor_t* sensor, int level);
    int (*set_brightness)(sensor_t* sensor, int level);
    int (*set_saturation)(sensor_t* sensor, int level);
    int (*set_sharpness)(sensor_t* sensor, int level);
    int (*set_denoise)(sensor_t* sensor, int level);
    int (*set_gainceiling)(sensor_t* sensor, int gainceiling);
    int (*set_quality)(sensor_t* sensor, int quality);
    int (*set_colorbar)(sensor_t* sensor, int enable);
    int (*set_whitebal)(sensor_t* sensor, int enable);
    int (*set_gain_ctrl)(sensor_t* sensor, int enable);
    int (*set_exposure_ctrl)(sensor_t* sensor, int enable);
    int (*set_hmirror)(sensor_t* sensor, int enable);
    int (*set_vflip)(sensor_t* sensor, int enable);
    int (*set_aec2)(sensor_t* sensor, int enable);
    int (*set_awb_gain)(sensor_t* sensor, int enable);
    int (*set_agc_gain)(sensor_t* sensor, int gain);
    int (*set_aec_value)(sensor_t* sensor, int gain);
    int (*set_special_effect)(sensor_t* sensor, int effect);
    int (*set_wb_mode)(sensor_t* sensor, int mode);
    int (*set_ae_level)(sensor_t* sensor, int level);
    int (*set_dcw)(sensor_t* sensor, int enable);
    int (*set_bpc)(sensor_t* sensor, int enable);
    int (*set_wpc)(sensor_t* sensor, int enable);
    int (*set_raw_gma)(sensor_t* sensor, int enable);
    int (*set_lenc)(sensor_t* sensor, int enable);
};

esp_err_t esp_camera_init(const camera_config_t* config);
esp_err_t esp_camera_deinit(void);
camera_fb_t* esp_camera_fb_get(void);
void esp_camera_fb_return(camera_fb_t* fb);
sensor_t* esp_camera_sensor_get(void);

#ifdef __cplusplus
}
#endif
