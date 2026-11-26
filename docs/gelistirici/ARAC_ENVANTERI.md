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
| Bul (parça, hat, yazı) | yok | yok | 1 |
| Yakınlaştır, uzaklaştır, sığdır, nesnelere sığdır, seçime sığdır | Sığdır düğmesi, tekerlek | kısmi | 1 |
| Döndür, çevir (alt/üst), aynala | R, F tuşları, sağ tık | kısmi (araç çubuğunda yok) | 1 |
| Grupla, grubu çöz | yok | yok | 2 |
| Kilitle, kilidi aç | L tuşu, sağ tık | kısmi | 1 |
| Şemadan güncelle | Güncelle | var | |
| Ayak izi kütüphanesi, ayak izi düzenleyici | ayak izi seçici, Footprint düzenleyici | var | |
| DRC | DRC (önem, istisna, gezinme, rapor) | var | |
| Kart istatistikleri | İstatistikler… | var | |
| Ölç | Ölç (Ctrl+Shift+M) | var | |
| Aralık / kısıt çözümlemesi (iki öğe arası hangi kural) | yok | yok | 2 |
| Ayak izini kütüphaneyle karşılaştır | yok | yok | 3 |
| Damla (teardrop) | yok | yok | 2 |

### Yardımcı çubuk (etkin aracın seçenekleri)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Yol genişliği seçimi | W ile döner, liste yok | kısmi | 1 |
| Via boyutu seçimi | hat sınıfından | kısmi | 1 |
| Etkin katman | katman listesi | var | |
| Katman çifti (via ile geçilen) | yok | yok | 2 |
| Izgara, yakınlık | ızgara panelde, yakınlık yok | kısmi | 2 |
| Kilitleri geçersiz kıl | yok | yok | 3 |

### Sol çubuk (görünüm anahtarları)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Izgara aç/kapa | yok | yok | 2 |
| Kutupsal koordinat | yok | yok | 3 |
| mm / mil | panelde | var | |
| Tam ekran imleç | yok | yok | 3 |
| 45° kısıtı | Boşluk tuşu | kısmi | 2 |
| Ratsnest göster, eğri ratsnest | göster var, eğri yok | kısmi | 3 |
| Dolgu görünüşü (dolu / kenar) | aç/kapa var | kısmi | 2 |
| Yüksek kontrast | var | var | |
| Hat vurgula | ` tuşu | kısmi (araç yok) | 2 |

### Sağ çubuk (çizim araçları)

| Araç | Bizde | Durum | Öncelik |
| --- | --- | --- | --- |
| Seç | var | var | |
| Yerel ratsnest (bir parçanınki) | yok | yok | 3 |
| Ayak izi yerleştir | Parça ekle | var | |
| Yol | Yol (X) | var | |
| Fark çifti yolu | yok (yalnız denetim) | yok | 1 |
| Uzunluk ayarı (tek hat) | menderes aracı | var | |
| Fark çifti eğiklik ayarı | yok | yok | 2 |
| Via | Via aracı | var | |
| Bakır alan | Dolgu alanı | var | |
| Yasak bölge (kural alanı) | yok | yok | 1 |
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
| Görünüm: nesneler (görünürlük, saydamlık) | görünürlük var, saydamlık yok | kısmi | 2 |
| Görünüm: ağlar (renk, sınıf) | Hatlar sekmesi, renk yok | kısmi | 2 |
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
| Hat vurgula aracı | ` tuşu | kısmi | 2 |
| Sembol, güç sembolü | var | var | |
| Kablo | var | var | |
| Bus, bus girişi | yok | yok | 3 |
| NC işareti | var | var | |
| Bağlantı noktası (junction) aracı | otomatik | kısmi | 2 |
| Etiket: yerel, global | var | var | |
| Etiket: hiyerarşik, yönerge | yok | yok | 3 |
| Kural alanı | yok | yok | 3 |
| Hiyerarşik sayfa, sayfa pini | yok | yok | 3 |
| Metin, metin kutusu, tablo | yok | yok | 1 |
| Dikdörtgen, daire, yay, çizgi, Bezier, resim | yok | yok | 2 |
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
| Delik sihirbazı (havşa, vida, cıvata) | delik var, sihirbaz yok | kısmi | 1 |
| Radyüs (kenar yuvarlatma) | yok (yalnız pah) | yok | 1 |
| Pah | Pah kır (Ctrl+B) | var | |
| Kabuk (içini boşalt) | yok | yok | 1 |
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
| Kesit görünümü | yok | yok | 1 |
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
| Çakışma algılama | yok | yok | 1 |
| Boşluk doğrulama | yok | yok | 1 |
| Delik hizası | yok | yok | 2 |
| Patlatılmış görünüm | yok | yok | 1 |
| Bileşen çoğaltma | Çoğalt | kısmi | 2 |
| BOM | var | var | |

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
