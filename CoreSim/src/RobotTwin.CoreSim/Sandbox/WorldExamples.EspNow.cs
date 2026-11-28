using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// [espnow] Two ESP32s 40 cm apart talking ESP-NOW: the transmitter broadcasts a counter every 200 ms, the receiver
        /// prints what arrives with its signal and shows the counter's lowest bit on its blue LED (IO2). Frames cross the
        /// world's radio medium, in both ESP32 modes.
        /// </summary>
        public static WorldDocument EspNowPair(PartLibrary lib)
        {
            var w = new WorldDocument
            {
                Title = "ESP-NOW iki kart",
                Description = "Verici her 200 ms'de bir sayacı ESP-NOW yayınıyla gönderir, alıcı onu yazar ve LED'inde gösterir."
            };
            var table = w.AddBody("box", 0.6, 0.2, 0.018, new WorldPose { X = -0.3, Y = -0.1 }, "AHŞAP");
            table.Name = "Masa";
            table.IsStatic = true;
            const double deck = 0.018;
            var tx = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = -0.25, Y = -0.03, Z = OnPins(deck) });
            tx.Name = "Verici";
            tx.Firmware = new FirmwareSource { FileName = "verici.ino", Target = "esp32:esp32:esp32", Source = EspNowTransmitterSketch };
            var rx = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = 0.15, Y = -0.03, Z = OnPins(deck) });
            rx.Name = "Alıcı";
            rx.Firmware = new FirmwareSource { FileName = "alici.ino", Target = "esp32:esp32:esp32", Source = EspNowReceiverSketch };
            foreach (var o in new[] { tx, rx }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = table.Id });
            return w;
        }

        /// <summary>The transmitter of <see cref="EspNowPair"/>.</summary>
        public const string EspNowTransmitterSketch = @"// ESP-NOW verici: her 200 ms'de bir sayacı yayın adresine (FF:FF:FF:FF:FF:FF) gönderir.
// Yayının alındı onayı olmaz: gönderme geri çağrısı her zaman başarılı der (gerçek yongadaki gibi).
#include <WiFi.h>
#include <esp_now.h>

uint8_t yayin[6] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
uint32_t sayac = 0;

void gonderildi(const wifi_tx_info_t* bilgi, esp_now_send_status_t durum) {
  Serial.printf(""GONDERILDI %lu %s\n"", (unsigned long)sayac, durum == ESP_NOW_SEND_SUCCESS ? ""tamam"" : ""olmadi"");
}

void setup() {
  Serial.begin(115200);
  WiFi.mode(WIFI_STA);   // ESP-NOW Wi-Fi radyosunu kullanır
  if (esp_now_init() != ESP_OK) {
    Serial.println(""ESP-NOW baslamadi"");
    return;
  }
  esp_now_register_send_cb(gonderildi);
  esp_now_peer_info_t es = {};
  memcpy(es.peer_addr, yayin, 6);
  es.channel = 0;
  es.encrypt = false;
  esp_now_add_peer(&es);
  Serial.println(""verici hazir"");
}

void loop() {
  sayac++;
  esp_now_send(yayin, (uint8_t*)&sayac, sizeof sayac);
  delay(200);
}
";

        /// <summary>The receiver of <see cref="EspNowPair"/>.</summary>
        public const string EspNowReceiverSketch = @"// ESP-NOW alıcı: gelen sayacı sinyal gücüyle yazar, en düşük bitini IO2'deki mavi LED'de gösterir.
#include <WiFi.h>
#include <esp_now.h>

const int LED = 2;

void alindi(const esp_now_recv_info_t* bilgi, const uint8_t* veri, int uzunluk) {
  uint32_t n = 0;
  if (uzunluk == sizeof n) memcpy(&n, veri, sizeof n);
  digitalWrite(LED, n & 1);
  Serial.printf(""ALINDI %lu %d dBm\n"", (unsigned long)n, (int)bilgi->rx_ctrl->rssi);
}

void setup() {
  Serial.begin(115200);
  pinMode(LED, OUTPUT);
  WiFi.mode(WIFI_STA);
  if (esp_now_init() != ESP_OK) {
    Serial.println(""ESP-NOW baslamadi"");
    return;
  }
  esp_now_register_recv_cb(alindi);
  Serial.println(""alici hazir"");
}

void loop() {
  delay(100);
}
";
    }
}
