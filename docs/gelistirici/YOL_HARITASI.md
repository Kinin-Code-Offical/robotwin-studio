# Yol haritası ve iş listesi

Bu sayfa, bir sonraki büyük sürüme kadar yapılacak işlerin canlı listesidir. Sürüm, listedeki işler bitip çok aşamalı testler, diğer programlarla karşılaştırmalar ve akademik doğrulamalar tamamlanınca **tek bir büyük güncelleme** olarak yayınlanır. O zamana kadar değişiklikler `main` dalına işlenir ve CHANGELOG'un `[Unreleased]` bölümüne yazılır; ara sürüm çıkmaz.

İşaretler: **[x]** bitti · **[~]** kısmen var, derinleşecek · **[ ]** yapılacak.

## A. Doğrudan istekler

- [x] **Unity tarzı açılış ekranı:** uygulamadan önce açılan ayrı bir pencere; logo, sürüm ve gerçek yükleme adımları.
- [~] **Optimizasyon:** her değişiklikte `--bench` ile önce/sonra ölçümü.
  - [x] Açılış: ilk çizim 0,98 s'den 0,77 s'ye indi.
  - [x] Şablon açarken takılmalar: kablolar arka planda oturuyor; en uzun kare 1926 ms'den 190 ms'ye indi.
  - [~] Ağır simülasyonlar:
    - [x] Çalıştır'a basınca donma: kablolar paralel oturuyor ve yol buluyor (PS5 uçak 8,1 s → 0,85 s, gezgin 0,82 → 0,30 s).
    - [x] Sıvı yüzeyi arka planda örülüyor (su bardağı 31 → 43 FPS).
    - [x] Sensörlü gezgin 61 FPS.
    - [~] Sıvı çözücüsünün kendisi: geçişler birleşti (alt adım başına 19 yerine 12 paralel döngü), basınç düzeltmesi yoğunluk geçişinin çekirdek eğimini yeniden kullanıyor, duvar testi cismin kendi çerçevesindeki kutusuyla eleniyor. Su bardağı dökmesinin tamamı 28,2 s'den 20,2 s'ye indi (11 s simülasyon); dökme anında 1 ms'lik adım 4,1 ms'den 2,9 ms'ye. Açılışta dinlendirme 0,9 s'den 0,7 s'ye. Kalanlar:
      - [ ] Dinlendirmenin 2 mm/s hız ölçütü hiç sağlanmıyor (g·h titreşimi en hızlı parçacığı ~24 mm/s'de tutuyor), her seferinde 0,4 s'nin tamamı çalışıyor: yer değişimine göre ölçülmeli.
      - [x] Kâseye dökülen su, yüzeyindeki akıntılar (dökmeden 5 s sonra 15–40 mm/s) sönünce, dökmeden ~8 s sonra uyuyor; gerçek su da bir süre döner.
    - [x] Kabın iç köşesindeki parçacıklar durgun suda 30–40 mm/s titriyordu: alanın ara değerlenmiş mesafesi köşede kısa kalıyordu. Temas artık gerçek katıyla. Durgunluk da hıza göre değil 0,2 s'deki yer değişimine göre ölçülüyor (konum tabanlı sıvı yerinde g·h² kadar titrer). Serbest bir tank suyuyla 2,6 s'de uyuyor; servonun tuttuğu kap sıvıyı mikrometrelik kıpırtısıyla uyandırmıyor.
  - [ ] ReadyToRun ön derlemesi (crossgen2 paketi için izin gerekiyor).
- [~] **Ayarlar:** kategoriler ve arama.
  - [x] Yedekleme: sıklık, belge başına kopya, klasör, boyut sınırı, kaydedince önceki sürüm, çökme sonrası kurtarma.
  - [x] Klavye kısayollarını düzenleme: çakışma uyarısı, sıfırlama; bütün sayfaların tuşları da (Shell/PageShortcuts.cs: Devre, PCB, Dünya, Kod, Parça).
  - [x] Temalar: koyu, siyah, yüksek karşıtlık; vurgu rengi.
  - [x] Çok sayıda tema: 9 koyu, 7 açık (pembe, şeftali, lavanta, nane, gökyüzü, kâğıt, açık), özel tema (ton, doygunluk, açık/koyu), 10 vurgu rengi; şema ve kod editörü açık temalarda açılıyor.
  - [x] PCB editörü de açık temalarda açılıyor (soluk kart, koyu baskı; bakır renkleri aynı).
  - [x] Gelişmiş: ayarları dışa ve içe aktarma, tanılama.
  - [x] Dil paketleri: çalışma zamanında çeviren altyapı (Shell/Lang), yerleşik İngilizce paket, kullanıcının kendi paketleri; İngilizce artık varsayılan dil.
    - [x] Yeni diller İngilizceye düşer; paketin kültürü (sayı/tarih biçimi); iki dilli arama; dile göre kılavuz ve haberler; örneklerin adları ve kod yorumları.
    - [x] Paket, kaynaktaki bütün metinlerden (C# sözcük ayrıştırıcısı) ve çalışan uygulamanın gösterdiklerinden (`--lang-missing`) çıkarılıyor.
    - [x] İngilizce kılavuz arayüzü İngilizce etiketleriyle anlatıyor ve İngilizce görüntüleri gösteriyor (`docs/images/en/`); her görüntünün nasıl çekildiği `docs/images/shots.json`'da, `tools/scripts/doc_images.py` iki dilde de üretiyor.
    - [ ] Yerleşik Uno örneklerinin (engelden kaçan, çizgi izleyen, servo tarama…) seri mesajları önceden derlenmiş `.hex` içinde ve Türkçe; İngilizce mesajlarla yeniden derlenmeli (testlerdeki beklenen çıktılar da güncellenmeli).
  - [ ] İngilizce paketin kapsamı: canlı ölçüm satırları, olay günlüğü ve CoreSim mesajları henüz Türkçe; kalıplar genişletilmeli.
- [~] **Kinematik ve ters kinematik:** çekirdek var ve hazır kol hücrelerinde kullanılıyor (ileri/ters kinematik, Jacobian, tekillik, profiller).
  - [x] Kullanıcının kendi kurduğu kollar (Robotics/WorldArm.cs): zincir, servo yükü, erişim, fizikle karşılaştırma.
  - [x] Arayüz: hedef nokta (yazarak ya da sahnede), önizleme, servoları sürme, açıları koda kopyalama, erişim ve tekillik uyarısı.
  - [x] Mobil robot kinematiği (Robotics/WorldVehicle.cs): diferansiyel, kızaklı, mecanum, omni; ters ve ileri kinematik, kayma, enkoder odometrisi ve sapma, yolların çizimi.
  - [x] Fizik düzeltmesi: lastik tabanı tek temas izi (dönüşler kinematikle %0,5 içinde).
  - [x] PID ayar araçları: motor hız döngüsü için adım testi, model, dört kural, gerçek fizikte deneme, Arduino kodu (Robotics/PidTuning.cs, MotorBench.cs). Konum döngüsü ve servo için ayarlayıcı henüz yok.
- [~] **UX:** kategorilere ayırma, sayfalar ve pencereler, daha zengin sağ tık menüleri, özel mantıklarda yardımcılar. Her yeni iş bu gözle tasarlanır.
- [x] Tek saat ve ortak hata ayıklama (kesme noktası, adım, değişkenler; devre ve dünya aynı anda durur).
- [x] Üstteki sekmeler: kaydırma, sabitleme, tam liste, dosya adları.
- [x] İlk açılış rehberi (İlk robotun).
- [x] Arduino derleyicisi kurulumla gelir.

## B. Mühendislik alanları (16 alanlık plandan)

### P0: temel

- [~] **Proje bütünlüğü:**
  - [x] Montaj projeyi izler.
  - [ ] Değişiklik etkisi önizlemesi (devre → PCB → montaj).
  - [ ] Kalıcı bileşen kimlikleri.
  - [ ] Proje sürümleri ve geri dönüş.
  - [ ] Eksik kütüphane / model / derleyici denetimi.
  - [ ] Şema–PCB–montaj tutarsızlık raporu.
  - [ ] Tasarım alternatifleri.
- [~] **Fizik doğrulama:**
  - [x] Referans testleri var.
  - [ ] Enerji ve momentum korunumu.
  - [ ] Zaman adımı yakınsaması.
  - [ ] Temas ve eklem kararlılığı.
  - [ ] Elektrik–mekanik–termal enerji bilançosu.
  - [ ] Sayısal belirsizlik raporu.
  - [ ] Model geçerlilik aralığı.
- [~] **Performans ve kararlılık:**
  - [x] Kare profili.
  - [x] `--bench`.
  - [x] Arayüz nefes payı.
  - [x] Çözücü başına gerçek zamanlı tanılama: performans göstergesi (Ctrl+Shift+F) çalışan dünyanın hızını, her çözücünün (devre, Arduino/ESP32 yazılımı, katı cisimler, sıvılar, kablolar, ısı, sürücüler, hava ve gaz, betikler, sensörler) simülasyon saniyesi başına maliyetini ve payını, fizik adımını, sıvının parçacık ve alt adım sayısını, belleği gösteriyor.
  - [ ] Ağır çözücüleri arayüz iş parçacığından ayırma.
  - [ ] Geometri, ağ ve çözüm önbellekleri.
  - [ ] LOD.
  - [ ] Uzun koşu bellek testleri.
  - [ ] Otomatik performans gerileme ölçümü.

### P1: öncelikli

- [~] **Parametrik 3B tasarım:** modelleyici, CSG ve montaja gönderme var. Eksikler:
  - ölçülü eskiz ve kısıtlar;
  - parametrik ölçüler;
  - montaj ilişkileri;
  - dişli, kayış, yay, rulman, kızak, vidalı mil;
  - 3B baskı uygunluğu;
  - STEP.
- [~] **Devre simülasyonu:** çözücü, ERC, Monte Carlo ve hasar var. Eksikler:
  - üretici SPICE modelleri;
  - AC ve frekans cevabı;
  - anahtarlamalı güç;
  - besleme açılışı;
  - osiloskopta matematik kanalları;
  - model sınırlarını gösterme.
- [~] **PCB ve üretim:** 32 katman, DRC, Gerber, BOM ve KiCad dışa aktarma var. Eksikler:
  - KiCad içe aktarma ve fark;
  - diferansiyel çift ve uzunluk eşleme;
  - DFM ve DFT;
  - tedarikçili BOM;
  - kutu ile çakışma;
  - Gerber'in ikinci bir görüntüleyiciyle karşılaştırılması.
- [~] **Gömülü yazılım:** Uno/Nano AVR emülatörü ve hata ayıklayıcı var. Eksikler:
  - register ve zaman çizelgesi;
  - protokol analizörleri ve hata enjeksiyonu;
  - ESP32 gerçek firmware emülatörü;
  - RP2040 / STM32;
  - kütüphane uyumluluk matrisi;
  - derleyici sürümünün projeyle saklanması.
- [~] **Robotik kontrol:** yukarıdaki kinematik maddesi, ayrıca:
  - yörünge sınırları (tork, jerk);
  - yol boyunca çarpışma;
  - yük kapasitesi ve sapma;
  - çevrim süresi;
  - ROS 2 / MoveIt uyumu.
- [~] **Sensörler:** modeller var. Eksikler:
  - her sensörde menzil, çözünürlük, gecikme ve gürültü modeli;
  - sıcaklık ve kayma;
  - arıza senaryoları;
  - füzyon deneyleri;
  - veri dışa aktarma ve etiketleme.
- [ ] **Akademik deney araçları:**
  - deney oluşturucu;
  - parametre taraması;
  - toplu ve başsız koşu;
  - Python ve Jupyter;
  - güven aralıkları;
  - otomatik deney raporu;
  - tohum ve ayarların saklanması.
- [~] **Endüstriyel otomasyon:** konveyörlü hücre var. Eksikler:
  - fotosel ve sınır anahtarı;
  - pnömatik;
  - istasyon durum makineleri;
  - çevrim süresi ve darboğaz;
  - arıza senaryoları;
  - PLC (ST / LD);
  - Modbus, OPC UA, HMI.
- [~] **Dış biçimler:**
  - [x] KiCad dışa aktarma.
  - [~] KiCad içe aktarma.
  - [ ] STEP.
  - [ ] URDF / Xacro.
  - [ ] ROS 2.
  - [~] Python API.
  - [ ] Modbus.
  - [ ] OPC UA.
  - [ ] FMI / FMU.

### P2: sonraki aşama

- [ ] Hidrolik, esnek yapılar ve yorulma.
- [ ] Gelişmiş motor ve rulman ısı modelleri.
- [ ] FEM ağ ve CFD ızgara yakınsaması.
- [ ] Yeni MCU aileleri.
- [x] İngilizce arayüz ve dil paketleri (varsayılan dil İngilizce).

## C. Büyük sürümden önce

- [ ] Çok aşamalı test ve hata avı: birim, uçtan uca, uzun koşu, bellek.
- [ ] Diğer programlarla karşılaştırma (yalnız taşınabilir sürümler; her indirme için ayrıca izin):
  - fizik: MuJoCo, PyBullet;
  - devre: ngspice;
  - PCB: KiCad;
  - robotik: CoppeliaSim / Gazebo.
- [ ] Akademik doğrulama tabloları: aynı senaryo, aynı sonuç, farklar ve nedenleri.
- [ ] Başka uygulamaların arayüz ve kullanım incelemesi: karmaşıklığı önleme fikirleri.
- [ ] Belgeler, ekran görüntüleri, tanıtım GIF ve videoları.
