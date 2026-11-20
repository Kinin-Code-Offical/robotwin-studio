# 6. Montaj

**Montaj** sayfası (Ctrl+5) devrenizden otomatik olarak bir araç kurar ve onu 3 boyutlu bir arenada çalıştırır. Devredeki motorlar tekerlekleri veya pervaneleri döndürür, sensörler arenayı okur; yük ve çarpmalar devreye geri yansır.

> Birden çok kartı, serbest parçaları ve kendi gövdelerinizi bir arada kullanmak istiyorsanız **Dünya** sayfası daha esnektir: [7. Dünya](07-dunya.md).

## Hangi devreden ne kurulur?

| Devrede | Kurulan araç |
| --- | --- |
| İki motor | Robot araba (iki tekerlekten sürüş + sarhoş teker) |
| Bir uçuş kontrolcüsü + dört motor | X düzeninde quadcopter |

TCRT5000 çizgi sensörleri, IR engel sensörleri ve HC-SR04 ultrasonik sensörler araca otomatik eklenir.

## Adım adım

1. Hazır bir örnekle başlayın: **Başlangıç** sayfasında **Robot: Çizgi İzleyen**, **Robot: Engelden Kaçan (Arduino)** veya **Drone: Mikro Quadcopter**. Bu örnekler doğrudan Montaj sayfasında açılır.
2. Kendi devreniz için **Devreden montaj oluştur** düğmesine basın. Devreyi değiştirdikten sonra **Otomatik montaj** ile aracı yeniden kurun.
3. **Başlat** ile simülasyonu başlatın; durdurmak için yanındaki durdur düğmesini kullanın.
4. **R** aracı başlangıç noktasına geri alır (devre çalışmaya devam eder).

## Kamera

**Kamera** seçenekleri: **Takip**, **Üstten**, **Araç üstü**, **Serbest**.

## Drone kontrolü

| Tuş | İşlev |
| --- | --- |
| Boşluk | Motorları kur / kapat (arm / disarm) |
| W / S | Yüksel / alçal |
| A / D | Sola / sağa dön (yaw) |
| Ok tuşları | İleri, geri, sola, sağa |
| M | Uçuş modunu değiştir |

## Ayarlar paneli

- **Gövde:** şase / gövde kütlesi, tekerlek çapı, kol uzunluğu, pervane çapı.
- **Gövde modeli (3B):** kendi STL / OBJ / PLY gövdenizi yükleyin; kütle, ağırlık merkezi ve eylemsizlik hacimden hesaplanır.
- **Ortam:** arena (oval, sekiz, engeller, boş, rampalar), genişlik, sürtünme katsayısı, yerçekimi, sıcaklık, kenar duvarları.
- **Telemetri:** araba için hız, alınan yol, yön, motor devri, sensörler; drone için durum, irtifa, eğim, yön, hız, motorlar.

## Arıza deneyleri

Gerçekte denemesi tehlikeli ya da pahalı olan durumları güvenle deneyin:

- **… kısa devre** (pilin adıyla, örneğin BT1): pil uçlarını 50 mΩ ile kısa devre eder (ısınma, termal kaçak).
- **… sıkıştır** (motorun adıyla, örneğin M1): motor milini kilitler; motor durma akımı çeker ve ısınır.
- **Pervane 1…4 kır** (yalnızca drone): itki kaybı ve dengesizlik.
- **Tümünü onar:** yanan parçaları onarır, arızaları kaldırır.

Arızanın devreye etkisini **Devre** sayfasındaki osiloskop ve **Olaylar** sekmesinde izleyebilirsiniz.

## Kayıt

**Kaydı kaydet** bu koşuyu `.rwsim` dosyası olarak saklar: araç yörüngesi, olaylar, ölçüm kanalları, seri çıktı ve projenin kendisi. Kaydı daha sonra açıp inceleyebilirsiniz.

---

[← Önceki: 5. Modelle](05-modelle.md) · [İçindekiler](README.md) · [Sonraki: 7. Dünya →](07-dunya.md)
