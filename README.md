# RobotWin Studio

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Windows](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6.svg)](#indirme-ve-kurulum)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/Kinin-Code-Offical/robotwin-studio?label=release)](https://github.com/Kinin-Code-Offical/robotwin-studio/releases/latest)

**Türkçe** · [English](#english) · **Belgeler / Docs: [kinin-code-offical.github.io/robotwin-studio](https://kinin-code-offical.github.io/robotwin-studio/)**

![RobotWin Studio açılış ekranı](docs/images/splash.png)

**RobotWin Studio**, robot ve elektronik projelerini baştan sona tek bir Windows uygulamasında yapmanızı sağlar: devreyi çizin, PCB'sini çıkarın, kodunu yazın, şasesini modelleyin, sonra her şeyi gerçek fizikli bir açık dünyaya koyup kablolayın ve birlikte çalıştırın. Arduino (ATmega328P) kodu gerçek derlenmiş HEX olarak emüle edilir, ESP32 kodu devreyle eş zamanlı (co-simulation) çalışır; motorlar, servolar, sensörler, kablolar ve parçalar birbirini gerçekten etkiler.

> **Belgeler:** Adım adım, ekran görüntülü kullanım kılavuzu ve geliştirici belgeleri **[belge sitesinde](https://kinin-code-offical.github.io/robotwin-studio/)** (kaynakları: [docs/kullanim](docs/kullanim/README.md)). Uygulama içinde: *Yardım → Çevrimiçi belgeler*.

> **1.5.1'de yeni:** Arduino derleyicisi kurulumla gelir (Uno / Nano kodu hemen derlenir) ve **İlk robotun** rehberi ilk robotunuzu 5–10 dakikada çalıştırır.
>
> **1.5.0'da yeni:** Sol çubuk üç grupta (Elektronik, Mekanik, Test). Parça sayfasında bir dosyada birden çok parça; **Montaja gönder** her parçayı ayrı gövde yapar. Montaj robotun kendisi: projeler PCB'li ya da PCB'siz gelir, birden çok proje bir arada durur, montaj projeyle kaydedilir ve devre, PCB, kod değişikliklerini izler. Robot örnekleri gerçek araç olarak kurulur. ESP32'de Arduino kütüphaneleri, 32 katmanlı PCB. **1.4.2:** kod sayfası bir kod editörü. Ayrıntılar: [11. Yenilikler](docs/kullanim/11-yenilikler.md), [CHANGELOG.md](CHANGELOG.md).

![Başlangıç sayfası](docs/images/home.png)

## İçindekiler

- [Ne yapabilirsiniz?](#ne-yapabilirsiniz)
- [Ekran görüntüleri](#ekran-görüntüleri)
- [Doğrulama: teoriden ne kadar sapıyor?](#doğrulama-teoriden-ne-kadar-sapıyor)
- [İndirme ve kurulum](#indirme-ve-kurulum)
- [Hızlı başlangıç: ilk dünyanız 5 adımda](#hızlı-başlangıç-ilk-dünyanız-5-adımda)
- [Aşama aşama özellikler](#aşama-aşama-özellikler)
- [Klavye kısayolları](#klavye-kısayolları)
- [Dosya türleri](#dosya-türleri)
- [Kaynaktan derleme](#kaynaktan-derleme)
- [Testler](#testler)
- [Komut satırı](#komut-satırı)
- [Proje yapısı](#proje-yapısı)
- [Yol haritası (geliştiriliyor)](#yol-haritası-geliştiriliyor)
- [Lisans](#lisans)
- [English](#english)

## Ne yapabilirsiniz?

Uygulamanın sol tarafındaki **"Tasarımdan gerçeğe hattı"** bir projenin aşamalarını üç grupta gösterir: **ELEKTRONİK** (Devre, PCB, Kod), **MEKANİK** (Parça, Montaj), **TEST** (Dünya).

| Aşama | Kısayol | Ne yapılır |
| --- | --- | --- |
| **Devre** | Ctrl+1 | Şema çizin, 256 gerçek parçadan seçin, analog çözücüyle canlı simüle edin; osiloskop, multimetre, Monte Carlo, arıza tespiti. |
| **PCB** | Ctrl+2 | 2'den 32'ye kadar katmanlı kart, bakır alanlar, etkileşimli ve otomatik yol çizimi, DRC, Gerber/Excellon ve KiCad 8 dışa aktarımı. |
| **Kod** | Ctrl+3 | Arduino Uno/Nano (arduino-cli ile) ve ESP32 (MSVC ile) kodu yazın, derleyin, emüle karta yükleyip çalıştırın. |
| **Parça** | Ctrl+4 | Blender benzeri 3D modelleyici, bir dosyada birden çok parça: temel şekiller, nokta/kenar/yüz düzenleme, ekstrüzyon, bıçak ve dilimleme, halka kesim, pah, kalınlık, CSG (birleşim/fark/kesişim), hazır robot parçaları, STL/OBJ/PLY. **Montaja gönder** her parçayı ayrı gövde yapar. |
| **Montaj** | Ctrl+5 | Robotun kendisi: bir ya da birden çok projenin kartı ve parçaları (PCB'li ya da PCB'siz) ile Parça'da modellenen gövdeler bir arada, bağlantılarıyla. Projeyle birlikte kaydedilir, açılınca devre, PCB ve kod değişikliklerini alır; araç projeleri şasesi ve tekerlekleriyle kurulur. |
| **Dünya** | Ctrl+6 | Kartları, parçaları, gövdeleri ve modelleri gerçek fizikli bir dünyaya koyun; kablolayın, mekanik olarak bağlayın, çalıştırın. |

Ayrıca **Başlangıç** (Ctrl+0), **Kütüphane** (Ctrl+7) ve **Ayarlar** (Ctrl+,) sayfaları vardır. Arayüz Türkçedir.

## Ekran görüntüleri

| | |
| --- | --- |
| ![Devre editörü ve canlı simülasyon](docs/images/schematic.png) | ![PCB editörü](docs/images/pcb.png) |
| **Devre:** şema editörü ve canlı simülasyon | **PCB:** katmanlar, yollar, DRC |
| ![3D modelleyici](docs/images/modeler.png) | ![Dünya: iki eksenli servo kol](docs/images/world-servo-arm.png) |
| **Modelle:** Blender benzeri 3D modelleyici | **Dünya:** Arduino ile iki eksenli servo kol |
| ![Modelle: dilimleme, pah ve kalınlık](docs/images/modeler-cut.png) | ![Modelle: halka kesimler](docs/images/modeler-cut-edit.png) |
| **Modelle:** dilimlenip döndürülmüş boru, pahlı blok, kalınlık verilmiş tepsi | **Modelle:** düzenleme modunda halka kesimler ve yeni araçlar |
| ![Dünya: kumandalı araba](docs/images/world-ps5-car.png) | ![Dünya: ultrasonik sensör](docs/images/world-sonar.png) |
| **Dünya:** ESP32 + PS5 kumandalı araba | **Dünya:** ultrasonik sensör ışın izleme ile ölçer |
| ![Dünya: PS5 kumandalı uçak pistte](docs/images/world-ps5-plane.png) | ![Dünya: uçak kalkışta, takip kamerası](docs/images/world-ps5-plane-flight.png) |
| **Dünya:** PS5 kumandalı uçak: balsa kabin, kanatçıklı kanat, üç tekerlekli iniş takımı | **Dünya:** pistten kalkış, takip kamerası |
| ![Dünya: kapalı gövdeli gezgin, röntgen görünümü](docs/images/world-enclosed-rover.png) | ![Dünya: trafik ışığı ve yaya butonu](docs/images/world-traffic-light.png) |
| **Dünya:** kapalı kutudaki elektronik; kablolar arka duvardaki delikten çıkar (röntgen) | **Dünya:** ESP32 trafik ışığı, yayaya yeşil |
| ![Dünya: sarkaç](docs/images/world-pendulum.png) | ![Dünya: katı yakıtlı roket rampada](docs/images/world-rocket-pad.png) |
| **Dünya:** sarkaç; periyodu teoriyle %0,1 içinde | **Dünya:** katı yakıtlı roket, radyal kanatçıklar |
| ![Dünya: su bardağı dökülüyor](docs/images/world-glass-pour.png) | |
| **Render (Hassas):** servo su dolu cam bardağı devirir; 40 000 parçacıklı su gerçek yoğunluk, viskozite ve yüzey gerilimiyle kâseye dökülür | |

### Dünya şablonları

Başlangıç sayfasındaki **Dünyalar** listesinde 34 hazır dünya vardır. Hepsi açılınca çalışır; hiçbirinde iç içe geçmiş parça ya da katı bir parçadan geçen kablo yoktur (tutarlılık denetimi). Tek bilinçli istisna: elektronik kutusundaki dik pil, sığma denetimini göstermek için kapağa çarpar.

<details>
<summary>Tüm şablonlar</summary>

| Şablon | Ne gösterir |
| --- | --- |
| Boş atölye | 4 × 3 m zemin; kendi kartlarınız ve parçalarınız için |
| Kabloyla bağlı iki kart | Ayrı PCB'ler, aralarında kablo; güç kartı LED kartını besler |
| Arduino ile iki eksenli servo kol | Uno iki SG90'ı sürer; kol kendi ağırlığıyla durur |
| Arduino, LCD ve OLED | I²C ekranlar 3B modelde canlı |
| İki tekerlekli araba | TT motorlar, sarhoş teker, zeminde gerçek sürtünme |
| Ultrasonik mesafe sensörü | HC-SR04 dünyaya ışın atarak ölçer |
| Pil, motor ve tekerlek | Test sehpasında dönen tekerlek |
| Fabrika hücresi | 6 eksenli kol, vakum tutucu, konveyör |
| SCARA al-bırak | Paralel tutucuyla blok taşıma |
| Pervane itki standı | Devir, itki ve akım |
| Mini drone kalkışı | Dört çekirdeksiz motor, X gövde |
| ESP32 dörtlü | Fırçasız motorlar, ESC, IMU, kendi uçuş yazılımı |
| Elektronik kutusu | Kapaklı içi boş gövde ve sığma denetimi |
| Motorlu planör (İHA) | ESP32 uçuş kartı, ağırlık merkezi pil ile |
| Planör | Balsa gövde, karbon kuyruk borusu, Arduino Nano |
| Mekanum araba | Yana kayan tekerlekler |
| Işıklar | LED'ler, güç LED'i, lamba, LED şerit (karanlık oda) |
| Trafik ışığı ve yaya butonu | ESP32 araç ve yaya ışıklarını yönetir |
| Kumandalı araba | ESP32 + PS5 kumanda, tasarlanmış kontrol kartı |
| Kapalı gövdeli gezgin | Kablo delikleri; ESP32 kare çizer |
| PS5 kumandalı uçak | Pistten kalkış, dengeleme, failsafe |
| Su roketi | 2 L PET şişe, 4 bar |
| Katı yakıtlı motor test standı | KNSB yanma, basınç ve itki |
| Katı yakıtlı roket | E-kibritle ateşleme, ~1 km, paraşüt |
| Malzeme ve yüzey vitrini | Ahşap, karbon, alüminyum yüzeyler |
| Kap ve bilyeler | İçi boş modellere bir şey konur |
| Su bardağı | Servo cam bardağı devirir, su kâseye dökülür (parçacıklı sıvı) |
| Sarkaç | Periyot ve sönüm, teoriyle karşılaştırma |
| Çizgi izleyen robot | Mat, solmuş ve parlak bant |
| Sensör gezgini | Kamera, LiDAR, GPS, IMU |
| ESP32 + OV2640 kamera | Kameradan bant bulma |
| Rüzgâr, fan, konveyör | Türbülanslı rüzgâr ve ifadeler |
| Arazi parkuru | Tepeler, asfalt yol, hız kesiciler |
| Kapalı atölye | Fayans, paspas, halı, ıslak zemin |

</details>

## Doğrulama: teoriden ne kadar sapıyor?

Her çözücü sonucu bilinen problemlerle sınanır: analitik çözümler, yayımlanmış kıyaslama problemleri ve datasheet değerleri. Aşağıdaki her satır, her derlemede çalışan bir otomatik testtir; sapma kabul sınırını aşarsa derleme başarısız olur.

| Alan | Ölçüm | Teori / referans | Simülasyon | Sapma | Kabul sınırı |
| --- | --- | --- | --- | --- | --- |
| Mekanik | Menteşeli sarkacın periyodu | 0,89769 s | 0,89767 s | −0,002 % | ±0,5 % |
| Mekanik | Havada düşen kürenin son hızı | 8,5568 m/s | 8,5567 m/s | −0,001 % | ±0,5 % |
| Mekanik | Sabit torkla araba ivmesi | 0,9714 m/s² | 0,9715 m/s² | +0,01 % | ±5 % |
| FEM | Konsol uç sehimi (Timoshenko) | 0,20156 mm | 0,19993 mm | −0,81 % | −3 / +2 % |
| FEM | Konsol kök gerilmesi | 60,00 MPa | 60,06 MPa | +0,11 % | ±5 % |
| FEM | Delikli plakada gerilme yığılması Kt | 3,133 | 2,946 | −6,0 % | ±8 % |
| Isı | Isıtılan plakanın enerji dengesi | 18,797 K | 18,798 K | +0,005 % | ±2 % |
| Aerodinamik | Kanat C_L (AR 6, 4°), taşıyıcı çizgi | 0,318 | 0,319 | +0,3 % | ±7 % |
| Akış (CFD) | Kanal akışı (Poiseuille) u_max | 1,0837·10⁻³ | 1,0770·10⁻³ | −0,6 % | ±1 % |
| Akış (CFD) | Silindir Cd, Schäfer–Turek 2D-1 (Re 20) | 5,5795 | 5,624 | +0,8 % | ±2 % |
| Akış (CFD) | Silindir Strouhal, Schäfer–Turek 2D-2 (Re 100) | 0,300 | 0,2999 | −0,03 % | ±5 % |
| Akış (CFD) | Küre Cd (Re 100, Schiller–Naumann) | 1,092 | 1,176 | +7,7 % | −8 / +10 % |
| Akış (CFD) | Küp Cd, Re 10⁵ (gerçek Re, LES) | ≈1,05 | 0,883 | −15,9 % | −22 / +15 % |
| Sıvı | 6 cm durgun suyun yan duvara kuvveti (ρgD²L/2) | 1,0576 N | 1,0795 N | +2,1 % | ±5 % |
| Sıvı | Baraj yıkılması cephesi (Martin ve Moyce 1952), T = 2,98 | 3,67 | 3,82 | +4,1 % | ±12 % |
| Gaz | Hava tankı boşalması, 0,5 s (adyabatik) | 4,671 bar | 4,709 bar | +0,8 % | −2 / +3 % |
| Elektrik | Motorun yüksüz devri (datasheet) | ≈209 rpm | 210 rpm | +0,5 % | −12 / +2 % |
| Sensör | GPS medyan yatay hatası (datasheet CEP) | 2,5 m | 2,64 m | +6 % | 1,2–4,5 m |

Mekanik, FEM ve ısı teoriyle binde birler düzeyinde uyuşur; en büyük sapmalar akışta, kaba ağlarda ve sonlu çözüm alanındadır. Akış çözücüsü ağ inceldikçe ikinci derece yakınsar (küp 20 hücreyle −%3,9, DFG silindiri 40 hücreyle +%0,2). Sekiz alandaki 54 ölçümün tamamı, ağ yakınsaması ve önce/sonra karşılaştırması: **[12. Doğrulama](docs/kullanim/12-dogrulama.md)** (uygulamada **Yardım → 12. Doğrulama**).

## İndirme ve kurulum

1. [Sürümler (Releases)](https://github.com/Kinin-Code-Offical/robotwin-studio/releases/latest) sayfasından `RobotWinStudio-Setup-<sürüm>.exe` (.NET dahil, en kolayı) ya da daha küçük **lite** paketi `RobotWinStudio-Setup-<sürüm>-lite.exe` dosyasını indirin. Her sürümde şu dosyalar ayrı ayrı bulunur:

   | Dosya | Ne için |
   | --- | --- |
   | `RobotWinStudio-Setup-<sürüm>.exe` | Windows x64 kurulum, .NET içinde |
   | `RobotWinStudio-Setup-<sürüm>-lite.exe` | Windows x64 kurulum, .NET 9 Masaüstü Çalışma Zamanı ister |
   | `RobotWinStudio-<sürüm>-win-x64-portable.zip` | Kurulumsuz: açın ve `RobotWinStudio.exe`'yi çalıştırın |
   | `RobotWin-Headless-<sürüm>-win-x64.zip` | Ekransız simülatör ve otomasyon API'si (Windows) |
   | `RobotWin-Headless-<sürüm>-linux-x64.tar.gz` | Ekransız simülatör ve otomasyon API'si (Linux; içindeki `OKUBENI.txt`'ye bakın) |
   | `RobotWinStudio-<sürüm>-source.zip` / `.tar.gz` | Kaynak kodu |
   | `SHA256SUMS.txt` | Her dosyanın SHA-256 özeti |

   Grafik arayüz Windows içindir; Linux'ta simülasyon çekirdeği ekransız çalışır (ESP32 kodu orada derlenemez).
2. Çalıştırın ve sihirbazı izleyin.
3. Kurulum varsayılan olarak yalnızca sizin kullanıcınız için `%LOCALAPPDATA%\Programs\RobotWin Studio` klasörüne yapılır; Başlat menüsü kısayolu, isteğe bağlı masaüstü kısayolu ve dosya türü ilişkilendirmeleri eklenir. Kaldırmak için Windows *Uygulamalar* listesini veya kurulum klasöründeki `Uninstall.exe` dosyasını kullanın.

Ana paket .NET'i içinde taşır, ek kurulum gerekmez. Lite paket daha küçüktür ama **.NET 9 Masaüstü Çalışma Zamanı** ister; yoksa kurulum sihirbazı kurmayı önerir. (1.0.0 sürümünde .NET dahil paketin adı `-tam.exe` idi.)

**Komut satırından kurulum seçenekleri:**

| Komut | Etki |
| --- | --- |
| `RobotWinStudio-Setup-1.5.1.exe /S` | Sessiz kurulum (bu kullanıcı için) |
| `... /S /ALLUSERS` | Tüm kullanıcılar için (`Program Files`, yönetici izni gerekir) |
| `... /S /D=C:\Araclar\RobotWin` | Kurulum klasörünü seçer |
| `... /S /PORTABLE /D=E:\RobotWin` | Taşınabilir: yalnızca dosyaları açar (USB bellek için; kısayol ve kayıt defteri yok) |
| `... /NODESKTOP`, `/NOTYPES`, `/LAUNCH` | Masaüstü kısayolu yok, dosya türü ilişkilendirmesi yok, kurulumdan sonra başlat |

**İsteğe bağlı derleyiciler** (yalnızca kendi kodunuzu derlemek için; hazır örnekler derlenmiş olarak gelir):

- **Arduino Uno/Nano:** `winget install ArduinoSA.CLI` ve ardından `arduino-cli core install arduino:avr`.
- **ESP32:** Visual Studio 2022 (veya Build Tools) ile **"C++ ile masaüstü geliştirme"** iş yükü. ESP32 çizimleri bilgisayarınızda MSVC ile yerel olarak derlenir.

Yollar otomatik bulunur; bulunamazsa **Ayarlar → Derleyiciler** bölümünden `arduino-cli` ve `vcvars64.bat` yollarını elle girebilirsiniz.

## Hızlı başlangıç: ilk dünyanız 5 adımda

En hızlı yol: **Başlangıç** sayfasındaki **Dünyalar** listesinden bir şablon açın (örneğin *Arduino ile iki eksenli servo kol*) ve **F5** ile çalıştırın.

Sıfırdan küçük bir dünya kurmak için:

1. **Yeni dünya** (Ctrl+Shift+N). 4 × 3 m'lik boş bir atölye açılır.
2. Sağdaki **Parçalar** sekmesinde *4×AA Pil 6 V* ve *TT Redüktörlü Motor 1:48* arayıp ekleyin. Motoru seçip denetçideki **Fizik → Sabit** anahtarını açın (masaya sabitlenmiş gibi durur).
3. **Mekanik** sekmesinden bir **Tekerlek** ekleyin. Tekerlek seçiliyken **Bağla** aracını (B) seçip motora tıklayın ve **"Motor miline sabitle (birlikte döner)"** seçeneğini seçin.
4. **Kablo çek** aracıyla (K) pilin `+` ucunu motorun bir ucuna, `−` ucunu diğer ucuna bağlayın. Fare pinin üzerindeyken pin adı ve gerilimi görünür.
5. **Dünyayı çalıştır** (F5). Motor döner, tekerlek döner, motorun sesi duyulur; alttaki **Kablolar ve ölçümler** sekmesinde akımı görürsünüz. **Shift+F5** durdurup başa döner.

Ayrıntılı anlatım: [docs/kullanim/07-dunya.md](docs/kullanim/07-dunya.md).

## Aşama aşama özellikler

### Devre

- Aranabilir parça kütüphanesi: dirençler, kondansatörler, diyotlar, LED'ler, BJT/MOSFET'ler, op-amp'ler (LM358, TL072…), 74HC lojik, regülatörler (7805, AMS1117…), piller, röleler, DC motorlar (TT, N20, 25GA-370…), servolar (SG90, MG996R…), sensörler (HC-SR04, TCRT5000, LM35, PIR…), I²C ekranlar (SSD1306, 1602/2004 LCD), MPU-6050, motor sürücüler (TB6612, L298N, DRV8833), Arduino Uno/Nano, ESP32 DevKitC/DevKit V1, kablosuz cihazlar.
- Kablo (W), etiket (L), genel etiket (Ctrl+L), güç sembolü (P), numaralandırma, bul/değiştir (Ctrl+F/Ctrl+H), elektriksel kural denetimi (ERC).
- **Canlı simülasyon** (F5): modifiye düğüm analizi + Newton-Raphson çözücü, ısınma ve parça hasarı (yanma, sigorta atması), simülasyon hızı 0.01×…20×. Çalışırken butonlara basın, potansiyometreleri çevirin, kablolara tıklayıp ölçün.
- Alt paneller: **Osiloskop** (4 kanal, tetikleme, ölçümler, imleçler), **Multimetre** (6000 sayım, true-RMS, sigortalı akım girişleri), **Seri monitör**, **Kart pinleri**, **Monte Carlo** (toleranslarla N koşu, histogram), **Olaylar**, **Arıza tespiti** (ters LED, dirençsiz LED, kısa devre, geri dönüş diyodu eksik… ve "Neden yandı?" açıklaması).
- Dışa aktarma: BOM (CSV), KiCad netlist, SPICE netlist, PNG/SVG, yazdır/PDF.

### PCB

- 2, 4 veya 6 bakır katman; kart ayarları (kalınlık, bakır ağırlığı, kör/gömülü via, hat sınıfları).
- Bakır alanlar (Z): ısıl bağlantı, öncelik, ada temizleme; **Doldur**.
- Etkileşimli yol çizimi (X): engellerin etrafından dolaşır, V ile via, 45°/90°; otomatik yerleştirme ve otomatik yol çizimi.
- DRC: boşluklar, yol genişliği, delik, halka, kart kenarı, avlu çakışması, bağlanmamış ağlar, akım kapasitesi.
- **Üretim:** Gerber RS-274X (X2) + Excellon + BOM + CPL tek ZIP'te. **KiCad'e aktar:** KiCad 8 projesi (`.kicad_pro/.kicad_sch/.kicad_pcb`, kütüphaneler). 3D kart görünümü, footprint editörü, kartı modül parça olarak kaydetme.

### Kod

- Kart başına kod; şablonlar (Blink, PWM, buton, analog okuma, HC-SR04, engelden kaçan robot, çizgi izleyen, I²C tarayıcı, MPU-6050).
- **Derle** (F7) ve **Yükle ve çalıştır** (F5). Hatalar Türkçeleştirilir ve satırında gösterilir.
- **ATmega328P emülatörü:** tam komut seti, kesmeler, Timer0/1/2 (PWM), USART, ADC, EEPROM, watchdog, I²C (TWI); SSD1306, PCF8574+HD44780 LCD ve MPU-6050 cihaz modelleri. Hazır HEX dosyası da yüklenebilir.
- **ESP32 eş zamanlı simülasyonu:** çizim MSVC ile yerel bir DLL'e derlenir ve Arduino API düzeyinde devreyle adım adım çalışır: GPIO, ADC, LEDC PWM, DAC, zamanlayıcılar, FreeRTOS görevleri/kuyrukları, Wire, Servo, Wi-Fi (dünya içinde sanal ağ), BLE alt kümesi, Bluepad32 kumandaları.

### Parça

- Bir dosyada birden çok parça: en üstteki her nesne ayrı bir parçadır, altına bağlanan (Ctrl+P) nesneler onunla tek parça olur.
- Kutu, silindir, koni, küre, simit, boru, kama, delikli plaka, profil (ekstrüzyon), döndürme.
- Düzenleme modu (Tab): köşe/kenar/yüz seçimi ve G/R/S ile taşıma-döndürme-ölçekleme, ekstrüzyon (E, yüzlerden ya da kenarlardan), içe öteleme (I), bıçak (K), halka kesim (Ctrl+R), pah kırma (Ctrl+B), kalınlık verme, bölme, yumuşatma, birleştirme.
- Dilimleme (K): nesneyi çizilen çizgi boyunca iki kapalı parçaya ayırır; parçalar ayrı ayrı taşınıp döndürülür.
- CSG: birleşim, fark (iç içe geçen kısmı kırpma), kesişim; ayna, dizi, ebeveyn bağlama.
- Hazır robot parçaları: 2WD şasi plakası, SG90 servo braketi, L braket, tekerlek göbeği, muhafaza kutusu + kapak.
- Malzeme ve doluluk oranından kütle; STL/OBJ/PLY içe/dışa aktarma; **Montaja gönder**: her parça ayrı gövde, yeniden gönderince yerinde güncellenir (kendi orijini ve bağlantıları korunur).

### Montaj

- Robotun kendisi: kurulurken hangi projelerin ve nasıl geleceği sorulur. **PCB'li**: PCB'ye lehimli parçalar kartın üzerinde, motor, servo, pil, sensör ve modüller ayrı gerçek 3D parçalar olarak, ağlara göre pin pin kablolu. **PCB'siz**: her parça ayrı. Birden çok proje aynı montajda yan yana.
- Projeyle birlikte kaydedilir. Her açılışta devre, PCB ve kod değişikliklerini alır: yeni parça eklenir, silinen kalkar, kod güncellenir; yerleştirdiğiniz yerler, bağlantılar ve kendi kablolarınız korunur.
- Araç projeleri (iki motorlu gezgin) gerçek parçalarla kurulur: şase plakası, motor millerine takılı tekerlekler, sarhoş teker, şase üstünde birbirine değmeden kart ve modüller, arenasında. Sağ motor gerçek kitlerdeki gibi aynalı takılır ve kabloları ters bağlanır.
- Parça sayfasındaki parçalar ayrı gövdeler olarak gelir; bağlantılar Bağla aracıyla kurulur, her şey gerçek fizikle çalışır.

### Dünya

- **Nesneler:** kendi tasarladığınız kartlar (devre + PCB + kod, aynı tasarımdan birden fazla kopya), tek başına katalog parçaları, mekanik gövdeler (kutu, plaka, kol, silindir, tekerlek, küre, masa), içe aktarılan 3D modeller (STL, OBJ, PLY).
- **Kablolar:** AWG kesiti, uzunluk ve renk; hazır jumper boyları (10/15/20/30/50 cm); gerçek bakır direnci ve voltaj düşümü, akım sınırı, aşırı yükte ısınıp yanma. Halat fiziğiyle sarkar; kısa kalırsa gerilir, fişi çekip çıkarır. **Kablolar ve ölçümler** sekmesi her kablonun boyunu uçlar arası mesafeyle karşılaştırır (bol / yeterli / gergin / yetmiyor) ve akım, güç, voltaj düşümü, yük yüzdesini gösterir.

  ![Kablolar ve ölçümler: boy, uçlar arası mesafe, voltaj düşümü](docs/images/world-cables.png)
- **Mekanik bağlantılar:** sabit (vida/yapıştırıcı), servo koluna/motor miline sabitleme, menteşe, kızak.
- **Fizik:** yerçekimi (Dünya, Ay, Mars, Jüpiter, sıfır), malzemeye göre sürtünme ve sekme, kutu/küre/silindir/üçgen ağ çarpışmaları, her parça kendi 3D modelinin biçimiyle çarpışır ve içi boş modeller (tepsi, kap, boru) dışbükey parçalara ayrılarak içlerine bir şey konabilir, 1 ms sabit adım; servolar ve DC motorlar devreyle çift yönlü bağlıdır (yük devreye geri döner).
- **Gaz ve roketler:** basınçlı kaplar, su roketi ve katı yakıtlı motorlar tek gaz fiziğiyle çalışır. Sıcak ürünlerde Noble–Abel gerçek gaz, izentropik lüle akışı ve Schmucker ayrılması hesaplanır. Ateşleme gerçek zincirle olur: e-kibritin köprü teli ısınır, ateşleyici barutu yanar, yakıt yüzeyi taşınım ve ışınımla ısınıp tutuşma sıcaklığına çıkar, alev baştan lüleye yayılır. Ateşleyici zayıfsa motor ateşlenmez. Yanma hızı r = a·pⁿ ile dar kanallarda aşındırıcı yanmayı (Lenoir–Robillard) içerir. Çeper ve lüle boğazı ısınır (Dittus–Boelter, Bartz) ve ısı kamerasında görünür. Geri sayımla kapanan fırlatma anahtarı ve tepe noktasında açılan paraşüt de vardır. **Katı yakıtlı roket** şablonu ~1 km'ye çıkıp paraşütle iner.

  ![Katı yakıtlı roket: ateşlemeden 0,1 s sonra 24 bar, 1275 °C, 100 N](docs/images/world-rocket-ignite.png)
- **Sıvılar:** su, deniz suyu, alkol, süt, yağ, gliserin, bal; gerçek yoğunluk, viskozite ve yüzey gerilimiyle parçacık olarak akar (konum tabanlı akışkan). Duvarlara gerçek basıncını uygular; kap suyun ağırlığını taşır, içindeki parçalar kaldırma kuvveti görür. Cam ve akrilik saydam çizilir, su yüzeyi Fresnel yansımasıyla görünür. Render'da sıvı tam hesaplanır (30 000 parçacığa kadar). **Su bardağı** şablonunda servo bardağı devirip suyu kâseye döker.
- **Sensörler:** HC-SR04 ultrasonik sensör dünyadaki nesnelere ışın izleme ile gerçekten ölçer; I²C ekranlar 3D model üzerinde canlı görünür.
- **Radyo ve USB:** 2.4 GHz ortamı (BLE, Wi-Fi, Bluetooth Classic kumandalar), uzaklık ve engelle zayıflama, IR (NEC) kumanda; DualSense/DualShock 4/Xbox kumandalar (gerçek kumanda veya ekrandaki sanal kumanda); bilgisayar + USB kablosu ile besleme ve seri monitör.
- **Sahne ağacı**, olay günlüğü, kablo ölçümleri, RF paneli; simülasyon sesleri (motor, servo, buzzer, röle, yanma).
- **Kayıt ve render** (Dünya araç çubuğunda ● Kayıt, Ctrl+R; ya da Simülasyon › Render, Ctrl+F5): kaydı bitirdiğinizde o süre ekrandan bağımsız olarak, seçtiğiniz doğrulukta (0,05 ms'ye kadar fizik adımı) baştan hesaplanır: her parçanın konumu, hızı, kuvvetleri, tüm akım/gerilim/güç sinyalleri; video kare hızında **her parçada FEM gerilmesi** (dörtyüzlü ya da kiriş elemanlar), **tüm montajın etrafında 3B hava akışı** (lattice-Boltzmann, montaja etkiyen kuvvet ve Cd) ve **ısınma**. Ardından aynı 3B sahnede video gibi oynatılır: zaman çizelgesinde gezinin, ağır çekim izleyin, her parçayı gerilmesine göre renklendirin (deformasyon büyütülmüş), temas ve eklem kuvvetlerini ok olarak, hava akışını kesit olarak görün; grafiğe tıklayınca o saniyeye atlanır. Sonuçlar `.rwanalysis` ya da CSV olarak saklanır.

  ![Render: her parçada gerilme, kuvvetler ve montaj etrafında hava akışı](docs/images/world-render.png)

### Pencere ve gezinme

- Belge sekmeleri (sürükleyerek sıralama), **bölünmüş görünüm** (Ctrl+\\), sayfayı **ayrı pencereye alma**, **odak modu**, **tam ekran** (F11), **komut paleti** (Ctrl+K) ile her şeyi arama.
- Otomatik yedekleme (`Belgeler\RobotWin\Yedekler`), son açılan dosyalar, arayüz ölçeği %75–150.

## Klavye kısayolları

| Kısayol | İşlev |
| --- | --- |
| Ctrl+N / Ctrl+Shift+N | Yeni devre / kart · Yeni dünya |
| Ctrl+O / Ctrl+S / Ctrl+Shift+S | Aç · Kaydet · Farklı kaydet |
| Ctrl+Z / Ctrl+Y | Geri al · Yinele |
| Ctrl+K | Komut paleti (her şeyi ara) |
| Ctrl+0 … Ctrl+7 | Başlangıç, Devre, PCB, Kod, Parça, Montaj, Dünya, Kütüphane |
| Ctrl+, | Ayarlar |
| Ctrl+B | Tasarım hattını genişlet / daralt |
| Ctrl+\\ | Bölünmüş görünüm aç / kapat |
| Ctrl+Tab / Ctrl+Shift+Tab / Ctrl+W | Sonraki / önceki sekme · Sekmeyi kapat |
| F5 / Shift+F5 / Ctrl+F5 / Ctrl+R | Simülasyonu başlat-duraklat · Durdur · Render (Dünya) · Kayıt al (Dünya) |
| F7 | Derle (Kod sayfası) |
| F10 | Menü çubuğu |
| F11 / Esc | Tam ekran · Tam ekrandan çık |

Sayfalara özgü kısayolların (devre, PCB, modelleyici, dünya) tam listesi: [docs/kullanim/09-kisayollar.md](docs/kullanim/09-kisayollar.md).

## Dosya türleri

Hepsi içinde `manifest.json` bulunan sürümlü ZIP kapsayıcılarıdır.

| Uzantı | İçerik |
| --- | --- |
| `.rwproj` | Proje / kart: şema, PCB, mekanik montaj, kart kodları ve son derlemeler, projeye özel parçalar, önizleme |
| `.rwworld` | Dünya: ortam, nesneler, kablolar, mekanik bağlantılar; içindeki kartlar tam proje olarak saklanır |
| `.rwpart` | Tek kullanıcı parçası (simülasyon modeli, parametreler, isteğe bağlı footprint ve 3D model) |
| `.rwlib` | Paylaşım için parça koleksiyonu |
| `.rwsim` | Simülasyon kaydı: ölçüm kanalları (CSV), olaylar, seri çıktı, araç yörüngesi, proje |
| `.rwmodel` | Modelle sayfasının 3D model belgesi |
| `.rwfp` | Footprint (PCB ayak izi) |

Kurulum bu türleri Windows'a kaydeder; elle yapmak için **Ayarlar → Dosya türleri** (yalnızca bu kullanıcı, HKCU).

## Kaynaktan derleme

Gereksinimler: Windows 10/11 x64, [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (`global.json` 9.0.112 ve üstünü ister), Git.

```powershell
git clone https://github.com/Kinin-Code-Offical/robotwin-studio.git
cd robotwin-studio
dotnet build RobotWinStudio.sln -c Debug
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe
```

Çıktılar `builds\<proje adı>\` altına gider (`Directory.Build.props`). Kurulum dosyası üretmek için Visual Studio C++ araçları gerekir:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.5.1
# -> builds\installer\RobotWinStudio-Setup-1.5.1.exe
```

Ayrıntılar: [docs/SETUP_WINDOWS.md](docs/SETUP_WINDOWS.md), [docs/BUILD_RELEASE.md](docs/BUILD_RELEASE.md).

## Testler

```powershell
dotnet test RobotWinStudio.sln
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
```

`--selftest` her örnek projeyi derler, simüle eder, PCB'sini yerleştirip yollarını çizer ve DRC çalıştırır; sonuç `SELFTEST PASS` olmalıdır. CI (`.github/workflows/ci.yml`) her push ve PR'da derleme, birim testleri ve self-test çalıştırır. Ayrıntılar: [docs/TESTING.md](docs/TESTING.md).

### Doğruluk: neye ne kadar güvenebilirsiniz

- **Analitik çözümlerle ve kıyaslama problemleriyle doğrulananlar:** konsol kiriş sehimi ve gerilmesi, delikli plakada gerilme yığılması (Kt ≈ 3), burulma, serbest parçada atalet dengesi, Poiseuille akışı, Schäfer–Turek silindir kıyaslamaları (Re 20 sürüklemesi, Re 100 girdap kopması), küre ve küp sürüklemesi, ısı iletimi, BMP280 datasheet örneği (adc_T = 519888 → 25,08 °C), motorun datasheet yüksüz akımı ve devri ile fizikteki mil turunun birbirini tutması. Sapmalar ve hata payları: [Doğrulama](#doğrulama-teoriden-ne-kadar-sapıyor).
- **Sizin parçanız için:** analitik tablolar, sizin parçanızın ağının ve mesnetlerinin gerçeğe ne kadar yakın olduğunu söylemez. Bunun için iki araç vardır. **Ağ yakınsaması denetimi** en kötü anı daha ince ağla yeniden çözer ve farkı yazar. **.inp dışa aktarma** aynı durumu (ağ, malzeme, mesnetler, o anki yükler) CalculiX / ANSYS / Abaqus'a taşır; sonucu orada çözüp karşılaştırırsınız. Nokta yüklerin hemen altındaki tepe gerilme her FEM kodunda ağ inceldikçe büyür, bu yüzden karşılaştırmada %98'lik değer kullanılır.
- **Parça yayılımı:** datasheet değeri gerçek parçanın tipik değeridir. Klon (yan sanayi) servo ve motorlar akımda %30–40 sapar. Ekranda okunan akımlar bu belirsizlikle (±%) gösterilir; render'daki belirsizlik bandı gerçek parçaların düşebileceği aralığı çizer. Güç kaynağı ve kablo kesiti seçerken tezgâh ölçümüyle doğrulayın.
- **Firmware:** ESP32'de SPI gerçekten bağlı cihazla konuşur. Yanlış CS pini, bağlanmamış MISO, uyumsuz SPI modu ve fazla yüksek saat gerçek karttaki gibi hatalı veri verir. Görev watchdog'u ESP-IDF 5 gibi çalışır: besleme kesilirse rapor basar ve kartı yeniden başlatır.

## Komut satırı

| Komut | Etki |
| --- | --- |
| `RobotWinStudio.exe <dosya>` | Bir `.rwproj`, `.rwworld`, `.rwsim`, `.rwpart` veya `.rwlib` dosyası açar |
| `--selftest [--selftest-out rapor.txt]` | Arayüzsüz uçtan uca kontrol; çıkış kodu 0 = geçti |
| `--snapshot ekran.png --page <sayfa> [--example <id>] [--simulate <sn>] [--fault <senaryo>] [--size 1600x960] [--wait <sn>] [--nav-expanded]` | Bir sayfayı PNG olarak kaydeder (belgeler ve görsel test için) |
| `--gallery <klasör> [--gallery-filter …] [--gallery-columns 4] [--gallery-tile 460x340]` | Tüm 3D parça modellerinin PNG özet sayfaları |
| `--export-logo <klasör>` | Logoyu PNG + ICO olarak dışa aktarır |
| `--register-file-types` / `--unregister-file-types` | Dosya türlerini kaydeder / kaldırır (kurulum programı kullanır) |

`--page` değerleri: `home`, `schematic`, `pcb`, `code`, `model`, `assembly`, `world`, `library`, `settings`. Dünya sayfasında `--fault` bir dünya şablonu kimliğidir (örneğin `--page world --fault ps5-car`).

## Proje yapısı

```text
RobotWinStudio.sln        Uygulama + çekirdek + testler
Studio/RobotWin.Studio/   WPF masaüstü uygulaması (sayfalar, dünya, 3D modeller, modelleyici, PCB, ölçü aletleri)
CoreSim/src/RobotTwin.CoreSim/  Simülasyon çekirdeği (analog çözücü, parçalar, firmware, PCB, fizik, dünya, dosya biçimleri)
CoreSim/native/esp32hal/  ESP32 Arduino HAL katmanı (C++; çizimlerle birlikte MSVC ile derlenir)
CoreSim/tests/            xUnit testleri
Installer/                Yerel Win32 kurulum programı (setup.cpp)
tools/build-installer.ps1 Kurulum dosyası üretir
docs/                     Belgeler, kullanım kılavuzu, ekran görüntüleri
FirmwareEngine/, NativeEngine/  Eski C++ motorları (uygulama kullanmaz)
```

Ayrıntılar: [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md), mimari: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Yol haritası (geliştiriliyor)

Aşağıdakiler üzerinde çalışılıyor; **henüz ana dala alınmadı** ve yayımlanmış sürümlerde yoktur:

- **Akışkanlar:** su altı, kaldırma kuvveti, sıvı akışı (gaz basıncı, lüle akışı ve su roketi artık var).
- **Tek saat ve birlikte hata ayıklama:** firmware kesme noktasında MCU, devre ve fizik birlikte durur; karışık sinyal bölümleme.
- **Mekanik ve endüstriyel parçalar:** vida/geçme, kırılma, dişli/kayış/yay/sönümleyici, profiller, lineer raylar, step motor ve sürücüler, pnömatik, paletli araçlar, yumuşak gövdeler.
- **Daha fazla MCU:** Mega, ATtiny, RP2040, STM32, ESP8266, ESP32-C3/S3; SPI/CAN/ESP-NOW/LoRa sürüleri; PLC (ladder/ST, Modbus).
- **Köprüler:** STEP/URDF/KiCad proje içe aktarma, `custom_parts` klasörü.
- **Dil:** İngilizce varsayılan arayüz, Türkçe ve eklenebilir dil paketleri.

## Katkı, güvenlik, belgeler

- [Kullanım kılavuzu (Türkçe)](docs/kullanim/README.md) · [Geliştirici belgeleri](docs/README.md) · [Sürüm notları](CHANGELOG.md)
- [Katkıda bulunma](CONTRIBUTING.md) · [Güvenlik](SECURITY.md) · [Davranış kuralları](CODE_OF_CONDUCT.md) · [Erişilebilirlik](docs/ACCESSIBILITY.md) · [Üçüncü taraf bildirimleri](THIRD_PARTY_NOTICES.md)
- Sorun bildirmek için: [GitHub Issues](https://github.com/Kinin-Code-Offical/robotwin-studio/issues)

## Lisans

MIT. Ayrıntılar için [LICENSE](LICENSE) dosyasına bakın.

---

## English

**RobotWin Studio** is a Windows desktop app for building robot and electronics projects end to end: draw the circuit, lay out the PCB, write the firmware, model the chassis, then drop everything into an open world with real physics, wire it up and run it all together. Arduino (ATmega328P) firmware runs as real compiled HEX on an emulated MCU, ESP32 sketches run in co-simulation with the circuit, and motors, servos, sensors, cables and parts genuinely affect each other. The user interface is in Turkish.

Documentation site: **[kinin-code-offical.github.io/robotwin-studio](https://kinin-code-offical.github.io/robotwin-studio/)** — the step-by-step user guide (complete in Turkish and English, with screenshots), the physics and maths reference and the developer docs.

> **New in 1.5.1:** the Arduino compiler comes with the installer (Uno / Nano code builds right away), and the **İlk robotun** (your first robot) guide gets your first robot running in 5–10 minutes.
>
> **New in 1.5.0:** The rail in three groups (electronics, mechanics, test). The Parça (part) page holds several parts per file, and *send to assembly* makes each one a body of its own. The Montaj (assembly) is the robot itself: projects come in with or without their PCB, several projects share one Montaj, and it is saved with its project and follows its circuit, PCB and code. Robot examples are built as real vehicles. Arduino libraries on the ESP32, PCBs of up to 32 layers. **1.4.2:** the code page is a code editor. Details: [11. What's new](docs/en/guide/11-whats-new.md), [CHANGELOG.md](CHANGELOG.md).

### What's inside

The left-hand **"design-to-real line"** (*Tasarımdan gerçeğe hattı*) shows a project's stages in three groups: **ELEKTRONİK** (electronics: circuit, PCB, code), **MEKANİK** (mechanics: part, assembly), **TEST** (world).

| Stage (UI name) | Shortcut | What you do |
| --- | --- | --- |
| Circuit (**Devre**) | Ctrl+1 | Schematic capture with 256 real parts, live analog simulation, 4-channel oscilloscope, multimeter, bench supply, tolerances, Monte Carlo, fault diagnosis, ERC; BOM/KiCad/SPICE/PNG/SVG export. |
| **PCB** | Ctrl+2 | Boards of 2 to 32 copper layers, copper zones, interactive router that walks around obstacles, auto-placement and auto-routing, DRC, Gerber RS-274X X2 + Excellon + BOM + CPL, KiCad 8 project export, footprint editor, 3D board view. |
| Code (**Kod**) | Ctrl+3 | Write, build (F7) and run (F5) firmware: Arduino Uno/Nano via `arduino-cli` on a cycle-counted ATmega328P emulator (timers/PWM, USART, ADC, EEPROM, watchdog, TWI with SSD1306/LCD/MPU-6050 models); ESP32 sketches compiled natively with MSVC against an Arduino-level HAL (GPIO, ADC, LEDC, DAC, timers, FreeRTOS, Wire, Servo, simulated Wi-Fi, BLE subset, Bluepad32). |
| Part (**Parça**) | Ctrl+4 | Blender-like modeller, several parts per file: primitives, edit mode (vertex/edge/face, extrude, inset, knife, loop cut, bevel, solidify, subdivide, …), slicing into two closed pieces, CSG union/difference/intersection, mirror/array, parametric robot parts, materials and mass, STL/OBJ/PLY import/export; *send to assembly* makes each part a body of its own. |
| Assembly (**Montaj**) | Ctrl+5 | The robot itself: the boards and parts of one or more projects (with or without their PCB) and the bodies modelled on the Parça page, joined. Saved with the project and brought up to date with its circuit, PCB and code when opened; rover projects are built on their chassis with the wheels on the motor shafts. Record and run fault experiments. |
| World (**Dünya**) | Ctrl+6 | Place your boards, catalogue parts, mechanical bodies and imported meshes in a physics world; wire them with real cables (AWG, standard jumper lengths, resistance and voltage drop, rope physics that pulls plugs out when too short, burn-out), join them (fixed, servo horn/motor shaft, hinge, slider) and run everything in lock-step: circuit, firmware, radio and rigid-body physics. Pressure vessels, water rockets and solid motors share one gas physics: Noble–Abel real gas for hot products, isentropic nozzle flow with Schmucker separation, ignition through the real chain (bridgewire, pyrogen, grain surface soak by convection and radiation, flame spread from the head end; a weak igniter fails), erosive burning, wall and throat heating (Dittus–Boelter, Bartz) shown in the thermal camera, launch switches on a countdown and parachutes at apogee — the **solid rocket** template climbs to ~1 km and comes down under its canopy. Ultrasonic sensors ray-cast the scene; BLE/Wi-Fi/Bluetooth gamepads and IR remotes are simulated; a PC + USB cable powers a board and opens its serial port. **Record and render** (● Record, Ctrl+R; Simulation › Render, Ctrl+F5): the recorded stretch is recomputed off-screen at full accuracy — every pose, force and signal, FEM stress of every part at video frame rate, 3D lattice-Boltzmann flow around the whole assembly and heating — and replayed like a video in the same 3D scene with a timeline, slow motion, per-part stress colours, force arrows, a flow slice and graphs that jump to the clicked second. |

Plus **Home** (Ctrl+0), **Library** (Ctrl+7: browse parts, make custom `.rwpart` parts, import KiCad symbols/footprints) and **Settings** (Ctrl+,). Window features: document tabs, split view (Ctrl+\\), detach a page into its own window, focus mode, full screen (F11), command palette (Ctrl+K), autosave backups.

Screenshots are shown in the [Turkish section above](#ekran-görüntüleri).

### Validation: how far from theory?

Every solver is checked against problems with a known answer: analytic solutions, published benchmarks and datasheet values. Each row is an automated test that runs on every build and fails it when the deviation exceeds the tolerance.

| Area | Measurement | Theory / reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- | --- |
| Mechanics | Hinged pendulum period | 0.89769 s | 0.89767 s | −0.002 % | ±0.5 % |
| Mechanics | Terminal velocity of a sphere in air | 8.5568 m/s | 8.5567 m/s | −0.001 % | ±0.5 % |
| FEM | Cantilever tip deflection (Timoshenko) | 0.20156 mm | 0.19993 mm | −0.81 % | −3 / +2 % |
| FEM | Plate with a hole, Kt | 3.133 | 2.946 | −6.0 % | ±8 % |
| Heat | Energy balance of a heated plate | 18.797 K | 18.798 K | +0.005 % | ±2 % |
| Aerodynamics | Wing C_L (AR 6, 4°), lifting line | 0.318 | 0.319 | +0.3 % | ±7 % |
| Flow (CFD) | Cylinder Cd, Schäfer–Turek 2D-1 (Re 20) | 5.5795 | 5.624 | +0.8 % | ±2 % |
| Flow (CFD) | Cylinder Strouhal, Schäfer–Turek 2D-2 (Re 100) | 0.300 | 0.2999 | −0.03 % | ±5 % |
| Flow (CFD) | Sphere Cd (Re 100, Schiller–Naumann) | 1.092 | 1.176 | +7.7 % | −8 / +10 % |
| Flow (CFD) | Cube Cd, Re 10⁵ (real Re, LES) | ≈1.05 | 0.883 | −15.9 % | −22 / +15 % |
| Liquids | Force of 6 cm of still water on a side wall | 1.0576 N | 1.0795 N | +2.1 % | ±5 % |
| Liquids | Dam-break front (Martin & Moyce 1952), T = 2.98 | 3.67 | 3.82 | +4.1 % | ±12 % |
| Gas | Air tank blow-down, 0.5 s (adiabatic) | 4.671 bar | 4.709 bar | +0.8 % | −2 / +3 % |
| Electrical | Motor no-load speed (datasheet) | ≈209 rpm | 210 rpm | +0.5 % | −12 / +2 % |

All 54 measurements in eight areas, grid convergence and a before/after comparison: **[12. Validation](docs/en/guide/12-validation.md)**.

### Download and install

1. Download `RobotWinStudio-Setup-<version>.exe` (carries .NET, nothing else to install) or the smaller **lite** package `RobotWinStudio-Setup-<version>-lite.exe` from [Releases](https://github.com/Kinin-Code-Offical/robotwin-studio/releases/latest). Each release also has, as separate files: `RobotWinStudio-<version>-win-x64-portable.zip` (no installation: unzip and run), `RobotWin-Headless-<version>-win-x64.zip` and `RobotWin-Headless-<version>-linux-x64.tar.gz` (the windowless simulator and automation API; the graphical app is Windows-only), the source as `RobotWinStudio-<version>-source.zip` / `.tar.gz`, and `SHA256SUMS.txt`.
2. Run it. The lite package needs the **.NET 9 Desktop Runtime**; its wizard offers to install it if missing. (In 1.0.0 the package with .NET was named `-tam.exe`.)
3. Default install is per user in `%LOCALAPPDATA%\Programs\RobotWin Studio`. Silent: `/S`; all users: `/S /ALLUSERS`; target folder: `/D=<dir>`; portable unpack: `/S /PORTABLE /D=<dir>`; also `/NODESKTOP`, `/NOTYPES`, `/LAUNCH`. Uninstall from Windows *Apps* or `Uninstall.exe`.

Optional compilers for your own firmware (bundled examples ship precompiled): `winget install ArduinoSA.CLI` then `arduino-cli core install arduino:avr` for AVR; Visual Studio 2022 / Build Tools with the **Desktop development with C++** workload for ESP32. Paths can be overridden in **Settings → Compilers** (*Ayarlar → Derleyiciler*).

### Quick start: your first world in 5 steps

1. **New world** (*Yeni dünya*, Ctrl+Shift+N): an empty 4 × 3 m workshop opens.
2. In the **Parts** tab (*Parçalar*) add *4×AA Pil 6 V* and *TT Redüktörlü Motor 1:48*; select the motor and switch on **Physics → Fixed** (*Fizik → Sabit*).
3. From the **Mechanical** tab (*Mekanik*) add a **Wheel** (*Tekerlek*); with the wheel selected, pick the **Join** tool (B), click the motor and choose **"Motor miline sabitle (birlikte döner)"** (fix to motor shaft).
4. With the **Cable** tool (K) connect battery `+` to one motor pin and `−` to the other.
5. **Run the world** (F5): the motor spins the wheel, you hear it, and the *Kablolar ve ölçümler* tab shows the cable current. Shift+F5 stops and resets.

Or simply open a template from the Home page (e.g. *Arduino ile iki eksenli servo kol*) and press F5.

### Keyboard shortcuts

Ctrl+N new circuit/board · Ctrl+Shift+N new world · Ctrl+O open · Ctrl+S save · Ctrl+Shift+S save as · Ctrl+Z/Ctrl+Y undo/redo · Ctrl+K command palette · Ctrl+0…7 pages · Ctrl+, settings · Ctrl+B expand rail · Ctrl+\\ split view · Ctrl+Tab/Ctrl+Shift+Tab/Ctrl+W tabs · F5/Shift+F5 run-pause/stop · F7 build · F10 menu bar · F11 full screen. Page-specific keys: [docs/kullanim/09-kisayollar.md](docs/kullanim/09-kisayollar.md).

### File types

All are versioned ZIP containers with a `manifest.json`: `.rwproj` (project/board: schematic, PCB, assembly, firmware sources and builds), `.rwworld` (world, with embedded boards), `.rwpart` (custom part), `.rwlib` (part collection), `.rwsim` (simulation recording), `.rwmodel` (modeller document), `.rwfp` (footprint).

### Build from source

Requirements: Windows 10/11 x64, .NET 9 SDK (pinned by `global.json`), Git.

```powershell
git clone https://github.com/Kinin-Code-Offical/robotwin-studio.git
cd robotwin-studio
dotnet build RobotWinStudio.sln -c Debug
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe
```

Installer (needs Visual Studio C++ tools): `powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.5.1` → `builds\installer\RobotWinStudio-Setup-1.5.1.exe`. Pushing a `v*` tag runs `.github/workflows/release.yml`, which builds every release file on the system it is for (both installers, the portable zip, the Windows and Linux headless packages, the source archives), checks that the portable app and the headless simulators start, and attaches them with `SHA256SUMS.txt` to the GitHub release. See [docs/BUILD_RELEASE.md](docs/BUILD_RELEASE.md).

### Tests

`dotnet test RobotWinStudio.sln` runs the xUnit suite in `CoreSim/tests`. `RobotWinStudio.exe --selftest --selftest-out selftest.txt` builds, simulates, auto-places, auto-routes and DRC-checks every example project and must end with `SELFTEST PASS`. `--snapshot` renders any page to PNG for visual checks. See [docs/TESTING.md](docs/TESTING.md).

**How far to trust it.**
- **Checked against analytic results:** cantilever deflection and stress, the plate-with-hole stress concentration (Kt ≈ 3), torsion, inertia relief, Poiseuille flow, cylinder shedding (Strouhal), sphere drag (Schiller–Naumann), heat conduction, the BMP280 datasheet example, and the motor's datasheet no-load current and speed against the physics shaft's turns.
- **For your own part:** the mesh and the supports matter more than any table. Two tools address this. The *mesh convergence check* re-solves the worst instant on a finer mesh and reports the change. The *.inp export* writes the same case (mesh, material, supports, loads at that instant) for CalculiX, ANSYS or Abaqus, so you can compare against an independent solver.
- **Part spread:** datasheet values are typical. Clone servos and motors differ by 30–40 % in current. Readings show this uncertainty, and the render's uncertainty band draws the range real parts may fall in. Bench-check before sizing a supply.
- **ESP32 behaves like the real chip:** SPI talks to the device that is really wired, and wiring or mode mistakes give wrong data as on the bench. The ESP-IDF task watchdog fires and reboots the chip.

### Command line

`RobotWinStudio.exe <file>` opens a file; `--selftest [--selftest-out f]`; `--snapshot out.png --page home|schematic|pcb|code|model|assembly|world|library|settings [--example id] [--simulate s] [--fault name] [--size WxH] [--wait s] [--nav-expanded]`; `--gallery <dir>`; `--export-logo <dir>`; `--register-file-types` / `--unregister-file-types`.

### Project layout

`Studio/RobotWin.Studio` (WPF app) → `CoreSim/src/RobotTwin.CoreSim` (simulation core) with `CoreSim/native/esp32hal` (ESP32 HAL sources) and `CoreSim/tests` (xUnit). `Installer/` + `tools/build-installer.ps1` produce the setup. `FirmwareEngine/` and `NativeEngine/` are legacy C++ engines from the 1.x design and are not used by the app. See [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

### Roadmap (in progress)

Being worked on, **not merged yet** and not in any release:

- Fluids: underwater, buoyancy, liquid flow (gas pressure, nozzle flow and water rockets are already in).
- One clock and co-debugging: a firmware breakpoint freezes MCU, circuit and physics together; mixed-signal partitioning.
- Mechanical and industrial parts: fasteners and fits, breakage, gears/belts/springs/dampers, extrusions, linear rails, steppers and drivers, pneumatics, tracked vehicles, soft bodies.
- More MCUs (Mega, ATtiny, RP2040, STM32, ESP8266, ESP32-C3/S3), SPI/CAN/ESP-NOW/LoRa swarms, PLC (ladder/ST, Modbus).
- Bridges: STEP/URDF/KiCad project import, a `custom_parts` folder.
- English as the default UI language, Turkish and add-on language packs.

### License

MIT. See [LICENSE](LICENSE). Contributions: [CONTRIBUTING.md](CONTRIBUTING.md) · Security: [SECURITY.md](SECURITY.md) · Accessibility: [ACCESSIBILITY.md](docs/ACCESSIBILITY.md) · Changes: [CHANGELOG.md](CHANGELOG.md).
