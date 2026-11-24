# Geliştirici belgeleri

RobotWin Studio'yu kaynaktan derleyenler, betikleyenler ya da fiziğini genişletenler için. Uygulamayı kullanmak: [kullanım kılavuzu](../kullanim/README.md). Her çözücünün nasıl çalıştığı: [fizik ve matematik](../fizik/README.md).

| Belge | İçindekiler |
| --- | --- |
| [Otomasyon API'si](API.md) | Başsız sunucu, JSON-RPC yöntemleri, sensörler, eyleyiciler, Python / ROS 2 |
| [Fizik betikleri (SDK)](SDK.md) | İfadeler, dünyanın içindeki C# betikleri, eklenti DLL'leri, özel devre parçaları; çalışan örneklerle |
| [Mimari](MIMARI.md) | Studio ↔ CoreSim katmanları, simülasyon döngüsü, yazılım eş simülasyonu, dünya hattı, dosya biçimleri, veri konumları |
| [Windows'ta kurulum](KURULUM.md) | Kurulacak araçlar, kaynaktan derleme ve çalıştırma |
| [Proje yapısı](PROJE_YAPISI.md) | Depoda ne nerede |
| [Testler](TESTLER.md) | Birim testleri, `--selftest`, ekran görüntüsü çizimi, CI |
| [Derleme ve sürüm](DERLEME.md) | Kurulum paketlerini derlemek, kurulum seçenekleri, sürüm iş akışı |
| [ESP32'nin iki çalışma modu](ESP32_MODLARI.md) | Hızlı simülasyon ve donanım uyumlu derleme; kendi Xtensa emülatörümüzün planı |
| [Yol haritası](YOL_HARITASI.md) | Bir sonraki büyük sürüme kadar yapılacakların canlı listesi |
| [Araçlar](ARACLAR.md) | .NET CLI, uygulamanın komut satırı kipleri, `build-installer.ps1`, dış derleyiciler |
| [Sorun giderme](SORUN_GIDERME.md) | Kurulum, yazılım derleme, simülasyon ve derlemede sık sorunlar |
| [Erişilebilirlik](ERISILEBILIRLIK.md) | Bugün çalışanlar, bilinen eksikler, sorun bildirme |

Ayrıca depo kökünde: [README](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/README.md), [CHANGELOG](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md), [CONTRIBUTING](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CONTRIBUTING.md), [SECURITY](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/SECURITY.md).

## Arşiv

[archive/](https://github.com/Kinin-Code-Offical/robotwin-studio/tree/main/docs/archive) 1.x Unity tabanlı tasarımın belgelerini (Unity ön yüzü, C++ NativeEngine/FirmwareEngine), eski planları ve tek seferlik raporları tarihçe için saklar. Bugünkü uygulamayı anlatmazlar.

## Bu belgelerin kuralları

- Her sayfa Türkçe ve İngilizce olarak, her dilde eksiksiz bulunur; bir sayfa iki dili karıştırmaz. Eşler `docs/_data/nav.yml` içinde listelenir; menüler ve dil düğmesi de oradan çalışır.
- Dosya başına bir konu; bir belge eklerken, adını değiştirirken ya da taşırken `nav.yml`'yi ve iki dizini güncelleyin.
- Kodun bugün ne yaptığını anlatın. Planlanan işler özellik anlatımına değil, README yol haritasına girer.
- Tam komutları ve arayüzdeki tam Türkçe etiketleri kullanın.
- Diyagramlar Mermaid (```` ```mermaid ````), formüller ```` ```math ```` blokları ve satır içinde `` $`…`$ ``: ikisi de GitHub'da ve sitede çizilir. Yazıyla çizilmiş diyagram kullanılmaz.
- Ürün adı her zaman **RobotWin Studio**'dur; sürümler yalnız sürüm numarası olarak geçer.
