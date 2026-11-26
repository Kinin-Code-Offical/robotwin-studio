# Araç envanteri: referans uygulamalar ve RobotWin Studio

KiCad 10, SolidWorks 2026 ve Blender 5.3'ün araçları tek tek, bizdeki karşılıklarıyla. Liste 26 Kasım 2026'da üç uygulamanın yerinde incelenmesiyle çıkarıldı; her yeni araçla güncellenir.

**Durum:**

- **var:** aynı işi gören araç var.
- **kısmi:** iş yapılıyor ama araç olarak yok (yalnız kısayol ya da menü) ya da eksik.
- **yok:** yapılamıyor.

**Öncelik:**

- **1:** sık kullanılan, eksikliği hemen hissedilen.
- **2:** tasarımı tamamlayan.
- **3:** ileri düzey.

Her eksik gerçek işleviyle eklenir: model, çizim, kayıt, dışa aktarma, denetim (DRC/ERC) ve test. Yalnızca düğme eklenmez.

## PCB (KiCad PCB düzenleyici)

### Üst araç çubuğu ve İncele menüsü

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Yeni, aç, kaydet | Dosya menüsü ve başlık çubuğu | var | |
| Kart ayarları (katman yığını, kurallar) | Kart ayarları… | var | |
| Yazdır, çizdir (PDF/SVG) | yalnız Gerber, KiCad | kısmi | 2 |
| Geri al, yinele | Ctrl+Z / Ctrl+Y, araç çubuğunda | var | |
| Bul (parça, hat, yazı) | Bul (Ctrl+F), görünüm çubuğunda | var | |
| Yakınlaştır, uzaklaştır, sığdır, nesnelere sığdır, seçime sığdır | görünüm çubuğu (kartın sağ üstü) | var | |
| Döndür, çevir (alt/üst), aynala | görünüm çubuğu, R, F | var | |
| Grupla, grubu çöz | yok | yok | 2 |
| Kilitle, kilidi aç | görünüm çubuğu, L | var | |
| Şemadan güncelle | Güncelle | var | |
| Ayak izi kütüphanesi, ayak izi düzenleyici | ayak izi seçici, Footprint düzenleyici | var | |
| DRC | DRC (önem, istisna, gezinme, rapor) | var | |
| Kart istatistikleri | İstatistikler… | var | |
| Ölç | Ölç (Ctrl+Shift+M) | var | |
| Aralık / kısıt çözümlemesi (iki öğe arası hangi kural) | Aralık çözümlemesi (palet): iki öğe (hat, via, parça, dolgu), ağlar ve sınıflar, gereken aralık ve kaynağı, her ortak katmanda gerçek aralık | var | |
| Ayak izini kütüphaneyle karşılaştır | yok | yok | 3 |
| Damla (teardrop) | Damla: ped ve via, eğri / düz, uzunluk ve genişlik oranı ve sınırları; hattın çizgisinde, ovalin ucunda çember; başka ağa aralığı bozacaksa daraltılır ya da konmaz; çizim, Gerber, döküm aralığı (KiCad dışa aktarımına yazılmaz) | var | |

### Yardımcı çubuk (etkin aracın seçenekleri)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Yol genişliği seçimi | yol seçenek çubuğu, W | var | |
| Via boyutu seçimi | yol / via seçenek çubuğu | var | |
| Etkin katman | katman listesi | var | |
| Katman çifti (via ile geçilen) | Katmanlar sekmesinde iki liste; V yol çizerken via koyup çiftin öbürüne, çizmezken etkin katmanı çiftin öbürüne geçirir (saklanır; kartta olmayan çift üst ↔ alt) | var | |
| Izgara, yakınlık | ızgara panelde, yakınlık yok | kısmi | 2 |
| Kilitleri geçersiz kıl | yok | yok | 3 |

### Sol çubuk (görünüm anahtarları)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Izgara aç/kapa | tuvalin üstündeki araç kutusunda ızgara düğmesi (ayarlarda saklanır, yakalama değişmez) | var | |
| Kutupsal koordinat | yok | yok | 3 |
| mm / mil | panelde | var | |
| Tam ekran imleç | yok | yok | 3 |
| 45° kısıtı | Boşluk tuşu | kısmi | 2 |
| Ratsnest göster, eğri ratsnest | göster var, eğri yok | kısmi | 3 |
| Dolgu görünüşü (dolu / kenar) | aç/kapa var | kısmi | 2 |
| Yüksek kontrast | var | var | |
| Hat vurgula | ` tuşu ve sol şeritte Hattı vurgula aracı (tıklanan öğenin hattı; aynısı ya da boşluk kaldırır) | var | |

### Sağ çubuk (çizim araçları)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Seç | var | var | |
| Yerel ratsnest (bir parçanınki) | yok | yok | 3 |
| Ayak izi yerleştir | Parça ekle | var | |
| Yol | Yol (X) | var | |
| Fark çifti yolu | Fark çifti yolu (ortak orta hat, gönyeli köşe, 45° ped çıkışı, uzunluk ve fark, ters uç ve yakın bakır uyarısı) | var | |
| Uzunluk ayarı (tek hat) | menderes aracı | var | |
| Fark çifti eğiklik ayarı | yok | yok | 2 |
| Via | Via aracı | var | |
| Bakır alan | Dolgu alanı | var | |
| Yasak bölge (kural alanı) | Alanlar grubunda (DRC, dolgu, iki yönlendirici, KiCad) | var | |
| Çizgi, yay, dikdörtgen, daire, çokgen | Çizim grubu | var | |
| Bezier, resim | yok | yok | 3 |
| Yazı | Yazı | var | |
| Yazı kutusu, tablo | yok | yok | 3 |
| Ölçü (hizalı) | Ölçü çizgisi | var | |
| Ölçü (dik, yarıçap, kılavuz, merkez) | yok | yok | 2 |
| Silici (tıklananı sil) | Del | kısmi | 3 |
| Izgara orijini, delik orijini | yok | yok | 3 |

### Paneller

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Görünüm: katmanlar | Katmanlar | var | |
| Görünüm: nesneler (görünürlük, saydamlık) | görünürlük anahtarları; yol, via, ped, dolgu saydamlığı (kaydırıcı + yazılır, saklanır) | var | |
| Görünüm: ağlar (renk, sınıf) | Hatlar sekmesi: sınıf, vurgula ve ağ rengi (12 düz renk; bakır her katmanda o renkte, kartla kaydedilir, üretim dosyalarını değiştirmez) | var | |
| Görünüm ön ayarları, görüş alanları | yok | yok | 3 |
| Seçim süzgeci | var | var | |
| Özellikler paneli | var (taşınır, küçülür, ayrılır) | var | |

## Şema (KiCad şema düzenleyici)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Sayfa ayarları, yazdır, çizdir | PNG/SVG, yazdır | var | |
| Bul / bul ve değiştir | var | var | |
| Yakınlaştırma düğmeleri | tekerlek, sığdır | kısmi | 2 |
| Hiyerarşide geri / yukarı / ileri | hiyerarşi yok | yok | 3 |
| Döndür, aynala | var | var | |
| Grupla | yok | yok | 3 |
| Sembol düzenleyici, sembol tarayıcı | Parça oluşturucu, kütüphane | var | |
| Numaralandır | var | var | |
| ERC (önem, istisna) | var | var | |
| Simülatör | yerleşik | var | |
| Ayak izi ata, alan tablosu, BOM | BOM penceresi; ayak izi parçada | kısmi | 2 |
| Seç | var | var | |
| Hat vurgula aracı | ` tuşu ve araç çubuğunda Hattı vurgula aracı (pin, tel, etiket; kartta da vurgular) | var | |
| Sembol, güç sembolü | var | var | |
| Kablo | var | var | |
| Bus, bus girişi | yok | yok | 3 |
| NC işareti | var | var | |
| Bağlantı noktası (junction) aracı | otomatik | kısmi | 2 |
| Etiket: yerel, global | var | var | |
| Etiket: hiyerarşik, yönerge | yok | yok | 3 |
| Kural alanı | yok | yok | 3 |
| Hiyerarşik sayfa, sayfa pini | yok | yok | 3 |
| Metin, metin kutusu | Metin (T), çerçeveli = metin kutusu | var | |
| Tablo | yok | yok | 3 |
| Dikdörtgen, daire, yay, çizgi, Bezier, resim | Çiz: çizgi, dikdörtgen, daire, yay (seçilir, taşınır, döndürülür, kopyalanır; KiCad'e gider ve gelir; görüntüye çizilir); Bezier ve resim sonra | kısmi | 3 |
| Silici | Del | kısmi | 3 |
| Izgara, birim, imleç, gizli pinler | ızgara var | kısmi | 3 |
| Kablo açı kipi (90° / 45° / serbest) | var | var | |
| Ağ gezgini | Hatlar sekmesi | var | |
| Özellikler paneli | var (taşınır, küçülür, ayrılır) | var | |

## Parça modelleme (SolidWorks parça, Blender)

### Eskiz

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Çizgi, dikdörtgen, daire, yay | eskiz düzenleyici | var | |
| Çokgen, kanal (slot), elips, spline | denetlenecek | kısmi | 2 |
| Eskiz radyüsü, eskiz pahı | yok | yok | 2 |
| Ofset | var | var | |
| Budama (trim), uzatma | budama var | kısmi | 2 |
| Eskizde aynala, doğrusal / dairesel çoğaltma | yok | yok | 2 |
| Akıllı ölçü, ilişkiler (çakışık, teğet, eş merkezli, yatay, dikey) | kısıtlar var | var | |
| Yapı (construction) geometrisi | yok | yok | 2 |

### Unsurlar

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Ekstrüzyon ile katı | Ekstrüzyon, yüz çıkarma | var | |
| Döndürerek katı | Döner gövde | var | |
| Süpürme, loft, sınır ile katı | yok | yok | 2 |
| Ekstrüzyon ile kes, döndürerek kes | Boolean fark | var | |
| Delik sihirbazı (havşa, vida, cıvata) | Delik sihirbazı (geçiş, silindir başlı, havşa, kılavuz, gömme somun; M2–M8, ISO ölçüleri; yüzeye tıkla) | var | |
| Radyüs (kenar yuvarlatma) | Yuvarla (radyüs): iki yüze teğet dairesel yay, parça sayısı, küresel köşeler | var | |
| Pah | Pah kır (Ctrl+B) | var | |
| Kabuk (içini boşalt) | Kabuk (seçili yüzleri açar, içe eşit duvar) ve Kalınlık ver | var | |
| Draft, kaburga (rib) | yok | yok | 2 |
| Doğrusal / dairesel çoğaltma | var | var | |
| Aynala | Ayna kopyası | var | |
| Referans düzlem, eksen, nokta | yok | yok | 2 |
| Helis, sarma (wrap) | yok | yok | 3 |
| Anında 3B (tutamaçla boyutlandırma) | tutamaçlar | var | |
| Düzenlenebilir özellik geçmişi | parametrik parçalar | kısmi | 1 |

### Değerlendir

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Ölç | Ölç | var | |
| Kütle özellikleri | Kütle özellikleri… | var | |
| Kesit özellikleri | yok | yok | 2 |
| Kesit görünümü | Kesit (X/Y/Z, kaydırıcı, ters, kesit yüzü kendi renginde) | var | |
| Geometri denetimi (kapalı ağ) | Durum: kapalı / açık ağ | var | |
| Draft, eğrilik, simetri analizi | yok | yok | 3 |
| Gövde karşılaştır | yok | yok | 3 |
| SimulationXpress (gerilme) | FEM analizi (Analiz) | var | |
| FloXpress (akış) | akış analizi | var | |

### Blender modelleme

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Ekle (temel şekiller) | Ekle (Shift+A) | var | |
| Nesne / düzenleme kipi, nokta / kenar / yüz seçimi | var | var | |
| Çıkar (extrude), içe kaydır (inset), pah (bevel), alt böl | var | var | |
| Döngü kesimi (loop cut), bıçak (knife) | yok | yok | 2 |
| Döndürerek çoğaltma (spin) | yok | yok | 3 |
| Birleştir (merge), yüz oluştur | var | var | |
| Orantılı düzenleme | yok | yok | 3 |
| Yapışma, görünüş kipleri, tel kafes, X-ray | var | var | |
| Tam görünüm (Ctrl+Space), N/T panelleri | Ctrl+Space var; N/T kısmi | kısmi | 2 |

## Montaj (SolidWorks montaj)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Bileşen ekle, taşı, döndür | var | var | |
| Standart ilişkiler (çakışık, paralel, dik, teğet, eş merkezli, mesafe, açı) | Hizala | var | |
| Gelişmiş ilişkiler (sınır, genişlik, simetrik) | denetlenecek | kısmi | 2 |
| Mekanik ilişkiler (dişli, kam, kremayer) | denetlenecek | kısmi | 2 |
| Çakışma algılama | Çakışma algılama (hacim, değenler, bağlı parçalar, yalnız seçili) | var | |
| Boşluk doğrulama | Boşluk doğrulama (en az boşluk, GJK ile kesin mesafe, iki uç arası çizgi) | var | |
| Delik hizası | yok | yok | 2 |
| Patlatılmış görünüm | Adımlar (yön, uzaklık, bağlı parçalar birlikte), otomatik (eksen / dışa doğru, zincir kat kat), kaydırıcı, oynat / topla | var | |
| Bileşen çoğaltma | Çoğalt | kısmi | 2 |
| BOM | var | var | |

## Montaj ve Dünya (Unity sahne editörü)

Unity 6000.6 yerinde incelendi (26 Kasım 2026): sahne araçları, kaplamalar, sahne ağacının sağ tık menüleri, Add Component'in bütün kategorileri, bileşen menüleri. Montaj ve Dünya aynı sahne editörünü kullanır; buradaki her araç ikisinde birden olur, Parça'nın tutamaçları aynı davranışa getirilir.

### Açılış ekranı

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Tam kaplayan görsel, sol üstte ürün adı ve sürüm, telif, görselin sahibi, yüklenen proje ve adım, kenarda ince ilerleme çubuğu | aynı düzen; görsel şimdilik lisansı serbest stok fotoğraf (kendi görselimiz gelince değişir) | var | |

### Sahne araçları

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Taşı (W): eksen okları, iki eksenin düzlem kareleri (kameraya bakan çeyrekte), ortası ekran düzleminde | Taşı tutamacı | var | |
| Döndür (E): X / Y / Z halkaları (ön yarı), görüş halkası, içi serbest, sürüklerken açı dilimi | Döndür tutamacı | var | |
| Ölçekle (R): eksen küpleri, ortada eşit ölçek | Ölçekle tutamacı (gövdeler, arazi nesneleri) | var | |
| Hepsi bir arada (Y) | Hepsi bir arada | var | |
| Dikdörtgen (T) | yok | yok | 3 |
| Pivot / Center, Global / Local | Pivot / Merkez, Yerel / Dünya | var | |
| Adımlı sürükleme (taşı 0,25 · döndür 15° · ölçek 1) | ızgara ve 15° varsayılan, Alt serbest | var | |
| Izgara ve yakalama penceresi: ızgarayı göster, en yakın ızgara, ızgara düzlemi X/Y/Z, saydamlık, ızgara boyu, adım (bağlı X/Y/Z), ızgaranın konumu ve dönüşü, etkin nesneden kopyala, dünyaya sıfırla | Izgara ve yakalama: yakala aç / kapa, ızgarayı göster, saydamlık, taşıma · döndürme · boyut adımları, seçiliyi ızgaraya oturt, varsayılana dön (ızgara düzlemi, eksen başına adım, ızgaranın konumu sonra) | kısmi | 3 |
| Köşe yakalama (V): köşeden köşeye, köşeden yüzeye, pivotu köşeye | yok | yok | 2 |
| Yüzeye yakalama (Ctrl+Shift) | Ctrl+Shift ile serbest sürüklemede ve taşı tutamacının ortasında: parçanın tabanı imlecin altındaki parça, arazi ya da zemin yüzeyine (kendisi ve takılıları atlanır) | var | |
| Bakışla döndürme (bir yüzeye çevir) | yok | yok | 3 |
| Kaplama menüsü: araçlar, araç seçenekleri, ızgara ve yakalama, çizim kipleri, görünüm seçenekleri, arama, yön, kameralar | yok | yok | 2 |

### Sahne ağacı (Hierarchy)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Kes, kopyala, yapıştır; alt nesne olarak yapıştır (yerel ya da dünya dönüşümünü koruyarak) | Kes / kopyala / yapıştır (takılı olanlar, bağlantılar, kablolar, ilişkiler); seçilinin üstüne takılı yapıştır | var | |
| Yeniden adlandır, çoğalt, sil | F2 satırda; çoğalt ve sil takılı olanlarla, "yalnız bunu sil" | var | |
| Tümünü seç, seçimi kaldır, tersine çevir, alt nesneleri seç; ağaçta çoklu seçim | Ctrl+tık (sahne ve ağaç), ağaçta Shift+tık aralık, sahnede Shift+sürükle dikdörtgen, Ctrl+A; tutamaç, kopyala, kes, çoğalt, sil hepsine (tersine çevir, alt nesneleri seç sonra) | kısmi | 3 |
| Boş nesne, boş alt nesne, boş üst nesne (grup) | Boş nesne (Ctrl+Alt+N), boş alt (Alt+Shift+N), boş üst (Ctrl+Shift+G); düzenleyicide grup, çalıştırmada yok (altındakiler onun takılı olduğu yere geçer) | var | |
| Sağ tıkta Oluştur ▸ türler (gövde şekilleri, ışık, kamera, ses, sensör, kart …) | Oluştur ▸ gövde şekilleri ve palet sekmeleri | kısmi | 2 |
| Etkin / devre dışı (Alt+Shift+A) | adın solunda kutu, Alt+Shift+A, ağaç menüsü; kapalı nesne ve üstündekiler sahneden ve çalıştırmadan çıkar (fizik, devre, sensörler), ağaçta soluk | var | |
| Görünüme taşı, görünümle hizala, görünümü seçime hizala | Görünüme taşı, Odakla | kısmi | 3 |
| Sürükleyerek üst nesne değiştirme | Bağla aracı | kısmi | 2 |
| Sahnede referansları bul | kablolar, bağlantılar, ilişkiler | var | |
| Varsayılan üst nesne | yok | yok | 3 |

### Denetçi (Inspector)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Bileşen yığını: her bileşen kendi başlığında (aç / kapa, katla, yardım, ⋮ menüsü) | bileşen kartları: katla, aç / kapa kutusu (kabuk, kap, paraşüt, arıza; ayarlar saklanır), ? (kılavuzun o bölümü, yoksa adıyla arama), Kaldır, ⋮ menüsü | var | |
| Bileşen ekle: aranabilir, kategorili liste | Bileşen ekle: arama, kategoriler, Enter, alt kısımda açıklama; eklenen bileşen kendi kartında açık gelir, görünüme kayar, vurgulanır; Kaldır ve ⋮ → Bileşeni kaldır (bileşenler: kabuk, kap, paraşüt, hava, arıza, eşleme, yay, rulman, davranış) | var | |
| Bileşen ⋮: sıfırla, kaldır, yukarı / aşağı, kopyala, değerleri yapıştır, yeni olarak yapıştır | kaldır, kopyala, değerleri yapıştır, yeni bileşen olarak yapıştır (kabuk, kap, paraşüt, arıza); Fizik'te sıfırla ve değerleri kopyala / yapıştır; Konum'da kopyala / yapıştır / sıfırla (yukarı / aşağı yok: kartların sırası sabit) | var | |
| Dönüşüm ⋮: konum / dönüş / ölçek / dünya dönüşümü kopyala ve yapıştır, özelliği sıfırla | konum / dönüş kopyala, yapıştır; dönüşü sıfırla; zemine indir | var | |
| Fizik gövdesi: kütle, doğrusal / açısal sönüm, kütle merkezi ve eylemsizlik otomatik ya da elle, yerçekimi, kinematik, ara değerleme, çarpışma algılama, konum / dönüş kilitleri (X/Y/Z) | kütle, sabit, doğrusal / açısal sönüm, konum / dönüş kilitleri (Kısıtlar ve sönüm); kütle merkezi, kinematik sonra | kısmi | 2 |
| Çarpışma şekilleri: kutu, küre, kapsül, ağ, tekerlek, arazi; tetik | çarpışma şekilleri | kısmi | 2 |
| Eklemler: menteşe, sabit, yay, karakter, yapılandırılabilir; sınırlar, yaylar, kırılma kuvveti ve torku | menteşe, kızak, sabit, yay ve sönüm, rulman | kısmi | 2 |
| Kısıtlar: hedefe bak, üst nesne, konum, dönüş, ölçek | yok | yok | 2 |
| Sabit kuvvet | yok | yok | 2 |
| Etiket, katman (çarpışma katmanları), statik | yok | yok | 2 |

## Sıra

Öncelik 1 olanlar şu gruplar hâlinde yapılır:

1. **PCB:**
   - Üst çubuk eylemleri: bul; yakınlaştırma düğmeleri; döndür, çevir, aynala, kilitle.
   - Yol seçenek çubuğu: genişlik ve via listesi.
   - Yasak bölge: DRC, yönlendirici ve dolgu ona uyar.
   - Fark çifti yolu.
2. **Şema:** metin ve metin kutusu (sayfa notları).
3. **Parça:** radyüs, kabuk, delik sihirbazı, kesit görünümü.
4. **Montaj:** çakışma algılama, boşluk doğrulama, patlatılmış görünüm.

Sonra öncelik 2 ve 3. Her grup bitince bu tablo güncellenir.

Durum: öncelik 1'in ilk grupları bitti (PCB, Şema, Parça, Montaj). Unity incelemesinden çıkan öncelik 1'lerden ızgara ve yakalama penceresi, sahne ağacının sağ tık menüsü, etkin / devre dışı, bileşenli Denetçi, Bileşen ekle, bileşen ve dönüşüm menüleri, fizik gövdesinin sönüm ve kilitleri bitti; çoklu seçim ve boş nesneler de geldi; boş nesneler, bileşen kartının ? yardımı ve sahnede dikdörtgenle seçim de geldi; bileşeni aç / kapa da geldi.
