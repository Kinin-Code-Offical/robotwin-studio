# 1. Başlarken

Bu bölümde RobotWin Studio'yu kurar, ekranın bölümlerini tanır ve ilk projenizi açarsınız.

## Kurulum

1. [GitHub Sürümler sayfasından](https://github.com/Kinin-Code-Offical/robotwin-studio/releases/latest) kurulum dosyasını, `RobotWinStudio-Setup-<sürüm>.exe`'yi indirin. Küçüktür: seçtiğiniz parçaları aynı sürümden kendisi indirir ve her birini doğrular.
2. Çalıştırın. İlk sayfada **dili** (kurulumun ve uygulamanın dili) ve **kurulum türünü** seçin:
   - **Tam** (önerilen): uygulama, kendi .NET 10'u, Arduino derleyicisi ve STEP / IGES desteği. Kurulur kurulmaz her şey internetsiz çalışır.
   - **Hafif**: yalnız uygulama; bilgisayarınızdaki .NET 10 Masaüstü Çalışma Zamanı'nı kullanır (yoksa kendi kopyasını getirir). Derleyici ve STEP / IGES sonradan eklenebilir.
   - **Özel**: bileşenleri kendiniz işaretleyin.
3. **Kurulum biçimini** seçin: bu kullanıcı için (varsayılan, yönetici izni istemez), tüm kullanıcılar için (yönetici ister) ya da **taşınabilir** (yalnız bir klasör, örneğin USB bellekte: kısayol, kayıt defteri girdisi ve dosya türü yok).
4. Kurulum bitince Başlat menüsünden **RobotWin Studio**'yu açın.

Bileşenleri sonradan eklemek ya da kaldırmak için **Ayarlar → Güncellemeler → Bileşenleri değiştir…**'i (ya da Windows **Ayarlar → Uygulamalar → RobotWin Studio → Değiştir**'i) kullanın. Kaldırmak için Windows **Ayarlar → Uygulamalar** listesini kullanın. Bir güncelleme yeni sürümü kurduktan sonra onu bir kez dener; yeni sürüm açılmazsa önceki sürümü olduğu gibi geri yükler. İnternetsiz kurmak için sürümün `.rwpack` dosyalarını kurulum dosyasının yanına koyun.

### Kendi kodunuzu derlemek için

| Kart | Gerekli araç | Kurulum |
| --- | --- | --- |
| Arduino Uno / Nano | Arduino derleyicisi: `arduino-cli`, AVR çekirdeği ve kütüphane listesi | **Kurulumun bir bileşenidir** (tam kurulumda vardır); hafif kurulumda **Ayarlar → Güncellemeler → Bileşenleri değiştir…** ile ekleyin |
| ESP32 | Visual Studio C++ araçları (MSVC) | Visual Studio 2022 veya Build Tools ile **"C++ ile masaüstü geliştirme"** iş yükü |

Kendi `arduino-cli`'nizi kullanmak isterseniz yolunu **Ayarlar → Derleyiciler** bölümüne yazın. Uygulama MSVC'yi kendisi bulur; bulamazsa yolunu aynı bölüme girin.

## İlk açılış

Uygulama açılırken bir açılış ekranı görünür: o an neyin yüklendiğini, ne kadarının bittiğini ve sürümü gösterir. Ardından **Başlangıç** sayfası gelir.

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
| Belge sekmeleri | Menünün yanında | Açık devreler ve dünyalar; kayıtlı bir dosya kendi adıyla görünür. Kaydedilmemiş değişiklik, kapatma düğmesinin yerinde bir nokta olarak görünür. Sekmeler sığmazsa şerit fare tekerleğiyle ya da oklarla kayar; **▾** bütün açık sekmeleri listeler. Sağ tıklayıp **Sabitle** derseniz sekme en solda kalır, toplu kapatmalarda kapanmaz ve dosya bir dahaki açılışta da sabit gelir. Sekmeleri sürükleyerek sıralayabilirsiniz. **Ctrl+Tab** sonraki sekme, **Ctrl+W** sekmeyi kapatır, **Ctrl+Shift+T** kapatılan dosyayı yeniden açar. |
| Tasarımdan gerçeğe hattı | Sol kenar | Sayfalar üç grupta: **ELEKTRONİK** (Devre → PCB → Kod), **MEKANİK** (Parça → Montaj), **TEST** (Dünya). Altta Başlangıç, Kütüphane, Ayarlar ve Yardım. Başlangıç'a sol üstteki logodan da dönülür. **Ctrl+B** hattı genişletir; her sayfanın ne işe yaradığı ve durumu yazar. |
| Sayfa | Orta | Seçili aşamanın editörü. |
| Durum çubuğu | En alt | Son mesaj, simülasyon durumu, parça ve kablo sayısı. |

![Gruplu sol çubuk (genişletilmiş)](../images/rail-groups.png)

### Pencere özellikleri

- **Komut paleti (Ctrl+K):** her komutu, sayfayı ve parçayı adını yazarak bulun. Ne istediğinizi cümle gibi de yazabilirsiniz, kelimelerin sırası ve ekleri önemli değil: "MOSFET ekle", "servo motorun akımını izle", "DRC çalıştır", "seçili parçayı PCB'de göster", "montaj ilişkilerini denetle". Açık sayfanın komutları önce gelir; yalnızca o anda yapılabilecek komutlar (bir parça seçiliyken "PCB'de göster" gibi) listelenir.
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
- **3B modeli değiştir…:** bir parçanın 3D görünümünü kendi STL / OBJ / PLY / WRL ya da STEP / IGES dosyanızla değiştirir. **Üretilen modele dön** eskisini geri getirir.
- **İçe aktar…:** `.rwpart` / `.rwlib` dosyaları ve KiCad sembol (`.kicad_sym`) ile ayak izi (`.kicad_mod`, `.pretty` klasörü) dosyaları.
- **Kütüphanemi dışa aktar:** kendi parçalarınızı tek bir `.rwlib` dosyasında paylaşın.

## Ayarlar

**Ayarlar** (Ctrl+,) sayfası kategorilere ayrılır. Soldaki listeden bir kategori seçin ya da üstteki **Ayarlarda ara** kutusuna bir sözcük yazın; arama bütün kategorilere bakar.

| Kategori | Neler var |
| --- | --- |
| Genel | Dil, açılışta başlangıç sayfası, ilk robot rehberi, proje klasörü, son dosyalar, uzunluk birimi |
| Görünüm ve tema | Tema galerisi (9 koyu, 7 açık tema ve özel tema), vurgu rengi, arayüz ölçeği, performans göstergesi, efektler ve ses, 3B kalite, 3B çizici (kendi GPU çizicimiz ya da WPF) |
| Kaydetme ve yedekleme | Otomatik yedek sıklığı, kaydederken önceki sürümü saklama, çökme sonrası kurtarma, yedek klasörü, belge başına kopya sayısı, klasörün boyut sınırı |
| Klavye kısayolları | Pencerenin ve bütün sayfaların kısayollarını değiştirme, çakışma uyarısı, tek tek ya da toplu sıfırlama |
| Simülasyon ve fizik | Parça hasarı, arıza olunca duraklat, ortam sıcaklığı, zaman adımı, başlangıç hızı, simülasyonu ayrı iş parçacığında çalıştırma |
| Dünya ve düzenleme | Yeni dünyaların yerçekimi, dünya editörü ızgarası |
| Derleyiciler | `arduino-cli` yolu, `vcvars64.bat` yolu |
| Güncellemeler, Eklentiler | Sürüm denetimi; eklenti klasörleri ve betikler |
| Gelişmiş | Ayarları dosyaya aktarma ve dosyadan alma, hata günlüğü, dosya türleri |

**Dil:**

- Uygulama ilk açılışta İngilizcedir. **General → Language** (Genel → Dil) kartından English ya da Türkçe seçin. Seçtiğiniz dil, uygulama yeniden açılınca her yerde geçerli olur; kartta **Şimdi yeniden başlat** düğmesi var.
- Dille birlikte değişenler:
  - pencerelerin, tuvallerin ve ölçü aletlerinin bütün metinleri;
  - sayı ve tarih biçimi (İngilizcede 1.5 mm, Türkçede 1,5 mm);
  - Yardım sayfasındaki kılavuz ve Haberler;
  - parça kataloğunun adları ve açıklamaları;
  - örneklerin adları ve örnek kodların yorumları.
- Aramalar (parça, ayar, komut paleti) iki dilde de çalışır: "resistor" da "direnç" de bulur.
- **Kendi dil paketinizi yapmak için:**
  1. `Lang/en.json` dosyasını kopyalayıp `de.json` gibi adlandırın. Anahtarlar arayüzün kaynak metinleridir (Türkçe), değerler İngilizceleri: değerleri kendi dilinize çevirin. `{…}` olan kalıplarda sayılar ve adlar `{0}`, `{1}` sırasıyla yerleşir.
  2. Dosyanın başındaki `"language"` alanına dilin adını, `"culture"` alanına sayı ve tarih biçimini (örneğin `"de-DE"`) yazın.
  3. Yeni dosyayı **Dil paketleri klasörü** düğmesinin açtığı klasöre koyun (`%APPDATA%\RobotWin\lang`). Dosyanın adı dilin kimliği olur.
  4. Paket listede "(kendi paketiniz)" olarak görünür. Çevirmediğiniz metinler İngilizce görünür, Türkçe değil; paketi parça parça büyütebilirsiniz.

**Temalar:** **Görünüm ve tema** kategorisindeki galeri her temayı pencerenin küçük bir resmi olarak gösterir. Tıkladığınız tema hemen uygulanır.

![Tema galerisi: koyu, açık ve özel temalar; vurgu renkleri](../images/settings-themes.png)

- **Koyu:** Grafit (varsayılan), Siyah (OLED ekranlar için), Gece mavisi, Derin okyanus, Orman, Mürdüm, Gül kurusu gece, Kahve, Yüksek karşıtlık.
- **Açık:** Açık, Kâğıt, Pembe, Şeftali, Lavanta, Nane, Gökyüzü. Açık temalarda şema bir kâğıda basılmış gibi koyu mürekkeple çizilir, PCB soluk bir kart üstünde koyu baskıyla görünür (bakır renkleri aynı kalır), kod düzenleyici de açılır ve sözdizimi renkleri açık zemine uygun tonlara geçer.
- **Özel:**
  - **Ton:** 0 kırmızı, 120 yeşil, 200 mavi, 330 pembe.
  - **Doygunluk:** zeminlerin ne kadar renkli olacağı.
  - **Açık zemin** anahtarı.
  - Kaydırırken pencere renk değiştirir; bırakınca kaydedilir.
- **Vurgu rengi:**
  - Seçili öğeler, odak ve bağlantılar için on renk; **T** işaretli olan temanın kendi rengidir.
  - Açık temalarda vurgu, karşıtlık için koyulaşır.
  - 3B görünümler ve ölçü aletlerinin ekranları kendi ışığıyla kalır.

**Yedekler nasıl çalışır?**

- Kaydedilmemiş sekmeler seçtiğiniz aralıkla yedek klasörüne kopyalanır; asıl dosyanız değiştirilmez.
- Bir dosyanın üstüne kaydettiğinizde eski hâli `ad-önceki-tarih` adıyla saklanır.
- Her belgenin en yeni kopyaları tutulur ve klasör boyut sınırını aşmaz; en eskiler silinir.
- Uygulama beklenmedik biçimde kapanırsa bir sonraki açılışta başlangıç sayfası o çalışmanın yedeklerini gösterir.

**Kısayol değiştirmek:** **Klavye kısayolları** kategorisinde düğmeye tıklayıp yeni tuşlara basın. Devre, PCB, Dünya, Kod ve Parça sayfalarının tuşları da buradadır; onlar tek harf olabilir. **Esc** vazgeçer, **Backspace** tuşu kaldırır. Başka bir komutla çakışan tuş turuncu bir notla işaretlenir. Tek bir harf pencere kısayolu olamaz, çünkü yazarken çalışırdı; **Ctrl**, **Alt** ya da **Shift** ile birlikte basın. **F1–F12** tek başına olur.

---

[İçindekiler](README.md) · [Sonraki: 2. Devre →](02-devre.md)
