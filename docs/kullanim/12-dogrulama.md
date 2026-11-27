# 12. Doğrulama: teoriden ne kadar sapıyor?

RobotWin Studio'daki her çözücü, sonucu bilinen problemlerle sınanır: analitik çözümler, yayımlanmış kıyaslama (benchmark) problemleri ve datasheet değerleri. Aşağıdaki her satır bir otomatik testtir; testler her derlemede (CI) çalışır ve sapma **kabul sınırını** aşarsa derleme başarısız olur.

- **Teori / referans:** analitik formül, kıyaslama probleminin yayımlanmış değeri ya da üretici verisi.
- **Simülasyon:** testin son çalıştırmada ölçtüğü değer.
- **Sapma:** simülasyonun referanstan farkı.
- **Kabul sınırı:** testin izin verdiği en büyük hata.

Testleri kendiniz çalıştırmak için: `dotnet test RobotWinStudio.sln`. Her test ölçtüğü değeri çıktısına yazar. Uygulamada bu bölüm komut paletinden (**Doğrulama raporu**) ve parçanın statik analizindeki güvenilirlik bölümünden açılır.

## 1. Mekanik (katı cisim, eklemler, kaynaklar)

| Ölçüm | Teori / referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| Menteşeli fiziksel sarkaç periyodu | 0,89769 s | 0,89767 s | −0,002 % | ±0,5 % |
| Sarkaç şablonu periyodu (28°, büyük genlik düzeltmeli) | 1,1069 s | 1,1073 s | +0,04 % | ±1 % |
| Sarkaç enerjisinin korunması | 0 | −0,80 % | −0,80 % | ±3 % |
| Sekme yüksekliği e²·h | 0,2113 m | 0,2111 m | −0,09 % | ±5 % |
| Sabit torkla araba ivmesi | 0,9714 m/s² | 0,9715 m/s² | +0,01 % | ±5 % |
| Eğimde kayma ivmesi | 2,894 m/s² | 2,894 m/s² | ≈0 % | ±5 % |
| P-kontrollü kolun sarkması | 1,405° | 1,405° | ≈0 % | ±1 % |
| Kaynağın taşıdığı ağırlık (0,5 kg) | 4,905 N | 4,905 N | ≈0 % | ±2 % |
| Kaynakta eğilme momenti | 0,1962 N·m | 0,196 N·m | −0,1 % | ±5 % |
| Serbest dönen kaynaklı grup, açısal momentum | korunur | −1,10 % | −1,10 % | ±1,5 % |
| Tel ile blok arasında momentum (etki-tepki) | 0 | 3,1·10⁻⁵ kg·m/s | iç alışverişin ≈%3,6'sı | 5·10⁻⁵ |
| Kafa kafaya çarpışma, toplam momentum (çelik, 1 kg ile 3 kg) | korunur | 2·10⁻¹⁶ kg·m/s fark | ≈0 | 4·10⁻⁹ |
| Ayrılma / yaklaşma hızı (Newton'un çarpışma yasası, e = 0,65) | 0,6500 | 0,6500 | ≈0 % | ±0,02 |
| Çarpışmada kaybolan enerji, (1 − e²)·½μv² | 1,9491 J | 1,9491 J | ≈0 % | ±2 % |
| Merkez dışı vuruş: momentum ve açısal momentum | korunur | 3·10⁻¹⁶ (‖L‖ 0,225) | ≈0 | %1 |
| Serbest kutu ara ekseninde (Dzhanibekov): devrilir, ‖L‖ 6 s'de | korunur | −0,14 %, yönü 0,02° | −0,14 % | ±2 %, 1° |
| Aynı kutunun dönme enerjisi, 6 s | korunur | −0,29 % | −0,29 % | ±3 % |
| Boşlukta atılan cismin mekanik enerjisi, 2 s | korunur | en çok %0,087 | 0,087 % | ±0,2 % |
| Sekmelerde enerji artışı (pasiflik) | 0 | 5·10⁻¹¹ J | ≈0 | başlangıcın %1'i |
| Havada düşen kürenin son hızı | 8,5568 m/s | 8,5567 m/s | −0,001 % | ±0,5 % |
| Rüzgârın kutuya kuvveti | 0,31607 N | 0,31557 N | −0,16 % | ±2 % |

### Zaman adımı yakınsaması

Aynı hareket, çözücünün adımı her seferinde yarıya indirilerek yeniden çalıştırılınca doğru yanıta yaklaşmalı, uygulamanın kullandığı adımda (0,5 ms, `PhysicsScene.MaxSubstep`) da yeterince yakın olmalıdır. Sayılar `TimeStepConvergenceTests`'ten:

| Ölçüm | 4 ms | 2 ms | 1 ms | 0,5 ms (uygulama) | 0,25 ms |
| --- | --- | --- | --- | --- | --- |
| Atılan cismin 1 s sonraki konum hatası | 19,6 mm | 9,8 mm | 4,9 mm | 2,45 mm | 1,23 mm |
| Menteşeli sarkacın periyodu, bağıl hata | 3,0·10⁻⁵ | 7,4·10⁻⁶ | 1,8·10⁻⁶ | 2,2·10⁻⁷ | 1,6·10⁻⁷ |
| Düşüp duran kutunun merkez yüksekliği (teori 30 mm) | 29,936 mm | 29,984 mm | 29,996 mm | 29,999 mm | 29,999 mm |
| Sallanan beş halkalı zincirde en büyük eklem ayrılması, 10 s | 0,54 mm | — | — | 0,048 mm | — |

Atılan cismin hatası her yarılamada tam yarıya iner: yarı örtük (simplektik) Euler birinci derecedir, hata da a·T·h/2 formülünün verdiği kadardır. Sarkaç periyodu ikinci dereceye yakın yakınsar; 0,25 ms'deki 1,6·10⁻⁷, karşılaştırılan analitik formülün kendi kesme hatasıdır (büyük genlik düzeltmesinin θ⁴ terimi). Temas sert ve çok sönümlü bir yay gibidir: kutu yükü altında en çok 64 µm iç içe geçer (en kaba adımda, yay adımla yumuşadığı için), ne batar ne havada kalır. Küçük ve hızlı cisimlerde çözücü çarpışmayı kaçırmamak için adımı kendisi küçültür (sürekli çarpışma algılama); atılan cisim bu etkinin dışında kalsın diye 40 cm'liktir.

### Uzun bekleme ve yığınlar

Yavaş sürünme ve kayma ancak zamanla görünür: her adımda biraz kaybeden bir itme çözücüsü, tutması gereken eğimde kutuyu aşağı yürütür, yığını yavaş yavaş eğer. Sayılar `LongRestTests`'ten (ahşap üstünde ahşap, μs 0,50, μk 0,30):

| Ölçüm | Teori | 0,5 ms (uygulama) | 2 ms |
| --- | --- | --- | --- |
| 20°'lik eğimdeki kutunun 30 s'de kayması | 0 | 0,000 mm | 0,000 mm |
| 25°'lik eğimde (sınır 26,6°) | 0 | 0,000 mm | 0,000 mm |
| 30°'lik eğimde kayma ivmesi | 2,356 m/s² | 2,357 m/s² | 2,361 m/s² |
| Yirmi bloklu yığının tepesi, 20 s (2 ms'de on blok) | 0 | 0,029 mm | 0,009 mm |
| 100 kat ağır bloğu taşıyan hafif bloğun gömülmesi | 0 | 0,16 mm | — |

Yığında bloklar ikide bir 2 mm kaydırılmıştır, hiçbir arayüz simetrik değildir. Temasın yapışık mı kayıyor mu olduğuna adımın başındaki harekete bakılarak karar verilir: adımın kendi kuvvetlerinin eklediği hız (eğimde yerçekimi, bir itme) statik sürtünmenin karşılayacağı şeydir. Daha yüksek yığınların ve kaba adımların sınırı [Mekanik](../fizik/02-mekanik.md) bölümünün geçerlilik aralığında.

## 2. Gerilme analizi (FEM, ikinci derece T10 elemanlar)

| Ölçüm | Teori | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| Konsol uç sehimi (Timoshenko) | 0,20156 mm | 0,19993 mm | −0,81 % | −3 / +2 % |
| Çekme çubuğu (çelik, 100 × 10 × 10 mm, 1 kN): orta kesitte gerilme F/A | 10,000 MPa | 10,000 MPa | ≈0 % | ±2 % |
| Aynı, uç uzaması F·L/(E·A) | 5,0000 µm | 4,9979 µm | −0,04 % | −3 / +1 % |
| Konsol kök gerilmesi | 60,00 MPa | 60,06 MPa | +0,11 % | ±5 % |
| Konsolda σxx (L/4) | 45,00 MPa | 45,06 MPa | +0,13 % | ±5 % |
| Kiriş elemanı uç sehimi ve kök gerilmesi | 0,2000 mm / 60 MPa | aynı | 0 % | ±0,01 % / ±0,1 % |
| Delikli plaka gerilme yığılması Kt (Peterson, sonlu genişlik) | 3,133 | 2,946 | −6,0 % | ±8 % |
| Serbest ivmelenen çubuk (atalet dengesi), orta gerilme | 250,00 kPa | 250,00 kPa | ≈0 % | ±10 % |
| Parça düzenleyicinin statik analizi: konsol uç sehimi (yüz seçimiyle sabitleme ve kuvvet, 2,5 mm eleman; analizin referans çözümü) | 0,2015 mm | 0,1995 mm | −1,0 % | ±5 % |
| Aynı çalışmada düzgün basınçlı konsol (10 kPa, alüminyum) | 21,74 µm | 21,84 µm | +0,5 % | −10 / +20 % |

## 3. Isı

| Ölçüm | Teori | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| 1B geçici iletim (Fourier serisi), en büyük hata | — | 0,0174 K / 80 K | %0,02 | 0,1 K |
| Isıtılan plakanın konveksiyon enerji dengesi | 18,797 K | 18,798 K | +0,005 % | ±2 % |

## 4. Aerodinamik ve uçuş

| Ölçüm | Teori / referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| Dikdörtgen kanat C_L (AR 6, 4°), taşıyıcı çizgi | 0,318 | 0,319 | +0,3 % | ±7 % |
| İndüklenmiş sürükleme (6°) | 0,0121 | 0,0139 | +15 % | −20 / +40 % |
| NACA 0012 kesiti, Re 3,6·10⁵ (Sheldahl ve Klimas 1981, SAND80-2114, Tablo 2): C_l 2° / 4° / 6° / 8° | 0,220 / 0,440 / 0,660 / 0,854 | 0,220 / 0,440 / 0,660 / 0,880 | 0 / 0 / 0 / +3 % | ±0,03 |
| Aynı, C_d 0° / 4° / 8° | 0,0079 / 0,0098 / 0,0153 | 0,0080 / 0,0095 / 0,0142 | +1 / −3 / −7 % | ±15 % |
| Aynı tablo, C_l,max Re 8·10⁴ / 1,6·10⁵ / 3,6·10⁵ / 7·10⁵ / 10⁶ / 2·10⁶ | 0,723 / 0,853 / 0,981 / 1,075 / 1,121 / 1,217 | 0,748 / 0,855 / 0,979 / 1,081 / 1,135 / 1,242 | +3,6 / +0,3 / −0,2 / +0,6 / +1,3 / +2,1 % | ±6 % |
| Aynı, stall açısı | 7 / 9 / 10 / 11 / 12 / 13° | 6,9 / 7,8 / 8,9 / 9,9 / 10,4 / 11,3° | −0,1 … −1,7° | −2 / +1° |
| Derin stall 45° / 60° / 90° (Re 3,6·10⁵): C_l | 1,085 / 0,875 / 0,070 | 1,080 / 0,901 / 0,000 | −0,005 / +0,026 / −0,07 | ±0,1 |
| Aynı, C_d | 1,075 / 1,470 / 1,800 | 0,982 / 1,491 / 2,010 | −9 / +1 / +12 % | ±15 % |

| Aynı tablo, stall'ın hemen ardı (11–25°, Re 1,6·10⁵ / 3,6·10⁵ / 7·10⁵ / 10⁶ / 2·10⁶; 30 nokta): kaldırmanın ayrılmış akışa düşüşü | örn. Re 3,6·10⁵: 12° 0,483, 14° 0,289, 20° 0,612 | 0,491 / 0,288 / 0,610 | ortalama \|ΔC_l\| 0,022, en çok 0,073 | ortalama 0,04, en çok 0,12 |

Stall'dan sonra kaldırma, Re'den neredeyse bağımsız bir doğruya (ayrılmış akış, derece başına ≈ 0,054) düşer; düşüş Re 1,6·10⁵'te bir dereceden kısa sürer, 10⁶'da birkaç dereceye yayılır. Model bu ikisini tablodan alır (`LeadingEdgeStall`); düşük hızda stall'a giren model uçak kaldırmanın çoğunu gerçekteki gibi birden kaybeder.

| Dört çubuk bağlantısı, 45° kol → yüzey | 20,7° | 20,69° | −0,05 % | ±0,5° |
| Model roket Cd (OpenRocket sınıfı) | 0,5–0,7 | 0,64 | aralıkta | 0,4–0,8 |

## 5. Akış (CFD, kafes Boltzmann)

Akış çözücüsü düzenlenmiş (regularized) çarpışma ve Smagorinsky LES ile gerçek Reynolds sayısında çalışır. Eğri duvarlarda duvarın gerçek konumunu kullanır (Bouzidi, ikinci derece sınır). Giriş, debiyi tam veren bir hız sınırıdır; yanlar ortam basınçlı açık sınırdır.

| Ölçüm | Referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| Kanal akışı (Poiseuille) u_max | 1,0837·10⁻³ | 1,0770·10⁻³ | −0,6 % | ±1 % |
| Silindir, Schäfer–Turek 2D-1 (Re 20) Cd | 5,5795 | 5,624 | +0,8 % | ±2 % |
| Aynı, kaldırma Cl | 0,0106 | 0,0100 | −6 % | 0,005–0,016 |
| Silindir, Schäfer–Turek 2D-2 (Re 100) Strouhal | 0,300 | 0,2999 | −0,03 % | ±5 % |
| Aynı, en büyük Cd | 3,23 | 3,337 | +3,3 % | ±5 % |
| Aynı, en büyük Cl | 1,00 | 0,966 | −3,4 % | 0,8–1,1 |
| Küre Re 100, Cd (Schiller–Naumann) | 1,092 | 1,176 | +7,7 % | −8 / +10 % |
| Küre Re 20, Cd (Schiller–Naumann) | 2,610 | 2,900 | +11,1 % | −8 / +15 % |
| Küp, yüzü akışa dönük, Re 10⁵ (gerçek Re, LES), Cd | ≈1,05 (Hoerner) | 0,883 | −15,9 % | −22 / +15 % |

Kürede kalan farkın çoğu çözüm alanının sonlu olmasından gelir: düşük Re'de küre akışı uzağa kadar bozar. Alan 4 çaptan 6 çapa genişleyince sapma Re 20'de %11,1'den %7,8'e, Re 100'de %11,1'den %7,7'ye iner. Standart sürükleme eğrisinin (Schiller–Naumann) kendisi de ölçümlerden ±%5 sapar.

**Ağ inceldikçe:** çözücü ikinci derece yakınsar; hücre boyu yarıya inince sapma yaklaşık dörtte birine iner. Testlerden daha ince ağlarla yapılan ölçümler:

| Ölçüm | Kaba ağ | İnce ağ |
| --- | --- | --- |
| DFG 2D-1 Cd (çapta 20 → 40 hücre) | +0,80 % | +0,20 % |
| DFG 2D-2 Strouhal / Cd max / Cl max (20 → 40) | −0,03 % / +3,3 % / −3,4 % | +0,27 % / +1,8 % / −1,6 % |
| Küp Re 10⁵ Cd (kenarda 12 → 16 → 20 hücre) | −15,9 % / −12,7 % | −3,9 % |

**Önce ve sonra** (bu sürümdeki akış iyileştirmeleri):

| Ölçüm | Önce | Şimdi |
| --- | --- | --- |
| Küre Re 20, Cd sapması | +30 % | +11 % |
| Silindir Re 100 | kapalı alanda Strouhal +21 % | DFG kıyaslamasında −0,03 % |
| Kanal girişinin debisi | %93 | %100 |
| Re 10⁵ akış | ağda Re 1500'e düşürülüyordu | gerçek Re (τ0 − ½ ≈ 2·10⁻⁵) |

**Uygulamadaki 3B akış analizi** her sonucun yanına güvenilirliğini yazar: gerçek Reynolds sayısı, hücre boyu, tıkanma oranı, sınır tabakanın çözülüp çözülmediği (y+), kuvvetin oturup oturmadığı ve ilk anın iki ızgarayla yeniden çözülmesinden gelen **ızgara belirsizliği (GCI)**. Sınır tabaka hücreden inceyse yüzey sürtünmesi eksik çözülür; keskin kenarlı küt cisimlerin (kutu, araba gövdesi) basınç sürüklemesi yine geçerlidir.

**Render'daki gerçek hava (zamanla, hareketli duvarlar):**

| Ölçüm | Teori / kaynak | Simülasyon | Sapma | Tolerans |
| --- | --- | --- | --- | --- |
| 40 × 30 × 20 mm blok 0,2 s düşer, havanın ona kuvveti (½ρv²·Cd·A, Cd 1,05) | 2,91 mN | 2,55 mN | −12 % | ±40 % |
| Rüzgârsız dünyada yerde duran parça: hava akışı | yok | yok | — | hiç akış karesi yok |

Blok düşerken hücre 3,3 mm'dir ve akış hâlâ gelişmektedir (cisim ivmelenir). Önceki sürüm duran bir montaja 2 m/s'lik bir "test hızı" üflüyordu; artık hava yalnızca rüzgârdan ve parçaların kendi hareketinden gelir.

## 6. Sıvılar (parçacıklı akışkan)

| Ölçüm | Teori / referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| 6 cm durgun suyun yan duvara kuvveti (ρgD²L/2) | 1,0576 N | 1,0795 N | +2,1 % | ±5 % |
| Durgun suyun iç yoğunluğu | 998,2 kg/m³ | 999,8 kg/m³ | +0,16 % | ±1 % |
| En büyük sıkışma | 0 | %1,02 | — | %3 |
| Serbest kabın taşıdığı su ağırlığı | 1,6922 N | 1,6922 N | 0,0 % | ±3 % |
| Dünya başlarken bardaktaki suyun kütlesi (ρV) | 192,67 g | 192,67 g | 0 % | ±0,01 % |
| Bardaktaki su, başlangıçtan 0,2 s sonra: ortalama hız / sıkışma | 0 / 0 | 21,9 mm/s / %1,04 | — | < 30 mm/s / < %2 |
| Su bardağı şablonu: kâseye dökülen su (ESP32 + SG90) | — | %97 | — | > %60 |
| Baraj yıkılması, cephe x/a (Martin ve Moyce 1952), T = 0,84 … 2,98 | 1,22 … 3,67 | 1,25 … 3,82 | +2,8 … +7,8 % | ±12 % |
| Hassas render, su bardağı: parçacık / kütle / en büyük sıkışma | — / 192,67 g / 0 | 41 760 / 192,7 g / %0,01 | — | tolerans %0,2 |

Bardaktaki suyun başlangıçtaki ortalama hızı, bu testte güçsüz servoya asılı bardağın hafif sallanmasından ve PBF'nin serbest yüzeyindeki birkaç parçacığın titreşiminden gelir; render'ın 0,1 ms adımında sıkışma %0,01'e iner.

Baraj yıkılması deneyinde kapak bir anda kalkmadığı için sayısal yöntemler deneyin genelde %5–10 önünde gider. Ayrıntılar: [Sıvılar](../fizik/07-sivilar.md).

## 7. Gaz ve roket

| Ölçüm | Teori / referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| Hava tankı boşalması, 0,5 s (adyabatik) | 4,671 bar | 4,709 bar | +0,8 % | −2 / +3 % |
| Aynı tank, 2 s (duvardan ısı geçişiyle adyabatik ile izotermal arasında) | 1,563–2,121 bar | 1,818 bar | aralıkta | arada olmalı |
| KNSB motor oda basıncı | 2,26 MPa (kararlı) | 2,58 MPa | +14 % (başlangıç geçişi) | −20 / +25 % |
| KNSB özgül itki | 110–140 s (literatür) | 136 s | aralıkta | 100–165 s |

## 8. Elektrik, elektromekanik, sensörler

| Ölçüm | Teori / referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| RC şarjı (1 kΩ, 1 µF, 5 V), uygulamanın adımında üstel eğriden en büyük fark | 0 | 5,21 mV | %0,10 | 10 mV |
| RL akımı (10 Ω, 10 mH, 10 V), aynı | 0 | 1,04 mA | %0,10 | 5 mA |
| Kayıpsız LC tankı (1 mH, 1 µF), 100 periyot sonra genlik | 1 V | 0,9999 V | −0,01 % | ±%1 |
| Aynı tankın periyodu, periyot başına 64 adım | 198,692 µs | 198,851 µs | +0,080 % (trapezin (ωh)²/12'si) | ±0,1 % |
| Periyot hatasının adım yarıya inince küçülmesi (T/32 → T/256) | 4 kat (ikinci derece) | 3,89 / 4,00 / 4,00 | — | 3,6–4,4 |
| Motorun yüksüz devri (6,28 V, datasheet) | ≈209 rpm | 210 rpm | +0,5 % | −12 / +2 % |
| Dişli motorun enerji bilançosu, 3 s: kaynağın verdiği = kablo + sargı + sürtünme + dişli + yüke iş + depolanan (volan, sargının alanı) | 8,313 J | 8,313 J (0,434 + 3,477 + 1,041 + 0,939 + 2,191 + 0,231) | −0,001 % | ±0,5 % |
| Aynı motorda sargı ısısı = sıcaklık artışı + havaya geçen | 3,477 J | 3,462 + 0,015 J | ≈0 | 10⁻⁶ J |
| %10 eğimde motor akımı artışı | 0,059 A | 0,055 A | −7 % | ±25 % |
| Kutu iterken akım artışı | 0,052 A | 0,054 A | +4 % | ±35 % |
| Enkoder kenar sayısı | 1940 | 1940 | 0 % | ±10 % |
| DC motorun kalkış akımı, tepe (6 V, 2 Ω, 1 mH, k 0,01; sargı ve rotor denklemlerinin ince adımlı çözümü) | 2,7906 A | 2,7914 A | +0,03 % | ±2 % |
| Aynı, tepeye varış | 1,90 ms | 1,90 ms | ≈0 | ±0,2 ms |
| MOSFET açılışı, kapı yükü devresi (R_g 10 Ω, C_gs 1,3 nF, C_gd 0,2 nF, 10 A kenetli): kapının eşiğe varışı R_g·C_iss·ln(V/(V − V_th)) | 5,350 ns | 5,350 ns | ≈0 % | ±3 % |
| Aynı, Miller platosu C_gd·ΔV_ds·R_g/(V − V_p) | 8,625 ns | 8,714 ns | +1,0 % | ±7 % |
| Aynı, 10 V'a kadar kapı yükü | 19,965 nC | 19,965 nC | ≈0 % | ±2 % |
| Step motor, ani yükte adım kaçırma torku (enerji yöntemi: 0,7245·K·I) | 0,1206 N·m | 0,1211 N·m | +0,4 % | −6 / +8 % |
| Aynı, yavaş artan yükte (K·I ± detent) | 0,144–0,188 N·m | 0,1854 N·m | aralıkta | aralık |
| LM7805 yük regülasyonu, 5 mA → 1,5 A (TI veri sayfası: tipik 10, en çok 25 mV) | 10 mV | 11,96 mV | +20 % | en çok 25 mV |
| LM7805 çıkış empedansı, çıkış kondansatörü yok (veri sayfası Şekil 7, grafikten): 100 Hz / 1 kHz / 10 kHz / 100 kHz | 8 / 9 / 28 / 270 mΩ | 8,0 / 8,7 / 32,7 / 305 mΩ | 0 / −4 / +17 / +13 % | ±40 % |
| LM7805, 0,1 µF üzerinde 0,1 → 0,6 A basamak (1 µs): çıkışın çöküşü, modelin denklemlerinin ince adımlı çözümüne göre | 244,5 mV | 243,9 mV | −0,2 % | ±5 % |
| GPS medyan yatay hatası (datasheet CEP) | 2,5 m | 2,64 m | +6 % | 1,2–4,5 m |
| Tekerlek ağının yarıçapı (ortalama / en kötü) | — | %0,07 / %1,47 | — | en kötü ≤ %3 |

## Sizin modeliniz için

Bu tablolar çözücülerin doğruluğunu gösterir; sizin modelinizin gerçeğe yakınlığı ayrıca ağınıza, mesnetlerinize ve parça verilerinize bağlıdır:

- **FEM:** **Ağ yakınsaması denetimi** en kötü anı daha ince ağla yeniden çözer ve farkı yazar. **.inp dışa aktarma** aynı durumu CalculiX, ANSYS ya da Abaqus'ta çözmeniz için yazar.
- **CFD:** 3B analizin notlarındaki ızgara belirsizliği (GCI) ve y+ değerine bakın.
- **Parça yayılımı:** datasheet değeri tipik değerdir; klon servo ve motorlar akımda %30–40 sapabilir. Render'daki belirsizlik bandı bu aralığı çizer.
- **Zaman adımı:** Render'daki **Zaman adımı denetimi** koşunun ilk 2 saniyesini fiziğin yarım adımıyla yeniden çalıştırır ve parçaların yolunun ve kaydedilen her büyüklüğün ne kadar değiştiğini yazar. Yarı örtük Euler birinci derece olduğundan bu fark, koşunun kendi adım hatasının ölçüsüdür: %2'nin altı adımdan bağımsız, %10'a kadar az duyarlı sayılır; %10'un üstündeyse daha yüksek doğrulukla render alın. Titreşen, çok az değişen ya da sayı olan kanallar (temas sayısı) karşılaştırılmaz: adımdan bağımsız olarak koşudan koşuya değişirler.

Render bu ölçülerin hepsini kaydının başında **Sayısal belirsizlik** başlığı altında bir arada yazar: zaman adımı, gerilme ağları, akış ızgarası (GCI) ve parça toleransları.
