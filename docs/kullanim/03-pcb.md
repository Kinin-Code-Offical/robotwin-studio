# 3. PCB

**PCB** sayfası (Ctrl+2) devrenizi baskılı devre kartına dönüştürür: parçaları yerleştirir, bakır yolları çizer, kuralları denetler ve üretime gönderilecek dosyaları çıkarırsınız.

![PCB sayfası](../images/pcb.png)

## Şemadan karta

1. Devreyi **Devre** sayfasında çizin.
2. **PCB** sayfasına geçin. Şemadaki parçalar karta gelir; bağlanması gereken pinler ince çizgilerle (bağlantı çizgileri) gösterilir.
3. Şemayı sonradan değiştirirseniz PCB sayfasına döndüğünüzde kart kendiliğinden güncellenir, yerleşiminiz korunur. Değişiklik yerleşimden bir şey götürecekse (karttan kalkan ya da ayak izi değişen parça, artık olmayan hatlardaki iz ve via'lar) kart olduğu gibi kalır ve sayfanın üstünde ne olacağı söylenir: **Kartı güncelle** ile uygulanır. O zamana kadar **Güncelle** düğmesi vurgulu durur, üzerine gelince her değişikliği listeler. Karttan kalkan parçanın yeri hatırlanır; şemaya geri dönerse oraya konur.
4. **Parça ekle** ile kütüphaneden doğrudan karta parça koyabilirsiniz; parça şemaya da eklenir.

![Kart şemanın gerisinde: güncellemeden önce soruyor](../images/pcb-ask.png)

## Kart ayarları

**Kart ayarları** düğmesi şunları açar:

- Bakır katman sayısı: **2**'den **32**'ye kadar çift sayılar (Üst bakır, İç 1 … İç 30, Alt bakır), KiCad'deki gibi. Seçilen yığın kart kalınlığına sığmıyorsa pencere en az gereken kalınlığı yazar; kalınlığı artırın ya da prepreg'i inceltin.
- Kart kalınlığı, bakır ağırlıkları, dielektrik.
- Kör / gömülü via'lar.
- Hat sınıfları (yol genişliği ve boşluk) ve üretim kuralları.

![Kart ayarları: 12 katmanlı yığın](../images/pcb-stackup.png)

Kart kenarını sol araç çubuğundaki **kart kenarı** aracıyla çokgen olarak çizebilir, **montaj deliği** ekleyebilirsiniz.

## Yerleştirme

- Parçaları fareyle sürükleyin. **R** döndürür, **F** parçayı alt / üst yüze çevirir, **L** kilitler.
- **Yerleştir** düğmesi bağlantılara göre otomatik yerleşim yapar; kilitli parçalar yerinde kalır.

## Yol çizimi

### Elle (etkileşimli)

1. **X** tuşuna basın (yol aracı).
2. Bir pede tıklayın ve fareyi hedefe doğru götürün. Yol, engellerin etrafından dolaşır ve boşluk kurallarına uyar.
3. Tıklayarak ara noktaları sabitleyin; hedef pede tıklayınca biter.

| Tuş | İşlev |
| --- | --- |
| V | Via koy ve katman değiştir |
| PgUp / PgDn | Via ile üst / alt katmana geç |
| / | Açı yönü (önce çapraz / önce düz) |
| Boşluk | 45° / 90° mod |
| W / Shift+W | Yol genişliği: sonraki / önceki |
| Backspace | Son parçayı geri al |
| Enter veya çift tık | Bitir |
| Esc | İptal |

Çizilmiş bir yolu **G** ile sürükleyebilirsiniz; bağlı uçlar da gelir.

### Otomatik

**Rota çiz** düğmesi tüm bağlantıları otomatik çizer: bütün katmanlarda arar, hat sınıfı genişlik ve boşluklarına uyar, gerekirse iç düzlemlere via atar.

## Bakır alanlar (dolgu)

1. **Z** ile dolgu alanı çizin veya hazır **+ GND (üst)** seçeneğini kullanın. 4 ve daha çok katmanlı kartlarda **+ GND düzlemi (İç 1)** de vardır.
2. **Doldur** ile alanları doldurun. **B** doldurur, **Ctrl+B** dolguyu boşaltır.

Dolgu, boşluk, ısıl bağlantı (thermal relief), öncelik ve ada temizleme kurallarına göre yapılır.

## Tasarım kural denetimi (DRC)

**DRC** düğmesi kartı denetler:

- Hat sınıfı boşlukları, yol genişliği, delik ve halka boyutu
- Kart kenarına uzaklık
- Parça avlusu (courtyard) çakışması, ped üstünde serigrafi
- Bağlanmamış hatlar, boşta kalan yol ve via'lar
- Güncel olmayan dolgular
- Yol akım kapasitesi

Hatalar listede görünür; birine tıklayınca kartta o yer gösterilir.

## 3D görünüm

**3D** düğmesi kartı parçalarıyla birlikte 3 boyutlu gösterir. Simülasyon çalışırken LED'ler yanar, ısınan parçalar kızarır.

## Üretim dosyaları

| Düğme | Ne üretir |
| --- | --- |
| **Üretim** | `<ad>-gerber.zip`: tüm bakır katmanların Gerber dosyaları, lehim maskesi, pasta, serigrafi, kart kenarı, PTH / NPTH / kör delik (Excellon) dosyaları, BOM ve dizgi dosyası (CPL) |
| **KiCad'e aktar** | `<ad>-kicad.zip`: KiCad 8 projesi (`.kicad_pro`, `.kicad_sch`, `.kicad_pcb`), netlist, sembol ve ayak izi kütüphaneleri, SPICE ve Gerber |

Gerber ZIP'ini doğrudan kart üreticisine yükleyebilirsiniz.

## Ayak izi ve modül

- **Footprint:** ayak izi düzenleyicisi. SMD ve THT pedler, serigrafi, avlu ve üretim katmanı çizip kullanıcı kütüphanenize kaydedersiniz.
- **Modül yap:** bu kartı kütüphaneye tek bir parça olarak kaydeder. Başka devrelere tek parça gibi eklenir, içi yine tam simüle edilir.

## Görünüm

| Tuş | İşlev |
| --- | --- |
| Home veya F | Sığdır |
| + / − / tekerlek | Yakınlaştır / uzaklaştır |
| Orta tuş veya sağ tuşla sürükle | Kaydır |
| PgUp / PgDn | Etkin katman |
| H | Yalnızca etkin katmanı göster / hepsini göster |
| 1 tuşunun solundaki tuş | İmleçteki hattı vurgula |
| Ctrl+Shift+M | Ölç |
| Ctrl+Shift+H | Ölçü çizgisi |
| T | Serigrafi yazısı |
| F1 veya ? | Tüm PCB kısayolları |

---

[← Önceki: 2. Devre](02-devre.md) · [İçindekiler](README.md) · [Sonraki: 4. Kod →](04-kod.md)
