# 11. Yenilikler

Her sürümde neyin geldiği, neyin değiştiği ve neyin düzeltildiği. Uygulama güncellendikten sonraki ilk açılışta başlangıç sayfası bu sürümün yeniliklerini gösterir; bu sayfaya her zaman **Yardım → 11. Yenilikler** ile ulaşabilirsiniz. Tüm teknik ayrıntılar (İngilizce): [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).

## 1.5.0 — 24 Kasım 2026

**Sol çubuk, Parça ve Montaj baştan: robotun bütün parçaları tek yerde, Montaj projeyi izliyor. ESP32'de Arduino kütüphaneleri, 32 katmanlı PCB.**

### Yenilikler

- **Sol çubuk üç grupta:** **ELEKTRONİK** (Devre, PCB, Kod), **MEKANİK** (Parça, Montaj), **TEST** (Dünya).
  - Başlangıç artık akışın bir adımı gibi durmuyor; alttaki araçlarda ve sol üstteki logoda.
  - Montaj'ın kendi simgesi var.
  - Genişletilince (Ctrl+B) her sayfa ne işe yaradığını ve durumunu yazar.
- **Modelle'nin yeni adı Parça; bir dosyada istediğiniz kadar parça olur.** En üstteki her nesne ayrı bir parçadır. **Montaja gönder** her parçayı ayrı bir gövde yapar:
  - Parçalar modellendikleri düzenle, var olanların yanına ve hiçbir şeye değmeden gelir.
  - Değiştirip yeniden gönderince yerinde güncellenir; kendi orijini ve bağlantıları korunur.
  - Silinen parçalar için montajdan kaldırılsın mı diye sorulur; iç içe duran ayrı parçalar için uyarı çıkar.
- **Montaj robotun kendisi:**
  - Kurarken hangi projelerin ve nasıl geleceği sorulur: **PCB'li** (lehimli kart + ayrı motor ve piller), **PCB'siz** (her parça ayrı) ya da **Otomatik**.
  - Birden çok proje aynı montajda yan yana durabilir; Parça sayfasındaki parçalar da gelebilir.
  - Projeyle birlikte kaydedilir (**Ctrl+S**).
  - Turuncu **MONTAJ** rozeti ve **Montajı çalıştır** düğmesiyle Dünya'dan ayrılır.
  - Ayrıntılar: [6. Montaj](06-montaj.md).
- **Montaj projeyi izler:** her açılışta devre, PCB ve kod değişiklikleri gelir.
  - Yeni parça eklenir, silinen kalkar, değişen parça ve kod yerinde güncellenir.
  - Parçaları koyduğunuz yerler, kurduğunuz bağlantılar ve kendi kablolarınız korunur.
- **Robot örnekleri Montaj'da gerçek araç:** Engelden kaçan ve çizgi izleyen robot gerçek parçalardan kurulur:
  - şase, motor millerine takılı tekerlekler, sarhoş teker;
  - şasenin üstünde birbirine değmeden kart, pil ve modüller;
  - kendi arenasında, kendi koduyla.
- **Denemek için ortam:** Montaj'ı atölye, çizgi pisti, engelli ya da rampalı alanda deneyin.
- **ESP32'de Arduino kütüphaneleri:** çizimin içerdiği kurulu kütüphaneler, kendi bağımlılıklarıyla birlikte çizimle derlenir. Kütüphaneler sekmesi ESP32 için de arar.
- **PCB'de 32 bakır katmana kadar** (KiCad'deki gibi). Kart ayarları penceresi, yığının sığması için en az gereken kalınlığı yazar.
- **Çizgi izleyen örnek gerçek optik TCRT5000 modülleri kullanır** (kızılötesi LED, fototransistör, LM393). Montajında gerçek tekerleklerle oval pisti izler: 8 saniyede bir tur, en çok 22 mm sapma.
- **Montaj açıkken soldaki Dünya** artık bir test dünyasına geçer; Montaj'ı yeniden göstermez.

### Düzeltmeler

- **Başsız çalıştırmada ve render'da sensörler kördü:** dünyanın sensörleri (çizgi modülleri, kamera, LiDAR, GPS, enkoder) yalnız devrede bir IMU varken kuruluyordu. Bu yüzden bir çizgi izleyenin render'ı hiçbir şey görmüyordu. Artık uygulamadaki gibi kurulurlar.
- **Arenadan kurulan dünyada başlangıç ve zemin kayboluyordu:** oval pistin başlangıcı (alt düzlük) düşüyordu ve zemin optik sensörlere beyaz yerine koyu gri görünüyordu.

- **Montaj projenin tek seferlik kopyasıydı:**
  - Sonraki devre ve kod değişiklikleri ona hiç gitmiyordu.
  - Proje yeniden adlandırılınca boş bir montaj kuruluyordu.
  - Aynı adlı iki proje tek montajı paylaşıyordu.
  - Uygulama kapanınca montaj işi kayboluyordu.
- **Parça sayfasından gönderince Dünya sayfasına atlanıyordu.** Parçalar devrenin kartının üstüne düşüyordu ve iç içe başlıyordu.
- **Kod sayfası Montaj'daki kartı ayrı bir kart gibi gösteriyordu.** Montaj'da "Kartı düzenle" ise bir kopyayı açıyordu.
- **Robot örnekleri Montaj'da dağınık parçalarla açılıyordu.** Başlangıç sayfası da var olmayan bir "Başlat" düğmesini anlatıyordu.
- **Türkçe Windows'ta MSVC iletileri bozuk karakterle okunuyordu** ("i�erme dosyas�").
- **`src/` altında aynı adlı iki `.cpp` birbirinin çıktısını eziyordu.**
- **Kart ayarları penceresi küçük ekranlarda ekrandan taşabiliyordu.**
- **Uzun durum yazıları sağdaki sayıların üstüne taşıyordu.**

## 1.4.2 — 24 Kasım 2026

**Kod sayfası bir kod editörü gibi çalışır: klasörler, her karta yükleme, gerçek başlıklardan tamamlama ve Arduino kütüphaneleri. Ayarlanabilir AC/DC güç kaynağı ve kategorili parça kütüphanesi.**

### Yenilikler

- **Kod sayfasında gezgin:**
  - **KARTLAR** bölümünde projedeki ve açık dünyadaki her programlanabilir kart kendi kod klasörüyle durur.
  - **KOD KLASÖRLERİM** bölümünde diskteki klasörler vardır: `Belgeler\RobotWin\Kod` ve diskten açtığınız her klasör (ör. Arduino IDE çizim klasörü).
  - Klasörlere dosya ve alt klasör (`src/`) eklenir; dosya ya da klasör içe aktarılır (sürükle-bırak da olur).
  - Klasör istediğiniz yere Arduino çizim klasörü olarak kaydedilir, zip olarak dışa aktarılır, yeniden adlandırılır, silinir.
- **Kod her karta yüklenir:** **Kart** listesinde projedeki ve açık dünyadaki her Arduino Uno, Nano ve ESP32 var. **Karta yükle ve çalıştır**, klasördeki kodu karta kopyalar, derler ve projeyi ya da dünyayı çalıştırır.
- **Çok dosyalı çizimler:** ana `.ino`, yanındaki `.h` / `.cpp` dosyaları ve `src/` alt klasörleri Arduino IDE'deki gibi birlikte derlenir. Bu Uno / Nano'da da ESP32'de de geçerlidir.
- **Kod tamamlama** kartın gerçek başlık dosyalarından gelir: `.`, `->`, `::` ve `#include <` yazınca ya da **Ctrl+Space** ile açılır. Değişkenin türünü bilir: `kol.` yazınca `Servo` üyeleri listelenir. Sağdaki panel imzayı ve açıklamayı gösterir. Parantez açınca parametreler satırın üstünde görünür.
- **Kütüphaneler sekmesi:** yaklaşık 10 000 Arduino kütüphanesinde arama yapar. **Kur ve ekle** kütüphaneyi kurar ve `#include` satırını ekler; kurulu kütüphaneler kaldırılabilir. Kurulu olmayan bir başlık işaretlenir ve onu sağlayan kütüphane önerilir.
- **Ayarlanabilir AC/DC güç kaynağı:** DC (CV/CC), AC (RMS gerilim, frekans, faz) ya da DC üstünde AC verir. AC'de akım sınırı iki yönde de geçerlidir ve rapor gerçek RMS değerleri verir. Hazır ayarlar: 12 V~ 50 Hz, 24 V~ 60 Hz, 230 V~ şebeke, 12 V DC + 1 V dalgalanma. Ayrıntılar: [8. Ölçü aletleri](08-olcu-aletleri.md#ayarlanabilir-acdc-güç-kaynağı).
- **Kategorili parça kütüphanesi:** her parçanın bir ana kategorisi ve bir alt kategorisi var (ör. Pasif → Direnç, Kondansatör; Ölçüm & Kaynak → Güç kaynağı, Sinyal jeneratörü), parça sayılarıyla. **Kütüphane** sayfasında her parçanın bir **Özellikler** tablosu var.

### Düzeltmeler

- **Servo kol şablonunda ikinci kol kendi ekseni etrafında dönüyordu:** yazılım dirseği bütün hareket boyunca omuzun tersine sürüyordu. Artık dirsek omuzu yarım açıyla ve 2°'lik adımlarla izler.
- **Modül kartlarındaki pin yazıları yanlış pinleri gösterebiliyordu:** kart baskıları pin isimleri olmadan önbelleğe alınıyordu. Bu yüzden pin sırası farklı bir modül (ya da pin eşlemesi değiştirilmiş bir modül) öncekinin VCC / GND yazılarını başka sinyal taşıyan pinlerin üstünde gösterebiliyordu.
- **HC-SR04'ün ön yüzündeki yazı pinlere göre tersti:** önden bakınca "Vcc" GND pininin üstündeydi.
- **Basit dünya modelleri:** servo kablosu üç renkli (kahverengi GND, kırmızı V+, turuncu SIG); motorların A / B uçları kırmızı / siyah.

## 1.4.1 — 24 Kasım 2026

**Render'daki hava gerçek hava; kablolar render'da simülasyondaki gibi; Devre'nin alt panelleri taşınır, kapanır, ayrı pencereye alınır.**

### Değişenler

- **Render'daki hava gerçek hava:** kaydın tamamı boyunca zamanla çözülür. Parçalar kayıttaki gibi hareket eder ve havayı kendi yüzey hızlarıyla iter; dünyanın rüzgârı kutunun yüzlerinden girer; zemin duvardır. Rüzgâr yoksa ve hiçbir şey kıpırdamıyorsa akış da yoktur. Eskiden duran montaja 2 m/s'lik bir "test hızı" üfleniyordu.
- **Hava 3B duman olarak görünür:** havayla taşınan parçacıklar hız yönünde kısa izler bırakır (mavi yavaş, kırmızı hızlı). Düz kesit düzlemi kalktı.
- **Rüzgâr tüneli testi** ayrı bir seçenek: hızı, yönü (0° = +X, 90° = +Y) ve eğimi seçilir.
- **Servoların, motorların ve kartların gerilmesi:** render listesinde artık bileşenler de var. Üstüne bir şey takılı olanlar (ör. ikinci servoyu taşıyan servo) varsayılan olarak işaretli.
- **Devre'nin alt sekmeleri** (osiloskop, multimetre, arıza tespiti, Monte Carlo, kart pinleri, seri monitör, olaylar) Dünya'nın panelleri gibi çalışır:
  - üst kenardan sürüklenerek boyutlandırılır;
  - kapatılır, çizimin yanına yerleştirilir ya da ayrı pencereye alınır;
  - şeridin sağındaki düğmeler seçili sekmeyi ayrı pencereye alır, yerini seçtirir ya da kapatır; bölmeyi gizler.

  Kapatılanlar **Görünüm → Devre panelleri** ile geri açılır.
- **Sol ray:** projenin aşamaları (Devre → PCB → Kod → Modelle → Montaj) aynı çizgide. Dünya ayrı bir belge olduğu için bir ayraçla altta duruyor.
- **Montaj kendini anlatır:** projenin devresinde parça yoksa boş bir dünya açmak yerine ne yaptığını söyler.
- **Dünyadaki kartlar Devre'den açılır:** Devre sayfası boşken açık dünyanın Arduino / ESP32 kartlarını listeler. Tek tıkla kartın devresi ve kodu açılır; şablondaki hazır program düzenlenebilir koda dönüşür.
- **Servo başlığına / motor miline tak:** "…ne tak" parçanın yüzünü çıkışa yatırır; kol gibi uzun bir parçanın ucu eksenin üstüne gelir ve kol dışarı uzanır. "Olduğu yerde sabitle" eski davranıştır.

### Düzeltmeler

- **Render'da kablolar uzun izler halinde uzuyordu:** editördeki kablo düzeninden çiziliyorlardı. Render kabloları ip olarak hiç kurmuyordu da. Artık canlı dünyadaki iplerin aynısı simüle edilir, kaydedilir ve oynatılır.
- **Render sahnesinde parçalar ve kablolar sürüklenebiliyordu;** render artık yalnızca izlenir.
- **Kaydedilen render'da hava akışı kayboluyordu;** artık kablolarla birlikte dosyaya yazılır.
- **Render'daki kanal aramasında Boşluk ve ok tuşları yazmıyordu.**
- **Servo gövdesinde gerilme 10¹³ MPa çıkıyordu:** eğri yüzeye taşınan ara düğümler bazı elemanları neredeyse katlıyordu. Artık bozulacak eğri elemanlar düz kalır. Ağ, yüzeyle bağlı olmayan ayrık parçacıkları da dışarıda bırakır.
- **Kayıtlı render açılınca soldaki ayarlar** pencerenin varsayılanlarını gösteriyordu; artık render'ın kendi ayarlarını gösterir.
- **Boş devre sayfasında başıboş prob rozetleri ve ölçü aleti kabloları** görünüyordu.

## 1.4.0 — 23 Kasım 2026

**Sıvılar gerçek fizikle akar; akış analizi gerçek Reynolds sayısında; her çözücünün teoriden sapması bir sayfada.**

### Yenilikler

- **Sıvılar:** su, deniz suyu, alkol, süt, ayçiçek yağı, gliserin ve bal parçacıklarla akar. Her birinin gerçek yoğunluğu, viskozitesi ve yüzey gerilimi kullanılır. Sıvı kabın duvarlarına gerçek basıncını uygular; kap suyun ağırlığını taşır, suya bırakılan bir parça kaldırma kuvveti görür. Su malzemeye göre ıslatır: camı çok iyi, PLA'yı daha az, PTFE'yi hiç. Havadaki damlacıklar dünyanın havasında sürüklenir.
- **Cam ve kaplar:** **CAM** malzemesi saydam çizilir; su bardağın içinden görünür. Silindire **tube.wall** ile et kalınlığı, **tube.bottom** ile taban verilince bardak ya da kâse olur. **Mekanik → Sıvılar** bölümünden su dolu ya da boş bir bardak ve su hacmi eklenir.
- **Su bardağı şablonu:** ESP32, yan yatırılmış bir SG90'ın kolundaki cam bardağı 90° devirir; 193 ml su kâseye dökülür.
- **Su gibi görünen su:** yüzey parçacıklardan kesintisiz kurulur. İnce tabaka ve düşen akış sürekli görünür. Fresnel yansıması (dik bakınca saydam, yatık bakınca gökyüzü), güneş parlaması ve 1/60 s pozlamalı damla izi var.
- **Render her şeyi tam hesaplar:**
  - sıvı doğruluk ayarının fizik adımında (Hassas'ta 0,1 ms) çözülür;
  - basınç, sıkışma %0,2'nin altına inene kadar yinelenir;
  - 40 000 parçacığa kadar (Referans'ta 120 000) kullanılır;
  - sıvı kaydı daha ince bir yüzeyle oynatılır.
- **Kayıtlı render'ı aç:** render penceresi bir `.rwanalysis` dosyasını yeniden hesaplamadan oynatır.
- **Akış analizi gerçek Reynolds sayısında:** düzenlenmiş çarpışma ve LES ile, eğri duvarlar gerçek konumlarında. Her sonuç, ne kadar güvenilir olduğunu yazar: ızgara belirsizliği (GCI), sınır tabaka (y+) ve tıkanma oranı.
- **Doğrulama sayfası:** **Yardım → 12. Doğrulama** ve README, her çözücünün teoriden ve kıyaslama problemlerinden sapmasını ve hata payını gösterir. Örneğin Schäfer–Turek silindiri %0,8, hidrostatik su basıncı %2,1, baraj yıkılması deneyi %4–8.

### Düzeltmeler

- **ESP32 kartlar render'da boşta kalıyordu:** render'ın çalıştırıcısında çizim derleyicisi yoktu. Artık render canlı dünyadaki yazılımın aynısını çalıştırır.
- **Küre sürüklemesi %30 yüksekti** (Re 20); şimdi standart eğrinin %8–11 içinde.
- **Kanal girişi debinin %93'ünü veriyordu;** giriş artık tam debiyi veren bir hız sınırı.
- **Yardım'daki çok sütunlu tablolar** son sütunu tek harf genişliğine eziyordu.

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
