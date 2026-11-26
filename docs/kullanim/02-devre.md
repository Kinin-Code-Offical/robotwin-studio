# 2. Devre

**Devre** sayfası (Ctrl+1) şema çizdiğiniz ve devreyi canlı simüle ettiğiniz yerdir. Simülasyon gerçek bir analog devre çözücüsüyle çalışır: gerilimler, akımlar, ısınma ve parça hasarı hesaplanır.

![Devre sayfası: çizgi izleyen robot şeması ve osiloskop](../images/schematic.png)

Sayfanın bölümleri:

- **Solda** aranabilir parça listesi.
- **Ortada** şema tuvali ve üstte araç çubuğu (**Simüle et** düğmesi, hız seçimi).
- **Sağda** özellikler paneli: seçili parça, proje bilgisi, parça toleransı, kısayollar.
- **Altta** ölçü ve bilgi sekmeleri: **Osiloskop**, **Multimetre**, **Arıza tespiti**, **Monte Carlo**, **Kart pinleri**, **Seri monitör**, **Olaylar**.

## Yeni bir devre

1. **Dosya → Yeni devre / kart** (Ctrl+N).
2. Boş tuvalde şu yazar: "Soldan bir parça seçip tuvale tıklayın. Bir pinden başlayıp başka bir pine tıklayarak kablo çekin."

## Parça ekleme

1. Soldaki arama kutusuna parçanın adını yazın (örneğin `direnç 10k`, `LED`, `LM358`, `arduino`).
2. Listeden parçayı seçin ve tuvale tıklayarak yerleştirin.
3. Klavyeden **A** tuşu da aranabilir parça listesini açar.
4. **P** tuşu güç sembolü ekler (GND, +5V…).

Kütüphanede pasifler, diyot ve LED'ler, transistörler ve MOSFET'ler, op-amp'ler, 74HC lojik entegreler, regülatörler, piller ve güç kaynakları, anahtar ve röleler, DC motorlar ve servolar, sensörler, ekranlar ve I²C modüller, motor sürücüler, Arduino Uno / Nano, ESP32 kartları ve kablosuz cihazlar bulunur.

Arama kutusunun üstündeki kategori listesi iki seviyelidir. Ana kategorilerin (ör. **Pasif**, **Güç**, **Ölçüm & Kaynak**) altında alt kategoriler gelir (ör. **Direnç**, **Kondansatör**, **Güç kaynağı**, **Sinyal jeneratörü**). Her satır içindeki parça sayısını gösterir. Bir alt kategori seçince yalnızca o tür parçalar listelenir. Aynı düzen **Kütüphane** sayfasında da vardır: ana kategori düğmelerinin altında, seçilen kategorinin alt kategorileri ikinci bir satırda çıkar. Bir parçayı seçince sağda bir **Özellikler** tablosu görünür. Tabloda simülasyon modelinin bütün değerleri (bu parçanın varsayılandan farklı olanları kalın), bacakları ve modelin adı yazar.

![Kütüphane: Ölçüm & Kaynak → Güç kaynağı alt kategorisi ve ayarlanabilir AC/DC kaynağın özellikleri](../images/library-categories.png)

**Anahtarlamalı regülatör modülleri:** **Güç → Regülatör** altında düşürücüler (LM2596 3 A, MP1584 mini 3 A, XL4015 5 A) ve yükselticiler (MT3608, XL6009) vardır. Pilden ya da adaptörden 5 V ya da 12 V almak için kullanılır. Modülün IN+ / IN− uçları girişe, OUT+ / OUT− uçları yüke bağlanır; çıkış gerilimi kartın trimpotudur (**Çıkış gerilimi**). Model, çipin anahtarlama periyodu üzerinden ortalanmış hâlidir. Bobin akımı, akım sınırı, çıkış ve giriş kondansatörleri gerçektekiyle aynı biçimde çalışır:
- **Verim:** giriş, çıkış gücünü ve anahtar, bobin, diyot ve anahtarlama kayıplarını çeker. Örneğin LM2596 12 V'tan 5 V'a 1 A'de yaklaşık %85 verir.
- **Düşüm:** düşürücünün çıkışı girişin altında kalır. LM2596'nın anahtarı yaklaşık 1,1 V düşürür, bu yüzden 5,5 V'tan 5 V alınamaz.
- **Akım sınırı:** sınırı aşan yükte akım sınırda kalır, çıkış düşer.
- **Açılış:** giriş UVLO geriliminin altındayken modül kapalıdır. Açılınca çıkış yumuşak başlama süresinde yükselir; kondansatörlerin şarj akımı da görünür.
- **Isınma:** kayıplar modülü ısıtır ve ısıl kapanmaya kadar götürebilir.

**Modelin sınırları:** her modelin bir sınırı vardır. Kütüphanedeki parça sayfasında ve devredeki parçanın denetçisinde **Modelin sınırları** başlığı, o modelin neyi dışarıda bıraktığını ya da sadeleştirdiğini yazar. Örneğin regülatör modülünde anahtarlama dalgalanması yoktur, düğmede kontak sıçraması yoktur, MOSFET seviye 1 modeldir. Bir sonucu okurken buna bakın.

## Kablolama

1. Bir pine tıklayın; kablo başlar.
2. İstediğiniz yerlerde tıklayarak köşe koyun.
3. Hedef pine tıklayın; kablo biter. Çift tık veya **Esc** de bitirir.

Yardımcı tuşlar:

| Tuş | İşlev |
| --- | --- |
| W | Kablo aracı |
| / | Köşe yönü (önce yatay / önce dikey) |
| Shift+Boşluk | Kablo modu: 90° → 45° → serbest |
| Backspace | Son köşeyi geri al |
| L / Ctrl+L | Hat etiketi / global etiket |
| Q | Bağlantı yok (NC) işareti |

Aynı adı taşıyan etiketler birbirine bağlıdır; `GND` adı toprak demektir. Kabloya sağ tıklayınca **Böl**, **segment sil** ve **etiket** seçenekleri çıkar.

## Düzenleme

| Tuş | İşlev |
| --- | --- |
| M / G | Taşı / sürükle (G'de kablolar uzar) |
| R / Shift+R | Döndür |
| X / Y | Yatay / dikey aynala |
| Ctrl+D | Çoğalt |
| Ctrl+C / Ctrl+X / Ctrl+V | Kopyala / kes / yapıştır (sekmeler arasında da çalışır) |
| Del | Sil |
| E | Özellikler (birden çok parça seçiliyken de) |
| V, F2 veya çift tık | Değeri yerinde düzenle (örneğin `4k7`) |
| D | Parçanın datasheet'i |
| Ctrl+F / Ctrl+H | Bul / bul ve değiştir (`*` ve `?` joker karakterleri) |
| F1 veya ? | Tüm kısayollar |

- **Numaralandır:** parça adlarını (R1, R2, C1…) satıra veya sütuna göre sırayla verir; PCB ve montajdaki adlar da güncellenir.
- **Elektriksel kural denetimi (ERC):** bağlanmamış pinleri, tek pinli hatları, çıkış çakışmalarını ve beslemesiz güç girişlerini bulur.
- **PCB kılıfı:** parçanın PCB'de kullanacağı ayak izini seçin.

## Simülasyon

1. Araç çubuğunda **Simüle et**'e basın veya **F5**'e basın.
2. Simülasyon çalışırken:
   - Butonlara ve anahtarlara tıklayın.
   - Potansiyometreleri fare tekerleğiyle çevirin.
   - Bir kabloya tıklayın: osiloskop o noktayı ölçmeye başlar.
3. **F5** tekrar basılınca duraklatır, **Shift+F5** durdurur. **1 ms ilerlet** düğmesi tek adım ilerletir.
4. Hız seçimi: **0.01×**, **0.1×**, **Gerçek zaman**, **5×**, **20×**.

Durum çubuğunda simülasyon zamanı görünür. Bilgisayar gerçek zamana yetişemezse "gerçek zamanın %…'i" yazar.

### Parça hasarı

Gerçek hayatta yanacak bir devre burada da yanar: dirençsiz LED, aşırı akım çeken bir motor, ters takılmış bir elektrolitik kondansatör… Yanan parça işaretlenir ve **Olaylar** sekmesine yazılır.

- **Arıza tespiti** sekmesinde **Neden?** düğmesi parçanın neden yandığını açıklar.
- Devreyi düzelttikten sonra **Hasarlı parçaları onar** ile parçaları yenileyin.
- Hasarı tamamen kapatmak için **Simülasyon → Parça hasarı (yanma, sigorta)** seçeneğini kapatın.

### Kart pinleri

Kodu olmayan bir Arduino veya ESP32'nin pinlerini **Kart pinleri** sekmesinden elle sürebilirsiniz: giriş, pull-up, LOW, HIGH veya PWM.

## Tasarım alternatifleri

Aynı devrenin sürümlerini bir arada tutabilirsiniz: bazı parçaların takılmadığı, bazı değerlerin ya da parçaların başka olduğu alternatifler. Örneğin flyback diyotu olmayan "ucuz" bir sürüm, ya da 330 Ω yerine 1 kΩ ile pil ömrü uzun bir sürüm.

1. **Simülasyon → Tasarım alternatifi → Alternatifler…** penceresinde **Yeni alternatif** ile bir tane ekleyin, adını ve ne için olduğunu yazın.
2. Alternatif seçiliyken bir parçayı seçin. Denetçisinin **Alternatif** bölümünde **Bu alternatifte takılmaz** ya da **Bu alternatifte değer** ile o alternatifteki halini verin. Yukarıdaki parametreler bütün alternatiflerindir.
3. Takılmayan parça şemada soluk ve çarpı işaretli görünür, değeri değişen parçanın değeri vurgulu yazılır.

Seçili alternatif **Simüle et**'in yanındaki listeden değiştirilir. Simüle edilen, malzeme listesine (BOM), üretim dosyalarına (Gerber paketindeki BOM ve dizgi dosyası) ve montaja giren odur. Takılmayan parçanın pedleri kartta kalır; kart bütün alternatifler için birdir.

**Karşılaştır** her alternatifi ve çizildiği hali aynı 1 saniye simüle eder ve yan yana koyar: beslemenin ortalama ve tepe gücü, çekilen yük (mAh), en sıcak parça ve bozulan parçalar. **Kartların kodunu da çalıştır** seçiliyse (öntanımlı) her alternatifte Uno / Nano'ların derlenmiş kodu ve ESP32'lerin çizimi de çalışır. ESP32 çizimleri önce derlenir. Her kartın son yazdığı satır **Kart çıktısı** sütununda görünür; böylece bir alternatifin kodun davranışını nasıl değiştirdiği de karşılaştırılır. Seçili değilse yalnız devre çalışır.

![Tasarım alternatifleri](../images/variants-window.png)

Alternatifler parçaları adlarıyla değil kimlikleriyle izler: bir parçanın adını değiştirmek onun alternatiflerdeki halini korur.

## KiCad şemasını açma

**Dosya → Aç…** (Ctrl+O) bir KiCad şemasını (`.kicad_sch`, KiCad 6–10) yeni bir devre olarak açar; alt sayfalar aynı klasörden okunur.

- Bağlantılar KiCad'in kurallarıyla çözülür: tel uçları ve telin üstündeki etiketler bağlanır, bağlantı noktası olmayan kesişme bağlanmaz; aynı adlı etiketler sayfası içinde, global etiketler ve güç sembolleri bütün tasarımda, gizli güç pinleri adlarıyla birleşir; sayfa pini alt sayfadaki aynı adlı hiyerarşik etiketle birleşir (sayfa birden çok kez kullanılmışsa her kopyasında ayrı).
- Veri yolları (bus) da: vektörler (`D[0..7]`, üyeler sıralarıyla eşleşir), gruplar (`I2C{SDA SCL}`, üyeler adlarıyla), takma adlar (`{USB}`; şemada ya da projede tanımlı); bir veri yolu sayfa pinleri üzerinden alt sayfalara geçer.
- Netler KiCad'in kendi netlistesiyle aynıdır: KiCad'le gelen bütün örnek tasarımlarda (73 sayfalık olanlar dahil) `kicad-cli`'nin çıkardığı netlisteyle karşılaştırılarak denenir.
- Her sembol, kütüphane içe aktarmadaki kurallarla bir simülasyon modeline eşlenir; değeri (10k, 100nF, 5V…) ve kılıfı kendi alanlarından gelir. Pil sembolünün gerilimi gerçek bir kimyanın hücre sayısına çevrilir (9 V = 6 alkalin hücre); hiçbiri tutmazsa düzenleyicili bir Li-ion paket olur (5 V: iki hücre ve düzenleyici).
- Birimleri ayrı çizilmiş semboller (çift op-amp'ın iki yarısı) her birim için ayrı parça olur: U1A, U1B.
- Parçalar KiCad'deki yerlerinde ve yönlerinde durur; sembollerimiz daha büyük olduğu için çizim oranında açılır, sığmayan parça sağa ya da aşağı kayar. Pinler netlerine kısa bir tel ve net etiketiyle bağlanır; GND'ye toprak, diğer güç netlerine besleme sembolü konur. Bağlantısız bırakılmış pinler işaretleriyle gelir.
- Modeli olmayan ve bacak sayısına uyan genel parçası da olmayan semboller alınmaz; KiCad'de "takılmaz" (DNP) işaretli olanlar benzetime girmez. Açılışta hangilerinin alınmadığı söylenir.

![KiCad şemasından açılan devre](../images/kicad-schematic-import.png)

## FMU modelleri (FMI 2.0)

Modelica, Simulink, 20-sim, OpenModelica gibi araçlardan dışa aktarılan bir **FMU** (`.fmu`, FMI 2.0 ortak benzetim ya da model değişimi) devrede bir blok olarak çalışır.

- **Dosya → Aç…** bir `.fmu`'yu bloğu yerleşmiş yeni bir devre olarak açar; **Kütüphane** sayfasındaki **Dış kütüphane ▾ → FMU (FMI 2.0, .fmu)…** onu kütüphanenize parça olarak ekler. FMU dosyası parçanın içindedir: projeyle birlikte taşınır.
- Bloğun her **girişi** solda, her **çıkışı** sağda bir pindir; **REF** gerilimlerin ölçüldüğü ve sürüldüğü referanstır (çoğunlukla GND'ye bağlanır). Giriş, pinin REF'e göre gerilimini okur; çıkış, değerini 1 Ω'luk iç dirençle pine sürer.
- FMU'nun parametreleri parçanın parametreleridir (başlangıç değerleriyle); **İletişim adımı**, devre ile FMU'nun buluştuğu aralıktır (öntanımlı FMU'nun kendi adımı, yoksa 1 ms). Her adımda girişler verilir, FMU kendi çözücüsüyle ilerler, çıkışlar bir sonraki adıma kadar tutulur.
- 64 bit Windows ikilisi (`binaries/win64`) olan FMU'lar çalışır. Ortak benzetim (Co-Simulation) FMU'su kendi çözücüsüyle ilerler. Yalnız model değişimi (Model Exchange) olan FMU'nun durumlarını RobotWin'in kendi tümleyicisi çözer. Bu tümleyici uyarlamalı Dormand–Prince 5(4) yöntemidir; zaman olayları ve durum olayları (bir olay göstergesinin işaret değiştirdiği an, ikiye bölmeyle bulunur) FMI 2.0'ın istediği gibi işlenir.
- Devre ile FMU yalnız iletişim noktalarında buluşur: girişler o anki gerilimlerdir, çıkışlar bir sonraki noktaya kadar sabit tutulur (sıfırıncı derece tutma).

![FMU bloğu](../images/fmu-block.png)

## Dışa aktarma

Devre sayfasından şunları dışa aktarabilirsiniz:

- Malzeme listesi (BOM): aşağıda
- KiCad netlist (`.net`)
- SPICE netlist (`.cir`)
- Resim (PNG / SVG)
- Yazdır / PDF (Ctrl+P)

### Malzeme listesi ve tedarik

**BOM** komutu malzeme listesini bir pencerede açar. Aynı parçalar tek satırdır; ölçü aletleri, jeneratörler ve laboratuvar kaynakları simülasyonun araçlarıdır, karta alınmadıkları için listede yer almaz.

- Her satıra **üretici**, **üretici parça numarası** (MPN), **tedarikçi**, **tedarikçi parça numarası**, **birim fiyat** ve tedarikçinin **en az** satış adedi yazılır. Bunlar projeyle saklanır; modeli olan parçalarda (IRLZ44N, LM7805, 1N5819…) MPN kendiliğinden gelir.
- **Kart sayısı** verilince her satırın siparişi hesaplanır: kartların istediği adet, tedarikçinin en azından azsa en az kadar sipariş edilir (turuncu). Alt satırda toplam tutar, kart başına tutar ve fiyatı girilmemiş satır sayısı görünür.
- **CSV kaydet** bütün listeyi kaydeder.
- **JLCPCB / LCSC**, **Digi-Key** ve **Mouser** o tedarikçinin satırlarını kendi yükleme biçiminde verir: JLCPCB'nin dizgi BOM'u (Comment, Designator, Footprint, LCSC Part #), Digi-Key ve Mouser'ın toplu sepet dosyası.

## Sonraki adım

Devreniz hazırsa **PCB** sayfasına geçin (Ctrl+2) veya kartın kodunu yazın (Ctrl+3). Ölçü aletlerinin ayrıntıları [8. Ölçü aletleri](08-olcu-aletleri.md) bölümündedir.

---

[← Önceki: 1. Başlarken](01-baslarken.md) · [İçindekiler](README.md) · [Sonraki: 3. PCB →](03-pcb.md)
