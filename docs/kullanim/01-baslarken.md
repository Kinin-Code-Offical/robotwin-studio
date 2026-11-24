# 1. Başlarken

Bu bölümde RobotWin Studio'yu kurar, ekranın bölümlerini tanır ve ilk projenizi açarsınız.

## Kurulum

1. [GitHub Sürümler sayfasından](https://github.com/Kinin-Code-Offical/robotwin-studio/releases/latest) iki paketten birini indirin:
   - `RobotWinStudio-Setup-<sürüm>.exe`: .NET içindedir, başka bir şey kurmanız gerekmez (önerilen).
   - `RobotWinStudio-Setup-<sürüm>-lite.exe`: daha küçüktür ama **.NET 9 Masaüstü Çalışma Zamanı** ister; yoksa sihirbaz kurmayı önerir.
2. Dosyayı çalıştırın ve sihirbazı izleyin.
3. Kurulum bitince Başlat menüsünden **RobotWin Studio**'yu açın.

Kurulum varsayılan olarak yalnızca sizin kullanıcınız için yapılır ve yönetici izni istemez. Kaldırmak için Windows **Ayarlar → Uygulamalar** listesini kullanın.

### Kendi kodunuzu derlemek için

| Kart | Gerekli araç | Kurulum |
| --- | --- | --- |
| Arduino Uno / Nano | Arduino derleyicisi: `arduino-cli`, AVR çekirdeği ve kütüphane listesi | **Kurulumla gelir**; ayrıca bir şey kurmanız gerekmez |
| ESP32 | Visual Studio C++ araçları (MSVC) | Visual Studio 2022 veya Build Tools ile **"C++ ile masaüstü geliştirme"** iş yükü |

Kendi `arduino-cli`'nizi kullanmak isterseniz yolunu **Ayarlar → Derleyiciler** bölümüne yazın. Uygulama MSVC'yi kendisi bulur; bulamazsa yolunu aynı bölüme girin.

## İlk açılış

Uygulama açılırken bir açılış ekranı görünür. Ardından **Başlangıç** sayfası gelir.

![Açılış ekranı](../images/splash.png)

Başlangıç sayfasında şunlar vardır:

- **Yeni dünya**, **Yeni devre / kart** ve **Aç…** düğmeleri.
- **Dünyalar:** hazır dünya şablonları (boş atölye, servo kol, iki tekerlekli araba, ultrasonik sensör, kumandalı araba…).
- **Devre ve robot örnekleri:** Başlangıç, Orta ve Robot düzeyinde hazır devreler (LED ve buton, Arduino Blink, LCD + OLED, ESP32 PWM, MOSFET ile motor, op-amp, 7805, RC filtre, röle, NAND, çizgi izleyen robot, engelden kaçan robot, mikro quadcopter).
- Sağda **Son açılanlar**, **Otomatik yedekler** ve sık kullanılan **Kısayollar**.

![Başlangıç sayfası](../images/home.png)

> İpucu: Bir örneğe tıklamak onu yeni bir sekmede açar. Örnekleri bozmaktan korkmayın; kaydetmediğiniz sürece orijinal hali değişmez.

## İlk robotun: etkileşimli rehber

Uygulamayı ilk açtığınızda 5–10 dakikalık bir rehber önerilir. Daha sonra **Başlangıç** sayfasındaki **İlk robotun** düğmesiyle ya da **Yardım → İlk robotun** ile yeniden açabilirsiniz.

Rehber sağ altta küçük bir kart olarak durur. Her adımda neye basacağınızı yazar ve o düğmenin çevresini turuncu bir çerçeveyle gösterir. Adımı yaptığınızda kendiliğinden fark eder ve sonrakine geçer. Karttaki düğme (örneğin **Servo kolu aç**, **Yükle ve çalıştır**) adımı sizin yerinize yapar; **Bu adımı atla** sonrakine geçer.

1. **Hazır servo kolunu açın:** Arduino Uno iki SG90 servoyu sürer.
2. **Çalıştırın ve izleyin:** kol tarar.
3. **Kodu açın:** kolun Arduino kodu Kod sayfasında açılır.
4. **Kodu değiştirin:** 23. satırdaki `step = 2` içinde 2 seçili gelir; 6 yazın. Kol üç kat hızlı tarar.
5. **Karta yükleyin:** kod derlenir, Uno'ya yüklenir, dünya baştan çalışır.
6. **Akımı ölçün:** **Kablolar ve ölçümler** sekmesinde servoların kablolarındaki akım canlı görünür.
7. **Kaydedin:** dünya, değiştirdiğiniz kodla birlikte saklanır.

![İlk robotun rehberi](../images/guide.png)

## Ekranın bölümleri

| Bölüm | Yeri | Ne işe yarar |
| --- | --- | --- |
| Menü çubuğu | En üst | **Dosya**, **Düzen**, **Görünüm**, **Simülasyon**, **Pencere**, **Yardım** menüleri. **F10** ile klavyeden açılır. |
| Belge sekmeleri | Menünün yanında | Açık devreler ve dünyalar. Sekmeleri sürükleyerek sıralayabilirsiniz. **Ctrl+Tab** sonraki sekme, **Ctrl+W** sekmeyi kapatır. |
| Tasarımdan gerçeğe hattı | Sol kenar | Sayfalar üç grupta: **ELEKTRONİK** (Devre → PCB → Kod), **MEKANİK** (Parça → Montaj), **TEST** (Dünya). Altta Başlangıç, Kütüphane, Ayarlar ve Yardım. Başlangıç'a sol üstteki logodan da dönülür. **Ctrl+B** hattı genişletir; her sayfanın ne işe yaradığı ve durumu yazar. |
| Sayfa | Orta | Seçili aşamanın editörü. |
| Durum çubuğu | En alt | Son mesaj, simülasyon durumu, parça ve kablo sayısı. |

![Gruplu sol çubuk (genişletilmiş)](../images/rail-groups.png)

### Pencere özellikleri

- **Komut paleti (Ctrl+K):** her komutu, sayfayı ve parçayı adını yazarak bulun.
- **Bölünmüş görünüm (Ctrl+\\):** iki sayfayı yan yana gösterir; örneğin devre ile PCB'yi birlikte görürsünüz.
- **Ayrı pencereye al:** **Pencere → Bu sayfayı ayrı pencereye al** ile bir sayfayı ikinci bir pencereye (örneğin ikinci monitöre) taşıyın.
- **Odak modu:** **Görünüm → Odak modu** gezinme çubuğunu gizler.
- **Panelleri taşımak (Dünya):** Dünya sayfasında palet, denetçi ve alt sekmeler başlıklarından sürüklenerek ayrı pencereye alınır; pencereyi kenara bırakınca sola, sağa ya da alt bölmeye yerleşir. Düzen saklanır. Bkz. [Dünya → Panelleri yerleştirme](07-dunya.md#panelleri-yerleştirme).
- **Tam ekran (F11):** **Esc** veya **F11** ile çıkılır.
- **Arayüz ölçeği:** **Görünüm → Arayüz ölçeği** (%75–%150).

## Dosyalar

| Uzantı | Ne saklar |
| --- | --- |
| `.rwproj` | Bir devre / kart projesi: şema, PCB, montaj, kartların kodu |
| `.rwworld` | Bir dünya: nesneler, kablolar, bağlantılar ve içindeki kartlar |
| `.rwmodel` | Parça sayfasında yaptığınız 3D parçalar |
| `.rwsim` | Simülasyon kaydı (ölçümler, olaylar, seri çıktı) |
| `.rwpart`, `.rwlib` | Kendi parçanız / parça kütüphanesi |

- **Kaydet:** Ctrl+S. **Farklı kaydet:** Ctrl+Shift+S. **Aç:** Ctrl+O.
- Uygulama kaydedilmemiş işinizi belirli aralıklarla `Belgeler\RobotWin\Yedekler` klasörüne yedekler. Yedekleri Başlangıç sayfasındaki **Otomatik yedekler** listesinden açabilirsiniz.
- Kurulum bu dosya türlerini Windows'a kaydeder; dosyaya çift tıklayınca RobotWin Studio açılır.

## Kütüphane

**Kütüphane** (Ctrl+7) sayfası tüm parçaları şema sembolü, PCB ayak izi ve 3D önizlemesiyle gösterir.

- **Bundan yeni parça:** var olan bir parçanın simülasyon modelini kullanıp kendi değerlerinizle yeni bir parça (`.rwpart`) oluşturur.
- **3B modeli değiştir…:** bir parçanın 3D görünümünü kendi STL / OBJ / PLY dosyanızla değiştirir. **Üretilen modele dön** eskisini geri getirir.
- **İçe aktar…:** `.rwpart` / `.rwlib` dosyaları ve KiCad sembol (`.kicad_sym`) ile ayak izi (`.kicad_mod`, `.pretty` klasörü) dosyaları.
- **Kütüphanemi dışa aktar:** kendi parçalarınızı tek bir `.rwlib` dosyasında paylaşın.

## Ayarlar

**Ayarlar** (Ctrl+,) sayfasındaki başlıca bölümler:

| Bölüm | Örnek ayarlar |
| --- | --- |
| Görünüm | Arayüz ölçeği, parçacık efektleri, simülasyon sesleri ve ses düzeyi, akım akışı animasyonu, 3D kalitesi, açılışta başlangıç sayfası |
| Simülasyon ve fizik | Parça hasarı, arıza olunca duraklat, ortam sıcaklığı, en büyük zaman adımı, başlangıç hızı |
| Dünya ve düzenleme | Yeni dünyaların yerçekimi, dünya editörü ızgarası, uzunluk birimi (mm / mil) |
| Dosyalar ve otomatik kayıt | Otomatik yedek aralığı, son dosyalar listesi, proje klasörü |
| Derleyiciler | `arduino-cli` yolu, `vcvars64.bat` yolu |

Ayrıca **Klavye kısayolları** listesi, **Dosya türleri** (Windows'ta bu uygulamayla aç), **Hakkında** ve **Varsayılanlara dön** düğmesi buradadır.

---

[İçindekiler](README.md) · [Sonraki: 2. Devre →](02-devre.md)
