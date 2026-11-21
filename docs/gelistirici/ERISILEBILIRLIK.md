# Erişilebilirlik

RobotWin Studio yoğun bir mühendislik aracıdır (şema, PCB, 3B dünya, grafikler). Bu sayfa bugün neyin çalıştığını, neyin henüz çalışmadığını ve sizi engelleyen bir şeyi bize nasıl bildireceğinizi açıkça yazar.

## Bugün çalışanlar

### Yazı ve karşıtlık

Koyu temanın yazı renkleri kullanıldıkları her yüzeyde WCAG 2.1 AA'yı (4,5:1) karşılar:

| Yazı | Renk | Tuval `#0E0E10` üstünde | Paneller `#161618` üstünde | Yükseltilmiş `#1C1C1F` üstünde |
| --- | --- | --- | --- | --- |
| Birincil | `#EDEDEF` | 16,5:1 | 15,5:1 | 14,5:1 |
| İkincil | `#B4B4BC` | 9,4:1 | 8,8:1 | 8,3:1 |
| Soluk (açıklamalar, ipuçları) | `#8E8E97` | 5,9:1 | 5,6:1 | 5,2:1 |
| Devre dışı | `#5C5C63` | 2,9:1 | 2,7:1 | 2,6:1 |

Devre dışı denetimler bilerek daha soluktur (WCAG etkin olmayan bileşenleri kapsam dışı tutar). Hiçbir arayüz yazısı 12 pikselden küçük değildir; denetçi etiketleri kesilmek yerine alt satıra kayar.

### Klavye

- Her menü komutuna klavyeden ulaşılır. **Ctrl+K** bütün komutları, sayfaları ve şablonları arayan komut paletini açar. *Yardım → Klavye kısayolları* her kısayolu listeler.
- Sık kısayollar: Ctrl+N / O / S (dosya), Ctrl+Z / Y (geri al / yinele), Ctrl+C / X / V / D (pano, çoğalt), Ctrl+F (bul), F5 / Shift+F5 (devre simülasyonunu başlat / durdur), F7 (kodu derle), Esc (tam ekrandan ya da geçerli araçtan çık).
- Şema: W kablo çizer, L etiket koyar, R döndürür, X aynalar, Del siler.
- PCB yollama: başlamak için bir bağlantı çizgisine tıklayın, **F** hattın en yakın pedine bitirir, **V** via koyar.

### Yalnız renge dayanmamak

- Simülasyon sorunları (yanan parçalar, aşırı yüklenen kablolar, kapanmayan bir kapak) yalnız renkle değil, denetçide, olay günlüğünde ve durum çubuğunda yazıyla da bildirilir.
- Kablo tanılaması renk kodunun yanında ölçülen değerleri (akım, gerilim düşümü, uzunluk) listeler.
- PCB düzenleyicisinde etkin katman dışındaki her katmanı soluklaştıran bir *Yüksek kontrast* seçeneği vardır.

### İpuçları

Araç çubuğu düğmelerinin, anahtarların ve denetçi alanlarının çoğunda onları açıklayan bir ipucu vardır.

## Bilinen eksikler

Bunlar bugünkü gerçek sınırlamalardır; çevrelerinden dolaşabilmeniz için listelenmiştir.

- **Ekran okuyucular.** Menüler, düğmeler, metin kutuları ve listeler gibi standart denetimler yazılarını WPF üzerinden Ekran Okuyucu'ya ve NVDA'ya verir. Çizim yüzeyleri vermez: şema, PCB, 3B dünya ve grafikler özel çizildiği için ekran okuyucular onları göremez. Onlar için henüz UI Automation adları eklenmedi.
- **Windows Yüksek Karşıtlık temaları** izlenmez. Uygulama her zaman kendi koyu temasını kullanır.
- **Renk haritaları.** Render penceresindeki gerilme ve hız renklendirmesi maviden kırmızıya bir "jet" ölçeği kullanır. Bazı renk körlüğü türlerinde okunması zordur ve algısal olarak düzgün değildir. Sayılar göstergede, grafiklerde ve denetçide vardır. Dünyanın ısı kamerası tekdüze "demir" paletini kullanır ve farenin altındaki noktanın tam sıcaklığını gösterir.
- **Dil.** Arayüz yalnız Türkçedir. İngilizce (ve başka diller) planlanmıştır. Belgeler Türkçe ve İngilizce eksiksiz ayrı ayrıdır.
- **Hareket.** Renderlar ve yeniden oynatmalar sürekli canlandırılır. Henüz "hareketi azalt" ayarı yoktur, ama oynatma her zaman duraklatılabilir.
- **Fareye dayalı düzenleme.** Parça yerleştirmek ve kablolamak, yol çizmek ve 3B görünümü döndürmek fare ya da dokunmatik yüzey ister. Birçok komutun klavye karşılığı vardır, ama her sürükleme işleminin yoktur.

## Planlananlar

- Tuvaller ve denetçi için UI Automation adları ve açıklamaları.
- Windows Yüksek Karşıtlık temasını izlemek ve açık bir tema.
- Varsayılan olarak renk körlüğüne uygun sıralı bir renk haritası (viridis türü) ve 3B modelde fareyle tam değerler.
- İngilizce arayüz.
- Yeniden oynatmalar ve 3B görünüm için "hareketi azalt" seçeneği.

## Bir sorunu bildirmek

RobotWin Studio'da bir şeyi kullanmak sizin için zor ya da imkânsızsa lütfen [bir sorun açın](https://github.com/Kinin-Code-Offical/robotwin-studio/issues). Ne yapmaya çalıştığınızı, neyin engel olduğunu ve hangi yardımcı teknolojiyi kullandığınızı (ekran okuyucu, büyüteç, yalnız klavye, yüksek karşıtlık teması …) yazın. Erişilebilirlik bildirimleri özellik isteği değil, hata olarak ele alınır.
