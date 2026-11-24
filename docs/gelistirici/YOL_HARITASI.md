# Yol haritası ve iş listesi

Bu sayfa, bir sonraki büyük sürüme kadar yapılacak işlerin canlı listesidir. Sürüm, listedeki işler bitip çok aşamalı testler, diğer programlarla karşılaştırmalar ve akademik doğrulamalar tamamlanınca **tek bir büyük güncelleme** olarak yayınlanır. O zamana kadar değişiklikler `main` dalına işlenir ve CHANGELOG'un `[Unreleased]` bölümüne yazılır; ara sürüm çıkmaz.

İşaretler: **[x]** bitti · **[~]** kısmen var, derinleşecek · **[ ]** yapılacak.

## A. Doğrudan istekler

- [x] **Unity tarzı açılış ekranı:** uygulamadan önce açılan ayrı bir pencere; logo, sürüm ve gerçek yükleme adımları.
- [~] **Optimizasyon:** her değişiklikte `--bench` ile önce/sonra ölçümü.
  - [x] Açılış: ilk çizim 0,98 s'den 0,77 s'ye indi.
  - [x] Şablon açarken takılmalar: kablolar arka planda oturuyor; en uzun kare 1926 ms'den 190 ms'ye indi.
  - [ ] Ağır simülasyonlar: sıvı (36 FPS), sensörlü gezgin (~40), PS5 uçak (~48).
  - [ ] ReadyToRun ön derlemesi (crossgen2 paketi için izin gerekiyor).
- [~] **Ayarlar:** kategoriler ve arama.
  - [x] Yedekleme: sıklık, belge başına kopya, klasör, boyut sınırı, kaydedince önceki sürüm, çökme sonrası kurtarma.
  - [x] Klavye kısayollarını düzenleme: çakışma uyarısı, sıfırlama. Sayfaya özel kısayollar henüz sabit.
  - [x] Temalar: koyu, siyah, yüksek karşıtlık; vurgu rengi.
  - [ ] Açık tema: kodda sabit yazılmış renklerin taranması gerekiyor.
  - [x] Gelişmiş: ayarları dışa ve içe aktarma, tanılama.
  - [ ] Dil paketleri: önce altyapı, sonra İngilizce paket.
- [~] **Kinematik ve ters kinematik:** çekirdek var ve hazır kol hücrelerinde kullanılıyor (ileri/ters kinematik, Jacobian, tekillik, profiller). Eksikler:
  - kullanıcının kendi kurduğu kollar;
  - arayüz (eklem sürme, hedef poz, çalışma alanı, tekillik uyarısı);
  - mobil robot kinematiği;
  - PID ayar araçları.
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
  - [ ] Çözücü başına gerçek zamanlı tanılama paneli (CPU, bellek, adım, gerçek zaman oranı).
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
- [ ] İngilizce arayüz ve dil paketleri. Belgeler İngilizce de var; arayüz şimdilik Türkçe.

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
