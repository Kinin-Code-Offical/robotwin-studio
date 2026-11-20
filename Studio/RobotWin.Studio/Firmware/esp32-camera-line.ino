// RobotWin Studio örneği: ESP32 + OV2640 kamera ile banttaki çizgiyi bulma.
// Kamera dünyayı kendi konumundan 160x120 gri tonlu (QQVGA) görür; alt 30 satırın her
// sütununun ortalamasını alır, en koyu sütunu bant olarak kabul eder.
// Işığı Ortam sekmesinden azaltın: pozlama uzar, kazanç ve gürültü artar, kontrast düşer.
#include "esp_camera.h"

void setup() {
  Serial.begin(115200);
  camera_config_t c = {};
  c.pin_pwdn = 32; c.pin_reset = -1; c.pin_xclk = 0;
  c.pin_sccb_sda = 26; c.pin_sccb_scl = 27;
  c.pin_d7 = 35; c.pin_d6 = 34; c.pin_d5 = 39; c.pin_d4 = 36;
  c.pin_d3 = 21; c.pin_d2 = 19; c.pin_d1 = 18; c.pin_d0 = 5;
  c.pin_vsync = 25; c.pin_href = 23; c.pin_pclk = 22;
  c.xclk_freq_hz = 20000000;
  c.pixel_format = PIXFORMAT_GRAYSCALE;
  c.frame_size = FRAMESIZE_QQVGA;
  c.jpeg_quality = 12;
  c.fb_count = 1;
  esp_err_t err = esp_camera_init(&c);
  if (err != ESP_OK) {
    Serial.printf("Kamera baslatilamadi: 0x%x (besleme ve kabloyu kontrol edin)\n", err);
    return;
  }
  Serial.println("Kamera hazir: 160x120 gri");
}

uint32_t frames = 0;

void loop() {
  camera_fb_t* fb = esp_camera_fb_get();
  if (!fb) {
    Serial.println("Kare alinamadi");
    delay(500);
    return;
  }
  frames++;
  int w = fb->width, h = fb->height;
  static uint32_t col[160];
  for (int x = 0; x < w; x++) col[x] = 0;
  uint32_t total = 0;
  for (int y = h - 30; y < h; y++) {
    for (int x = 0; x < w; x++) {
      uint8_t v = fb->buf[y * w + x];
      col[x] += v;
      total += v;
    }
  }
  int darkest = 0;
  for (int x = 1; x < w; x++) if (col[x] < col[darkest]) darkest = x;
  uint32_t mean = total / (30 * w);
  uint32_t dark = col[darkest] / 30;
  int contrast = (int)mean - (int)dark;
  Serial.printf("kare %u  t=%ld.%03ld s  ortalama %u  bant x=%d  kontrast %d  %s\n", frames,
                fb->timestamp.tv_sec, fb->timestamp.tv_usec / 1000, mean, darkest, contrast,
                contrast > 25 ? "BANT VAR" : "bant yok");
  esp_camera_fb_return(fb);
  delay(200);
}
