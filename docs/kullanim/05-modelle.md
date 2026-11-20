# 5. Modelle

**Modelle** sayfası (Ctrl+4) robotunuzun mekanik parçalarını (şase, braket, kutu, tekerlek göbeği…) 3 boyutlu modellediğiniz yerdir. Çalışma şekli Blender'a benzer: nesne modu, düzenleme modu ve klavyeyle hızlı dönüşümler. Biten parçayı tek tuşla dünyaya gönderebilirsiniz.

![Modelle sayfası](../images/modeler.png)

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
- **Profil:** dikdörtgen, daire, çokgen, kanal veya serbest bir çizimi kalınlık vererek uzatın (ekstrüzyon).
- **Döndürme:** bir profili eksen etrafında döndürerek gövde oluşturun.
- **Robot parçaları:** 2WD şasi plakası, SG90 servo braketi, L braket, Tekerlek göbeği, Muhafaza kutusu + kapak. Bunların ölçüleri sonradan değiştirilebilir.

## Taşıma, döndürme, ölçekleme

1. Nesneyi tıklayarak seçin (**A** hepsini seçer, **Alt+A** seçimi kaldırır).
2. **G** taşı, **R** döndür, **S** ölçekle.
3. İsterseniz **X**, **Y** veya **Z** ile bir eksene kilitleyin ve bir sayı yazın (örneğin `G` `X` `20` → X yönünde 20 mm).
4. **Enter** onaylar, **Esc** iptal eder.

Ekrandaki taşıma / döndürme / ölçekleme tutamaçlarını da sürükleyebilirsiniz. **Yapış** açıkken ızgaraya yapışır; **Ctrl** basılıyken yapışma geçici olarak kapanır.

## Boolean (CSG) işlemleri

Delik açmak, cep oymak veya parçaları birleştirmek için:

1. Önce "araç" nesneleri, en son asıl nesneyi seçin (en son seçilen etkin nesnedir).
2. Araç çubuğundan seçin:
   - **Birleşim (∪):** araçları etkin nesneyle birleştirir.
   - **Fark (−):** araçları etkin nesneden çıkarır (delik, cep).
   - **Kesişim (∩):** yalnızca ortak hacmi bırakır.

Diğer nesne işlemleri: **Ctrl+J** birleştir, ayna kopya (X / Y / Z), doğrusal ve dairesel dizi, **Ctrl+P** ebeveyn yap, **Alt+P** ebeveyni kaldır, **Shift+D** çoğalt, **H** gizle, **Alt+H** hepsini göster.

## Düzenleme modu

**Tab** ile düzenleme moduna geçin. **1**, **2**, **3** tuşları nokta, kenar ve yüz seçimidir.

| Tuş | İşlev |
| --- | --- |
| E | Ekstrüzyon (yüzü dışarı çek) |
| I | İçe öteleme (inset) |
| F | Yüz doldur |
| M | Birleştir |
| P | Seçimi ayrı nesne yap |
| X veya Del | Sil |
| Shift+N | Normalleri yeniden hesapla |

**Alt böl**, **Yumuşat** (Catmull–Clark), mesafeye göre birleştirme, üçgenleme ve normalleri ters çevirme gibi işlemler panelde ve sağ tık menüsündedir.

## Malzeme ve kütle

Her nesneye bir malzeme (PLA, PETG, ABS, Naylon, Akrilik, Kontrplak, MDF, FR4, Alüminyum, Çelik, Karbon, Kauçuk) ve doluluk oranı (3D baskı için) verin. Kütle hacimden otomatik hesaplanır; dünyaya gönderdiğinizde bu kütle kullanılır.

## Görünüm modları

- **Katı** (Z), **Tel** kafes (Shift+Z), **X-ray** (Alt+Z), **Kenarlar**.
- **Ölç** aracı: iki noktaya tıklayın, mesafeyi gösterir (köşelere yapışır).

## Kaydetme ve dışa aktarma

- Model `.rwmodel` dosyası olarak kaydedilir (Ctrl+S).
- **İçe aktar:** STL, OBJ, PLY.
- **Dışa aktar:** STL (mm), OBJ, PLY. Açık (kapalı olmayan) ağlar için uyarı verir. 3D yazıcıya göndermek için STL kullanın.

## Dünyaya gönder

**Dünyaya gönder** düğmesi seçili nesneleri (seçim yoksa tüm modeli) hesaplanan kütlesiyle birlikte etkin dünyaya tek bir nesne olarak ekler. Açık bir dünya yoksa yeni bir dünya oluşturulur. Sonra parçayı dünyada motorlara, servolara ve diğer parçalara bağlayabilirsiniz: [7. Dünya](07-dunya.md).

---

[← Önceki: 4. Kod](04-kod.md) · [İçindekiler](README.md) · [Sonraki: 6. Montaj →](06-montaj.md)
