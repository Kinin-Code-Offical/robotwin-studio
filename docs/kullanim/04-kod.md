# 4. Kod

**Kod** sayfası (Ctrl+3) devredeki programlanabilir kartların (Arduino Uno, Arduino Nano, ESP32) yazılımını yazdığınız, derlediğiniz ve emüle karta yükleyip çalıştırdığınız yerdir. Her kartın kodu projenin içinde saklanır.

## Başlamadan önce

- Devrede programlanabilir bir kart olmalıdır. Yoksa sayfa **Programlanabilir kart yok** der; **Arduino Uno ekle** düğmesiyle bir Uno ekleyebilirsiniz.
- Arduino kodu derlemek için `arduino-cli` kurulu olmalıdır:

  ```text
  winget install ArduinoSA.CLI
  arduino-cli core install arduino:avr
  ```

- ESP32 kodu derlemek için Visual Studio C++ araçları (MSVC) kurulu olmalıdır.

Ayrıntılar: [1. Başlarken](01-baslarken.md#kendi-kodunuzu-derlemek-için-isteğe-bağlı).

## Adım adım: ilk kodunuz

1. **Kart** listesinden kodunu yazacağınız kartı seçin.
2. **Örnek** listesinden bir şablon seçin, örneğin **Blink (D13 LED)**.
3. **Derle** düğmesine veya **F7**'ye basın. Alttaki **Derleyici** sekmesinde derleme çıktısı görünür. Hatalar Türkçe açıklanır ve ilgili satır işaretlenir.
4. **Yükle ve çalıştır** düğmesine veya **F5**'e basın. Kod derlenir, emüle karta yüklenir ve simülasyon başlar.
5. **Seri monitör** sekmesinde `Serial.print` çıktısını görün. Alttaki kutuya yazıp **Gönder** ile karta veri yollayın.

Araç çubuğundaki diğer düğmeler simülasyonu durdurur ve kartı resetler.

## Arduino şablonları

| Şablon | Ne yapar |
| --- | --- |
| Blink (D13 LED) | D13'teki LED'i saniyede bir yakıp söndürür |
| PWM ile parlaklık (D9) | D9'daki LED veya motorun gücünü yavaşça artırıp azaltır |
| Buton ile LED | D2'deki buton basılıyken D13'teki LED yanar |
| Analog okuma + seri | A0'daki gerilimi okuyup seri monitöre yazar |
| HC-SR04 mesafe | Ultrasonik sensörle mesafeyi cm olarak yazar |
| Engelden kaçan robot | HC-SR04 ve iki motorla engelden kaçar |
| Çizgi izleyen (2 sensör) | İki TCRT5000 ile çizgi izler |
| I²C tarayıcı | I²C veri yolundaki cihazların adreslerini bulur |
| MPU-6050 ivme | İvme ve jiroskop değerlerini okur |

ESP32 kartları için yeni kod, GPIO2'deki LED'i yakıp söndüren bir ESP32 Blink ile başlar.

## Arduino nasıl çalışır?

Kodunuz gerçek `avr-gcc` ile (`arduino-cli` üzerinden) derlenir. Ortaya çıkan HEX dosyası, çevrim doğruluğunda çalışan bir **ATmega328P emülatöründe** devreyle birlikte adım adım çalışır. Desteklenenler:

- Tüm komut seti ve kesmeler
- Dijital pinler, harici kesmeler ve pin değişim kesmeleri
- Timer0/1/2 ve PWM (`analogWrite`)
- Seri port (USART), ADC (`analogRead` devredeki gerçek gerilimi okur), EEPROM, watchdog
- I²C (TWI); SSD1306 OLED, PCF8574 + HD44780 LCD ve MPU-6050 modülleri gerçekten cevap verir

> İpucu: Arduino IDE'de derlediğiniz bir `.hex` dosyanız varsa, **Devre** sayfasındaki **Kart pinleri** sekmesinde **HEX yükle…** ile doğrudan yükleyebilirsiniz.

## ESP32 nasıl çalışır?

ESP32 kodu (çizim) bilgisayarınızda MSVC ile yerel olarak derlenir ve devreyle eş zamanlı çalışır. İlk derleme biraz uzun sürer, çünkü ESP32 donanım katmanı da kurulur; sonraki derlemeler önbellekten hızlı gelir.

Desteklenenler:

- `pinMode`, `digitalRead/Write`, `attachInterrupt`
- `analogRead` (zayıflatma ve çözünürlük ayarlarıyla), `analogWrite`, `ledc*` PWM, `dacWrite`, `touchRead`, `tone`, `pulseIn`
- Donanım zamanlayıcıları, `millis`, `micros`, gecikmeler
- FreeRTOS görevleri, kuyruklar, semaforlar
- `Serial`, `Wire` (I²C), `EEPROM`, `Servo` / `ESP32Servo`
- `WiFi` (dünya içinde sanal ağ; İnternet yok), BLE (temel özellikler), **Bluepad32** (PS5, PS4, Xbox kumandaları)

`SPI` yalnızca boş bir taklittir; SPI cihazları çalışmaz.

## Dünyadaki kartların kodu

Dünyaya koyduğunuz bir kartın kodunu yazmak için, dünyada kartı seçin ve denetçideki **Kartın kodunu yaz** düğmesine basın. Kart, Kod sayfasında kendi sekmesinde açılır. Ayrıntılar: [7. Dünya](07-dunya.md).

---

[← Önceki: 3. PCB](03-pcb.md) · [İçindekiler](README.md) · [Sonraki: 5. Modelle →](05-modelle.md)
