# 6. Montaj

**Montaj** (Ctrl+5) projenizin devresini gerçek bir montaja çevirir ve onu bir dünya sekmesinde açar. Burada hazır bir araç şablonu yoktur: devredeki her parça gerçek 3B parçası olarak gelir, şaseyi, tekerlekleri, eklemleri ve kabukları siz kurarsınız. Her şey [Dünya](07-dunya.md) sayfasındaki gerçek fizikle çalışır.


## Neyi nereye koyar?

| Devrede | Dünyada |
| --- | --- |
| PCB'ye yerleştirilmiş parçalar | Kartın üstünde kalır; kart tek bir nesnedir, devresi, PCB'si ve kodu içindedir |
| PCB'de olmayan parçalar (motor, servo, pil, sensör, Arduino/ESP32 modülü…) | Her biri kendi gerçek 3B parçası olarak kartın çevresine yerleşir |
| Hatlar (net) | Kartın pinleriyle parçaların pinleri arasında kablolar; atlama kabloları 5 cm adımlarla gerçek uzunlukta |

Kartın pinleri kablolarda `RefDes.Pin` biçiminde görünür (ör. `J2.1`).

## Adım adım

1. Bir devre açın ya da **Başlangıç** sayfasından bir robot örneği seçin (**Robot: Çizgi İzleyen**, **Robot: Engelden Kaçan (Arduino)**, **Drone: Mikro Quadcopter**).
2. **Montaj**'a geçin (Ctrl+5). Montaj bir kez kurulur; sonraki seferlerde aynı dünya sekmesi geri gelir.
3. **Mekanik** sekmesinden bir şase (plaka ya da [kabuk](07-dunya.md#mekanik-gövdeler)), tekerlekler, sarhoş teker ekleyin; parçaları **Bağla** (B) ile şaseye, tekerlekleri motor millerine sabitleyin.
4. **F5** ile çalıştırın. Motorlar devrenin hesapladığı hızla döner, sensörler dünyayı ölçer, yük ve çarpmalar devreye geri yansır.

> Örnek: denizaltıdan çift kanatlı uçağa kadar her şeyi aynı araçlarla kurarsınız; şablon sınırı yoktur. Kanatlar, kabuklar, basınçlı kaplar ve itme çubukları için bkz. [7. Dünya](07-dunya.md).

## Kartın içine bakmak

Kart nesnesini seçip **Kartı düzenle (devre · PCB · kod)** deyin: devre, PCB ve kod kendi sekmesinde açılır; yaptığınız değişiklik dünyadaki karta işlenir.

## Kayıt ve arıza deneyleri

Montaj bir dünya olduğu için dünyanın bütün araçları burada da çalışır: **Kayıt** ile simülasyonu kaydedip render penceresinde yeniden oynatabilir, pil kısa devresi ya da motor sıkışması gibi arızaları deneyebilir, **Görünüm → Isı kamerası** ve **Görünüm → Çarpışma şekilleri** ile içeri bakabilirsiniz.

---

[← Önceki: 5. Modelle](05-modelle.md) · [İçindekiler](README.md) · [Sonraki: 7. Dünya →](07-dunya.md)
