# ESP32'nin iki çalışma modu

Aynı ESP32 çizimi iki yolla çalışır: bugünkü **hızlı simülasyon** ve gerçek karta yüklenebilen **donanım uyumlu derleme**. İkincisinin simülasyonda da çalışması için gerçek Xtensa komutlarını yürüten kendi emülatörümüz gerekir; araç zincirini değiştirmek tek başına donanım emülasyonu getirmez. Bu sayfa planı, bugünkü altyapıyla bağlantısını ve aşamaları anlatır. İşin takibi [yol haritasında](YOL_HARITASI.md).

## Bugün nasıl çalışıyor

| Konu | Bugünkü durum |
| --- | --- |
| Derleme | `.ino`, `ArduinoSketchPreprocessor` ile C++'a çevrilir; MSVC (`cl.exe /LD`) onu `rw_esp32hal.lib`'e karşı bir Windows x64 DLL'i yapar (`CoreSim/.../Firmware/Esp32/Esp32SketchCompiler.cs`). HAL kaynakları `CoreSim/native/esp32hal/` içindedir, kütüphane karma ile önbelleğe alınır. |
| Yükleme | DLL aynı süreçte `LoadLibraryW` ile açılır (`Esp32Native.cs`). Sözleşme: `rw_init`, `rw_run_until`, `rw_pin_changed`, `rw_serial_rx` …, ABI sürüm 2 (`rw_host_api.h`). |
| Kapsam | GPIO, kesmeler, ADC/DAC/dokunma, LEDC/analogWrite/tone, zamanlayıcılar, Serial 0–2, Wire 0–1, SPI, Wi-Fi (TCP/UDP/sunucu), BLE, Bluepad32, FreeRTOS (görev, kuyruk, semafor, bildirim), görev bekçisi, kamera, EEPROM, derin uyku. ESP-NOW yok. |
| Bağlantı | Her GPIO kartın `DigitalPin`'ine bağlı (`Esp32CoSimulation.cs`); I2C `I2cBus`, SPI `SpiBus`, radyo `Esp32RadioBridge` üzerinden. |
| Zaman | Simülasyon zamanıyla kilitli adım: dünya 100 µs'lik dilimlerle `rw_run_until` çağırır. Komut sayımı yok; her HAL çağrısı sabit bir süre "harcar". `getCycleCount` = çalışma süresi × MHz. |
| Sınırlar | Komut düzeyinde zamanlama yok; 64 bit Windows bellek modeli (işaretçi 8 bayt, 520 KB SRAM sınırı yok); ESP-IDF'in kendi davranışı (önbellek, flaş, bölümler, kesme önceliği) yok; Linux'ta başsız koşuda kart yazılımsız çalışır. |

Ayrıca örnek alınacak bir emülatörümüz var: döngü sayımlı ATmega328P (`Firmware/Atmega328P*.cs`: çözücü, zamanlayıcılar, PWM, UART, ADC, TWI, kesmeler; `FirmwareCoSimulation` dilimi döngüye çevirir; `Debug/` ELF/DWARF okur ve hata ayıklar).

## Mod 1: Hızlı simülasyon (bugünkü, kalıyor)

- MSVC + RobotWin Arduino HAL, süreç içi DLL. Varsayılan mod.
- Güçlü yanı: çok sayıda ESP32'li robotik sahne (dron, araç filosu, radyo ağı), saniyeler içinde derleme, Wi-Fi/BLE/kumanda dahil tam API.
- Eklenecekler: ESP-NOW; işaretçi boyutu ve bellek bütçesi uyarıları (gerçek kartta taşacak dizileri yakalamak için çizimin statik bellek kullanımı Mod 2'nin derleyicisiyle ölçülüp gösterilir).

## Mod 2: Donanım uyumlu derleme

- Çizim, Espressif'in Arduino-ESP32 çekirdeğiyle (sabit bir sürüm; altında ESP-IDF 5.x) gerçek `xtensa-esp32-elf-gcc` ile derlenir. Çıktı: `bootloader.bin`, `partitions.bin`, uygulama `.bin`, birleşik `.bin` ve simgeli `.elf`.
- Araç zinciri, RobotWin ile gelen `arduino-cli` ile **RobotWin'in kendi veri klasörüne** kurulur (`tools/arduino/data` yanında, kullanıcının Arduino IDE'sine dokunmadan). İlk kullanımda indirilir (onaylı indirme), SHA-256 ile doğrulanır, sürüm projeyle saklanır (aynı proje yıllar sonra aynı derleyiciyle derlenir).
- **Karta yükle:** kartın COM bağlantı noktası seçilir; çekirdekle gelen `esptool` yazar. Web yükleyicileri için birleşik `.bin` dışa aktarılır.
- Derleme hataları Mod 1'deki gibi `.ino` satırlarına eşlenir (önişlemci ortak).
- Mod 2 seçili bir kart, derleyici kurulu değilse ya da emülatörün henüz desteklemediği bir şey varsa açıkça söyler; sessizce Mod 1'e düşmez.

## Mod 2 simülasyonda: RobotWin Xtensa emülatörü

Gerçek firmware'i simülasyonda çalıştırmak için ESP32'nin komutlarını yürüten kendi emülatörümüz yazılır (QEMU değil: motorlar bizim, dış araçlar yalnız derleyici ve yükleyici olarak kullanılır). AVR emülatörünün düzeni izlenir.

| Katman | İçerik |
| --- | --- |
| Çekirdek | Xtensa LX6, ESP32'nin seçenekleriyle: 64 kayıtlık pencereli kayıt dosyası (CALL4/8/12, ENTRY, RETW, pencere taşma/boşalma istisnaları), yoğun (16 bit) komutlar, MUL16/MUL32/MULH, DIV32, tek duyarlıklı FPU, boolean kayıtlar, sıfır ek yüklü döngüler (LOOP), NSA, MIN/MAX/CLAMPS/SEXT, S32C1I (FreeRTOS'un atomik işlemleri), 1–5. düzey kesmeler + NMI, CCOUNT/CCOMPARE. Komut başına döngü sayımı. |
| Yonga (SoC) | İki çekirdek (PRO_CPU, APP_CPU) ortak bellekle; IRAM/DRAM (520 KB), flaş önbellek eşlemesi (IROM/DROM, MMU), RTC belleği, kesme matrisi, eFuse (MAC). Açılış: uygulama `.elf`'inin bölümleri doğrudan yüklenir, `call_start_cpu0` çalışır; maske ROM'un kullanılan işlevleri araç zinciriyle gelen ROM ELF'inden ya da yüksek düzeyde (HLE) karşılanır. |
| Çevre birimleri | Yazmaç düzeyinde: GPIO matrisi ve IO_MUX, UART0–2, LEDC, MCPWM, zamanlayıcı grupları ve bekçiler, I2C0–1, SPI2–3 (flaş SPI'ı yedek depo olarak), SAR ADC1–2, DAC, RMT, PCNT, dokunma. Her biri bugünkü CoreSim aygıtlarına bağlanır (`DigitalPin`, `I2cBus`/`II2cDevice`, `SpiBus`/`ISpiDevice`): devre ve dünya tarafında değişiklik gerekmez. |
| Radyo | Wi-Fi/BLE'nin PHY/MAC kapalı kodu anlamlı biçimde öykünülemez. `esp_wifi_*`, `esp_netif`/lwIP soket ve `esp_bt_*` giriş noktaları ELF simge tablosundan bulunup yakalanır ve Mod 1'in kullandığı `Esp32RadioBridge`'e bağlanır (yazmaç düzeyinde olmayan tek parça; belgelerde böyle yazılır). |
| Zaman | Mod 1 gibi 100 µs dilimlerle kilitli; dilim × 240 MHz döngü, çekirdek başına. FreeRTOS kendi zamanlayıcısını gerçek tick kesmesiyle sürer. `WAITI` (boşta bekleme) bir sonraki kesmeye atlar. |
| Hata ayıklama | Mevcut ELF/DWARF okuyucusu ve hata ayıklayıcı: kesme noktası, satır adımı, yazmaç ve bellek görünümü, iki çekirdek ayrı. |
| Hız | C# yorumlayıcısı + çözülmüş komut önbelleği. Yoğun döngülü yazılımda gerçek zamanın altında kalabilir; bu yüzden Mod 2 doğrulama ve tek kartın derinlemesine testi içindir, çok kartlı sahne Mod 1'de kalır. Bir dünyada kartlar ayrı ayrı mod seçebilir (on kart hızlı, test edilen bir kart emülasyonda). |

### Ölçüm: gerçek bir çizim ROM'dan ne kullanıyor

Arduino-ESP32 3.3.13 ile derlenen `esp32-dimmer` çiziminin ELF'i: 7 yüklenecek bölüm (DROM `0x3F400020`, RTC hızlı bellek, DRAM `0x3FFBDB60`, IRAM `0x40080000`, IROM `0x400D0020`, RTC yavaş bellek), giriş `0x40081B08` (`call_start_cpu0`). Kod, maske ROM'daki 347 simgeden yalnız 12'sine başvuruyor: libgcc'nin çift duyarlıklı yardımcıları (`__adddf3`, `__subdf3`, `__divdf3`, `__extendsfdf2`, `__fixdfsi`), RF kalibrasyonunun `esp_rom_regi2c_read_mask`/`write`'ı ve newlib'in `open/read/write/close` saplamaları. FPU'nun bölme yardımcı komutlarını (DIV0.S …) hiç kullanmıyor. Sonuç: ROM görüntüsü gerekmez; bu işlevler adresleri araç zincirinin `esp32.rom*.ld` dosyalarından okunup yakalanır ve C#'ta karşılanır.

## Doğrulama

- **Komut testleri:** gerçek derleyiciyle derlenen, kendi sonucunu UART'a "PASS/FAIL" diye yazan test yazılımları; her komut grubu için.
- **Mod farkı raporu:** her ESP32 şablonu iki modda aynı süre çalıştırılır; seri çıktı ve pin izleri (`GoldenTraceHarness`) karşılaştırılır, farklar ve nedenleri raporlanır.
- **Gerçek kartla karşılaştırma (sonra):** aynı yazılım gerçek kartta çalışırken seri çıktısı ve seçili pinleri USB üzerinden kaydedilir, emülatörün iziyle karşılaştırılır.

## Donanım içeren test (HIL, P2)

Gerçek bir ESP32 simülasyonun parçası olur: RobotWin onun UART'ını ve seçili pinlerini (ikinci bir "RobotWin prob" kartı ya da bir G/Ç genişletici üzerinden) dünyaya bağlar; sensör değerleri simülasyondan karta, çıkışlar karttan simülasyona akar. Donanım gerektirdiği için emülatörden sonra gelir.

## Arayüz

- Kart denetçisinde **Çalışma modu:** *Hızlı simülasyon* / *Donanım uyumlu (ESP32 emülatörü)*. Projede ve dünya dosyasında kart başına saklanır.
- Kod sayfasında her iki modda **Kart için derle** (`.bin`) ve **Karta yükle** (bağlantı noktası seçimi).
- Durum çubuğu ve olay günlüğü hangi motorun çalıştığını söyler; Mod 2'de bellek kullanımı (IRAM/DRAM/flaş) gösterilir.

## Durum (3. aşama bitti)

Arduino-ESP32 3.3.13 ile gerçek yonga için derlenen `esp32-dimmer` firmware'i (değiştirilmeden, `.elf` ve birleşik `.bin`) kendi emülatörümüzde açılıyor: iki çekirdekte FreeRTOS, IPC görevleri, `setup()` Serial'e "ESP32 dimmer hazir" yazıyor, `loop()` potansiyometreyi ADC'den okuyup LEDC'yi 5 kHz'de sürüyor ve 200 ms'de bir `pot=1058  1000 mV  parlaklik= 25%` basıyor (IO34'te 1,0 V). Bu noktaya gelirken bulunanlar: FreeRTOS'un iki çekirdekli `yield`'i çekirdekler arası kesmeye (DPORT `FROM_CPU_INTR`) dayanıyor; UART FIFO'su AHB eşinden (`0x60000000` = `0x3FF40000`) yazılıyor; esp_timer zamanlayıcı grubunun LAC sayacını sayıyor ve bölücüsünü açılıştaki APB saatinden kuruyor (önyükleyicinin bıraktığı 80 MHz bu yüzden önemli); LEDC sürücüsü `DUTY_START`'ın donanımca silinmesini bekliyor; `analogReadMilliVolts` eFuse'taki fabrika Vref'ine dayanıyor.

ADC'nin dönüşümü Espressif'in karakterizasyonunu (IDF'nin kalibrasyonda kullandığı doğru ve 11 dB eğrisi) tersine çevirir: gerçek yonganın verdiği kodlar çıkar ve kalibre edilen gerilim pad gerilimine eşit olur. Hızlı modun HAL'i ideal doğruyu kullanır (1,0 V → 1241); iki mod arasındaki bu fark 6. aşamanın raporunda yer alacak.

## Aşamalar

1. Çalışma modu ayarı (veri modeli, dosya biçimi, denetçi) + araç zinciri yöneticisi + donanım derlemesi + karta yükleme.
2. Xtensa LX6 çekirdeği ve komut testleri.
3. Yonga: bellek haritası, ELF yükleme, ROM, iki çekirdek, kesme matrisi, FreeRTOS tick → Blink ve Serial.
4. Çevre birimleri → radyosuz bütün şablonlar.
5. Wi-Fi/BLE yakalama → radyolu şablonlar.
6. Mod farkı raporu, performans (boşta atlama, çözülmüş blok önbelleği), belgeler.
7. (P2) Donanım içeren test.
