# 7. Dünya

**Dünya** sayfası (Ctrl+6) her şeyin bir araya geldiği yerdir. Tasarladığınız kartları, katalogdaki parçaları, mekanik gövdeleri ve 3D modelleri gerçek fizikli bir ortama koyar, kablolarla bağlar, vidalar veya menteşelerle birleştirir ve hepsini birlikte çalıştırırsınız. Devre, kartların yazılımı, radyo ve fizik aynı anda, adım adım hesaplanır.

![Arduino ile iki eksenli servo kol](../images/world-servo-arm.png)

## Denetçi: nesnenin özellikleri

Sağdaki **Denetçi** seçili nesnenin yalnızca gerçekten kullandığı bölümleri gösterir: önce ad, konum ve yön, fizik (sabitlik, malzeme, kütle), boyut ve görünüş; sonra nesnenin türüne özgü bölümler (kart, bileşen, kanat bağlantısı, kabuk, anahtar, kumanda). Bir gövdeye eklenebilen ek fizikler (**Kabuk**, **Basınçlı kap**, **Kurtarma paraşütü**, **Hava (özel ayar)**, **Davranış**) yalnızca açıksa görünür ve başlıklarında **Kaldır** vardır; diğerleri **Özellikler → Özellik ekle ▾** menüsündedir.

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
| Arduino ile step motorlu Z ekseni (vidalı mil) | Uno, A4988 ile NEMA 17'yi döndürür; T8 vidalı mil taşıyıcıyı rampalı hızla 40 mm kaldırıp indirir |
| Arduino, LCD ve OLED (I²C kablolu) | Uno, 16×2 LCD, 128×64 OLED ve potansiyometre I²C kablolarıyla bağlı |
| İki tekerlekli araba (gerçek fizik) | PLA şase, iki TT motor ve tekerlek tek aks üzerinde, bilyeli sarhoş teker, 4×AA pil |
| Ultrasonik mesafe sensörü ve engeller | HC-SR04 önündeki engele gerçekten ölçüm yapar |
| Pil, motor ve tekerlek | 4×AA pil TT motoru sürer, tekerlek motor milinde |
| Fabrika hücresi (robot kol + konveyör) | 6 eksenli kol, konveyör bandı, ışık perdesi |
| SCARA al-bırak (paralel tutucu) | SCARA kol parçaları paralel tutucuyla taşır |
| Pervane itki standı | 8520 motorlar ve pervaneler: devir, itki, tork, akım canlı |
| Mini drone kalkışı | Dört 8520 motorlu 40 g X gövde itkiyle havalanır |
| ESP32 dörtlü | Fırçasız motorlar, ESC'ler, MPU-6050; ESP32'deki uçuş yazılımı gövdeyi dengede tutar |
| Elektronik kutusu | Kapaklı içi boş gövde, içinde Uno ve 9 V pil; sığma denetimi, röntgen ve patlatılmış görünüm |
| Motorlu planör (İHA) | 1 m planör, 2212 fırçasız motor, ESP32 uçuş kartı; denge yalnız pilin konumuyla |
| Planör | Balsa gövde, karbon kuyruk borusu, Nano + iki servo, itme çubukları; elden atılır |
| Mekanum tekerlekli araba | Dört TT motor ve mekanum tekerleklerle yana kayar |
| Işıklar: LED'ler ve lambalar | Karanlık odada LED'ler, RGB LED, güç LED'i, ampul ve LED şerit |
| Kumandalı araba (ESP32 + PS5 kumanda) | Kendi PCB'li ESP32 + DRV8833 kartı, USB-C şarjlı 2S pil, eşleşmiş DualSense |
| PS5 kumandalı uçak: pistten kalkış, ESP32 uçuş kartı | 1 m eğitim uçağı, kanatçıklar, üç tekerlekli iniş takımı, Bluepad32 uçuş kartı, model uçak sahası ([ayrıntı](#ps5-kumandalı-uçak)) |
| Su roketi | 2 L PET şişe, 0,6 L su, 4 bar; su jeti, hava itkisi, kanatçıklar |
| Katı yakıtlı motor test standı (KNSB) | İki BATES şeker yakıt tanesi; basınç ve itki yanma hızından çıkar |
| Katı yakıtlı roket: e-kibritle fırlatma ve paraşüt | G sınıfı KNSB motorlu 41 mm roket; 2 s geri sayımla kapanan anahtar e-kibriti ateşler, roket ~1 km'ye çıkar, tepe noktasında paraşüt açılır |
| Kap ve bilyeler: içi boş parçalar | Modelleyicide yapılmış tepsi ve boru; bilyeler ve küpler tepsinin içine, bir bilye borunun deliğinden yere düşer |
| Su bardağı: servo suyu döker | ESP32, yan yatırılmış bir SG90'ın kolundaki cam bardağı 90° devirir; 193 ml su kâseye dökülür ([sıvılar](#sıvılar)) |
| Çizgi izleyen robot | Mat, solmuş ve parlak bant üstünde 5 kanallı TCRT5000 |
| Sensör gezgini | Kamera, LiDAR, GPS, IMU, pusula ve enkoderler canlı |
| ESP32 + OV2640 kamera ile bant bulma | Kamera pisti yukarıdan görür, ESP32 bandı bulur |
| Rüzgâr, fan, konveyör ve ifadeler | Türbülanslı rüzgâr, fan, konveyör ve ifadeyle yazılan kuvvetler |
| Arazi parkuru, Kapalı atölye | Şekillendirilmiş arazi, boyalı zemin katmanları |

## Ekranın bölümleri

- **Üst araç çubuğu:** Seç ve taşı, **Kablo çek** (K), **Bağla** (B), döndür, çoğalt, sil, geri al / yinele; **Dünyayı çalıştır**, **10 ms ilerlet**, **Durdur ve başa dön**, hız seçimi; kamera takibi, üstten görünüm, hepsini sığdır.
- **Sol panel sekmeleri:** **Sahne**, **Kartlar**, **Parçalar**, **Mekanik**, **Ortam**.
- **Ortada** 3D görünüm.
- **Sağda** denetçi: seçili nesnenin adı, konumu, fiziği, kabloları ve bağlantıları.
- **Alt sekmeler:** **Olaylar**, **Seri monitör**, **Kablolar ve ölçümler**, **RF**, **Sensörler**.

### Panelleri yerleştirme

Palet, denetçi ve alt sekmelerin her biri (Olaylar, Seri monitör, Kablolar ve ölçümler, RF, Sensörler) taşınabilir bir paneldir:

- **Ayrı pencereye almak:** Panelin başlığını ya da alttaki sekmesini tutup sürükleyin. Panel kendi penceresine kopar ve fareyle birlikte gelir; başlıktaki pencere düğmesi de aynı işi yapar. Pencereyi ikinci ekrana taşıyabilirsiniz. Ayrı penceredeki panel de simülasyon sırasında canlı güncellenir.
- **Kenara yerleştirmek:** Ayrı pencereyi başlığından sürükleyip ana pencerenin üzerine getirince **Sol**, **Sağ** ve **Alt** kılavuzları çıkar. Fare bir kenara yaklaşınca panelin oturacağı alan vurgulanır, bırakınca panel oraya yerleşir. Ortada bırakılan pencere ayrı kalır. Pencerenin üstündeki **Sola / Sağa / Alta** düğmeleri sürüklemeden yerleştirir. Pencereyi kapatmak paneli son durduğu yere geri koyar.
- **Aynı kenarda birden çok panel:** Aynı kenara konan paneller üst üste dizilir. Aralarındaki çizgiyi sürükleyerek yüksekliklerini, kenar çizgisini sürükleyerek sütun genişliğini ayarlayın. Örneğin Seri monitörü denetçinin altına, Olaylar'ı paletin altına alıp alt bölmeyi kapatarak sahneyi büyütebilirsiniz.
- **Panel menüsü:** Başlıktaki **⋯** düğmesi ya da başlığa veya sekmeye sağ tık: **Solda**, **Sağda**, **Alt bölmede (sekme)**, **Ayrı pencerede**, **Yukarı / Aşağı taşı**, **Gizli**. Palet ve denetçi alt bölmeye konamaz.
- **Panel düzeni** (araç çubuğunun sağ ucundaki düğme) ya da **Görünüm → Dünya panelleri:** her panelin nerede olduğunu gösterir ve değiştirir. Gizlenen paneller buradan geri açılır. Ayrıca **Alt bölmeyi göster**, **Sol ve sağ panelleri yer değiştir**, **Ayrı pencereleri geri yerleştir** ve **Düzeni sıfırla** buradadır.

Düzen, ayrı pencerelerin konumu ve boyutu dahil saklanır; programı kapatıp açtığınızda aynen gelir. Sahne + Oyun görünümünde (G) **Paneller** kapalıyken kenar sütunları ve alt bölme gizlenir, ayrı penceredeki paneller açık kalır.

![Denetçi ve Seri monitör solda, Palet ve Olaylar sağda; Kablolar ve ölçümler altta](../images/world-panels.png)

![Ayrı pencere sürüklenirken çıkan yerleştirme kılavuzları; sol kenar seçili, panelin oturacağı alan vurgulu](../images/world-panel-guides.png)

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

**Mekanik** sekmesindeki **Temel şekiller:** Kutu, Plaka / şase, Kol / çubuk, Silindir, Tekerlek (kauçuk lastikli), Küre / bilye, Masa / sabit zemin. Boyut ve renk denetçiden ayarlanır. **Gövde kabukları** (kapaklı içi boş gövdeler) içinde kart ve pil taşır; **Uçuş yüzeyleri** (kanat, irtifa dümenli yatay kuyruk, yön dümenli dikey kuyruk) gerçek kaldırma, sürükleme ve moment üretir.

### Çarpışma şekilleri

Her parçanın katı gövdesi kendi 3D modelinden çıkarılır: yuvarlak bir motor gövdesi yuvarlanır, kanat sivrilen biçimiyle, kumanda kendi dış hattıyla çarpışır (sınır kutusu değil). Bacaklar, pinler ve kablo uçları gibi ince parçalar gövdeye katılmaz. **Görünüm → Çarpışma şekilleri (dünya)** fiziğin gerçekten çarpıştırdığı gövdeleri modellerin üzerinde yarı saydam çizer; simülasyon çalışırken gövdelerle birlikte hareket eder. İçi boş modeller (modelleyicide yapılmış ya da içe aktarılmış tepsi, kap, boru, U braket) dışbükey parçalara ayrılır; böylece içlerine bir şey konabilir, bir bilye borunun deliğinden geçer. Örnek: **Kap ve bilyeler** şablonu. Matematiği: [Katı cisim ve temas](../fizik/02-mekanik.md).

![Kap ve bilyeler: tepsinin zemin ve duvar parçaları, borunun dilimleri](../images/world-containers.png)

### Basınçlı kaplar ve roketler

**Mekanik → Basınçlı kaplar ve roketler** bölümünde PET şişe (su roketi), hava tankı, katı yakıtlı motor ve roket kanatçığı vardır. Silindir, kutu veya küre bir gövdeyi denetçide **Özellik ekle → Basınçlı kap (gaz fiziği)** ile basınçlı kap yapabilirsiniz:

- **Gaz, basınç, iç hacim, duvar, su:** kabın içindekiler. Gaz ideal gaz yasasıyla (p·V = m·R·T) hesaplanır.
- **Lüle:** boğaz ve çıkış çapı, jetin çıktığı yüz. Akış izentropiktir; basınç oranı yeterince yüksekse boğazda ses hızına ulaşır, genişleyen lüle gazı süpersonik hızlandırır. İtki ṁ·vₑ + (pₑ − pₐ)·Aₑ olarak gövdeye lülenin olduğu yerden uygulanır.
- **Vana açılır (s):** rampa lüleyi bu ana kadar kapalı tutar.
- **Su:** lüleyi kapattığı sürece önce su çıkar (Bernoulli; roketin ivmesi suyu lüleye bastırır), sonra hava.
- **Katı yakıt:** KNSB, KNDX veya APCP tanesi; yanma hızı basınca bağlıdır (r = a·pⁿ). Oda basıncı, yanma yüzeyinin ürettiği gaz ile lüleden çıkan gazın dengesinden kendiliğinden çıkar; itki eğrisi hazır okunmaz. Motora bağlı bir **e-match** devreden ateşlenince ya da **Ateşleme zamanı** gelince tutuşur.
- **Patlama:** basınç, duvarın çeper gerilmesini (σ = p·r/t) malzemenin dayanımına çıkarırsa kap patlar.

Kabın kütlesi gaz, su ve yakıt azaldıkça düşer; sıcak gaz duvarı ve lüle boğazını ısıtır (ısı kamerasında görünür). Çalışırken denetçide basınç, sıcaklık, itki, yakıt yüzeyinin sıcaklığı, yanan yüzey oranı ve durum canlı okunur. **Su roketi**, **Katı yakıtlı motor test standı** ve **Katı yakıtlı roket** şablonlarıyla başlayın. Matematiği: [Gaz ve roketler](../fizik/05-gaz-ve-roket.md).

### Ateşleme ve fırlatma

Katı yakıt, e-kibrit ateşlendiği an yanmaz. E-kibrit ve üzerindeki ateşleyici barutu yanar, sıcak gazı yakıt yüzeyini ısıtır; yüzeyi tutuşma sıcaklığına çıkan bölge yanar ve alev lüleye doğru yayılır. Denetçide **Ateşleyici barutu (g)** ve **Ateşleyici yanma süresi (ms)** ayarlanır. Barutu 0 yaparsanız yalnız e-kibrit başı kalır; şekerli yakıt çoğu zaman tutuşmaz ve olay günlüğü nedenini yazar ("yakıt yüzeyi en çok 122 °C oldu").

E-kibriti ateşlemenin iki yolu var:

- **Fırlatma kontrolcüsü:** pil → anahtar → e-kibrit → pil. Anahtarın denetçisinde **Kendiliğinden kapanır (s)** bir geri sayım verir. Simülasyon çalışırken **Kapat** düğmesi anahtarı elle kapatır.
- **Kartla:** bir MOSFET'i karttan sürüp e-kibriti ateşleyin (ör. altimetre ya da uzaktan kumanda ile).

E-kibrit, motorun **Ateşleme** ayarında seçilir ya da motora mekanik olarak bağlanır.

### Paraşüt

Herhangi bir gövdeye denetçide **Özellik ekle → Kurtarma paraşütü** ile paraşüt verin ve **Kanopi çapı (m)** girin. Paraşüt, altimetre tırmanmanın bittiğini görünce (ya da **Açılma zamanı**nda) atılır. Kanopi, ipler gerilince dolma süresiyle açılır; açılma şoku ve iniş hızı fizikten çıkar. Çalışırken kanopi sahnede çizilir.

![Katı yakıtlı roket rampada: karton gövde, kanatçıklar, pil ve fırlatma anahtarı](../images/world-rocket-pad.png)

![Ateşlemeden 0,1 s sonra: 24 bar, 1275 °C, 100 N itki; yakıt yüzeyinin tamamı yanıyor](../images/world-rocket-ignite.png)

![Tepe noktasında açılan 45 cm paraşütle iniş](../images/world-rocket-chute.png)

### Sıvılar

![Su bardağı şablonu: servo cam bardağı deviriyor, su kâseye dökülüyor](../images/world-glass-pour.png)

**Mekanik → Sıvılar** bölümünden su dolu ya da boş bir cam bardak, veya bir su hacmi (kutu ya da silindir) eklenir. Sıvı nesnesinin kutusu ya da silindiri, dünya başlarken sıvının dolduracağı hacimdir; içinde duran katı parçaların yeri boş kalır. Denetçide sıvının türü seçilir: su, deniz suyu, etil alkol, süt, ayçiçek yağı, gliserin, bal. Her birinin gerçek yoğunluğu, viskozitesi ve yüzey gerilimi kullanılır.

- Sıvı parçacıklarla çözülür ve kabın duvarlarına gerçek basıncını uygular. Bir bardak suyun ağırlığını taşır, suya bırakılan bir parça kaldırma kuvveti görür, bal yavaş akar.
- Cam (malzeme **CAM**) ve akrilik saydam çizilir; su bardağın içinden görünür. Bir silindire **tube.wall** ile et kalınlığı, **tube.bottom** ile taban verilince bardak ya da kap olur.
- Canlı görüntüde sıvı başına yaklaşık 3000 parçacık kullanılır ve sıvı her 1 ms'de çözülür; durgun su "uyur".
- Render her şeyi tam hesaplar (doğruluk ayarına göre 6000–30 000 parçacık, her fizik adımında) ve sıvıyı daha ince bir yüzeyle yeniden oynatır.

Denklemler ve doğrulama: [Sıvılar](../fizik/07-sivilar.md).

### 3D modeller

**Dışarıdan model → 3D model ya da robot içe aktar (STL, OBJ, PLY, STEP, IGES, URDF)…** ile kendi modelinizi ekleyin. Kütle, ağırlık merkezi ve eylemsizlik malzemeden hesaplanır. Parça sayfasında yaptığınız bir parçayı sağ tık menüsündeki **Test dünyasına tek gövde olarak koy** ile buraya getirebilirsiniz. Robotun parçaları ise **Montaja gönder** ile robotun montajına gider ([5. Parça](05-modelle.md), [6. Montaj](06-montaj.md)).


### Robot tanımları (URDF ve xacro)

ROS'ta yazılmış bir robotu (`.urdf` ya da `.xacro`) aynı düğmeyle ya da **Dosya → Aç** ile getirin; Aç onu kendi dünyasında açar. Gövdesi olan her bağlantı bir parça olur, sıfır duruşunda yerine konur; şekli görsel ağlarından (STL, OBJ, PLY, STEP), okunamazsa çarpışma ağlarından gelir, renkleri URDF'nin malzemelerinden. Gövdesi olmayan çerçeveler (`tool0` gibi) üst bağlantıya katılır. Döner ve sürekli eklemler menteşe, kayar eklemler kızak olur; sınırları gelir. Her hareketli eklemin bir **motoru** vardır (URDF `<ros2_control>` ya da `<transmission>` ile motorlu eklemlerini sayıyorsa yalnız onların; ötekiler, örneğin sarhoş tekerleğin dönen ekseni, serbest döner. Sayılan eklemin `command_interface`'i `velocity` ise hız, `position` ise konum alır. `<mimic>` eklemi, örneğin tutucunun ikinci parmağı, kendi hedefi olmadan izlediği eklemi `multiplier` ve `offset` ile izler; denetçide hedef yerine izlediği eklem görünür): URDF'nin `effort` değeri tork sınırı, `velocity` değeri en yüksek hız olur. Döner ve kayar eklemler konum tutar (başta 0), sürekli eklemler hız alır (başta durur). Böylece kol yerçekimine karşı duruşunu korur. Parçayı seçip denetçinin **Mekanik bağlantılar** bölümündeki **Motor hedefi** alanını değiştirin; çalışırken eklem hemen o açıya döner.

Kök bağlantı ROS'taki `world` ise ona sabit bağlanan parça tabana vidalanır. Gövdesiz başka bir kök (gezgin robotlardaki `base_footprint`) robotun bir çerçevesidir; altındaki parça o çerçeveyi taşır. Yoksa kol sabit, tekerlekleri olan bir robot (adında *wheel* geçen sürekli eklemler) serbest durur. `package://paket/…` ağları URDF'nin klasöründen yukarı doğru paketin klasörü (adı ya da `package.xml`'deki adı) aranarak bulunur. xacro'da özellikler, `${…}` ifadeleri (aritmetik, karşılaştırma, `pi`, `radians()` ve öteki matematik işlevleri), varsayılan ve blok parametreli makrolar, `include`, `if`/`unless`, `insert_block`, `arg` ve `$(find …)` çalışır; YAML okuma (`load_yaml`) ve DAE (Collada) ağları henüz yoktur. Gazebo için yazılmış algılayıcılar (`<gazebo reference="bağlantı"><sensor type="ray">` ya da `gpu_lidar`, ve `type="imu"`) hızları, bağlantı üzerindeki konumları, tarama açıları, menzilleri, gürültüleri ve eklentilerinin konu adlarıyla (`~/out:=scan`, `<topicName>`, `<topic>`) parçaya eklenir ve ROS bağlantısında yayımlanır; kamera ve derinlik algılayıcıları henüz alınmaz. Okunamayan her şey içe aktarmanın sonunda listelenir.

### ROS 2 bağlantısı (rosbridge)

Dünya, ROS 2 tarafında çalışan `rosbridge_server`'a bağlanabilir (ROS 2'de: `ros2 launch rosbridge_server rosbridge_websocket_launch.xml`, WSL ya da başka bir bilgisayar olabilir). Paletin **Ortam** sekmesinde **ROS 2 (rosbridge)** bölümüne adresi yazın (öntanımlı `ws://localhost:9090`) ve **ROS 2'ye bağlan**'a basın. Dünya çalışırken:

| Konu | Yön | Tür | İçerik |
| --- | --- | --- | --- |
| `/clock` | yayın | `rosgraph_msgs/msg/Clock` | Simülasyonun saati, 100 Hz (`use_sim_time` ile çalışan düğümler için). |
| `/joint_states` | yayın | `sensor_msgs/msg/JointState` | Her menteşe ve kızağın konumu (rad / m) ve hızı, URDF'deki adıyla, 50 Hz. |
| `/joint_command` | dinleme | `sensor_msgs/msg/JointState` | Adı geçen eklemlerin motor hedefi: konum (rad / m) ya da sürekli eklemlerde hız (rad/s). |
| `/joint_trajectory` | dinleme | `trajectory_msgs/msg/JointTrajectory` | Konum motorlu eklemler noktalardan zamanında geçer (ros2_control'ün yörünge denetleyicisinin konusu gibi): iki nokta arası konum ve hızlardan kübik, hız yoksa doğrusal; son noktada durur. Motorlar kendi tork ve hız sınırlarıyla izler. Yeni yörünge ya da `/joint_command` çalışanın yerini alır. |
| `/odom` | yayın | `nav_msgs/msg/Odometry` | Yerde serbest duran robotun kök çerçevesinin (`base_footprint`) başladığı yere göre konumu ve kendi çerçevesindeki hızı, 50 Hz. Simülasyonun gerçeğidir (tekerlek kayması içinde yok). |
| `/tf` | yayın | `tf2_msgs/msg/TFMessage` | `odom` → kök çerçeve. Üstündeki bağlantıları `robot_state_publisher` URDF ve `/joint_states` ile yayımlar. |
| `/cmd_vel` | dinleme | `geometry_msgs/msg/Twist` | İleri hız (`linear.x`, m/s) ve dönüş hızı (`angular.z`, rad/s); diferansiyel sürüş gibi sürüş tekerleklerinin hızına çevrilir. 0,5 s yeni komut gelmezse tekerlekler durur. |
| `/scan` | yayın | `sensor_msgs/msg/LaserScan` | URDF'nin bildirdiği lazer tarayıcı: ışınları bu dünyaya kendi hızında atılır (kendi robotunu görmez), gürültüsü bildirildiği gibi; dönüşü olmayan ışın `range_max`'ın ötesindedir. |
| `/imu` | yayın | `sensor_msgs/msg/Imu` | URDF'nin bildirdiği IMU: taşındığı parçanın hareketinden yön, açısal hız ve özgül kuvvet (duran robotta yukarı doğru 9,81 m/s²), bildirilen gürültüyle. |

Sürüş tekerlekleri, tabana takılı, hız motorlu ve ekseni robotun yanına bakan eklemlerdir; tekerlek izi ve yarıçapı robotun kendi şekillerinden ölçülür (ros2_control'ün diferansiyel sürüş denetleyicisine elle yazılan değerler). İkinci bir robotun konuları ve çerçeveleri `/robot2` altına gider. Bölümde gönderilen ve gelen mesaj sayısı, ROS'un gördüğü eklemler, gezginin tekerlekleri, izi ve yarıçapı, son `/cmd_vel` komutu ve komutlardaki bilinmeyen adlar görünür. Bağlantı dünya durunca da açık kalır; bir sonraki çalıştırmada kendiliğinden kullanılır.
### Fotoelektrik sensör ve sınır anahtarı

**Parçalar → Robot Hücresi** altında. **Fotoelektrik sensör**ün merceği parçanın +X yönüne bakar. *Difüz* kipte cisimden yansıyan ışığı görür: menzil beyaz kart (%90 yansıtma) içindir; yansıyan ışık uzaklığın karesiyle azaldığından koyu cisimde kısalır (kauçuk, %8: 300 mm'lik sensörde yaklaşık 90 mm). *Reflektörlü* ve *karşılıklı* kipte ışın menzil uzaklığındaki reflektöre ya da alıcıya gider, rengi ne olursa olsun araya giren her cisim keser. **Çıkış açık** ayarı OUT'un cisim varken mi yokken mi yüksek olacağını seçer; çıkış **tepki süresi** kadar gecikir. Dünya çalışırken ışın sahnede çizilir.

**Sınır anahtarı**nın makaralı kolu +X yönüne uzanır. Bir cisim makarayı **ön strok**tan fazla bastırınca C–NO kapanır, C–NC açılır; yarım milimetre geri gelince bırakır. Kontaklar devrede gerçek anahtardır: bir PLC girişine, Modbus modülüne, röleye ya da doğrudan motorun devresine bağlanır.

### PLC (Yapısal Metin)

**Parçalar → Robot Hücresi → PLC** sekiz sayısal girişi (I0–I7 → `%IX0.0`–`%IX0.7`) ve sekiz çıkışı (Q0–Q7 ← `%QX0.0`–`%QX0.7`) olan, IEC 61131-3 Yapısal Metin (ST) çalıştıran bir PLC'dir. Her **çevrim süresinde** girişleri okur (G/Ç geriliminin yarısının üstü 1), programı bir kez çalıştırır ve çıkışları yazar. Denetçideki düzenleyicide program yazılırken denetlenir; hata satırıyla birlikte gösterilir. **Kaydet ve PLC'ye yükle** dünya çalışırken programı PLC'ye de yükler (soğuk başlar). Panelde RUN/STOP, tarama sayısı, giriş ve çıkış bitleri ve değişkenler canlı görünür; PLC'nin lambası RUN'da yeşil, STOP'ta kırmızıdır.

Desteklenenler: `VAR … END_VAR` (`AT %IX0.0`, `%QX0.0`, `%MW0` adresleriyle), BOOL, INT, DINT, UINT, WORD, REAL, TIME (`T#1s500ms`), 16#FF ve 2#1010 sayıları; TON, TOF, TP, R_TRIG, F_TRIG, CTU, CTD, CTUD, SR, RS blokları; `IF … ELSIF … ELSE`, `CASE … OF` (istasyonun adımları), `FOR`, `WHILE`, `REPEAT`, `EXIT`; ABS, SQRT, MIN, MAX, LIMIT, SEL ve `INT_TO_REAL` gibi dönüşümler. Tamsayılar PLC'deki gibi taşar (INT 16 bit). Derlenmeyen program ya da çalışırken hata (sıfıra bölme, bitmeyen döngü) PLC'yi STOP'a alır ve çıkışları düşürür; **Yeniden başlat** RUN'a döndürür. Yeni PLC; lamba, flaşör ve bir itici istasyonu (parça gelince silindiri ileri sür, ileride geri çek, geride say) içeren örnek programla gelir. **Modbus TCP sunucusu** açılırsa (öntanımlı port 5021) SCADA ya da HMI PLC'ye bağlanır: tutma yazmaçları `%MW0`–`%MW255` (yazılabilir; program bir sonraki taramada görür), ayrık girişler `%IX`, bobinler `%QX`.

**Dil** *Merdiven* seçilirse program merdiven diyagramıdır (LD): **Merdiven düzenleyiciyi aç** solda değişkenleri (`VAR … END_VAR`), ortada basamakları gösterir. Her basamak yedi sütun kontak ve bloktan, sağda bobin sütunundan oluşur; araç çubuğundan seçili hücreye normalde açık / kapalı kontak, yükselen / düşen kenar, karşılaştırma, TON, TOF, TP, CTU, tel, bobin, ters bobin, set ve reset bobini konur, **│ bağ** hücrenin sağından alt satıra dikey bağ çizer (paralel kol; tutma devresi böyle kurulur). Seçili hücrenin değişkeni, parametresi (`T#2s`, `5`, `>= 50`) ve sayıcının sıfırlama girişi alttaki kutulardadır. Program çizilirken denetlenir; **Oluşan Yapısal Metin** basamakların neye çevrildiğini gösterir. Dünya çalışırken güç geçiren kontaklar ve TRUE bobinler yeşil yanar.

**Çevrim süresi ve darboğaz:** PLC'nin **Adım değişkeni** (öntanımlı `Adim`, istasyonun `CASE` adımlarını tutan değişken) izlenir: panel şu anki adımı ve ne kadardır orada olduğunu, tamamlanan çevrimleri (ilk adıma her dönüş) ve ortalama çevrim süresini, en uzun süren adımı ve çevrimin yüzde kaçını aldığını gösterir. İstasyonu yavaşlatan adım (örneğin kısılmış bir silindir stroku) böyle bulunur.

### HMI (operatör paneli)

Alt paneldeki **HMI** sekmesi, PLC'lerin değişkenlerine bağlı bir operatör panelidir. **+ Gösterge** kartında türü (lamba, düğme, anahtar, sayı, ayar, çubuk), PLC'yi ve değişkeni (programdaki adıyla ya da `%MW3` gibi bir adresle) seçip **Ekle**'ye basın. Dünya çalışırken lambalar ve sayılar programın değerlerini gösterir; **düğme** basılı tutulduğu sürece değişkeni TRUE yapar (operatörün parmağı gibi), **anahtar** açıp kapatır, **ayar** yazılan değeri Enter ile programa verir. Girişe (`%IX`) bağlı değişkenler yazılamaz: onları pinleri belirler. Panel dünyayla birlikte kaydedilir.

### Pnömatik silindir

**Parçalar → Robot Hücresi → Pnömatik Silindir** çift etkili bir silindir ve üstündeki tek bobinli 5/2 valftir; mil parçanın +X yönünde çıkar. **SOL** bobini enerjilenince (bobin gerilimin %70'inde çeker, %40'ında bırakır) besleme ön hazneye, arka hazne egzoza bağlanır ve piston ileri gider; bobin bırakılınca geri döner. Her hazne kendi hava kütlesi ve sıcaklığıyla çözülür: valften akış ISO 6358'e göredir (kritik basınç oranının altında boğulmuş), dolan hava ısınır, genleşen soğur, duvar oda sıcaklığına çeker. Pistonu basınç × alan iter, contalar sürtünür; uçlarda strok biter. **Hız ayarı** egzozu kısar (çıkış kısmalı hız denetimi). **EXT** ve **RET** uç konumlardaki manyetik sensörlerin çıkışlarıdır. Bobin devrede gerçek bir yüktür: bir Modbus modülünün, kartın ya da sınır anahtarının devresinden sürülür. Mil kutuları iter; panelde konum, hız, iki haznenin basıncı, kuvvet ve harcanan hava (normal litre) görünür.

### Arıza senaryoları

Fotosel, sınır anahtarı, pnömatik silindir ve Modbus modülü, denetçide **Özellik ekle → Arıza senaryosu** ile belli bir anda bozulabilir: kirli lens (menzil düşer), takılı ya da ölü çıkış, yapışık kontak, kırık kol, conta kaçağı, takılı valf, düşük besleme, kopuk ağ. **Başlama zamanı** ve varsa **büyüklüğü** (kalan menzil, kaçak, basınç) ayarlanır; arıza başlayınca **Olaylar**'a yazılır. Böylece PLC programının arızaya tepkisi (sonsuza dek bekleyen bir adım, zaman aşımı alarmı) gerçek arızadan önce denenir.

### Modbus TCP uzak G/Ç

**Parçalar → Robot Hücresi → Modbus TCP Uzak G/Ç** modülünün 8 sayısal girişi (DI1–DI8) ve 8 sayısal çıkışı (DO1–DO8) vardır; kablolarla ışık perdesine, konveyörün fotoseline, robotun girişlerine bağlanır. Dünya çalışırken modül bir Modbus TCP sunucusudur (öntanımlı `127.0.0.1:5020`, birim 1): aynı bilgisayardaki PLC, HMI ya da SCADA programı (Codesys, Node-RED, `pymodbus` …) gerçek bir modüle bağlanır gibi bağlanır. **Dinlenen adres** `0.0.0.0` yapılırsa ağdaki başka bilgisayarlar da bağlanabilir.

| Modbus | Adres | Anlamı |
| --- | --- | --- |
| Bobin (01, 05, 15) | 0–7 | DO1–DO8: yazılan değer çıkış pinini sürer |
| Ayrık giriş (02) | 0–7 | DI1–DI8: pindeki gerilim G/Ç geriliminin yarısını aşarsa 1 |
| Giriş yazmacı (04) | 0 | Girişlerin bit sözcüğü (DI1 = bit 0) |
| Tutma yazmacı (03, 06, 16, 23) | 0 | Çıkışların bit sözcüğü; yazılırsa bobinler ondan alınır |

Aynı portu paylaşan modüller birim kimlikleriyle ayrılır (ağ geçidi arkasındaki cihazlar gibi). Açılan portlar ve açılamayanlar **Olaylar** sekmesinde yazar.

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

En çok iki eklemle birbirine bağlı parçalar birbiriyle çarpışmaz. Bir nesneyi sürüklediğinizde ona takılı her şey de gelir. Bağlantıyı kaldırmak için denetçide **Bağlantıyı çöz**.

### Vidalama, yapıştırma, bant, kelepçe, lehim

Sabit bir bağlantının denetçide **bağlantı türü** seçilir. Seçim yalnız bir etiket değildir; bağlantının üç fiziğini belirler:

1. **Dayanım:** Vidada parça başına taşınan yük × vida sayısı, yapıştırıcıda kayma dayanımı × yapışan alan alınır. Yapışan alan, küçük parçanın en geniş yüzünün yarısı sayılır. Bağlantıdan geçen kuvvet bunu aşarsa bağlantı kopar ve olay günlüğüne yazılır.
2. **Isı geçişi:** Isı, bağlantı katmanından (iletkenlik ÷ kalınlık) ve gerçekten değen yüzey payından geçer. İnce yapıştırıcı katmanı ısıyı vida başlarından çok daha iyi geçirir: epoksiyle birleşen iki alüminyum blok bir dakikada aynı sıcaklığa gelir, kelepçeyle aralarında 55 K fark kalır.
3. **Sıcaklık:** Yapıştırıcılar ve plastik dişler ısınınca yumuşar. Dayanım, yumuşama sıcaklığından bırakma sıcaklığına kadar doğrusal düşer. Bağlantının sıcaklığı, iki parçanın ısı ağındaki ortalamasıdır. Sıcak tutkalla tavana yapıştırılmış 1 kg'lık blok 25 °C'de tutar, parçalar 95 °C'deyken düşer.

| Bağlantı türü | Dayanım | Isı geçişi (katman, temas payı) | Yumuşar → bırakır |
| --- | --- | --- | --- |
| Sabit (belirtilmemiş) | kırılmaz | 400 W/m²K, %15 | — |
| M3 vida + somun (2 adet) | 2 × 1000 N | 400 W/m²K, %15 | — |
| Plastiğe vida (M3, 2 adet) | 2 × 250 N | 400 W/m²K, %12 | 60 → 110 °C |
| Japon yapıştırıcısı (siyanoakrilat) | 6 MPa × alan | 4000 W/m²K, %50 | 80 → 120 °C |
| Epoksi | 15 MPa × alan | 2000 W/m²K, %70 | 60 → 110 °C |
| Sıcak tutkal | 1,5 MPa × alan | 400 W/m²K, %30 | 55 → 85 °C |
| Çift taraflı köpük bant | 0,5 MPa × alan | 100 W/m²K, %80 | 90 → 150 °C |
| Kelepçe (zip bağı) | 220 N | 400 W/m²K, %5 | 80 → 160 °C |
| Lehim (Sn63/Pb37) | 30 MPa × alan | 200 000 W/m²K, %30 | 150 → 183 °C |

Kapak (kutu kapağı) ayrıca **vidalı** işaretlenebilir; kenara oturduğunda vidalanır, oturmazsa "vidalanamıyor" uyarısı çıkar.

### Dişli, kayış ve vidalı mil

Menteşeyle ya da kızakla bağlı bir parçayı seçip **Özellik ekle ▾ → Dişli, kayış, vidalı mil** ile dünyadaki başka bir eklemi seçin, sonra türünü:

| Tür | Eklemler | Ne olur |
| --- | --- | --- |
| **Dişli çifti** | menteşe + menteşe | Öteki dişli diş sayılarının oranında, ters yönde döner. |
| **Kayış** | menteşe + menteşe | Kasnak çaplarının oranında, aynı yönde döner (zincir de böyledir). |
| **Kremayer ve pinyon** | menteşe + kızak | Pinyonun her turu çevresi kadar kaydırır. |
| **Kayışlı taşıyıcı** | menteşe + kızak | 3B yazıcının X/Y ekseni gibi: kasnağın her turu çevresi kadar. |
| **Vidalı mil** | menteşe + kızak | Milin her turu somunu hatvesi kadar ilerletir. |

Bağ bir motor değil, bir kısıttır: iki eklem birlikte hareket eder ve sürülen taraftaki yük sürücüye geri yansır. Taşıyıcıyı kaldıran vidanın motoru ağırlığı tork olarak hisseder. Ölçüler parçadan gelir: silindirin çapı dişlinin diş sayısıdır (modül 1), kasnağın ya da vida milinin çapıdır. Silindir değilse 20 ve 40 diş, GT2 kasnağı ya da T8×8 vida alınır. **Ters yön**, çapraz kayışı, araya giren bir ara dişliyi ya da sol vidayı anlatır.

**Verim** dişin ya da vida dişinin sürtünmesidir ve taşıdığı yükle orantılıdır. Varsayılanlar: düz dişli %98, kremayer %95, kayış %97. Vidalı milde verim dişin eğiminden hesaplanır (trapez diş, plastik ya da bronz somun, μ = 0,15): T8×8 yaklaşık %66, T8×2 yaklaşık %36. Verim %50 ya da altındaysa vida **kendini kilitler**: motor dururken yük mili geri çeviremez. Bu yüzden T8×2 milli dikey eksen olduğu yerde kalır, T8×8 milli eksen yavaşça iner. Çalışırken **Canlı ölçüm** bağın yükünü ve eklemin hızını gösterir.

**Boşluk**, dişliler arasındaki toplam boşluktur; dişlide derece, kremayer, kayışlı taşıyıcı ve vidada milimetre olarak sürülen tarafta verilir. Boşluğun içinde iki taraf birbirinden bağımsız hareket eder, dişler ancak yanaklar değince iter. Yön değiştiren bir motor boşluğu geçene kadar sürülen tarafı oynatmaz; 1° boşluk 0,1 rad/s'de 175 ms bekleme demektir. **Kayış rijitliği** (N/mm) kayışın yük altında esnemesidir: bir kayış iki tarafı yay gibi bağlar, 0 rijittir.

### Yay ve sönüm

Menteşeyle ya da kızakla bağlı bir parçaya **Özellik ekle ▾ → Yay ve sönüm** ile eklemin kendi yayı verilir. Menteşede bu bir burulma yayıdır (**N·mm/°**), kızakta helezon yay (**N/mm**). Katalogdaki yay sabiti olduğu gibi girilir. Yay, eklemi **serbest konumuna** çeker ve motorla birlikte çalışır: geri dönüş yaylı bir kolu tutan motor, yayın k·θ torkunu taşır. **Sönüm** hızla orantılı bir frendir (amortisör, gaz yay, kapı kapatıcı). Yay örtük çözülür, bu yüzden çok sert yay da kararlı kalır. Kızakta 1 kg kütle 1000 N/m yayda 9,81 mm sarkar ve 199 ms periyotla salınır; kritik sönümle hiç aşmadan yerine oturur. Çalışırken **Canlı ölçüm** yayın torkunu ya da kuvvetini ve eklemin konumunu gösterir.

### Rulman ve yatak

**Özellik ekle ▾ → Rulman / yatak** ile eklemin döndüğü ya da kaydığı yatak seçilir. Yatağın sürtünmesi taşıdığı yükle artar. Menteşede sürtünme torku μ × yük × delik çapı / 2'dir (SKF'nin basitleştirilmiş sürtünme momenti). Kızakta sürtünme kuvveti μ × raya dik yüktür.

| Menteşe | μ | Kızak | μ |
| --- | --- | --- | --- |
| Bilyalı rulman (608) | 0,0015 | Doğrusal rulman (LM8UU) | 0,003 |
| İğneli rulman | 0,0025 | Bilyalı ray (MGN) | 0,004 |
| Bronz burç (yağlı) | 0,08 | Tekerlekli taşıyıcı (V-slot) | 0,005 |
| Plastik burç (POM / PTFE) | 0,15 | Kayar yatak (plastik, kuru) | 0,15 |
| Pim, baskı parçasında (kuru) | 0,3 | | |

10 kg'lık bir volan 8 mm'lik bilyalı rulmanda 10 rad/s'den iki saniyede ancak 0,02 rad/s yavaşlar, bronz burçta 1,26 rad/s. Ağır bir parça yatağını daha çok frenler. Bağlantının **sürtünme** değeri yükten bağımsız kısım olarak kalır (keçe, gres). Çalışırken **Canlı ölçüm** yatağın o anki sürtünmesini gösterir.

### Güç aktarım parçaları: gerçekteki gibi kurulan mekanizma

**Mekanik** paletinin **Güç aktarımı** grubunda gerçek ölçülü parçalar vardır: POM düz dişliler (modül 1, 20/30/40/60 diş, involüt profil), pirinç z12 pinyon, GT2 kasnaklar (20/40/60 diş), 608ZZ, 688ZZ, 625ZZ ve 6000ZZ rulmanlar, KP08 yataklı rulman, Ø8 ve Ø5 çelik miller, T8 vidalı miller (hatve 8 ve 2) ile pirinç somunları, LM8UU doğrusal rulman, mil bileziği ve 5 × 8 esnek kaplin. 3B modelleri ölçülerinden kodla üretilir: dişlinin involüt dişleri, kasnağın GT2 boşlukları, rulmanın bilezikleri ve kapakları, vidanın çok ağızlı trapez dişi. Kütleleri malzemelerinden (POM, pirinç, çinko döküm, çelik, alüminyum) ya da katalog değerinden gelir. Halka biçimli parçaların çarpışma şekli de halkadır, mil deliğinden geçer.

Parçaları gerçek bir makinedeki gibi yerleştirin. Bağlantıları siz çizmezsiniz; **Mekanizmayı kur** (ya da dünyayı çalıştırmak) parçaların yerinden okur:

- Rulmanın ya da KP08'in deliğinden geçen mil, o rulmanda **döner**. Menteşenin sürtünmesi rulmanın türünden gelir.
- Deliği mile oturan dişli, kasnak, bilezik ya da kaplin mile **setuskurla sabitlenir**.
- Çubuğa geçen LM8UU çubuk boyunca **kayar**.
- Bölüm daireleri değen, eksenleri paralel ve modülü aynı iki dişli **kavrar**: eksen mesafesi m·(z₁+z₂)/2 olmalıdır.
- Vidalı mile geçen somun, bağlı olduğu kızaklı taşıyıcıyı **sürer**. Sağ el vida: mil üstten bakınca saat yönünde dönünce somun yükselir.
- Kavrayan dişliler dişi dişe değil, dişi boşluğa gelecek biçimde başlar: kurulurken sürülen dişli en çok yarım diş adımı çevrilir.
- Bölüm dairesi kremayerin bölüm çizgisine değen ve ekseni kremayere dik pinyon kremayerle **kavrar** (kremayer m1, bölüm çizgisi üst yüzün 1 mm altında). Kremayer bir kızakta kayıyorsa pinyon onu sürer. Kremayer sabitse pinyon onun üstünde yuvarlanır ve milinin döndüğü arabayı taşır (CNC portalı gibi). Pinyonun her turu çevresi kadar yol demektir: z20 için 62,8 mm.

**Kayış:** bir kasnağı seçip denetçide **Kayış tak → (öteki kasnak)** düğmesine basın. Eksenler paralel, kayış olukları aynı düzlemde olmalıdır. Kayışın boyu iki düz açıklık ile sardığı yaylardan hesaplanır. Alınacak kapalı GT2 kayışın boyu ve gerdirmek için kasnağın ne kadar kaydırılacağı yazılır. 20T ile 40T kasnak 80 mm arayla 220,5 mm kayış ister; 232 mm'lik kayış için kasnak 5,75 mm kaydırılır. Miller çap oranında, aynı yönde bağlanır. Kayış sahnede gerçek GT2 ölçüleriyle çizilir: 0,63 mm sırt ve 2 mm adımlı dişler ilk kasnağın oluklarına oturur, kasnak döndükçe dişler kayışla yürür.

Varsayım yapılmaz: mile takılı olmayan dişli düşer, rulmandan geçmeyen mil tutulmaz, dar delik takılmaz, bol delikte mil boşta kalır. Böyle durumlar **Olaylar** sekmesinde ve parçanın denetçisinde yazar. Rulmanları ve KP08'leri gövdeye bağlayın ya da **Sabit** işaretleyin; KP08 sabitse taban yuvalarında iki M6 cıvata görünür. Kurulan bağlantılar **Mekanik bağlantılar** bölümünde görünür ve düzenlenebilir.

**Step motor:** **Parçalar** sekmesindeki NEMA 17 (17HS4401 ya da ince 17HS08) ve A4988 sürücüyle bir eksen baştan sona kurulur. Bağlantılar şunlardır:
- 12 V → VMOT, 5 V → VDD;
- STEP'e kartın pini ya da bir kare dalga, DIR'e yön;
- 1A/1B → motorun A+/A−, 2A/2B → B+/B−.

Motorun Ø5 D kesitli miline geçen kaplin, dişli ya da kasnak mile sabitlenir. Kaplinin öteki ucundaki vidalı mil motorla döner. Motor gerçek denklemleriyle çalışır:
- iki sargının direnci ve endüktansı;
- rotor açısına bağlı karşı-EMK ve tork (1,8° adım);
- mıknatısların takılma torku.

Sürücü STEP kenarlarını sayar, MS1–MS3 ile 1/16'ya kadar mikro adım atar ve faz akımlarını akım sınırına göre VMOT'un yettiği kadar sürer. Bu yüzden hız arttıkça tork düşer. Motorun torku fizikteki rotoru döndürür. Milin ataleti, vidanın yükü ve sürtünmesi motorun kendisine yansır: aşırı yük ya da rampasız hızlı başlangıçta motor gerçekteki gibi adım kaçırır.

A4988'in yanında iki sürücü daha var; ayakları aynıdır:
- **DRV8825:** M0–M2 ile 1/32 mikro adım. Kendi 3,3 V mantığını yapar, VDD ayağı yoktur (o ayak FAULT). VMOT 8,2–45 V.
- **TMC2209:** MS1/MS2 ile 1/8, 1/16, 1/32 ya da 1/64. Her adımı iki STEP kenarı arasında 1/256'ya yayar (MicroPlyer), bu yüzden motor sessiz ve düzgün döner. VM 4,75–29 V.

Akım sınırını sürücünün **Akım sınırı** değeriyle ayarlayın; bu, karttaki Vref ayarının karşılığıdır.

**Arduino ile Z ekseni:** **Arduino ile step motorlu Z ekseni** şablonundaki `z-axis-stepper` yazılımı (yerleşik, kütüphanesiz) bir Uno'dan A4988'i sürer: D2 → DIR, D3 → STEP, D8 → EN. Taşıyıcıyı T8 vidalı milde 40 mm kaldırır ve indirir. Hızı sabit ivmeyle artırıp azaltır (AccelStepper'ın yaptığı rampa). Rampasız tam hızla başlayan motor durur, rampayla başlayan izler.

**Montaj ilişkileri:** parçaları yüzlerinden ve eksenlerinden birbirine göre yerleştirin. Araç çubuğundaki **Hizala** aracını (H) seçin, iki parçada birer yüz ya da eksen tıklayın; milin, tekerin ya da dişlinin yuvarlak yanına tıklamak ekseni seçer. Açılan menüde uygun ilişkiler çıkar:
- **Yüz yüze:** iki yüz birbirine dayanır.
- **Aynı hizada:** iki yüz aynı yöne bakar ve aynı düzlemde durur.
- **Eş eksenli:** mil deliğinde.
- **Paralel**, **dik**, **mesafe** ve **açı**.

Sabit parça yerinde kalır, öteki parça olabildiğince az hareketle yerine oturur. Vidalı ya da gruplu parçalar onunla birlikte gider. Parçanın denetçisinde **Montaj ilişkileri** bölümü vardır. Burada aralıkları ve açıları değiştirebilir, ilişkiyi kaldırabilirsiniz; parçanın kaç serbestliği kaldığı da yazar. Örneğin deliğindeki milin iki serbestliği kalır (kayar ve döner), omzu da yatağa dayanırsa bire iner.

İlişkiler parçayı yerleştirir; çalışırken parçayı tutan eklemdir. Bir ya da hiç serbestlik kaldığında **İlişkilerden eklem kur** düğmesi o eklemi kurar: dönmek kaldıysa ekseninde menteşe, kaymak kaldıysa kızak, hiçbiri kalmadıysa sabit bağlantı. Öteki ilişkilerle çelişen bir ilişki eklenmez ve çeliştiği ilişkiler adıyla yazılır. Bir parçayı sürüklediğinizde ona bağlı parçalar da ilişkileriyle birlikte gelir.

Fizik bir animasyon değildir. Dişliler ve vida kısıt çözücüyle bağlanır (sanayideki çok gövdeli dinamik programları da böyle yapar). Motorun torkunu yük, verim, boşluk ve rulman sürtünmesi belirler. Dişlerin birbirine tek tek değmesini çarpışmayla çözmek hem yavaş hem kararsız olurdu; dişliler bu yüzden kendi gövdelerinde değil, kavrama kısıtında buluşur. Kendi 3B modelinizi kullanmak isterseniz **Bağla** aracıyla menteşe ya da kızak kurun, sonra **Özellik ekle → Dişli, kayış, vidalı mil** ile eşleyin. Fizik aynı yoldan çalışır.

### Kol kinematiği: ileri ve ters kinematik

Servolarla kurduğunuz bir kolun bir parçasını seçin (örneğin servo kol şablonunda **Kol 2**). Denetçide **Kinematik (kolun ucu)** bölümü çıkar. Program, bağlantıları izleyerek tabandan seçtiğiniz parçaya giden zinciri kendisi bulur.

- **Taban:** sabit bir nesne ya da zemine bağlı parça. Kol serbest duruyorsa zincirin en ağır parçası taban sayılır.
- **Eksenler:** zincirdeki her servo kolu, motor mili, menteşe ve kızak.
- **Uç noktası:** seçtiğiniz parçanın son eksene en uzak yüzünün ortası.

![Servo kolun ters kinematiği: erişim alanı, iskelet ve servoların panelden sürülmesi](../images/world-arm-kinematics.png)

| Bölümde | Ne gösterir |
| --- | --- |
| Servo satırları | Her servonun açısı (90° kurulduğu konum, 0–180° arası) ve ağırlığın o servoya yüklediği torkun kilitlenme torkuna oranı. Menteşe ve kızaklar "serbest" yazar: açılarını fizik belirler. |
| Uç (model) | Açılardan hesaplanan uç noktası (ileri kinematik). |
| Uç (fizik) | Çalışırken fiziğin ölçtüğü uç noktası ve modelle farkı. Fark, eklemlerin esnemesi ve servonun ölü bandı kadardır; servo kolda 0,2–0,3 mm. |
| Tekilliğe uzaklık | %0 olduğunda kol gergin ya da katlanmıştır: ucu bazı yönlere ancak çok büyük açılarla oynatabilirsiniz. %10'un altında uyarı çıkar. |
| Erişim | Ucun servoların bütün açılarında gidebildiği en yakın ve en uzak mesafe. |

**Ters kinematik:**

1. Hedef noktayı milimetre olarak yazın ya da **Sahnede seç** düğmesine basıp sahnede bir yere tıklayın. Nokta, ucun o anki yüksekliğindeki düzlemden alınır.
2. **Çöz** düğmesine basın. Servo açıları bulunur; serbest menteşeler olduğu gibi kalır.
3. Çözümü kullanın:
   - Dünya dururken **Önizle**, servoları bu açılara çevirip kolu sahnede gösterir.
   - Çalışırken **Servoları sür**, servolara bu açıları verir. Bu, sinyal kablosuna servo test cihazı takmak gibidir: yazılımın darbelerinin yerine geçer, servo ise kendi hızı, torku ve beslemesiyle döner. **Yazılıma bırak** komutu geri verir.
   - **Açıları kopyala**, açıları koda yapıştırılacak sabitler olarak panoya alır, servonun bağlı olduğu pinle birlikte (örneğin `const int OMUZ_SERVOSU = 75;   // 74.9° · D9`).

Hedef ulaşılamıyorsa en yakın nokta ve nedeni yazılır. Neden ya erişim dışı olması ya da servo sınırıdır.

Bütün servo eksenleri paralelse (servo kol şablonundaki gibi) uç tek bir düzlemde gezer. Bu durumda hedef o düzleme indirilir. Fark 1 mm'den büyükse bu da yazılır.

**Motorlu eksenler (içe aktarılan robotlar):** URDF ya da xacro ile gelen bir kolun eklemleri motorludur ([Robot tanımları](#robot-tanımları-urdf-ve-xacro)); ters kinematik onları da çözer ve satırlarında bağlantının adı görünür. Dururken **Önizle** kolu çözülen duruşta gösterir, **Motor hedefi yap** çözümü motorların hedefi olarak kaydeder (dünya çalışınca kol oraya gider; geri alınabilir). Çalışırken **Eksenleri sür** motorlara çözülen değerleri verir; her eksen URDF'deki tork ve hız sınırıyla gider, satırda motorun hedefi (→) görünür. Motorlu eksenler koda girmez; **Açıları kopyala** yalnız servolar için çıkar.

Çözümde bir servo kilitlenme torkunun yarısından fazlasını harcıyorsa uyarı çıkar. **Erişim alanını sahnede göster** açıkken şunlar çizilir:

| Renk | Ne |
| --- | --- |
| Mavi noktalar | Ucun gidebildiği yerler |
| Sarı çizgi | Kolun eksenlerden uca iskeleti |
| Turuncu | Hedef |
| Yeşil | Ulaşılan en yakın nokta |

### Tekerlekli robotun kinematiği ve odometri

Tekerlekli bir robotun şasisini seçin (örneğin araba şablonunda plaka). Denetçide **Sürüş kinematiği** bölümü çıkar.

![Mekanum araba yana kayarken: tekerlek hızları, tekerleklerden ve gerçek hız, odometri sapması, sahnede iki yol](../images/world-vehicle-kinematics.png)

Program, millerinde tekerlek olan motorları bulur ve sürüş türünü kendisi tanır:

| Tür | Özelliği |
| --- | --- |
| Diferansiyel (2 tekerlek ve sarhoş teker) | İleri gider ve döner; yana gidemez. |
| Kızaklı (4 tekerlek) | Dönerken tekerlekler yana kayar. |
| Mecanum | Her yöne gider. |
| Omni | Her yöne gider. |

İz genişliği, aks aralığı ve tekerlek çapı not olarak yazılır.

| Bölümde | Ne gösterir |
| --- | --- |
| Tekerlek satırları | Çalışırken her tekerleğin dev/dk hızı ve ileri mi geri mi döndüğü. |
| Tekerleklerden | Tekerlek hızlarından hesaplanan şasi hızı ve dönüş hızı (ileri kinematik). |
| Gerçek | Şasinin fizikte gerçekten gittiği hız. Fark büyükse kayma yüzdesi yazılır. |
| Odometri | Tekerlek enkoderlerinden tahmin edilen konum ve yön. |
| Sapma | Tahminin gerçek konumdan farkı ve gidilen yola oranı. |

**Odometriyi sıfırla**, tahmini gerçek konuma getirir. Her çalıştırma da kendiliğinden sıfırdan başlar. **Yolları sahnede göster** açıkken iki yol çizilir:

- turuncu: tahmin edilen yol;
- yeşil: gerçek yol.

Düz zeminde iki tekerlekli araba 46 cm'lik bir yayda yolun %1'inden az sapar. Tekerlek patinajı, kızaklı dönüş ve tümsekler sapmayı büyütür, gerçekte olduğu gibi.

**Ters kinematik:** istediğiniz ileri hızı, (mecanum ve omni için) yana hızı ve dönüş hızını yazın. Her tekerleğin dev/dk olarak ne kadar ve hangi yöne dönmesi gerektiği hemen altında görünür. Dönüş yarıçapı da yazılır. Kızaklı araçta dönüşün tekerleklerin hesapladığından yavaş olacağı ayrıca belirtilir.

### PID ayarlayıcı: motorun hız döngüsü

Bir DC motoru seçin. Denetçide **Hız kontrolü → PID ayarlayıcı…** düğmesine basın ya da sahne ağacında motora sağ tıklayın. Ayarlayıcı kendi penceresinde açılır.

Deneyler dünyanın bir kopyasında yapılır: yazılım çalışmaz, fizik tamdır. Motorun yükü, ona taktığınız tekerlek ve sürdüğü araçtır. Birimler yazılımınızdakiyle aynıdır: komut PWM (0–255), hız çıkış milinin dev/dk'sı.

![PID ayarlayıcı: adım testi ve model, seçilen kuralın modeldeki ve gerçek fizikteki yanıtı, PWM komutu](../images/world-pid-tuner.png)

1. **Adım testi:**
   - Besleme gerilimini (dünyadaki pilden kendiliğinden alınır) ve test PWM'ini girin, **Adım testini çalıştır**'a basın.
   - Duran motora PWM birden verilir ve hız oturana kadar ölçülür.
   - Ölçümden birinci dereceden ölü zamanlı bir model çıkarılır (Smith'in iki nokta yöntemi). Kazanç (K), zaman sabiti (τ) ve ölü zaman (θ) yazılır, model ölçümün üstüne çizilir.
2. **Kural:**
   - Yazılımınızda PID'in çalıştığı periyodu girin; yarım periyot ölü zamana eklenir.
   - İsterseniz kapalı çevrim zaman sabitini (τc) ve denenecek hızı da girin.
   - Dört kural listelenir. Her birinin kazançları (Kp, Ki, Kd) ve modelde öngörülen yükselme, aşma ve oturma süreleri görünür:

     | Kural | Ne zaman |
     | --- | --- |
     | SIMC (PI) | Çoğu motor hız döngüsü için ilk seçim |
     | IMC (PID) | Ölü zaman belirginse |
     | Ziegler–Nichols | Klasik, agresif |
     | Cohen–Coon | Ölü zaman büyükse |

3. **Gerçek fizikte dene:**
   - Seçtiğiniz kazançlar, kopyaladığınız kodla aynı ayrık PID olarak fizikte çalışır: ölçümün türevi, çıkış sınırı ve sınırda integralin durması (anti-windup).
   - Modelin öngördüğü yanıt ile fizikte olan aynı grafikte görünür, sayılarla karşılaştırılır.
   - PWM çoğu zaman sınırdaysa ya da fizikteki aşma modelden çok fazlaysa nedeni yazılır.
4. **Kodu kopyala:** kazançlar, periyot ve sınırlarla birlikte Arduino'ya yapıştırılacak bir `pidStep(setpoint, y)` işlevi.

**Konum döngüsü (enkoderli motor, DC servo):** pencerenin üstündeki seçimden **Konum döngüsü**'nü seçin. Adım testi aynıdır; model hızı verir, konum onun integralidir.

- Kurallar SIMC'nin tümleyen süreç kurallarıdır:

  | Kural | Ne zaman |
  | --- | --- |
  | SIMC (PD) | İlk seçim: aşmasız ve hızlı |
  | SIMC (PID) | Yük çekiyorsa (kol, yerçekimi): kalıcı hatayı kapatır |
  | SIMC (PID), yumuşak | Sürtünme ve boşluk varsa |

- Ayar noktası derecedir (boş: 90°). Fizikte açı enkoder gibi sayılır, tur aşınca da sürer.
- Konum döngüsü motoru iki yönde sürer: **İki yönlü sürücü** açık kalır.
- Kopyalanan kodda `y`, enkoderden okunan açıdır. Dönen değerin işareti yönü, büyüklüğü PWM'i verir.

## Fizik ve ortam

Her nesnenin denetçisinde **Fizik** bölümü:

- **Sabit:** nesne zemine veya masaya sabitlenmiştir, hareket etmez.
- **Malzeme:** fiziğin bildiği her malzeme: PLA, PETG, ABS, PET, Akrilik, Alüminyum, Çelik, Bakır, Kurşun, Karbon, Ahşap, Kontrplak, Balsa, MDF, Karton, Kauçuk, FR4. Sürtünme ve sekme malzemeye göre değişir. Listenin altındaki kart malzemenin sayılarını gösterir: yoğunluk, elastisite modülü, akma dayanımı, ısı iletimi, özgül ısı, ışınım ve yumuşama/erime sıcaklığı.
- **Doluluk (%)** ve **Kütle (g, 0 = otomatik)**.

**Görünüş ve yüzey** bölümü (gövdeler ve içe aktarılan modeller):

- **Renk:** hazır renk kutularından birine tıklayın, **Renk kodu**na `#RRGGBB` yazın ya da **Malzeme rengi** ile malzemenin kendi rengine dönün.
- **Yüzey:** mat boya, parlak boya/vernik, parlatılmış metal, fırçalanmış metal, siyah eloksal, oksitli/paslı, ahşap damarı, karbon örgü, kraft kâğıt, kauçuk kaplama, 3B baskı katmanları. Doku gerçek ölçekte çizilir (0,2 mm baskı katmanı, 2×2 dimi karbon örgü…). Yüzey yalnız görüntü değildir: ısıl ışınımı (ε) belirler. Parlatılmış alüminyum (ε 0,05) ısısını çok az ışınımla verir, siyah eloksal (ε 0,85) çok daha hızlı soğur. Isı ağı, soğuma ve ısı kamerası bu değerle hesaplanır. Örnek: **Malzeme ve yüzey vitrini** şablonu.

![Malzeme ve yüzey vitrini: ahşap, karbon, fırçalanmış alüminyum, pas, kraft, kauçuk, PLA baskı](../images/world-surfaces.png)

![180 s sonra ısı kamerası: parlatılmış blok hâlâ sıcak, siyah eloksal blok soğumuş](../images/world-surfaces-thermal.png)

**Ortam** sekmesi:

- **Yerçekimi:** Dünya 9.81, Ay 1.62, Mars 3.71, Jüpiter 24.8, Sıfır yerçekimi.
- **Ortam sıcaklığı**, **zemin genişliği / derinliği**, **zemin sürtünmesi (μ)**, **çevre duvarları**.
- **Engel ekle** / **Engelleri temizle**.
- Hava, rüzgâr, türbülans ve kuvvet alanları: bkz. [Fizik betikleri (SDK)](../gelistirici/SDK.md).

Servolar devreden gelen darbe komutunu okur; kolun açısı ve çektiği akım devreye geri döner. DC motorlar devrenin hesapladığı hızla döner; mile binen yük motora geri yansır. Sert çarpmalar ve kopan bağlantılar **Olaylar** sekmesine yazılır. Hava sürüklemesi yüz yüz etkir: bir parçanın bağlı başka bir parçanın örttüğü yüzü havayı görmez ([Aerodinamik](../fizik/04-aerodinamik.md)).

## Isı kamerası

**Görünüm → Isı kamerası (dünya)** modelleri gerçek bir ısı kamerası gibi sıcaklığa göre renklendirir (demir paleti). Fare bir parçanın üstündeyken o noktanın sıcaklığı ve parçanın düğümleri (TT motorun sargısı, gövdesi, dişli kutusu) görünür. Matematiği: [Isı](../fizik/03-isi.md).

## Sensörler ve ekranlar

Sensörler hazır değer üretmez. Gerçek veri sayfası özellikleriyle dünyayı ölçerler: ışın atarlar, zemine bakarlar, gövdenin hareketini alırlar. Sonra kendi gürültülerini ve hatalarını eklerler. Hepsi **Parçalar** sekmesindedir.

| Sensör | Ne ölçer | Nasıl hesaplanır | Hata ve sınırlar |
| --- | --- | --- | --- |
| **HC-SR04 ultrasonik** | Engele uzaklık | Fizik sahnesine ışın atılır; ilk çarpan cisim, sensörün kendi montajı sayılmaz | Yankı süresi ses hızından; menzil ve açı veri sayfasından |
| **Çizgi sensörü** (TCRT5000, QRE1113, TCRT/QTR dizileri) | Zemindeki çizgi | Kızılötesi LED ve fototransistör: zeminin dağınık yansıtması, parlak bantta ayna yansıması, güneş ve lamba ışığı. Fototransistör devrede gerçek akım kaynağıdır, doyuma girer | Yükseklikle sinyal değişir (TCRT5000'de tepe 2,5 mm, 0,2–15 mm). Mat siyah bant ≈ %6 yansıtır |
| **LiDAR** (A1 sınıfı, LD06 sınıfı) | 360° mesafe taraması | Her örnekte sahneye ışın atılır; tarama hızı ve örnek sayısı veri sayfasından | A1: 8000 örnek/s, 5,5 Hz, 0,15–12 m, ≈ %1. LD06: 4500 örnek/s, 10 Hz, 0,02–12 m, ±45 mm. Koyu yüzey daha kısa menzil verir |
| **IMU** (MPU-6050, GY-521) | İvme ve açısal hız | Gövdenin fizikteki gerçek hareketinden özgül kuvvet ve dönme hızı alınır | Açılışta sapma, yavaş sapma kayması, beyaz gürültü (400 µg/√Hz, 0,005 °/s/√Hz), sayısal alçak geçiren süzgeç |
| **GPS** (NEO-6M, NEO-M8N) | Konum, hız | Ortamın coğrafi konumundan; NMEA çıkışı | CEP 2,5 m (M8N: 2,0 m), gökyüzü görüşüne göre büyür; soğuk başlatma 27 s, sıcak 1 s; en çok 5 Hz (M8N: 10 Hz) |
| **Pusula** | Manyetik yön | Ortamın manyetik alanı ve gövdenin yönü | Sapma ve gürültü |
| **Barometre** (BMP280, MS5611) | Basınç, yükseklik | Standart atmosfer (ISA), sensörün yüksekliğinde | MS5611: 10 cm çözünürlük, dönüşüm süreleri ve PROM doğrulaması |
| **İvmeölçer ±200 g** (ADXL375) | Kalkış ve çarpma ivmesi | Gövdenin gerçek ivmesi ve motor titreşimi | 49 mg/LSB, 13 bit, veri sayfası gürültüsü |
| **Derinlik** (MS5837) | Su basıncı, derinlik | p = p_atm + ρ·g·h | Sıvının yoğunluğu okumayı belirler |
| **Enkoderli motor** | Dönüş | Motor milinin fizikteki açısı | Darbe sayısı motor modelinden |
| **Kamera** | Görüntü | Aşağıya bakın | |

- **I²C ekranlar** (SSD1306 OLED, 16×2 LCD) kartın yazdıklarını 3D modelin üzerinde canlı gösterir.
- LED'ler ve lambalar yanar; aşırı ısınan parçalar kızarır, bozulan parçalardan kıvılcım çıkar.

### Kamera nasıl çalışır

Kamera (OV7670, OV2640 / ESP32-CAM, 720p USB, Pi kamera v2 / IMX219), sahneyi kendi merceğinden gerçekten görüntüler. Görüntü, sensörün yapacağı işlemlerden sırayla geçer:

1. **Çekim:** Kamera, merceğinin konumundan (nesnenin +X yönüne bakar) ve modelin yatay görüş açısıyla, seçilen çözünürlükte sahneyi çizer. Örneğin OV2640 için 54,5°, IMX219 için 62,2°.
2. **Pozlama:** Otomatik pozlama önce örtücü süresini, sonra kazancı ayarlar. Sahne ne kadar karanlıksa görüntü o kadar gürültülü olur.
3. **Gürültü:** Foton atış gürültüsü (piksel kuyusunun doluluğuna göre) ve okuma gürültüsü kazançla birlikte büyür. Değerler modelin piksel boyutundan ve kuyu kapasitesinden gelir.
4. **Hareket:** Kamera dönerken pozlama süresi boyunca bulanıklık oluşur. Yuvarlanan örtücü satırları sırayla okuduğu için hızlı dönüşte görüntü eğilir.
5. **Mercek ve çıkış:** Kenarlarda vinyet oluşur, sonra nicemleme yapılır. Sürücü JPEG istediyse görüntü JPEG'e sıkıştırılır.
6. **Kare hızı:** Sensörün o çözünürlükteki sınırına uyar (OV2640: UXGA'da 15, SVGA'da 30, CIF'te 60 kare/s). Bir ESP32 sürüyorsa DVP bağlantısının sınırı da geçerlidir.

ESP32 taslakları kamerayı gerçek `esp_camera` arayüzüyle kullanır: `esp_camera_init`, `esp_camera_fb_get`, `sensor->set_framesize`. Kamera kart tarafından beslenmiyorsa başlatma hata döndürür. **Sensörler** sekmesi her sensörün son ölçümünü ve kameranın son karesini gösterir.

![Ultrasonik sensör ve engel](../images/world-sonar.png)

## Kumandalar, radyo ve USB

![ESP32 ve PS5 kumandalı araba](../images/world-ps5-car.png)

**Parçalar → Kablosuz ve USB** kategorisinde: DualSense (PS5), DualShock 4 ve Xbox kumandaları, **Bilgisayar + USB kablosu**, **Wi-Fi Modem (2.4 GHz)**, **BLE Beacon Etiketi**, **IR Kumanda (NEC, 21 tuş)**, **TSOP38238 IR Alıcı**.

### Kumanda

1. Kumandayı seçin. Denetçide **Eşleştirme modu (PS + Create basılı)** ile ESP32'yle eşleştirin.
2. **Sanal kumandayı aç (fare / klavye)** ile ekrandaki kumandayı kullanın (W/S gaz, A/D direksiyon), ya da gerçek bir kumandayı USB veya Bluetooth ile bilgisayara bağlayın. Görünmezse **Kumandaları yeniden tara**.
3. **ESP32'ye uzaklık** kaydırıcısı (simülasyon çalışırken) sinyalin uzaklıkla nasıl zayıfladığını gösterir.
4. **Kumandayı aç/kapat** ve **Eşleşmeyi unut** düğmeleri de buradadır.

ESP32 seçiliyken **Örnek kodu yükle: kumandalı araba (Bluepad32)** hazır bir araba kodu yükler.

### PS5 kumandalı uçak

![Pistin başında PS5 kumandalı uçak: balsa kabin, karbon kuyruk borusu, kanatçıklı kanat, üç tekerlekli iniş takımı](../images/world-ps5-plane.png)

**PS5 kumandalı uçak** şablonu, bir model uçak sahasında asfalt pistin başında duran 1 m'lik bir eğitim uçağıdır. Hiçbir şey betikli değildir: uçağı siz uçurursunuz, fizik ve uçuş kartının yazılımı sonucu belirler.

- **Gövde:** içi boş balsa kabin (kanadın altında vidalı balsa kapak), Ø8 karbon kuyruk borusu, 4° dihedralli Clark Y kanat (dış yarılarında kanatçıklar; onları gövdenin içinde, kanadın tam altında duran bir SG90 burulma çubuklarıyla oynatır), itme çubuklarıyla irtifa ve yön dümenleri.
- **Güç:** 2212 1000KV fırçasız motor, 8×4,5" pervane, 20 A ESC, 3S 850 mAh LiPo. Kütle 402 g; ağırlık merkezi, kurşun konmadan yalnız pilin yeriyle veterin %30'unda.
- **İniş takımı:** çelik tel bacaklar ve alüminyum lama. Tekerlekler akslarında serbest döner; naylon göbeğin çelik tel üzerindeki sürtünmesi de hesaplanır.
- **Uçuş kartı:** ESP32 + MPU-6050, Bluepad32 ile DualSense'i okur. Kod gerçek bir ESP32'de de çalışır (ESP32'yi seçip **Kod**'a bakın).

| Kumanda | Klavye (sanal kumanda) | İşlev |
| --- | --- | --- |
| Options (≡), R2 bırakıkken | Enter | Motoru kurar (ışık çubuğu yeşil, kısa titreşim) |
| ✕ | Space | Motoru kilitler, gaz hemen kesilir (ışık kırmızı) |
| R2 | F | Gaz |
| Sağ çubuk sağ / sol | L / J | Kanatçıklar (yatış) |
| Sağ çubuk geri / ileri | K / I | İrtifa dümeni (burun yukarı / aşağı) |
| Sol çubuk sağ / sol | D / A | Yön dümeni |
| △ | R | **Dengeli** / **Manuel** mod (ışık yeşil / turuncu) |
| Yön tuşu yukarı / aşağı | ↑ / ↓ | Yunuslama trimi (0,5°) |

**Uçmak:** Options ile kurun, R2'yi sonuna kadar basın. Uçak pistte hızlanır; yaklaşık 9–10 m/s'de sağ çubuğu biraz geri çekin, kalkar. **Dengeli** modda çubuk bir açı ister (en çok 45° yatış, 25° yunuslama), bırakınca uçak kendini düzler. Gaz azaldıkça burun kendiliğinden biraz iner, böylece gazı kestiğinizde uçak hız kaybedip düşmez, süzülür (yaklaşık 7 m/s, 6:1). **Manuel** modda çubuk dümenleri doğrudan oynatır.

**Menzil ve arıza modu:** DualSense ile ESP32 arasındaki Bluetooth bağlantısı gerçek bir radyo bağlantısı gibi uzaklıkla zayıflar; açık sahada yaklaşık 90 m'de kopar (**RF** sekmesinde RSSI görünür). Bağlantı koparsa uçuş kartı motoru keser ve uçak burnu hafif aşağıda, 10° yatışla geniş bir daire çizerek süzülür. Kumanda yeniden menzile girince kontrol hemen geri gelir.

**Kamera:** **Sahne + Oyun** görünümünde (G) oyun tarafı **Pilot gözü** ile açılır: bir RC pilotunun gördüğü gibi, kumandanın durduğu yerden uçağa bakar ve uzaklığa göre yakınlaşır. **Takip kamerası** uçağın boyuna göre arkasından izler.

![Takip kamerası: tırmanan uçak, asfalt pist ve çim saha](../images/world-ps5-plane-flight.png)

### USB

**Bilgisayar** nesnesini bir karta yöneltip **USB'yi tak** deyin. Kablo takılıyken kart 5 V ile beslenir ve kartın seri portu **Seri monitör** sekmesine bağlanır. **USB'yi çıkar** ile ayırın.

**USB-C şarjlı lityum piller** (**Parçalar → Güç**): USB-C girişli 18650 hücre, 2S 18650 paket (7.4 V) ve sabit 1.5 V veren USB-C AA hücreler. İçlerinde gerçek elektronik çalışır: koruma devresi (aşırı deşarj 2.4 V, aşırı şarj, aşırı akımda keser; boş bir paket ancak şarj cihazı takılınca uyanır) ve sabit akım / sabit gerilim şarj devresi (4.2 V'a kadar 1 A, sonra akım azalır). **Bilgisayar + USB kablosu** nesnesini pile yöneltip takın: pil şarj olur, çıkarınca şarj durur.

### Radyo ortamı

Wi-Fi, BLE ve Bluetooth sinyalleri uzaklıkla, engellerle ve gövdelerle zayıflar. Alttaki **RF** sekmesinde cihazlar, sinyal gücü (RSSI), kanal ve saniyedeki paket sayısı görünür. **Sönümleme**, **Dalgalar** ve **Bağlantı çizgileri** seçenekleri radyo görselleştirmesini açar / kapatır. IR kumanda yalnızca görüş hattında çalışır; tuşları denetçideki **IR kumanda tuşları (NEC)** bölümündedir. Matematiği: [Radyo](../fizik/06-radyo.md).

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
- **Gerilme:** işaretlediğiniz her parça, "gerilme kare hızında" (varsayılan 15 kare/s) ve en yüklü anlarda, o anki gerçek yükleriyle FEM ile çözülür. İnce uzun parçalarda kiriş elemanları kullanılır. Gövdeler ve modellerin yanında servolar, motorlar ve kartlar da listededir. Üstüne bir şey takılı olanlar (ör. üstünde ikinci servo taşıyan servo) varsayılan olarak işaretlidir.
- **Hava akışı (3B, zamanla):** dünyadaki gerçek hava, kaydın tamamı boyunca zamanla çözülür (3B lattice-Boltzmann). Parçaları kayıttaki gibi hareket eden, dünyaya sabit bir kutu kullanılır:
  - hareket eden her parça havayı kendi yüzey hızıyla iter ve sürükler;
  - dünyanın rüzgârı (esintileriyle) kutunun yüzlerinden girer;
  - zemin bir duvardır;
  - kutu gerçekten hareket eden parçaların çevresine kurulur, hücre en ince hareketli parçayı çözecek kadar küçüktür.

  Hava başka hiçbir şeyle sürülmez. Dünyada rüzgâr yoksa ve hiçbir parça kıpırdamıyorsa akış da yoktur; **Günlük** bunu yazar. Hava, havayla taşınan **duman parçacıklarıyla** 3B gösterilir: her parçacık hızı yönünde kısa bir iz bırakır (mavi yavaş, kırmızı hızlı), durgun hava görünmez. Her karede havanın hareketli parçalara uyguladığı kuvvet de yazılır.
- **Rüzgâr tüneli testi (isteğe bağlı):** dünyada olmayan sabit bir havayla ölçüm yapar. Montaj birkaç anda kayıttaki duruşunda tutulur ve seçtiğiniz **hız**, **yön** (0° = +X, 90° = +Y) ve **eğimde** hava üflenir. Sonuç montaja etkiyen kuvvet, sürükleme katsayısı (Cd) ve akım çizgileridir. Notlar sonucun ne kadar güvenilir olduğunu yazar:
  - y+;
  - kuvvetin oturup oturmadığı;
  - iki ızgarada çözülen ilk andan gelen ızgara belirsizliği.

  Çözücülerin teoriden sapması: [12. Doğrulama](12-dogrulama.md).
- **Isınma:** kartlardaki bileşenlerin güç kaybından iletim, taşınım ve ışınım çözülür.

**Oynatma**

- **Oynat / duraklat:** Boşluk tuşu. **Kare kare ilerleme:** ← →. **Başa / sona:** Home / End. Hız ×0,05 ile ×2 arasında seçilir (ağır çekim).
- **Renk:** gerilme (von Mises, deforme şekil büyütülmüş) ya da hız okları. **Ölçek:** render'ın en yüksek değeri ya da her malzemenin akma sınırı.
- **Kuvvetler:** temas kuvvetleri turuncu, eklem kuvvetleri camgöbeği ok. **Hava:** o anki duman (rüzgâr tünelinde akım çizgileri ve aerodinamik kuvvet, mor ok).
- **Kablolar** render'da simülasyondaki iplerin kendisi olarak çizilir: sarkar, gerilir ve çekilen fiş yuvasından çıkar. Render sahnesi yalnızca izlenir; içindeki parçalar ve kablolar taşınamaz.
- Kanal aramasında yazarken Boşluk ve ok tuşları metne gider; oynatma kısayolları ancak kutunun dışındayken çalışır.
- **Grafikler:** kaydedilen her kanal. Grafiğe tıklayınca ya da sürükleyince o saniyeye atlanır.
- **Parçalar · gerilme:** her parçanın en kötü anı ve emniyet katsayısı. Satıra tıklayınca o ana gidilir.
- **Isı** ve **Günlük** sekmeleri; sonuçlar `.rwanalysis` ya da CSV olarak saklanır.

**Canlı girdiler de kaydedilir.** Kayıt sürerken elle verdiğiniz her girdi, etkili olduğu simülasyon anıyla birlikte saklanır: kumanda raporları (çubuklar, tetikler, tuşlar, hareket; yalnızca değiştiklerinde), kumandayı açıp kapama, eşleştirme, seri monitörden gönderilen metin, USB tak / çıkar, IR tuşları, bant başlat / durdur, ışık perdesi testi ve fırlatma anahtarı. Render bunları aynı anlarda yeniden uygular; **Günlük** sekmesi kaç girdinin oynatıldığını yazar. Nesneleri fareyle sürüklemek henüz kaydedilmez.

Render seçtiğiniz doğrulukla (adım boyu) hesaplar. Girdiler aynı olsa da kaotik bir hareket (ör. tutunma kaybındaki bir uçak) canlı koşudan zamanla biraz ayrışabilir.

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
