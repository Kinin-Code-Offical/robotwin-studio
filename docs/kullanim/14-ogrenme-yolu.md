# 14. Öğrenme yolu: beş düzey, uygulamalı görevler

Bu yol RobotWin Studio'yu baştan sona öğretir: önce devre, sonra kartın yazılımı, sonra mekanik ve PCB, sonra robotlar, en sonda endüstri hücreleri. Her düzey uygulamanın içindeki hazır bir örnekle başlar ve kendi başınıza yapacağınız görevlerle biter. Her görevin sonunda neyi gördüğünüzü denetleyebileceğiniz bir **beklenen sonuç** vardır; değerler simülasyonun kendi çözücülerinden gelir, gerçek bir tezgâhta ölçeceğinize yakındır.

Örnekler başlangıç sayfasındaki **Devre ve robot örnekleri** kartlarıdır (karta tıklayınca önce ne öğrettiği ve gereken araçlar görünür), dünya şablonları **Dünyalar** listesindedir. Bir örneği bozmaktan çekinmeyin: üstüne kaydetmedikçe aslı değişmez.

İlk kez açıyorsanız başlangıç sayfasındaki **İlk robotunuz** rehberiyle başlayın (5–10 dakika); bu yol onun devamıdır.

Her düzeyi uygulamanın içinde adım adım da yürüyebilirsiniz: **Yardım → Öğrenme yolu** ya da komut paletinde "öğrenme yolu", ya da düzeyin ilk örneğinin bilgi ekranında **Rehberi başlat**. Köşedeki kart örneği açar ve ne yapacağınızı söyler; gördüğü adımlarda (örnek açıldı, simülasyon çalışıyor, sayfa açık) kendiliğinden ilerler, göremediklerinde (bir değeri okumak, kendi düzenlemeniz) **Yaptım, sonraki** ile geçersiniz. Son kart düzeyin kendi görevidir ve sonraki düzeye götürür.

## Düzey 1: Elektronik temelleri

**Başla:** *LED ve Buton* örneği.

1. **F5** ile simülasyonu başlatın, butona basın. LED yanar. LED'e tıklayıp denetçinin üstündeki canlı değerlere bakın.
   - Beklenen: 9 V pil, 470 Ω ve kırmızı LED'le akım yaklaşık 15 mA; LED'in üstünde yaklaşık 2 V.
2. Direnci silin ve yeniden başlatın. LED'in akımı sınırını aşar ve LED yanar; nedeni parçanın üstünde ve alttaki **Olaylar** sekmesinde yazar. Geri alın (**Ctrl+Z**).
3. *RC Alçak Geçiren Filtre* örneğini açın, osiloskobu (alttaki **Osiloskop** sekmesi) çıkışa bağlayın.
   - Beklenen: 1 kΩ ve 1 µF ile zaman sabiti RC = 1 ms, köşe frekansı 1/(2πRC) ≈ 159 Hz; 1 kHz kare dalgada çıkış üstel yükselir, 1 ms sonra son değerin yaklaşık %63'ü.
4. **REF** ile bu dalgayı saklayın, kondansatörü iki katına çıkarıp yeniden çalıştırın: iki dalga üst üste görünür, durum satırı sapmayı yazar.
5. *Op-Amp Yükselteç* ve *7805 Güç Kaynağı* örneklerinde kazancı ve düzenleme düşümünü ölçün.

**Kendi göreviniz:** 5 V'tan 3,3 V'luk bir gerilim bölücü kurun ve 10 kΩ'luk bir yük altında çıkışın ne kadar düştüğünü multimetreyle ölçün.

## Düzey 2: Gömülü yazılım

**Başla:** *Arduino: Gerçek Firmware* örneği (yazılımı hazır derlenmiş gelir).

1. **Kod** sayfasını açın. `delay` değerini değiştirip derleyin (**F7**) ve çalıştırın (**F5**): LED'in yanıp sönme hızı değişir.
2. Kodda bir pin numarasının üstüne gelin: alttaki satır o pinin kartta neye bağlı olduğunu yazar ("D13 → R1" gibi), parça devrede de seçilir.
3. Bir satıra kesme noktası koyun (satır numarasına tıklayın), çalıştırın, **Değişkenler** ve **Yazmaçlar** sekmelerinde değerleri izleyin, adım adım ilerleyin.
4. Çizim `Serial`'a yazıyorsa Arduino TX pinini (D1) bit bit sürer: D1'e osiloskop probu bağlayın, **Çözücü**'de UART'ı seçin ve şeritteki bir bayta tıklayın; imleçler baytın başına ve sonuna gider.
5. *ESP32: PWM Işık Ayarı* örneğinde potansiyometreyi çevirin: LED'in parlaklığı PWM görev oranıyla değişir; osiloskopta PWM'i görün.
   - Beklenen: 5 kHz PWM; görev oranı potansiyometrenin konumuyla doğrusal.

**Kendi göreviniz:** butona basıldıkça bir sayacı artırıp LCD'ye yazan bir çizim yazın; butonun sıçramasını (debounce) yazılımla giderin ve osiloskopta sıçramayı görün.

## Düzey 3: Mekanik ve PCB

**Başla:** *MOSFET ile Motor Sürme* örneği.

1. **PCB** sayfasında kartı otomatik yerleştirin, yolları çizin ve **DRC** çalıştırın. Bir hatayı **Sorunlar** panelindeki anahtar düğmesiyle istisna olarak işaretleyin ya da düzeltin.
2. Devrede bir parça seçin: PCB'de de seçili olur (ortak seçim).
3. Üretime hazırlık özetine bakın, Gerber ve delik dosyalarını dışa aktarın.
4. **Parça** sayfasında bir motor tutucu çizin (eskiz, ölçü, ekstrüzyon), ağırlık merkezine ve kütlesine bakın.
5. *Elektronik kutusu* dünya şablonunda kartın kutuya sığdığını ve kablo deliklerini denetleyin.

**Kendi göreviniz:** 50 × 30 mm'lik bir kart tasarlayın, köşelerine M3 delikleri koyun, DRC'yi temiz geçirin ve kutusunu Parça sayfasında modelleyin.

## Düzey 4: Robotik

**Başla:** *Robot: Engelden Kaçan (Arduino)* örneği (Montaj'da kurulu açılır).

1. Montaj'da **▶ Dünyayı çalıştır** ile robotu kendi koduyla sürün. Bir motoru seçin: kabloları, bağlantıları, ağırlık merkezi ve çalışırken torku (**Teknik vurgu**) üstünde görünür.
2. *Arduino ile iki eksenli servo kol* şablonunda ters kinematiği açın, kolu bir hedefe gönderin; ulaşamadığı bir hedef verin ve nedenini okuyun (hangi eksenin sınırı ya da ne kadar uzakta).
3. *Sarkaç* şablonunda periyodu ölçün.
   - Beklenen: bileşik sarkaç için T = 2π√(I/mgh), 30°'den bırakılınca yaklaşık 1,11 s.
4. *Çizgi izleyen robot* şablonunda farklı bant türlerinde sensör okumalarını karşılaştırın; *Sensör gezgini*nde kamera, LiDAR ve IMU verilerine bakın.
5. *Mekanum tekerlekli araba*da tekerlek hızlarını ters kinematikle hesaplatın ve aracı yana sürün.

**Kendi göreviniz:** *Robot: Engelden Kaçan*ın kodundaki engel mesafesini ve dönüş süresini değiştirip parkuru en az çarpışmayla tamamlatın; **Deneyler** (13. bölüm) ile bu iki değeri tarayın ve %95 güven aralığıyla en iyisini seçin. *Robot: Çizgi İzleyen* ise yazılımsız, analog bir devredir: aynı işi dirençlerle yapar.

## Düzey 5: Endüstri

**Başla:** *Fabrika hücresi (robot kol + konveyör)* şablonu.

1. Hücreyi çalıştırın; istasyon çevrim süresine ve darboğaza bakın.
2. **Parçalar → Robot Hücresi → PLC** ile bir PLC ekleyin, girişlerine sensörleri, çıkışlarına konveyörü bağlayın ve Yapısal Metin (ST) ile yazın (7. bölüm, PLC). Merdiven diyagramında canlı değerleri izleyin, HMI panelinden hücreyi durdurup başlatın.
3. *SCARA al-bırak* şablonunda tutucunun zamanlamasını değiştirin, kaçırılan parçaları sayın.
4. Hücreyi Modbus TCP ya da OPC UA ile dışarıdan izleyin (7. bölüm).
5. Bir arıza senaryosu (sensör takılması, valf gecikmesi) ekleyip hücrenin nasıl davrandığını görün.

**Kendi göreviniz:** konveyöre ikinci bir istasyon ekleyin, çevrim süresini %20 kısaltacak sıralamayı bulun ve sonucu bir deney raporuyla gösterin.

## Sonra

- Kendi parçanızı kütüphaneye ekleyin, kendi fizik kodunuzu eklentiyle yazın (geliştirici rehberi: SDK).
- Bir projeyi KiCad'e aktarın, bir FMU modelini devreye takın, bir URDF robotunu dünyaya getirin.
- Bulduğunuz bir eksiği bildirin: **Yardım → Sorun bildir…**
