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

1. Önce "araç" nesneleri, en son asıl nesneyi seçin (en son seçilen etkin nesnedir; Shift+tık seçime ekler).
2. Özellikler panelindeki **Kes, kırp, birleştir** bölümünden (ya da araç çubuğundan, sağ tık menüsünden) seçin:
   - **Fark (kırp):** iç içe geçen araçların hacmini etkin nesneden oyar (delik, cep, kırpma).
   - **Birleşim:** araçları etkin nesneyle tek kapalı gövdede birleştirir.
   - **Kesişim:** yalnızca ortak hacmi bırakır.

Araç nesneleri işlemden sonra kaldırılır; sonuç yine kapalı (su geçirmez) bir gövdedir.

## Dilimleme

Bir nesneyi iki parçaya ayırmak için nesneyi seçin ve **K**'ye (ya da **Dilimle**) basın. Ekranda kesme çizgisinin başına ve sonuna tıklayın: nesne o çizgi boyunca, bakış yönünde kesilir. İki parça da kesit yüzüyle kapatılır, yani ikisi de kapalı gövdedir. Kesitteki delikler açık kalır (boru kesilince halka kesit çıkar). Sonra parçalardan birini seçip **G** ile taşıyabilir, **R** ile döndürebilirsiniz.

![Dilimlenmiş boru, pah kırılmış blok, kalınlık verilmiş tepsi](../images/modeler-cut.png)

Diğer nesne işlemleri: **Ctrl+J** birleştir, ayna kopya (X / Y / Z), doğrusal ve dairesel dizi, **Ctrl+P** ebeveyn yap, **Alt+P** ebeveyni kaldır, **Shift+D** çoğalt, **H** gizle, **Alt+H** hepsini göster.

## Düzenleme modu

**Tab** ile düzenleme moduna geçin. **1**, **2**, **3** tuşları nokta, kenar ve yüz seçimidir.

| Tuş | İşlev |
| --- | --- |
| E | Ekstrüzyon: yüzleri normal yönünde çıkarır; nokta/kenar modunda seçili kenarlardan yeni yüz çekip taşır |
| I | İçe öteleme (inset) |
| K | Bıçak: ekrana çizilen çizgi boyunca keser (seçim varsa yalnız seçili yüzleri). **C** kipi değiştirir: kes / sağ tarafı sil / sol tarafı sil; silinen tarafın açıklığı kapatılır |
| Ctrl+R | Halka kesim: fareyle bir kenarın üzerine gelin, dörtgen halkası boyunca yeni kenar halkaları eklenir; tekerlek veya +/− kesim sayısını değiştirir |
| Ctrl+B | Pah kır (bevel): seçili kenarların yerine düz pah yüzü; fareyle genişlik ya da mm yazın. Üç pahın buluştuğu köşeye köşe yüzü eklenir |
| F | Yüz doldur |
| M | Birleştir |
| P | Seçimi ayrı nesne yap |
| X veya Del | Sil |
| Shift+N | Normalleri yeniden hesapla |

**Kalınlık ver** yüzeye içe doğru et kalınlığı verir: açık bir yüzey kalın bir duvar olur, kapalı bir gövde içi boş kabuğa döner (örneğin üst yüzü silinmiş kutu → tepsi). Köşelerde kalınlık eşit kalır.

**Alt böl**, **Yumuşat** (Catmull–Clark), mesafeye göre birleştirme, üçgenleme ve normalleri ters çevirme gibi işlemler panelde ve sağ tık menüsündedir.

![Düzenleme modu: halka kesimler ve kalınlık verilmiş tepsi](../images/modeler-cut-edit.png)

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
