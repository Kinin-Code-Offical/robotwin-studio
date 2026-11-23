# Sıvılar

Su, yağ, bal gibi sıvılar dünyada parçacıklarla çözülür. Her parçacık sıvının kütlesinden eşit bir pay taşır; parçacıklar gerçek yoğunlukta durur, gerçek viskozite ve yüzey gerilimiyle akar. Sıvı katı parçaları iter, parçalar da sıvıyı iter: bir bardak suyun ağırlığını taşır, suya bırakılan bir parça kaldırma kuvveti görür.

## Sıvının kendisi: konum tabanlı akışkan (PBF)

Sıkıştırılamazlık, her parçacık için bir yoğunluk kısıtıdır (Macklin ve Müller 2013). Bu kısıt, sahnedeki diğer XPBD kısıtları (kabloların teli gibi) ile aynı biçimde çözülür.

- **Yoğunluk:** komşulardan SPH toplamı, $`\rho_i = \sum_j m\,W(\lVert x_i - x_j\rVert, h)`$. $`W`$ kübik spline çekirdektir; $`h`$ parçacık aralığının iki katıdır, yaklaşık 33 komşu.
- **Parçacık kütlesi:** $`m = \rho_0 / S_0`$. Burada $`S_0`$ kusursuz kübik kafesin çekirdek toplamıdır, yani dinlenmedeki sıvı tam olarak gerçek yoğunluktadır.
- **Kısıt:** $`C_i = \max(\rho_i/\rho_0 - 1,\ 0)`$. Basınç yalnızca iter; serbest yüzey serbesttir.
- **Düzeltme:** $`\lambda_i = -C_i / (\sum_k \lvert\nabla_k C_i\rvert^2 + \varepsilon)`$ ve $`\Delta x_i = \tfrac{m}{\rho_0}\sum_j (\lambda_i + \lambda_j)\nabla W_{ij}`$. Bu her alt adımda birkaç kez yinelenir (canlı görüntüde 4, render'da 5–12).

## Duvarlar: yoğunluk haritası

Her katı parça ve zemin, duvarın gerisinde "sıvı varmış gibi" yoğunluk katkısı verir (Koschier ve Bender 2017). Katkı, parçacığın duvara uzaklığı $`d`$'nin bir fonksiyonudur ve yerelde düz bir duvar varsayılır:

```math
\rho_\text{duvar}(d) = \frac{m}{s^3}\,\Phi(d), \qquad \Phi(d) = \int_{z>d} W\,\mathrm{d}V = \int_d^h A(z)\,\mathrm{d}z, \qquad A(z) = 2\pi\int_z^h W(r)\,r\,\mathrm{d}r
```

$`\Phi`$ ve $`A`$ bir kez tablolanır. Bunun üç sonucu vardır:

- Duvar, arkasındaki sıvının hidrostatik basıncını taşır.
- Parçacık duvara değmeden geri itilir.
- Bu itişin tepkisi duvarın cismine kuvvet olarak uygulanır.

Düz yüzlü basit cisimlerde (kutu, zemin) bu düz duvar formülü kullanılır. Çok parçalı ya da örgü cisimlerde (bir bardak gibi) cismin kendi çerçevesindeki bir ızgarada katının çekirdek integrali doğrudan önceden hesaplanır: $`\Phi(x) = \int_\text{katı} W(\lVert x - y
Vert)\,\mathrm{d}y`$, her hücrenin katı payı işaretli uzaklıktan bulunup çekirdekle evriştirilerek. Böylece bardağın iç köşesi, ince duvarı ve duvarın arkasındaki bir parça gerçekte olduğu kadar basınç uygular. Aynı yöne bakan iki düz yüzden arkadaki, öndekinin gölgesinde kaldığı için basınç katmaz.

Uzaklık, her cismin işaretli uzaklık fonksiyonundan gelir. Çok parçalı ya da örgü cisimlerde (25 dışbükey parçadan oluşan bir bardak gibi) bu fonksiyon, cismin kendi çerçevesinde bir ızgarada bir kez örneklenir. Hücre, parçacık aralığının üçte biridir. Sorgu bu ızgarada üç doğrusal ara değerlemedir, normal de alanın gradyanından gelir.

**Tünelleme yok:** bir parçacık bir adımda katı bir duvarı aşamaz. Bu, cismin kendi çerçevesinde denetlenir (bardak dönüyor olabilir). Uzaklık en fazla gidilen yol kadar değişebildiği için iki uçtaki boşluk toplamı hareketten büyükse kesin ışın atılmaz. Çoğu parçacık bu sayede ucuzca geçer.

## Viskozite ve yüzey gerilimi

- **Viskozite:** sıvının gerçek kinematik viskozitesi $`\nu = \mu/\rho`$. Morris ve arkadaşlarının (1997) SPH Laplace operatörü kullanılır:

  ```math
  \frac{\mathrm{d}v_i}{\mathrm{d}t} = \sum_j \frac{2\nu\,m}{\rho_j}\,\frac{r_{ij}\cdot\nabla W_{ij}}{r_{ij}^2 + 0{,}01\,\ell^2}\,(v_i - v_j)
  ```

  Örtük olarak alınır; bu yüzden bal ($`\nu \approx 7\cdot10^{-3}`$ m²/s) da aynı adımda kararlıdır. Duvar, cisimle birlikte hareket eden kaymasız bir sınırdır. Katkısı, duvarın gerisindeki yarı uzay üzerinden $`\int -W'(r)/r\,\mathrm{d}V = 2\pi\int_d^h W(z)\,\mathrm{d}z`$ ile hesaplanır.
- **Yüzey gerilimi:** parçacıklar arası bir çekim (kohezyon) olarak uygulanır: $`F(r) = \varepsilon \sin(\pi r/h)`$. Kuvvet $`\varepsilon`$, sıvının gerçek $`\sigma`$ değerinden Kirkwood–Buff (Fowler) bağıntısıyla hesaplanır. Bu bağıntı, düz bir kesitin enerjisini verir; $`n`$ m³ başına parçacık sayısıdır:

  ```math
  \sigma = \frac{\pi n^2}{8}\int_0^h F(r)\,r^4\,\mathrm{d}r
  ```

  Sıvının içinde çekimler birbirini götürür. Yüzeyde, ince bir tabakada ve bir akış boyunca sıvıyı bir arada tutar; seyrek parçacıklarda da çalışır.
- **Islanma:** her duvar, sıvı dolu bir yarı uzay gibi çeker; çekim $`(1 + \cos\theta)/2`$ ile ölçeklenir. Bu, Young–Dupré tutunma işini verir: $`W = \sigma(1 + \cos\theta)`$. Temas açısı $`\theta`$ cismin malzemesinden gelir:

  | Malzeme | Cam | Çelik | PLA | ABS | Akrilik | Ahşap | Kauçuk | PTFE |
  | --- | --- | --- | --- | --- | --- | --- | --- | --- |
  | Su ile temas açısı θ | 25° | 70° | 75° | 80° | 70° | 60° | 100° | 110° |

  Yağ ve alkol çoğu yüzeyi daha iyi ıslatır (≈20°). Duvar da sıvının çekişini tepki olarak taşır.
- **Hava direnci:** havaya açık parçacıklar (sprey, damla, ince akış) dünyanın havasında sürüklenir. Her parçacık, kendi hacminde bir küre olarak ele alınır; sürükleme katsayısı Schiller–Naumann bağıntısından gelir ($`C_D = 24/Re\,(1 + 0{,}15\,Re^{0{,}687})`$, Re 1000'in üstünde 0,44). Rüzgâr da hesaba katılır. Sıvının iç kütlesi kendini korur.

| Sıvı (20 °C) | Yoğunluk (kg/m³) | Viskozite (mPa·s) | Yüzey gerilimi (mN/m) |
| --- | --- | --- | --- |
| Su | 998,2 | 1,002 | 72,8 |
| Deniz suyu | 1024,8 | 1,08 | 73,5 |
| Etil alkol | 789,3 | 1,20 | 22,3 |
| Süt | 1030 | 2,0 | 45 |
| Ayçiçek yağı | 919 | 55,8 | 33,5 |
| Gliserin | 1261 | 1410 | 63,4 |
| Bal | 1420 | ≈10 000 | 50 |

## Zaman adımı

Bir alt adımda hiçbir parçacık, parçacık aralığının 0,3'ünden fazla yol almaz (CFL koşulu). Su hızlandıkça alt adım sayısı kendiliğinden artar.

**Canlı görüntü** etkileşimlidir:

- Sıvı her 1 ms'de çözülür (yaklaşık 3000 parçacık, 4 yineleme). Daha uzun adım, durgun bir su sütununu %8 sıkıştırıyordu; 1 ms'de sıkışma %1'in altında kalır.
- Durgun bardaktaki durgun su "uyur".

**Render** her şeyi tam hesaplar; ne kadar sürdüğü önemsizdir:

- Sıvının alt adımı doğruluk ayarının fizik adımından uzun olmaz (Hassas'ta 0,1 ms); uyku yoktur.
- Sıvı, dünya başlarken hacmini tam dolduran bir ızgarayla (yükseklikte tam sayıda katman) kurulur, kütlesi ρ·V'ye eşitlenir ve zaman ilerlemeden yoğunluk kısıtı izdüşürülerek durgun hâle getirilir: su ilk andan gerçek kütlesinde ve yoğunluğunda, hareketsiz başlar.
- Basınç, en büyük sıkışma bir toleransın altına inene kadar yinelenir (Hassas'ta %0,2, Referans'ta %0,05); sabit bir yineleme sayısıyla yetinilmez.
- Doğruluk ayarına göre 8 000–120 000 parçacık kullanılır (Hassas'ta 40 000).
- Yüzey daha ince bir ızgarada çıkarılır.

## Görünüş

Yüzey parçacıklardan şu adımlarla kurulur:

1. Parçacık merkezleri komşularına doğru yumuşatılır (Yu ve Turk 2013). Bu, durgun yüzeyi düzleştirir.
2. Her parçacığın çekirdeği, komşularının dağılımına göre (kovaryansının ana eksenleri) gerilir. Bunlar Yu ve Turk'ün anizotropik çekirdekleri:
   - ince bir tabakada düzlem içinde uzar, normal yönde incelir;
   - ince bir akışta akış yönünde uzar;
   - sıvının içinde yuvarlak kalır.

   Böylece kuru bir tabana yayılan tabaka ve düşen akış kesintisiz görünür.
3. $`\phi = \sum_j W(\lvert G_j(x - \bar x_j)\rvert)\det G_j / S_0`$ alanı bir ızgarada örneklenir.
4. $`\phi = 1/2`$ yüzeyi "surface nets" yöntemiyle çıkarılır; normaller alanın gradyanından gelir.
5. İkiden az komşusu olan, tek başına uçan parçacıklar damlacık olarak çizilir. Canlı görüntüde her damla, gerçek bir kameranın 1/60 s'lik pozlamasında çizeceği iz kadar hızı yönünde uzatılır; düşen ince bir akış kopuk toplar değil, bir şerit olarak görünür.

WPF'te gölgelendirici olmadığı için optik köşe başına hesaplanır:

- Yüzeyin kameraya göre Fresnel yansıması Schlick yaklaşımıyla bulunur; su için $`F_0 = ((n-1)/(n+1))^2 \approx 0{,}02`$.
- Dik bakınca sıvı neredeyse saydamdır ve kendi rengini gösterir.
- Yatık bakınca gökyüzünü yansıtır.
- Keskin bir parlama güneşi taşır.

Cam ve akrilik parçalar sıvıdan sonra çizilir; bu sayede su bardağın içinden görünür.

## Doğrulama

Sayılar [12. Doğrulama](../kullanim/12-dogrulama.md) sayfasındadır:

- **Durgun su:** 6 cm su, yan duvara hidrostatik kuvvetini %2 içinde uygular. İç yoğunluk gerçek yoğunluğun %0,2 içindedir, en büyük sıkışma %1'in altındadır (render'ın 0,1 ms adımında %0,01).
- **Başlangıç:** bardaktaki su dünya başlarken tam ρV kütlesinde ve hareketsizdir.
- **Serbest kap:** zemindeki serbest bir kap, içindeki suyun ağırlığını %0,1 içinde taşır.
- **Baraj yıkılması:** su cephesi Martin ve Moyce (1952) deneyinin %3–8 önündedir. Sayısal yöntemler bu deneyde genelde önde gider, çünkü deneyde kapak bir anda kalkmamıştır.

## Sınırlar

- **Parçacıktan ince film yok:** bir parçacıktan ince bir tabaka (kuru tabana yayılan ilk su gibi) parçalara bölünür. Canlı görüntüde parçacık 3–4 mm'dir, Hassas render'da 1,7 mm, Referans'ta 1,2 mm. Döken bir bardağın dış yüzünden süzülen ince film de bu yüzden görünmez.
- **Kaba çözünürlükte yüzey gerilimi:** birkaç parçacık yarıçaplı bir damla salınmaz. Parçacıklar düzenli bir dizide kilitlenir ve küçük Laplace basıncı onları yeniden dizemez. Yüzey gerilimi büyük ölçekte (akışı ve tabakayı bir arada tutmak, ıslatma) doğru etkir, milimetre altı dinamikte etkimez.
- **Hava kabarcığı yok:** sıvının içinde hava kabarcığı oluşmaz.
