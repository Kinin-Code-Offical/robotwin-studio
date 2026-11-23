# 12. Doğrulama: teoriden ne kadar sapıyor?

RobotWin Studio'daki her çözücü, sonucu bilinen problemlerle sınanır: analitik çözümler, yayımlanmış kıyaslama (benchmark) problemleri ve datasheet değerleri. Aşağıdaki her satır bir otomatik testtir; testler her derlemede (CI) çalışır ve sapma **kabul sınırını** aşarsa derleme başarısız olur.

- **Teori / referans:** analitik formül, kıyaslama probleminin yayımlanmış değeri ya da üretici verisi.
- **Simülasyon:** testin son çalıştırmada ölçtüğü değer.
- **Sapma:** simülasyonun referanstan farkı.
- **Kabul sınırı:** testin izin verdiği en büyük hata.

Testleri kendiniz çalıştırmak için: `dotnet test RobotWinStudio.sln`. Her test ölçtüğü değeri çıktısına yazar.

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
| Havada düşen kürenin son hızı | 8,5568 m/s | 8,5567 m/s | −0,001 % | ±0,5 % |
| Rüzgârın kutuya kuvveti | 0,31607 N | 0,31557 N | −0,16 % | ±2 % |

## 2. Gerilme analizi (FEM, ikinci derece T10 elemanlar)

| Ölçüm | Teori | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| Konsol uç sehimi (Timoshenko) | 0,20156 mm | 0,19993 mm | −0,81 % | −3 / +2 % |
| Konsol kök gerilmesi | 60,00 MPa | 60,06 MPa | +0,11 % | ±5 % |
| Konsolda σxx (L/4) | 45,00 MPa | 45,06 MPa | +0,13 % | ±5 % |
| Kiriş elemanı uç sehimi ve kök gerilmesi | 0,2000 mm / 60 MPa | aynı | 0 % | ±0,01 % / ±0,1 % |
| Delikli plaka gerilme yığılması Kt (Peterson, sonlu genişlik) | 3,133 | 2,946 | −6,0 % | ±8 % |
| Serbest ivmelenen çubuk (atalet dengesi), orta gerilme | 250,00 kPa | 250,00 kPa | ≈0 % | ±10 % |

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

## 6. Sıvılar (parçacıklı akışkan)

| Ölçüm | Teori / referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- |
| 6 cm durgun suyun yan duvara kuvveti (ρgD²L/2) | 1,0576 N | 1,0772 N | +1,9 % | ±5 % |
| Durgun suyun iç yoğunluğu | 998,2 kg/m³ | 999,7 kg/m³ | +0,15 % | ±1 % |
| En büyük sıkışma | 0 | %0,95 | — | %3 |
| Serbest kabın taşıdığı su ağırlığı | 1,6922 N | 1,6928 N | +0,04 % | ±3 % |
| Dünya başlarken bardaktaki suyun kütlesi (ρV) | 192,67 g | 192,67 g | 0 % | ±0,01 % |
| Bardaktaki su, başlangıçtan 0,2 s sonra: ortalama hız / sıkışma | 0 / 0 | 22,8 mm/s / %0,90 | — | < 30 mm/s / < %2 |
| Su bardağı şablonu: kâseye dökülen su (ESP32 + SG90) | — | %97 | — | > %60 |
| Baraj yıkılması, cephe x/a (Martin ve Moyce 1952), T = 0,84 … 2,98 | 1,22 … 3,67 | 1,26 … 3,82 | +3,6 … +8,2 % | ±12 % |

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
| Motorun yüksüz devri (6,28 V, datasheet) | ≈209 rpm | 210 rpm | +0,5 % | −12 / +2 % |
| %10 eğimde motor akımı artışı | 0,059 A | 0,055 A | −7 % | ±25 % |
| Kutu iterken akım artışı | 0,052 A | 0,054 A | +4 % | ±35 % |
| Enkoder kenar sayısı | 1940 | 1940 | 0 % | ±10 % |
| GPS medyan yatay hatası (datasheet CEP) | 2,5 m | 2,64 m | +6 % | 1,2–4,5 m |
| Tekerlek ağının yarıçapı (ortalama / en kötü) | — | %0,07 / %1,47 | — | en kötü ≤ %3 |

## Sizin modeliniz için

Bu tablolar çözücülerin doğruluğunu gösterir; sizin modelinizin gerçeğe yakınlığı ayrıca ağınıza, mesnetlerinize ve parça verilerinize bağlıdır:

- **FEM:** **Ağ yakınsaması denetimi** en kötü anı daha ince ağla yeniden çözer ve farkı yazar. **.inp dışa aktarma** aynı durumu CalculiX, ANSYS ya da Abaqus'ta çözmeniz için yazar.
- **CFD:** 3B analizin notlarındaki ızgara belirsizliği (GCI) ve y+ değerine bakın.
- **Parça yayılımı:** datasheet değeri tipik değerdir; klon servo ve motorlar akımda %30–40 sapabilir. Render'daki belirsizlik bandı bu aralığı çizer.
