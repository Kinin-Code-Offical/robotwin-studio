# 7. Dünya

**Dünya** sayfası (Ctrl+6) her şeyin bir araya geldiği yerdir. Tasarladığınız kartları, katalogdaki parçaları, mekanik gövdeleri ve 3D modelleri gerçek fizikli bir ortama koyar, kablolarla bağlar, vidalar veya menteşelerle birleştirir ve hepsini birlikte çalıştırırsınız. Devre, kartların yazılımı, radyo ve fizik aynı anda, adım adım hesaplanır.

![Arduino ile iki eksenli servo kol](../images/world-servo-arm.png)

## Bir dünya açmak

- **Yeni dünya** (Ctrl+Shift+N): 4 × 3 m'lik boş bir atölye.
- **Bu devreyi yeni bir dünyaya koy:** açık devrenizi bir kart olarak yeni dünyaya koyar.
- **Aç…:** kayıtlı bir `.rwworld` dosyası.
- **Dünya şablonları** (Başlangıç sayfası veya **Dosya → Dünya şablonundan**):

| Şablon | İçerik |
| --- | --- |
| Boş atölye | 4 × 3 m zemin, 25 °C |
| Kabloyla bağlı iki kart | 9 V güç kartı ve LED kartı ayrı PCB'lerde; iki kablo bağlanınca LED'ler yanar |
| Arduino ile iki eksenli servo kol | Uno iki SG90 servoyu sürer; ikinci servo birincinin koluna vidalı |
| Arduino, LCD ve OLED (I²C kablolu) | Uno, 16×2 LCD, 128×64 OLED ve potansiyometre I²C kablolarıyla bağlı |
| İki tekerlekli araba (gerçek fizik) | PLA şase, iki TT motor ve tekerlek, bilyeli sarhoş teker, 4×AA pil |
| Ultrasonik mesafe sensörü ve engeller | HC-SR04 önündeki engele gerçekten ölçüm yapar |
| Pil, motor ve tekerlek | 4×AA pil TT motoru sürer, tekerlek motor milinde |
| Kumandalı araba (ESP32 + PS5 kumanda) | ESP32 + Bluepad32, DRV8833, iki TT motor, USB'li bilgisayar ve eşleşmiş DualSense |

## Ekranın bölümleri

- **Üst araç çubuğu:** Seç ve taşı, **Kablo çek** (K), **Bağla** (B), döndür, çoğalt, sil, geri al / yinele; **Dünyayı çalıştır**, **10 ms ilerlet**, **Durdur ve başa dön**, hız seçimi; kamera takibi, üstten görünüm, hepsini sığdır.
- **Sol panel sekmeleri:** **Sahne**, **Kartlar**, **Parçalar**, **Mekanik**, **Ortam**.
- **Ortada** 3D görünüm.
- **Sağda** denetçi: seçili nesnenin adı, konumu, fiziği, kabloları ve bağlantıları.
- **Alt sekmeler:** **Olaylar**, **Seri monitör**, **Kablolar ve ölçümler**, **RF**.

## Adım adım: ilk dünyanız

Bu örnekte bir pil bir motoru, motor da bir tekerleği döndürecek.

1. **Yeni dünya** (Ctrl+Shift+N) açın.
2. **Parçalar** sekmesinde aramaya `pil` yazın ve **4×AA Pil 6 V**'u ekleyin. Sonra `motor` yazıp **TT Redüktörlü Motor 1:48**'i ekleyin.
3. Motoru seçin. Sağdaki **Fizik** bölümünde **Sabit (zemine/masaya sabitlenmiş)** anahtarını açın. Böylece motor dönerken yerinden kaçmaz.
4. **Mekanik** sekmesinden bir **Tekerlek** ekleyin.
5. Tekerlek seçiliyken **Bağla** aracını (B) seçin ve motora tıklayın. Açılan menüden **"Motor miline sabitle (birlikte döner)"** seçeneğini seçin.
6. **Kablo çek** aracını (K) seçin. Pilin `+` pinine, sonra motorun bir pinine tıklayın. Aynısını pilin `−` pini ve motorun diğer pini için yapın. Fare bir pinin üstündeyken pinin adı, canlı gerilimi ve kaç kablo bağlı olduğu görünür.
7. **Dünyayı çalıştır** (F5). Motor döner, tekerlek döner, motor sesi duyulur.
8. Alttaki **Kablolar ve ölçümler** sekmesinde her kablonun akımını, gücünü ve yük yüzdesini görün.
9. **Durdur ve başa dön** (Shift+F5) ile her şeyi başlangıç durumuna getirin.

## Nesne türleri

### Kartlar

Kendi tasarladığınız devre + PCB + kod üçlüsü. **Kartlar** sekmesinde:

- **Açık sekmeden:** açık bir devreyi karta dönüştürüp ekler.
- **Proje dosyasından kart ekle (.rwproj)…**
- **Yeni boş kart tasarla**
- **Hazır modül kartları:** Arduino Uno, Arduino Nano, ESP32 DevKit V1, ESP32 DevKitC, fırçalı uçuş kontrolcüsü.

Aynı tasarımı birden çok kez koyabilirsiniz; kopyalar aynı devreyi paylaşır. Kart seçiliyken denetçide:

- **Kartı düzenle (devre · PCB · kod):** kart kendi sekmesinde açılır.
- **Kartın kodunu yaz:** kartın kodunu Kod sayfasında açar ([4. Kod](04-kod.md)).
- **Ayrı proje dosyası olarak dışa aktar…**

### Parçalar

Katalogdaki tek bir parça: servo, motor, pil, sensör, ekran, Arduino… **Parçalar** sekmesinde arama kutusu ve kategoriler vardır: Geliştirme, Elektromekanik, Güç, Sensör, Ekran, Anahtarlama, **Kablosuz ve USB**. Parametreleri denetçideki **Parametreler** bölümünden değiştirin.

Tek başına konmuş bir Arduino veya ESP32'yi **Kart tasarımına dönüştür ve kodunu aç** ile programlanabilir bir karta dönüştürebilirsiniz.

### Mekanik gövdeler

**Mekanik** sekmesindeki **Temel şekiller:** Kutu, Plaka / şase, Kol / çubuk, Silindir, Tekerlek (kauçuk lastikli), Küre / bilye, Masa / sabit zemin. Boyut ve renk denetçiden ayarlanır.

### Çarpışma şekilleri

Her parçanın katı gövdesi kendi 3D modelinden çıkarılır: yuvarlak bir motor gövdesi yuvarlanır, kanat sivrilen biçimiyle, kumanda kendi dış hattıyla çarpışır (sınır kutusu değil). Bacaklar, pinler ve kablo uçları gibi ince parçalar gövdeye katılmaz. **Görünüm → Çarpışma şekilleri (dünya)** fiziğin gerçekten çarpıştırdığı gövdeleri modellerin üzerinde yarı saydam çizer; simülasyon çalışırken gövdelerle birlikte hareket eder.

### Basınçlı kaplar ve roketler

**Mekanik → Basınçlı kaplar ve roketler** bölümünde PET şişe (su roketi), hava tankı, katı yakıtlı motor ve roket kanatçığı vardır. Silindir, kutu veya küre bir gövdeyi denetçide **İçinde gaz tutan kapalı kap** yapabilirsiniz:

- **Gaz, basınç, iç hacim, duvar, su:** kabın içindekiler. Gaz ideal gaz yasasıyla (p·V = m·R·T) hesaplanır.
- **Lüle:** boğaz ve çıkış çapı, jetin çıktığı yüz. Akış izentropiktir; basınç oranı yeterince yüksekse boğazda ses hızına ulaşır, genişleyen lüle gazı süpersonik hızlandırır. İtki ṁ·vₑ + (pₑ − pₐ)·Aₑ olarak gövdeye lülenin olduğu yerden uygulanır.
- **Vana açılır (s):** rampa lüleyi bu ana kadar kapalı tutar.
- **Su:** lüleyi kapattığı sürece önce su çıkar (Bernoulli; roketin ivmesi suyu lüleye bastırır), sonra hava.
- **Katı yakıt:** KNSB, KNDX veya APCP tanesi; yanma hızı basınca bağlıdır (r = a·pⁿ). Oda basıncı, yanma yüzeyinin ürettiği gaz ile lüleden çıkan gazın dengesinden kendiliğinden çıkar; itki eğrisi hazır okunmaz. Motora bağlı bir **e-match** devreden ateşlenince ya da **Ateşleme zamanı** gelince tutuşur.
- **Patlama:** basınç, duvarın çeper gerilmesini (σ = p·r/t) malzemenin dayanımına çıkarırsa kap patlar.

Kabın kütlesi gaz, su ve yakıt azaldıkça düşer; sıcak gaz duvarı ısıtır (ısı kamerasında görünür). Çalışırken denetçide basınç, sıcaklık, itki ve durum canlı okunur. **Su roketi** ve **Katı yakıtlı motor test standı** şablonlarıyla başlayın.

### 3D modeller

**Dışarıdan model → 3D model içe aktar (STL, OBJ, PLY)…** ile kendi modelinizi ekleyin. Kütle, ağırlık merkezi ve eylemsizlik malzemeden hesaplanır. Modelle sayfasında yaptığınız parçaları **Dünyaya gönder** ile doğrudan buraya getirebilirsiniz ([5. Modelle](05-modelle.md)).

## Kablolar

- Her kablonun bir kesiti (AWG), uzunluğu ve rengi vardır. Varsayılan 24 AWG'dir; DC motor veya pil bağlarken 22 AWG.
- Kablonun direnci gerçek bakır direncidir. Akım sınırı aşılırsa kablo ısınır ve yanar; **Kablolar ve ölçümler** sekmesinde **YANDI** yazar.
- Kartlar, bir kablo bağlanana kadar elektriksel olarak birbirinden ayrıdır. İki kart arasında sinyal taşıyorsanız GND'yi de bağlayın.
- Kabloyu kaldırmak için denetçide **Kabloyu sök**'e basın.

## Mekanik bağlantılar

Bir nesne seçiliyken **Bağla** (B) aracıyla başka bir nesneye tıklayın:

| Seçenek | Ne olur |
| --- | --- |
| **Servo kolu / Motor miline sabitle (birlikte döner)** | Nesne servo koluna veya motor miline takılır ve onunla döner. Yalnızca hedefin kolu veya mili varsa çıkar. |
| **… gövdesine sabitle (vida / yapıştırıcı)** | İki nesne tek parça gibi hareket eder |
| **Menteşe ile bağla (serbest döner)** | Bir eksen etrafında serbest döner |
| **Kızakla bağla (doğrusal kayar)** | Bir doğru boyunca kayar |

Birbirine bağlı parçalar birbiriyle çarpışmaz. Bir nesneyi sürüklediğinizde ona takılı her şey de gelir. Bağlantıyı kaldırmak için denetçide **Bağlantıyı çöz**.

## Fizik ve ortam

Her nesnenin denetçisinde **Fizik** bölümü:

- **Sabit:** nesne zemine veya masaya sabitlenmiştir, hareket etmez.
- **Malzeme:** PLA, PETG, ABS, Akrilik, Alüminyum, Çelik, Karbon, Ahşap, MDF, Kauçuk, FR4. Sürtünme ve sekme malzemeye göre değişir.
- **Doluluk (%)** ve **Kütle (g, 0 = otomatik)**.

**Ortam** sekmesi:

- **Yerçekimi:** Dünya 9.81, Ay 1.62, Mars 3.71, Jüpiter 24.8, Sıfır yerçekimi.
- **Ortam sıcaklığı**, **zemin genişliği / derinliği**, **zemin sürtünmesi (μ)**, **çevre duvarları**.
- **Engel ekle** / **Engelleri temizle**.

Servolar devreden gelen darbe komutunu okur; kolun açısı ve çektiği akım devreye geri döner. DC motorlar devrenin hesapladığı hızla döner; mile binen yük motora geri yansır. Sert çarpmalar ve kopan bağlantılar **Olaylar** sekmesine yazılır.

## Sensörler ve ekranlar

- **HC-SR04 ultrasonik sensör** dünyadaki nesnelere ışın göndererek gerçekten ölçer. Işın ve çarptığı nokta mesafeye göre renklenerek çizilir.

  ![Ultrasonik sensör ve engel](../images/world-sonar.png)

- **I²C ekranlar** (SSD1306 OLED, 16×2 LCD) kartın yazdıklarını 3D modelin üzerinde canlı gösterir.
- LED'ler ve lambalar yanar; aşırı ısınan parçalar kızarır, bozulan parçalardan kıvılcım çıkar.

## Kumandalar, radyo ve USB

![ESP32 ve PS5 kumandalı araba](../images/world-ps5-car.png)

**Parçalar → Kablosuz ve USB** kategorisinde: DualSense (PS5), DualShock 4 ve Xbox kumandaları, **Bilgisayar + USB kablosu**, **Wi-Fi Modem (2.4 GHz)**, **BLE Beacon Etiketi**, **IR Kumanda (NEC, 21 tuş)**, **TSOP38238 IR Alıcı**.

### Kumanda

1. Kumandayı seçin. Denetçide **Eşleştirme modu (PS + Create basılı)** ile ESP32'yle eşleştirin.
2. **Sanal kumandayı aç (fare / klavye)** ile ekrandaki kumandayı kullanın, ya da gerçek bir kumandayı USB veya Bluetooth ile bilgisayara bağlayın. Görünmezse **Kumandaları yeniden tara**.
3. **ESP32'ye uzaklık** kaydırıcısı (simülasyon çalışırken) sinyalin uzaklıkla nasıl zayıfladığını gösterir.
4. **Kumandayı aç/kapat** ve **Eşleşmeyi unut** düğmeleri de buradadır.

ESP32 seçiliyken **Örnek kodu yükle: kumandalı araba (Bluepad32)** hazır bir araba kodu yükler.

### USB

**Bilgisayar** nesnesini bir karta yöneltip **USB'yi tak** deyin. Kablo takılıyken kart 5 V ile beslenir ve kartın seri portu **Seri monitör** sekmesine bağlanır. **USB'yi çıkar** ile ayırın.

**USB-C şarjlı lityum piller** (**Parçalar → Güç**): USB-C girişli 18650 hücre, 2S 18650 paket (7.4 V) ve sabit 1.5 V veren USB-C AA hücreler. İçlerinde gerçek elektronik çalışır: koruma devresi (aşırı deşarj 2.4 V, aşırı şarj, aşırı akımda keser; boş bir paket ancak şarj cihazı takılınca uyanır) ve sabit akım / sabit gerilim şarj devresi (4.2 V'a kadar 1 A, sonra akım azalır). **Bilgisayar + USB kablosu** nesnesini pile yöneltip takın: pil şarj olur, çıkarınca şarj durur.

### Radyo ortamı

Wi-Fi, BLE ve Bluetooth sinyalleri uzaklıkla, engellerle ve gövdelerle zayıflar. Alttaki **RF** sekmesinde cihazlar, sinyal gücü (RSSI), kanal ve saniyedeki paket sayısı görünür. **Sönümleme**, **Dalgalar** ve **Bağlantı çizgileri** seçenekleri radyo görselleştirmesini açar / kapatır. IR kumanda yalnızca görüş hattında çalışır; tuşları denetçideki **IR kumanda tuşları (NEC)** bölümündedir.

## Sahne ağacı

**Sahne** sekmesi dünyadaki her şeyi neyin neye takılı olduğuna göre ağaç olarak gösterir (örneğin "servo koluna takılı", "mile takılı", "sabit"). Çalışırken 60 °C üstündeki sıcaklıkları ve **ARIZALI** nesneleri gösterir. Altında **Kablolar** listesi vardır. Sağ tık: **Odakla**, **Alt nesneleri gizle/göster**; çift tık nesneye odaklanır.

## Çalıştırma

| Düğme / tuş | İşlev |
| --- | --- |
| **Dünyayı çalıştır** / **Duraklat** / **Devam** (F5) | Başlat, duraklat, sürdür |
| **10 ms ilerlet** | Tek adım |
| **Durdur ve başa dön** (Shift+F5) | Durdurur, her şeyi başlangıç konumuna getirir |
| Hız | 0.01×, 0.1×, 0.5×, Gerçek zaman, 5× |

Kartta yazılım yoksa **Olaylar** sekmesine "Yüklü yazılım yok (kart boşta)" yazılır.

## Kayıt ve render

Gerçek zamanlı çalıştırma ekrana yetişmek zorundadır. **Render** ise bir kaydı ekrandan bağımsız olarak, seçtiğiniz doğrulukta baştan sona yeniden hesaplar ve sonra video gibi oynatır.

1. Araç çubuğunda **● Kayıt**'a basın (Ctrl+R). Dünya baştan çalışmaya başlar.
2. İstediğiniz an **Kaydı bitir**'e basın. Render penceresi açılır ve bu süre hesaplanmaya başlar. **Simülasyon › Render** (Ctrl+F5) ile süreyi kendiniz de girebilirsiniz.

![Render: gerilme, kuvvetler ve hava akışı](../images/world-render.png)

**Hesaplananlar**

- **Her kare:** her nesnenin konumu ve yönü, servo kolu ve motor mili açıları, hızlar, ivmeler, temas ve eklem kuvvetleri, her parçanın akım, gerilim, güç ve sıcaklık sinyalleri, devre düğümleri, seri port çıktısı.
- **Gerilme:** işaretlediğiniz her parça, "gerilme kare hızında" (varsayılan 15 kare/s) ve en yüklü anlarda, o anki gerçek yükleriyle FEM ile çözülür. İnce uzun parçalarda kiriş elemanları kullanılır.
- **Hava akışı:** bütün hareketli parçalar o anki konumlarında birlikte, 3B lattice-Boltzmann ile çözülür. Rüzgâr, dünyanın rüzgârı eksi montajın kendi hızıdır; montaj duruyorsa test hızı kullanılır. Sonuç montaja etkiyen kuvvet ve sürükleme katsayısıdır (Cd).
- **Isınma:** kartlardaki bileşenlerin güç kaybından iletim, taşınım ve ışınım çözülür.

**Oynatma**

- **Oynat / duraklat:** Boşluk tuşu. **Kare kare ilerleme:** ← →. **Başa / sona:** Home / End. Hız ×0,05 ile ×2 arasında seçilir (ağır çekim).
- **Renk:** gerilme (von Mises, deforme şekil büyütülmüş) ya da hız okları. **Ölçek:** render'ın en yüksek değeri ya da her malzemenin akma sınırı.
- **Kuvvetler:** temas kuvvetleri turuncu, eklem kuvvetleri camgöbeği ok. **Hava:** montajın ortasından geçen hız kesiti (mavi durgun, sarı serbest akış, kırmızı hızlanan hava) ve aerodinamik kuvvet (mor ok).
- **Grafikler:** kaydedilen her kanal. Grafiğe tıklayınca ya da sürükleyince o saniyeye atlanır.
- **Parçalar · gerilme:** her parçanın en kötü anı ve emniyet katsayısı. Satıra tıklayınca o ana gidilir.
- **Isı** ve **Günlük** sekmeleri; sonuçlar `.rwanalysis` ya da CSV olarak saklanır.

Elle verilen kumanda girdileri (gamepad, sahnedeki düğmeler) render'da henüz yeniden oynatılmıyor. Render dünyanın programlı davranışını (firmware, motorlar, fizik) baştan hesaplar.

## Tezgâhla karşılaştırma

Simülasyonun gerçeğe ne kadar yakın olduğunu en iyi aynı düzeneği tezgâhta çalıştırıp ölçerek görürsünüz. Render penceresinin **Grafikler** sekmesinde **Tezgâh ölçümü içe aktar (CSV)** ile bir ölçüm kaydı açın, soldan karşılaştırılacak simülasyon kanalını (ör. servonun akımı) seçin ve **Seçili kanalla karşılaştır**'a basın.

RobotWin iki sinyali zamanda hizalar, ölçümü grafiğe turuncu çizer ve şunları yazar:

- **gecikme:** ölçüm kaydının simülasyona göre ne kadar kaydığı;
- **ölçüm ≈ k × sim + c:** gerçek parçanın modelden oransal farkı (örneğin klon servo 1,30×);
- RMSE, en büyük fark ve zamanı;
- **uyum:** farkın ölçüm aralığının %10'u içinde kaldığı zamanın oranı;
- **ilk ayrılma:** iki sinyalin 50 ms'den uzun süre ayrıldığı ilk an (örneğin kolun gerçekte bir yere takıldığı an).

Kayıt biçimi: ilk satır başlık, ilk sütun zaman. Başlıkta `ms` ya da `us` varsa birim ona göre alınır, yoksa saniye kabul edilir. Ayırıcı virgül, noktalı virgül ya da sekme olabilir; ondalık virgül de olur. Başlığında `mA` / `mV` olan sütunlar A / V kanallarıyla karşılaştırılırken otomatik çevrilir.

Örnek kayıt çizimi (Arduino + INA219, servonun beslemesine seri):

```cpp
#include <Wire.h>
#include <Adafruit_INA219.h>
#include <Servo.h>
Adafruit_INA219 ina;
Servo s;
void setup() {
  Serial.begin(115200);
  ina.begin();
  s.attach(9);
  Serial.println("t_ms,akim_mA");
}
void loop() {
  unsigned long t = millis();
  s.write((t / 1000) % 2 ? 135 : 45);   // simülasyondaki komutla aynı hareket
  Serial.print(t); Serial.print(','); Serial.println(ina.getCurrent_mA());
  delay(5);                             // 200 Hz
}
```

Seri monitör çıktısını `.csv` olarak kaydedip içe aktarın. Aynı çizimi simülasyondaki karta da yükleyin ve render alın.

## Fare ve klavye

| Fare | İşlev |
| --- | --- |
| Nesneyi sol tuşla sürükle | Taşı (takılı her şeyle birlikte) |
| Shift + sürükle | Yüksekliği değiştir |
| Alt + sürükle | 5 mm ızgarayı yok say |
| Boşlukta sol tuşla sürükle | Görünümü döndür |
| Sağ tuşla sürükle | Kaydır |
| Tekerlek | Yakınlaştır |
| Nesneye çift tık / boşluğa çift tık | Odaklan / üstten görünüm |

| Tuş | İşlev |
| --- | --- |
| K | Kablo çek |
| B | Bağla |
| R / Shift+R | 15° döndür / ters yönde döndür |
| Ctrl+D | Çoğalt |
| Del | Sil |
| Ctrl+Z / Ctrl+Y | Geri al / yinele |
| T | Kamera seçili nesneyi takip etsin |
| F | Seçime / her şeye sığdır |
| Esc | İptal, seçme aracına dön |

Nesneler yalnızca simülasyon dururken taşınabilir. Konumu ve açıyı denetçideki **Konum ve yön** alanlarına sayı yazarak da girebilirsiniz.

## Kaydetme

**Kaydet** (Ctrl+S) dünyayı `.rwworld` dosyası olarak saklar. İçindeki kartlar tam projeleriyle (devre, PCB, kod) birlikte kaydedilir; dosyayı başka bir bilgisayarda açtığınızda her şey hazırdır.

---

[← Önceki: 6. Montaj](06-montaj.md) · [İçindekiler](README.md) · [Sonraki: 8. Ölçü aletleri →](08-olcu-aletleri.md)
