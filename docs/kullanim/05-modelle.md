# 5. Parça

**Parça** sayfası (Ctrl+4) robotunuzun mekanik parçalarını (şase, braket, kutu, tekerlek göbeği…) 3 boyutlu modellediğiniz yerdir. Çalışma şekli Blender'a benzer: nesne modu, düzenleme modu ve klavyeyle hızlı dönüşümler.

Bir dosyada istediğiniz kadar parça olur:
- Sağdaki **Parçalar** listesinde en üstteki her nesne ayrı bir parçadır.
- Bir nesneyi ötekine bağlarsanız (**Ctrl+P**) ikisi tek parça olur; örneğin bir göbeğe takılı lastik.
- Biten parçaları **Montaja gönder** ile robotun [Montaj](06-montaj.md)'ına koyarsınız; her biri ayrı bir gövde olur.

![Parça sayfası](../images/modeler.png)

## Gezinme

| Fare / tuş | İşlev |
| --- | --- |
| Orta tuşla sürükle | Görünümü döndür |
| Shift + orta tuşla sürükle | Kaydır |
| Tekerlek | Yakınlaştır / uzaklaştır |
| Numpad 1 / 3 / 7 | Önden / sağdan / üstten görünüm |
| Numpad 5 | Perspektif / ortografik |
| Home | Her şeyi sığdır |
| Numpad . | Seçime odaklan |

## Nesne ekleme

**Ekle** düğmesi veya **Shift+A** ile:

- **Temel şekiller:** Kutu, Silindir, Koni, Küre, Simit, Boru, Kama, Delikli plaka.
- **Profil:** dikdörtgen, daire, çokgen, kanal veya serbest bir çizimi kalınlık vererek uzatın (ekstrüzyon). **Koniklik (draft)** açısı (SolidWorks'teki draft) duvarları yukarı doğru içe eğer, delikleri genişletir: kalıptan çıkacak ya da kolay basılacak parçalar için. Eksi açı tersine genişletir.
- **Geçiş (loft):** alt profilden üst profile geçen katı (SolidWorks ve Fusion'daki loft): kare tabandan yuvarlak ağza bir huni, bir hava kanalı geçişi. İki profil de dikdörtgen, daire, çokgen ya da kanal olabilir; **Yükseklik** aralarındaki mesafe, **Burulma** üst profili döndürür (burgu).
- **Süpürme (sweep):** bir kesiti 3B bir yol boyunca taşır (SolidWorks ve Fusion'daki sweep): kulp, boru, bükülmüş çubuk, kablo kanalı. Kesit profil düzenleyicisiyle, yol **Yol noktaları** kutusuna "x,y,z; …" (mm) yazılarak verilir. Köşelerde kesit ikiye bölen düzlemde durur, böylece duvar kalınlığı bükümde korunur; kesit yol boyunca kendi çevresinde dönmez.
- **Döndürme:** bir profili eksen etrafında döndürerek gövde oluşturun.
- **Robot parçaları:** 2WD şasi plakası, SG90 servo braketi, L braket, Tekerlek göbeği, Muhafaza kutusu + kapak. Bunların ölçüleri sonradan değiştirilebilir.

### Ölçülü eskiz

Bir profili ölçülü eskizle de çizebilirsiniz. Profili seçin, denetçide **Ölçülü eskize çevir…** düğmesine basın. Dikdörtgen dış şekil ve yuvarlak delikler ölçüleriyle gelir. Eskiz penceresinde:

1. **Çizgi**, **Dikdörtgen**, **Daire** ve **Yay** ile kabaca çizin. Yakın bir noktaya tıklamak o noktayı paylaşır; neredeyse yatay ya da dikey çizilen çizgi öyle kalır.
2. Seçip ilişki verin: yatay, dikey, paralel, dik, eşit, teğet, çakışık, orta nokta, eşmerkezli, simetrik, sabit.
3. Ölçü verin: uzunluk, mesafe, yatay/dikey ölçü, çap, yarıçap, açı. Değeri kısıt listesinden değiştirin.
4. İki çizginin birleştiği köşe noktasını seçince mm kutusunun yanında **Köşe radyüsü** ve **Pah** çıkar (SolidWorks'teki eskiz radyüsü ve eskiz pahı). Çizgiler geri kesilir; radyüs iki çizgiye teğet bir yayla (yarıçapı ölçü olarak gelir), pah düz bir kenarla birleştirir. Köşe, çizgilerin uzantısında sanal sivri olarak kalır; çizgilerin uzunluk ölçüleri ona kadar ölçmeye devam eder. **Yardımcı geometri** açıkken çizilenler profile girmez, yalnız yol gösterir.
5. **Aynala**: tek bir yardımcı çizgiyi (eksen) ve aynalanacak çizgi, daire ve yayları birlikte seçin. Kopyalar eksenin öbür yanına gelir; her kopya noktası aslına simetri ilişkisiyle bağlıdır, yani aslı değişince kopya da izler. Eksen üstündeki noktalar kopyalanmaz, paylaşılır; kopyalanan daireler aslıyla eşit kalır.
6. **Buda** (SolidWorks'teki güçlü budama): bir çizginin tıklanan parçası, iki yanındaki kesişimler arasında silinir; hiçbir şey kesmiyorsa çizginin tamamı gider. Kesilen uç, onu kesen öğenin üstünde tutulur. **Uzat**: çizginin tıklanan yakınındaki serbest ucu, önünde kesiştiği ilk çizgi, daire ya da yaya kadar uzar. Budanan ya da uzatılan çizginin uzunluk ölçüsü kaldırılır (artık o çizgi yok).
7. **Doğrusal çoğalt / Dairesel çoğalt** (SolidWorks'teki eskiz çoğaltma): çizgi, daire ya da yay seçince altta **Adet** (aslı dahil) ile X ve Y adımı (mm) çıkar; bir nokta da seçerseniz onun çevresinde döndürür, adımın yerine yayılma **Açı**sı gelir (360° tam tur, eşit aralık). Her kopya bir öncekine ölçüyle bağlıdır: sırada yatay ve dikey ölçü, dairede merkezden çıkan yardımcı kollar arasında eşitlik ve açı. Aslı taşınınca ya da büyüyünce bütün sıra izler; bir aralığın ölçüsünü kısıt listesinden değiştirebilirsiniz.

Her değişiklik hemen çözülür ve çizim ölçülere oturur. Pencerenin sağında eskizin durumu yazar:
- kaç serbestlik kaldığı;
- **tam kısıtlı** olduğu (her nokta ve yarıçap belirli, çizgiler yeşil);
- hangi kısıtın başkalarını tekrarladığı.

Öteki kısıtlarla çelişen bir kısıt geri alınır ve çeliştiği kısıtlar adıyla yazılır. Kapalı çizgi ve yay döngüleri profil olur: en büyüğü dış şekil, içindekiler delik.

Pencere kapandıktan sonra ölçüler denetçide de durur. Bir ölçüyü değiştirdiğinizde parça yeniden çözülür.

### Parametreler ve formüller

Hiçbir şey seçili değilken denetçide **Parametreler** bölümü vardır. Burada adlı değerler tanımlayın: `genislik = 80`, `yukseklik = genislik / 2`, `delik_x = genislik * 3 / 4`. Bir parametre sayı ya da öteki parametrelerden bir formül olabilir. Liste hangi sırada yazılırsa yazılsın, parametreler birbirine gereken sırayla hesaplanır. Döngü (a = b, b = a), tanımsız ad ve iki kez tanımlanan ad adıyla yazılır.

Bir parçanın ölçüsüne (kalınlık, çap…) ya da ölçülü eskizin bir ölçüsüne `=genislik` gibi bir formül yazınca o ölçü parametreye bağlanır. Bağlı ölçünün adının önünde **ƒ** görünür; kutuya tıklayınca formülü gösterir. Parametre değişince bağlı ölçüler de, eskizler de yeniden çözülür. Ölçüye düz bir sayı yazmak bağı kaldırır.

Formüllerde şunlar kullanılabilir:
- `+ − * / % ^` ve parantez;
- ondalık nokta ya da virgül (`2,5`);
- `pi`;
- derece cinsinden `sin cos tan asin acos atan atan2`, ayrıca `sqrt abs min max round floor ceil hypot`.

Fonksiyon değerlerini `;` ile ayırın (`max(a; b)`), ya da virgülden sonra boşluk bırakın (`max(a, b)`). Düz sayı alanlarına da `20*2` ya da `sqrt(2)*10` yazılabilir.

## Taşıma, döndürme, ölçekleme

1. Nesneyi tıklayarak seçin (**A** hepsini seçer, **Alt+A** seçimi kaldırır).
2. **G** taşı, **R** döndür, **S** ölçekle.
3. İsterseniz **X**, **Y** veya **Z** ile bir eksene kilitleyin ve bir sayı yazın (örneğin `G` `X` `20` → X yönünde 20 mm).
4. **Enter** onaylar, **Esc** iptal eder.

Ekrandaki taşıma / döndürme / ölçekleme tutamaçlarını da sürükleyebilirsiniz. **Yapış** açıkken ızgaraya yapışır; **Ctrl** basılıyken yapışma geçici olarak kapanır. Döndürme, Montaj ve Dünya'daki **Izgara ve yakalama** penceresinin döndürme adımına oturur (varsayılan 15°): üç sayfa aynı adımı kullanır. Tutamaçlar da aynı davranır: döndür halkalarının kameraya bakan yarısı çizilir ve tutulur, tutamaç sürüklerken yerinde kalır, dönülen açı halkanın düzleminde bir dilim ve yanında sayı olarak görünür.

## Boolean (CSG) işlemleri

Delik açmak, cep oymak veya parçaları birleştirmek için:

1. Önce "araç" nesneleri, en son asıl nesneyi seçin (en son seçilen etkin nesnedir; Shift+tık seçime ekler).
2. Özellikler panelindeki **Kes, kırp, birleştir** bölümünden (ya da araç çubuğundan, sağ tık menüsünden) seçin:
   - **Fark (kırp):** iç içe geçen araçların hacmini etkin nesneden oyar (delik, cep, kırpma).
   - **Birleşim:** araçları etkin nesneyle tek kapalı gövdede birleştirir.
   - **Kesişim:** yalnızca ortak hacmi bırakır.

Araç nesneleri sahneden kalkar ama kaybolmaz: her biri etkin nesnenin **Özellik geçmişi**ne bir özellik olarak girer (SolidWorks'teki özellik ağacı gibi). Sonuç yine kapalı (su geçirmez) bir gövdedir.

### Özellik geçmişi

Fark, birleşim, kesişim ve delik sihirbazının delikleri parçayı ağa çevirmez. Parçanın tabanı (örneğin kutu ve ölçüleri) olduğu gibi kalır, işlemler denetçide **Özellik geçmişi** altında sırayla listelenir ve parça her değişiklikte bunlardan yeniden kurulur. Bu yüzden kutuyu uzatınca yuva yine boydan boya keser.

- Her özelliğin kutusu onu **bastırır**: parçaya uygulanmaz, ama silinmez.
- **↑ / ↓** sırasını değiştirir (önce göbek eklenip sonra delinirse delik göbeği de deler).
- **Düzenle** özelliği açar: aracın kendi ölçüleri (silindirin çapı, boyu) ve parçaya göre **Konum X / Y / Z**; deliklerde **Ölçü**, **Tür** ve **Derinlik**.
- **Ayır** aracı yeniden ayrı bir nesne yapar; **Sil** özelliği geçmişten çıkarır.
- **Geçmişi ağa uygula** tabanı ve özellikleri tek bir düzenlenebilir ağa çevirir. Düzenleme kipi (Tab) ve yuvarla, pah gibi ağ işlemleri de bunu yapar.

Her değişiklik geri alınabilir.

### Delik sihirbazı

**Kes, kırp, birleştir** bölümündeki **Delik sihirbazı…** (SolidWorks'teki gibi) deliği çapla değil vida ölçüsüyle açar: **Tür** (geçiş deliği, silindir başlı vida yuvası, 90° havşa, kılavuz deliği, ısıyla gömme somun), **Ölçü** (M2–M8), **Geçme** (sıkı / normal / gevşek, ISO 273) ve **Derinlik** (0: boydan boya; gömme somunda somunun boyu). Ölçüler standartlardandır: silindir başlı yuva DIN 974-1, havşa ISO 15065, kılavuz çapları kaba diş. **Yüzeye yerleştir**'den sonra her tıklama tıklanan yüzeye, yüzeyin dik yönünde bir delik açar; sağ tık bitirir. Her delik geri alınabilir.

## Dilimleme

Bir nesneyi iki parçaya ayırmak için nesneyi seçin ve **K**'ye (ya da **Dilimle**) basın. Ekranda kesme çizgisinin başına ve sonuna tıklayın: nesne o çizgi boyunca, bakış yönünde kesilir. İki parça da kesit yüzüyle kapatılır, yani ikisi de kapalı gövdedir. Kesitteki delikler açık kalır (boru kesilince halka kesit çıkar). Sonra parçalardan birini seçip **G** ile taşıyabilir, **R** ile döndürebilirsiniz.

![Dilimlenmiş boru, pah kırılmış blok, kalınlık verilmiş tepsi](../images/modeler-cut.png)

Diğer nesne işlemleri: **Ctrl+J** birleştir, ayna kopya (X / Y / Z), doğrusal ve dairesel dizi, **Ctrl+P** ebeveyn yap, **Alt+P** ebeveyni kaldır, **Shift+D** çoğalt, **H** gizle, **Alt+H** hepsini göster.

**Referans geometri** (SolidWorks'teki referans düzlem, eksen ve nokta): **Ekle** (Shift+A) ▸ **Referans düzlem**, **Referans eksen** ya da **Referans nokta** seçime göre yapılır. Düzlem: düzenleme kipinde seçili üç noktadan, seçili bir düzlemden 20 mm uzakta, seçili iki paralel düzlemin ortasında ya da seçili bir eksene dik. Eksen: iki noktadan, seçili yuvarlak bir parçanın (silindir, koni, boru, simit, döner gövde) ekseninde ya da seçili iki düzlemin kesiştiği yerde. Nokta: seçili noktada ya da seçili parçaların ortasında. Seçim yoksa görünümün ortasında yatay bir düzlem, dik bir eksen ya da bir nokta gelir. Referansların katısı yoktur: dışa aktarmaya, kütleye ve montaja girmez; ince bir çerçeve, kesik bir eksen çizgisi ya da bir artı olarak adıyla çizilir ve çizgisine tıklanarak seçilir. Taşıma, döndürme ve ebeveyn bağı her nesnedeki gibidir; denetçide yalnız yeri ve çizim boyu vardır. Bir düzlem parçalarla birlikte seçiliyken sağ tık ▸ **Ayna kopyası · Düzlem1 düzlemine göre** parçaları o düzleme göre aynalar; bir eksen seçiliyken **Dairesel dizi…** kopyaları o eksenin çevresinde dağıtır.

Blender'daki gibi **N** sağdaki yan paneli (parçalar ve özellikler), **T** soldaki araç rafını gizler ve geri getirir; **Ctrl+Space** görünümün çevresindeki her şeyi kaldırır.

## Düzenleme modu

**Tab** ile düzenleme moduna geçin. **1**, **2**, **3** tuşları nokta, kenar ve yüz seçimidir. Seçili noktaları, kenarları ya da yüzleri **G** / **R** / **S** ile taşıyın, döndürün, ölçekleyin (nesne modundaki gibi eksen kilidi ve sayı girişiyle).

Seçim: **Alt+tık** (sürüklemeden) tıklanan kenarın halkasını seçer, yüz modunda yüz halkasını; **Shift+Alt+tık** ekler. **L** seçimle bağlantılı bütün parçayı seçer. **Ctrl+Numpad +/−** seçimi bir halka büyütür ya da küçültür.

| Tuş | İşlev |
| --- | --- |
| E | Ekstrüzyon: yüzleri normal yönünde çıkarır; nokta/kenar modunda seçili kenarlardan yeni yüz çekip taşır |
| I | İçe öteleme (inset) |
| K | Bıçak: ekrana çizilen çizgi boyunca keser (seçim varsa yalnız seçili yüzleri). **C** kipi değiştirir: kes / sağ tarafı sil / sol tarafı sil; silinen tarafın açıklığı kapatılır |
| Ctrl+R | Halka kesim: fareyle bir kenarın üzerine gelin, dörtgen halkası boyunca yeni kenar halkaları eklenir; tekerlek veya +/− kesim sayısını değiştirir |
| Ctrl+B | Pah kır (bevel): seçili kenarların yerine düz pah yüzü; fareyle genişlik ya da mm yazın. Üç pahın buluştuğu köşeye köşe yüzü eklenir. Tekerlek ya da **+** parça sayısını artırır: pah yuvarlanır |
| Yuvarla (radyüs) | SolidWorks'teki radyüs: seçili kenarlar iki yüze teğet dairesel bir yayla yuvarlanır (8 parçayla başlar, tekerlekle değişir); her kenarı yuvarlanmış köşe küre parçasıyla kapanır, gövde kapalı kalır |
| F | Yüz doldur |
| M | Birleştir |
| P | Seçimi ayrı nesne yap |
| X veya Del | Sil |
| Shift+N | Normalleri yeniden hesapla |

**Kalınlık ver** yüzeye içe doğru et kalınlığı verir: açık bir yüzey kalın bir duvar olur, kapalı bir gövde içi boş kabuğa döner (örneğin üst yüzü silinmiş kutu → tepsi). Köşelerde kalınlık eşit kalır. **Kabuk** (SolidWorks'teki gibi) aynısını seçili yüzleri açarak yapar: yüz modunda (3) açılacak yüzleri seçin (kutunun üstü, bir yan), **Kabuk**'a basıp duvar kalınlığını sürükleyin ya da yazın; gövde açık ağızlı, eşit duvarlı bir muhafaza olur.

**Alt böl**, **Yumuşat** (Catmull–Clark), mesafeye göre birleştirme, üçgenleme ve normalleri ters çevirme gibi işlemler panelde ve sağ tık menüsündedir.

![Düzenleme modu: halka kesimler ve kalınlık verilmiş tepsi](../images/modeler-cut-edit.png)

## Malzeme ve kütle

Her nesneye bir malzeme (PLA, PETG, ABS, Naylon, Akrilik, Kontrplak, MDF, FR4, Alüminyum, Çelik, Karbon, Kauçuk) ve doluluk oranı (3D baskı için) verin. Kütle hacimden otomatik hesaplanır; dünyaya gönderdiğinizde bu kütle kullanılır. Özellikler panelinin **Ölçüm** bölümü boyutları, hacmi, yüzey alanını, kütleyi, ağırlık merkezini ve asal atalet momentlerini gösterir. **Kütle özellikleri…** tam raporu açar (SolidWorks'teki gibi): ağırlık merkezinde atalet tensörü (g·mm², dünya eksenleri), asal eksenler ve momentler; birden çok nesne seçiliyse toplamları (paralel eksen teoremiyle). **Panoya kopyala** raporu metin olarak verir.

### 3B baskı denetimi

Basılan bir malzemedeki (PLA, PETG, ABS, Naylon) parçanın denetçisinde **3B baskı** bölümü vardır. Yazıcınızı seçin; parça durduğu gibi (Z yukarı, en alt noktası tablada) denetlenir:
- **Tabla:** yazıcının baskı hacmine sığıp sığmadığı (yan çevrilmiş hâli de denenir).
- **Ağ:** kapalı olup olmadığı; açık ağ dilimlenemez.
- **Sarkma:** 45°'den yatık, aşağı bakan ve tablada olmayan yüzlerin alanı; bunlar destek ister. Altı yönden biri daha az sarkma veriyorsa yazılır, **Bu yüzü tablaya çevir** parçayı o yöne döndürür.
- **Duvar:** her yüzden parçanın içinden karşı yüze ölçülen en ince duvar. İki çizgiden (0,9 mm) ince yerler uyarılır.
- **Tabla teması** ve **tahmin:** filament (g, m), süre ve katman sayısı. Tahmin; çevre duvarlarını, dolulukla iç kısmı ve sarkmaların altındaki desteği yazıcının akışıyla sayar.

## Görünüm modları

- **Katı** (Z), **Tel** kafes (Shift+Z), **X-ray** (Alt+Z), **Kenarlar**.
- **Ölç** aracı: iki noktaya tıklayın, mesafeyi gösterir (köşelere yapışır).
- **Kesit** (SolidWorks'teki kesit görünümü): bütün nesneler X, Y ya da Z boyunca bir düzlemle kesilir, kameraya bakan yarı kaldırılır ve kesit yüzü kendi renginde dolu gösterilir; duvarlar, delikler ve kaburgalar görünür. Görünümün üstündeki çubukta eksen seçilir, kaydırıcı düzlemi modelin içinden geçirir, **Ters** öbür yarıyı gösterir. Yalnız görüntüdür: nesneler değişmez; düzenleme modundaki nesne kesilmez. Çubuktaki **Özellikler…** SolidWorks'teki kesit özellikleridir: düzlemdeki kesitin alanı, çevresi, ağırlık merkezi, ağırlık merkezinden geçen eksenlere göre atalet momentleri ve çarpımı, asal momentler ve asal eksenin açısı, kutupsal moment, atalet yarıçapları ve kesit modülleri (mm, mm², mm³, mm⁴). Seçili nesnelerin, seçim yoksa görünenlerin kesiti alınır; sonuç panoya kopyalanabilir. Bir kirişin ya da profilin eğilmeye ne kadar dayanacağını görmek için kullanılır.

## Kaydetme ve dışa aktarma

- Model `.rwmodel` dosyası olarak kaydedilir (Ctrl+S).
- **İçe aktar:** STL, OBJ, PLY ve CAD dosyaları: STEP (.step, .stp), IGES (.iges, .igs). CAD dosyaları Open CASCADE ile ağa çevrilir; montajdaki her gövde yerinde ve renginde gelir.
- **Dışa aktar:** STL (mm), OBJ, PLY ve STEP (mm). STEP'te üçgenler dikilip kapalıysa katı gövdeye çevrilir; SolidWorks, Fusion, FreeCAD gibi CAD programları gövde olarak açar. Açık (kapalı olmayan) ağlar için uyarı verir. 3D yazıcıya göndermek için STL kullanın.

## Montaja gönder

**Montaja gönder** düğmesi seçili parçaları (seçim yoksa hepsini) projenin [Montaj](06-montaj.md)'ına gönderir:

- Her parça hesaplanan kütlesiyle ayrı bir gövde olur.
- Parçalar modellendikleri düzenle gelir ve montajda var olanların yanına, yere kurulur.
- Projenin montajı henüz yoksa önce kurulur. Parça sayfasından ayrılmazsınız.
- Bir parçayı değiştirip yeniden gönderince yerinde güncellenir. Kendi orijini ve bağlantıları yerinde kalır.
- İç içe duran iki ayrı parça için uyarı çıkar. Silinen parçalar montajdan kaldırılsın mı diye sorulur.

![Montaja gönderilen parçalar robotun yanında](../images/montaj-parts.png)

Bir parçayı test dünyasına engel ya da rampa gibi **tek bir gövde** olarak koymak için sağ tık menüsünden **Test dünyasına tek gövde olarak koy** deyin. Bu seçenek montajı değil, açık bir test dünyasını hedefler.

---

[← Önceki: 4. Kod](04-kod.md) · [İçindekiler](README.md) · [Sonraki: 6. Montaj →](06-montaj.md)
