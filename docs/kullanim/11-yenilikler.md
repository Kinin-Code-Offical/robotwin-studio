# 11. Yenilikler

Her sürümde neyin geldiği, neyin değiştiği ve neyin düzeltildiği. Uygulama güncellendikten sonraki ilk açılışta başlangıç sayfası bu sürümün yeniliklerini gösterir; bu sayfaya her zaman **Yardım → 11. Yenilikler** ile ulaşabilirsiniz. Tüm teknik ayrıntılar (İngilizce): [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).

## 1.3.0 — 23 Kasım 2026

**Kablolar gerçek bir telin gidebildiği yerden gider; birbirine bağlı parçalar artık iç içe geçmez; PS5 kumandalı uçak; roket ateşlemesi gerçekteki zinciriyle.**

### Yenilikler

- **Kablo yolu:** Her kablo, iki fişi arasındaki boşlukta katı parçalara değmeyen en kısa yoldan döşenir. Kapalı bir gövdenin içindeki kablo içeride kalır; dışarı çıkması gereken kablo gerçek bir delikten çıkar. Otomatik kablo boyu bu yolun %10 fazlası artı 15 mm'dir.
- **Gerçek kablo delikleri:** Kabukta "Kablo deliği" işaretli bir duvar, deliğin çevresindeki çerçevedir; tel ve itme çubuğu yalnızca açıklıktan geçer. Deliğin boyu, yüksekliği ve yatay konumu her yüz için ayrı ayarlanır.
- **Tünelleme yok:** Hızlı hareket eden bir tel ince bir duvarı tek adımda atlayamaz; deliğin kenarına bastırılan tel duvarın içinden değil, deliğe geri kayar.
- **Tutarlılık denetimi:** Birbirine bağlı ama iç içe geçmiş parçalar (örneğin pin uçları tabana gömülü bir kart), katı bir parçanın içinden geçen teller, başka bir parçanın içinde kalan fişler ve uçları arasında yol olmayan teller simülasyon başlarken **Olaylar** sekmesinde raporlanır.
- **İçi boş borular:** Silindire boru et kalınlığı verilebilir; roket gövde borusunun içinde motor merkezleme halkalarıyla durur.
- **PS5 kumandalı uçak:** 1 m'lik eğitim uçağı, model uçak sahasında pistten kalkar. DualSense ile uçar; dengeleme, menzil dışında arıza modu (failsafe) ve takip kamerası vardır.
- **Canlı girişler render'da tekrar oynatılır:** Kayıt sırasında kumanda, seri port, USB, IR, konveyör ve anahtar olayları zamanlarıyla saklanır; analiz aynı girişlerle baştan hesaplanır.
- **Kaynaklı parçalar tek rijit gövde:** Kırılmaz sabit bağlantılarla birleşen parçalar tek gövde olarak çözülür; hafif bir parçadan geçen zincir artık esnemez, kaynakların taşıdığı yük yine ölçülür.
- **Roket ateşlemesi gerçekteki gibi:** e-kibrit ateşleyici barutu yakar, yakıt yüzeyi ısınıp tutuşur, alev lüleye doğru yayılır; aşındırıcı yanma, gerçek gaz, lüle akış ayrılması, motor gövdesinin ısınması, geri sayımlı fırlatma anahtarı ve paraşüt.
- **Vida ve yapıştırıcı gerçek fizikle:** sabit bağlantıya M3 vida, yapıştırıcı, köpük bant, kelepçe ya da lehim seçilir; kopma kuvveti ve ısı iletimi buna göre hesaplanır.
- **Malzemeler, renkler ve yüzeyler:** malzeme listesi fiziğin bildiği her malzemeyi sayılarıyla gösterir; renk seçimi ve yüzey işlemi.
- **Sade denetçi:** sağ panel yalnızca nesnenin kullandığı bölümleri gösterir; basınçlı kap, paraşüt, hava ayarı ve davranışlar açıksa görünür (**Kaldır** ile), diğerleri **Özellik ekle ▾** menüsündedir.
- **Taşınabilir paneller:** palet, denetçi ve alt sekmeler sola, sağa, alta yerleşebilir ya da ayrı pencere olur.
- **Başsız çalıştırma kablo fiziği:** dışa aktarılan geometri dosyası fiş konumlarını da içerir; başsız koşucu uygulamadaki aynı kabloları simüle eder.
- **Yeni şablonlar:** Kapalı gövdeli gezgin (kablo delikleri, ESP32 kare çizer), Trafik ışığı ve yaya butonu (ESP32), Sarkaç (periyodu teoriyle %0,1 içinde uyuşur), PS5 kumandalı uçak, Katı yakıtlı roket.

### Düzeltmeler

- **Teller duvarlardan geçiyordu:** PS5 uçağında 9 tel kabinin yan duvarlarından dışarı çıkıyordu; şimdi pistten kalkıştan inişe hiçbiri bir duvardan geçmiyor.
- **Teller hareket eden parçaların bir adım gerisinde kalıyordu:** 12 m/s'deki bir uçağın içinde 12 mm. Teller artık gövdelerden sonra hesaplanır.
- **Teller dünyaya göre sönümleniyordu:** uçuşta tellere yaklaşık 4 g'lik sahte bir geri itme demekti. Sönüm artık telin uçlarına göre, hava sürüklemesi telin çevresindeki havaya göre hesaplanır.
- **Roket kanatçıkları gövdeye teğet duruyordu** (fırıldak gibi); artık gövdeden dışarı doğru, radyal duruyor.
- **Şablonlarda iç içe geçmiş parçalar:** pin uçları tabana gömülü kartlar, servonun içindeki pil, havada duran servo, birbirinin içinden geçen X gövde kolları, Uno'nun ve pilin içindeki LiDAR direği, kuyruğa giren itme çubukları düzeltildi.
- **Uçağın servo fişleri gövdenin dışında kalıyordu:** kabin fişleri içine alacak kadar uzatıldı; kuyruk borusu ve itme çubukları arka duvardaki 16 mm'lik delikten, motor kabloları motorun yanındaki 7 mm'lik delikten geçer.
- **İleri uçuşta pervane itkisi** artık ilerleme oranına göre azalır; duran pervane katı bir disk gibi sürüklenmez.
- **Uygulama ile başsız koşucu arasında parça kütleleri farklıydı;** ikisi de artık veri sayfası kütlelerini kullanır.
- **Oyun görünümü araziyi çizmiyordu;** artık çim, pist ve yollar gökyüzüyle birlikte görünür.

## 1.2.0 — 22 Kasım 2026

**Modelle'de kesme araçları; bütün mesajlar tek dilde.**

- **Bıçak (K):** çizilen çizgi boyunca keser; seçili yüzleri ya da bir tarafı silerek. **Dilimle:** nesneyi iki kapalı parçaya ayırır.
- **Fark, birleşim, kesişim** kendi bölümlerinde; **halka kesim (Ctrl+R)**, **pah kır (Ctrl+B)**, **kalınlık ver**, **kenar ekstrüzyonu**.
- **Seçim:** kenar ve yüz halkaları, bağlı parça (L), seçimi büyütme ve küçültme.
- Kesme işlemlerinden sonra parçalar kapalı kalır, hacim korunur.
- KiCad içe aktarma, çözücü, ESP32 ve API mesajları yalnızca Türkçe.

## 1.1.0 — 21 Kasım 2026

**Tek ortak fizik: devre, katı cisimler, ısı, hava, gaz ve radyo birbirini etkiler.**

- **Gerçek Montaj:** montaj devrenin kendisinden kurulur; ağlar gerçek uzunlukta jumper kablolara dönüşür.
- **Modelin şeklinde katı cisimler**, **uçuş** (kanat, kuyruk, kanatçık, perdövites), **gaz fiziği ve roketler** (su roketi, katı yakıt), **ısı ağı** ve ısı kamerası.
- **Analiz ve render:** FEM gerilme, 2B CFD, 3B akım çizgileri, kaydedilen koşuyu her parçadaki gerilmeyle yeniden oynatma.
- **Endüstriyel robot hücresi**, **sensörler ve kameralar** (çizgi, LiDAR, IMU, GPS), **arazi düzenleyici**.
- **Otomasyon API ve SDK:** Python/Gymnasium, ROS 2 köprüsü, başsız çalıştırma.
- **Uygulama içi Yardım (F1), Haberler ve otomatik güncelleme.**

## 1.0.0 — 20 Kasım 2026

**İlk genel sürüm:** kartlarınızı tasarlayın, dünyaya koyun, kablolayın ve hepsini birlikte gerçek fizikle simüle edin. Tasarımdan gerçeğe hattı (Devre → PCB → Kod → Modelle → Montaj → Dünya), KiCad düzeyinde şema editörü, PCB, Arduino ve ESP32 emülasyonu, Blender benzeri modelleyici, açık dünya, radyo ve kumandalar, ölçü aletleri.
