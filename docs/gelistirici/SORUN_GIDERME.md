# Sorun giderme

Önce kurulu uygulamanın sorunları, sonra kaynaktan derlerken çıkan sorunlar. Kullanıcıya yönelik sık sorulan sorular kılavuzdadır: [10. Sık sorulan sorular](../kullanim/10-sss.md).

İlk bakılacak yerler: pencerenin altındaki durum çubuğu, **Olaylar** sekmesi ve çökme günlüğü `%APPDATA%\RobotWinStudio\crash.log` (Ayarlar → *Hakkında* → *Ayar ve günlük klasörünü aç*).

## Kurulum ve başlatma

| Belirti | Çözüm |
| --- | --- |
| Uygulama açılmıyor ya da .NET istiyor | Microsoft'tan **.NET 9 Masaüstü Çalışma Zamanı**'nı (x64) kurun, ya da kurulumu yeniden çalıştırıp önerisini kabul edin, ya da .NET'i içinde taşıyan tam paketi `RobotWinStudio-Setup-<sürüm>.exe` kullanın (`-lite` taşımaz). |
| Windows SmartScreen kurulum için uyarıyor | Dosyanın projenin GitHub Sürümler sayfasından geldiğini doğrulayın; sonra *Ek bilgi → Yine de çalıştır*. |
| Bütün kullanıcılara kurulum başarısız | `/ALLUSERS`, `Program Files`'a yazar ve yönetici yetkisi ister; yoksa varsayılan kullanıcıya kurulumu kullanın. |
| `.rwproj`/`.rwworld` dosyasına çift tıklayınca uygulama açılmıyor | Ayarlar → *Dosya türleri* → *Windows'ta bu uygulamayla aç* (ya da Dosya → *Dosya türlerini bu uygulamayla ilişkilendir*). |
| Yazılar ya da denetimler çok küçük / büyük | Görünüm → *Arayüz ölçeği* (%75–150), ya da Ayarlar → *Görünüm*. |

## Yazılım derleme

| Belirti | Çözüm |
| --- | --- |
| **Derle**'ye (F7) basınca "arduino-cli bulunamadı" | `winget install ArduinoSA.CLI`, yeni bir terminal açın, `arduino-cli core install arduino:avr`. Alışılmadık bir yere kuruluysa Ayarlar → *Derleyiciler* → *arduino-cli yolu*'nu ayarlayın. Kurduktan sonra uygulamayı yeniden başlatın, yeni `PATH`'i görsün. |
| Derleme "platform not installed" / eksik `arduino:avr` ile başarısız | `arduino-cli core install arduino:avr` çalıştırın. |
| Bir kütüphane (`#include <...>`) bulunamıyor | arduino-cli ile kurun, ör. `arduino-cli lib install "Adafruit SSD1306"`. |
| ESP32: "MSVC (vcvars64.bat) bulunamadı" | **C++ ile masaüstü geliştirme** iş yüklü Visual Studio 2022 ya da Build Tools kurun, ya da Ayarlar → *vcvars64.bat yolu*'nu ayarlayın. |
| ESP32: ilk çalıştırma uzun sürüyor | HAL kütüphanesi ve çizim ilk kullanımda MSVC ile derlenir, sonra `%LOCALAPPDATA%\RobotWin\esp32hal-cache` içinde saklanır. Sonraki çalıştırmalar hızlı başlar. O klasörü silmek yeniden derlemeyi zorlar. |
| ESP32: SPI cihaz yanıt vermiyor | ESP32 HAL'inde SPI yalnız bir koçandır (aktarımlar `0xFF` döner). I²C cihazlar kullanın. |
| ESP32: `WiFi` İnternet'e ulaşamıyor | Wi-Fi yalnız dünyanın içinde simüle edilir (kartlar arası UDP ve akış soketleri); İnternet ya da DNS yoktur. |

## Simülasyon

| Belirti | Çözüm |
| --- | --- |
| Durum "gerçek zamanın %…'i" gösteriyor | Bilgisayar gerçek zamana yetişemiyor. Daha yavaş bir hız seçin, ağır sayfaları kapatın, Ayarlar → *3D kalitesi*'ni düşürün ya da *En büyük zaman adımı*'nı artırın. |
| Dünyanın 3B görünümü siyah, titrek ya da bozuk | Kendi GPU çizicimiz ekran kartının sürücüsüyle anlaşamıyor olabilir. Ayarlar → *3B çizici* → *WPF* ile eski çizime dönün; `--render-test dosya` çizicinin bu makinede doğru çizip çizmediğini yazar. Direct3D 11 hiç açılamazsa uygulama kendiliğinden WPF'ye döner. |
| Bir parça yandı / sigorta attı ve hiçbir şey çalışmıyor | Bu simüle edilmiş hasardır. Nedenini düzeltin (bkz. **Arıza tespiti** → *Neden?*), sonra *Hasarlı parçaları onar* (devre) ya da *Tümünü onar* (Montaj). Hasarı yok saymak için Simülasyon → *Parça hasarı*'nı kapatın. |
| Dünyadaki bir kart hiçbir şey yapmıyor; günlükte "Yüklü yazılım yok (kart boşta)" | Kartın programı yok. *Kartın kodunu yaz* ile açın, derleyin ve çalıştırın. Beslendiğini de denetleyin (pil kablosu ya da takılı bir PC + USB kablosu). |
| İki kart birbirinin sinyalini görmüyor | Kartlar bir kablo bağlayana kadar elektriksel olarak ayrıdır; aralarına GND'yi de bağlayın. |
| Bir kablo **YANDI** gösteriyor | Akım sınırını aştı. Daha kalın kesit (daha küçük AWG) kullanın ya da akımı sınırlayın. |
| Nesneler sürüklenemiyor | Nesneler yalnız dünya simülasyonu dururken taşınabilir (Shift+F5). |
| Kumanda bulunamıyor | Kumandayı seçin → *Kumandaları yeniden tara*; ya da *Sanal kumandayı aç (fare / klavye)*. Önce *Eşleştirme modu* ile eşleştirin. |
| Bir parça beklenmedik şekilde çarpışıyor ya da yuvarlanmıyor | Görünüm → *Çarpışma şekilleri (dünya)* ile fiziğin gerçekten kullandığı gövdeye bakın. |
| Ses yok | Simülasyon → *Simülasyon sesleri* ve Ayarlar → *Ses düzeyi*. |

## Kaynaktan derleme

| Belirti | Çözüm |
| --- | --- |
| `A compatible .NET SDK was not found` | `global.json`'da sabitlendiği gibi .NET 9 SDK'yı (9.0.112 ya da daha yeni bir 9.0 özellik bandı) kurun. |
| `RobotWinStudio.exe` kullanımda olduğu için derleme başarısız | Yeniden derlemeden önce çalışan uygulamayı (ve `--snapshot`/`--selftest` süreçlerini) kapatın. |
| `builds\` içinde çok sayıda `RobotWin.Studio_*_wpftmp` klasörü | WPF geçici projeleri; `builds\` klasörüyle birlikte güvenle silinebilir. |
| `build-installer.ps1`: "Visual Studio C++ araçları bulunamadı" | C++ iş yükünü kurun; betik onu `vswhere` ile bulur. |
| PowerShell betiği engelliyor | `powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 ...` olarak çalıştırın. |
| PowerShell'den düzenlenen bir dosyada Türkçe karakterler bozuluyor | Windows PowerShell 5.1'in `Get-Content`/`Set-Content` komutları ANSI kod sayfasını kullanır. Dosyaları bir düzenleyicide düzenleyin ya da `-Encoding utf8` verin. |
