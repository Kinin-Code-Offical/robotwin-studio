# Araçlar

Günlük geliştirme için yalnız .NET CLI, uygulamanın kendi komut satırı kipleri ve bir PowerShell betiği gerekir.

## .NET CLI

| İş | Komut |
| --- | --- |
| Derleme (hata ayıklama) | `dotnet build RobotWinStudio.sln -c Debug` |
| Derleme (sürüm) | `dotnet build RobotWinStudio.sln -c Release` |
| Birim testleri | `dotnet test RobotWinStudio.sln` |
| Uygulamayı çalıştırma | `builds\RobotWin.Studio\Debug\net10.0-windows\RobotWinStudio.exe` |
| Temizleme | `dotnet clean RobotWinStudio.sln` (ya da `builds\`'i silin) |
| Güvenlik açığı olan paketler | `dotnet list RobotWinStudio.sln package --vulnerable` |

## Uygulamanın komut satırı kipleri

`RobotWinStudio.exe` aynı zamanda bir araçtır. Bütün kipler işini bitirince kapanır. Ekran görüntüsü, ölçüm, öz test, galeri ve çakışma çalıştırmaları varsayılan ayarlar, varsayılan panel düzeni ve boş bir son dosyalar listesiyle başlar ve bunların hiçbirini kaydetmez: kullanıcının kendi ayarları olduğu gibi kalır.

| Kip | Kullanım |
| --- | --- |
| `--selftest [--selftest-out dosya]` | Her örneğin arayüzsüz uçtan uca denetimi (bkz. [Testler](TESTLER.md)). |
| `--snapshot cikti.png --page <sayfa> [...]` | Belgeler ve görsel denetim için bir sayfayı PNG'ye çizer (bkz. [Testler](TESTLER.md)). |
| `--overlap-report <dosya>` | Her dünya şablonunu kurar ve iç içe geçen ya da kabuğuna sığmayan parçaları listeler. |
| `--bench <dosya> [--bench-seconds 4] [--bench-filter id,id] [--bench-copies N]` | Kare hızı ölçümü: her dünya şablonunu (ya da adı verilenleri) açar, dururken ve çalışırken ölçer. `--bench-copies N` her şablonu aynı dünyada N kez (nesneleri, bağlantıları ve kablolarıyla, ızgarada) kurar: büyük sahne ölçümü. Satırlar p95, p99, en kötü kare ve işlemin belleğini de yazar. Kare hızını, kare sürelerini, karenin her bölümünün maliyetini ve şablonun açılışının nereye gittiğini yazar. Günlük ölçüm sürerken yazılır; son satırı o anda yapılan adımı söyler. |
| `--startup-report <dosya>` | Normal bir açılış (açılış ekranı dahil); her adımın işlem başından itibaren zamanını yazar ve kapanır. |
| `--snapshot cikti.png --page splash` | Açılış ekranını açılışın ortasındaki hâliyle çizer. |
| `--snapshot cikti.png --page world --fault <şablon> --renderer wpf` | 3B görünümü bu çalıştırma için WPF'ye (ya da `gpu` ile kendi çizicimize) çizdirir; hiçbir şey kaydedilmez. `--bench` ile de çalışır, iki çiziciyi karşılaştırmak için. |
| `--esp32-build cizim.ino [--esp32-out klasör] [--esp32-install]` | Çizimi gerçek ESP32 için Espressif'in araç zinciriyle derler (donanım uyumlu mod); `.bin`, `.bootloader.bin`, `.partitions.bin`, `.merged.bin` ve `.elf` klasöre kopyalanır, günlük `cizim.esp32build.txt`'ye yazılır. `--esp32-install` araç zinciri yoksa önce kurar. |
| `--snapshot cikti.png --page schematic --example esp32-dimmer --esp32-mode hardware --simulate 1.5 --fault serial` | Bu çalıştırmanın devre ve dünyadaki bütün ESP32'lerini donanım uyumlu modda çalıştırır (Xtensa emülatöründe, hiçbir şey kaydedilmez; `fast` tersi) ve seri monitörü gösterir (`--fault log`: olaylar). |
| `--esp32-compare rapor.md [--esp32-seconds 8]` | İki mod yan yana (6. aşama): her ESP32 dünya şablonu aynı simülasyon süresince hızlı modda ve Xtensa emülatöründe çalışır; rapor her şablon için seri satırları (birebir ve sayılar maskelenerek, açılış günlükleri hariç), her ESP32 çıkış pininin son seviyesini ya da PWM'ini ve iki modun hızını yazar; emülatörde olmayanı kullanan şablonlar nedeniyle listelenir. |
| `--esp32-compat rapor.txt` | İki modun uyumu: dünya şablonlarındaki ve örneklerdeki her ESP32 çizimi gerçek araç zinciriyle derlenir; her biri için flaş, RAM, süre ya da ilk hata. Hepsi derlenirse çıkış kodu 0. |
| `--render-test dosya` | Çizim testi: sahne aynası ve GPU çizicisi, küçük sahneler çizilip geri okunarak denetlenir (bkz. [Testler](TESTLER.md)). |
| `--snapshot cikti.png --page <sayfa> --theme pembe:gul` | Sayfayı bir tema ve vurgu rengiyle çizer; yalnız bu çalıştırma için, hiçbir şey kaydedilmez. |
| `--snapshot cikti.png --page world --fault <şablon> --simulate <s> --wait <s> --perf` | Performans göstergesini yalnız bu çalıştırma için açar (hiçbir şey kaydedilmez). Dünya `--simulate` saniye adımlanır, sonra `--wait` saniye canlı çalışır; gösterge her çözücünün simülasyon saniyesi başına maliyetini gösterir. |
| `--snapshot cikti.png --page world --fault <şablon> --simulate 1 --wait 300 --memory-log dosya.tsv` | Uzun canlı koşu: dünya `--wait` saniye çalışırken tam çöp toplamadan sonraki yönetilen bellek ve çalışma kümesi iki saniyede bir yazılır. Sızıntı, sürekli yükselen bir çizgi olarak görünür. |
| `--snapshot cikti.png --page <sayfa> --theme <tema> --contrast-report dosya` | Sayfada (açık pencereleri ve açılır menüleriyle) o temada okunamayan her yazıyı dosyaya ekler: arkasındaki düz zeminlere (yarı saydamlar karıştırılarak) karşı WCAG karşıtlığı 3:1'in altında kalanlar. Yazı başına bir satır: sayfa, tema, oran, renkler, yeri, metin. |
| `python tools/scripts/contrast_check.py <RobotWinStudio.exe> [--quick]` | `--contrast-report`'u her sayfada 16 temanın hepsiyle, rehberin görüntü durumlarında (diyaloglar, açılır menüler, ayarlayıcılar) ve her Yardım bölümünde dördüyle çalıştırır; okunamayan yazıları gruplayıp yazar, varsa 1 ile çıkar. |
| `python tools/scripts/token_scan.py [--list \| --check \| --update]` | Arayüzün kendi dosyalarında tasarım tokenlarını atlayan yerleri sayar: XAML'de renk değeri, C#'ta `Color.FromRgb` / `Brushes.*` ve yazı ölçeğinin (12, 13, 14, 20, 30) dışındaki boyutlar. Çizim kodu (tuvaller, 3B, semboller, ölçü aletleri) teknik renkleri bilerek çizer, sayılmaz. `--check` bir dosyanın sayısı `tools/tokens/baseline.json`'dakinden çok olursa 1 ile çıkar; `--update` bugünkünü taban yapar. |
| `python tools/scripts/layout_check.py <RobotWinStudio.exe> [--sizes 1280x720,960x540]` | `--layout-report`'u her sayfada 1920 × 1080'lik ekranın %100, %125, %150 ve %200 ölçekte verdiği boyutlarda (1920 × 1080, 1536 × 864, 1280 × 720, 960 × 540) çalıştırır: pencerenin ya da kaydırmayan bir panelin kestiği, ya da bir başkasının üstüne binen düğme, kutu, liste (`Shell/LayoutAudit.cs`); kaydırılan alanda görünüm dışında kalan sayılmaz. Bulgu varsa 1 ile çıkar. |
| `--lang en` | Bu çalıştırmayı bir dil paketinin diliyle açar (`--snapshot` ile sayfayı o dilde çizer). |
| `--lang-missing <dosya>` | `--snapshot` ile: sayfada paketin çevirmediği metinleri dosyaya, her biri bir JSON satırı olarak ekler (`--page library` bütün parça kataloğunu ve örnekleri de ekler). Dünyanın ve devrenin olay günlüğündeki her kayıt da, görünsün görünmesin, denetlenir. |
| `--api <port> [--world f.rwworld]` | Uygulama, pencere açmadan otomasyon sunucusu olarak (bkz. [Otomasyon API'si](API.md)). |
| `--bake-geometry f.rwworld [--out g.json]` | Uygulamanın gövde geometrisini (model zarfları dahil) başsız araç için dünyanın yanına yazar. |
| `--gallery <klasör> [--gallery-filter metin] [--gallery-columns 4] [--gallery-tile 460x340]` | Kodla üretilen her 3B bileşen modelinin toplu görüntüsü. |
| `--export-logo <klasör>` | Logoyu PNG boyutları + ICO olarak dışa aktarır (ayrıca Ayarlar → *Hakkında*). |
| `--register-file-types`, `--unregister-file-types` | `.rwproj`, `.rwworld`, `.rwsim`, `.rwpart`, `.rwlib`, `.rwfp`, `.rwmodel` türlerini geçerli kullanıcı için kaydeder / kaldırır (HKCU). Kurulum programı kullanır. |
| `<dosya>` | Bir RobotWin dosyasını açar. |

### Kılavuz görüntülerini yenilemek

Kılavuzdaki her görüntü, onu üreten argümanlarla (pencereden kesiliyorsa kırpma kutusuyla) `docs/images/shots.json` içinde kayıtlıdır. `tools/scripts/doc_images.py` hepsini üretir:

```powershell
$exe = "builds\RobotWin.Studio\Release\net10.0-windows\RobotWinStudio.exe"
python tools\scripts\doc_images.py $exe                         # Türkçe: docs\images\
python tools\scripts\doc_images.py $exe --lang en               # İngilizce: docs\images\en\
python tools\scripts\doc_images.py $exe --lang en home modeler  # yalnız bunlar
```

İngilizce kılavuz `docs/images/en/` klasörünü kullanır; İngilizcesi olmayan görüntüde Türkçesine düşer. `--frame`, `--arm`, `--pid` gibi seçiciler nesnenin Türkçe adını alır; İngilizcede örneğin verdiği İngilizce adı da bulur.

## tools/build-installer.ps1

Uygulamayı yayımlar, bileşenleri dizer (uygulama, kendi .NET 10 çalışma zamanı, Arduino derleyicisi, Open CASCADE köprüsü), her birini yerel kurulum programıyla bir `.rwpack`'e paketler, tek kurulum programı `RobotWinStudio-Setup-<sürüm>.exe`'yi onların boyutları ve SHA-256 özetleriyle derler ve hepsini taşınabilir bir klasöre kurarak kanıtlar; çıktı `builds\installer\release` içindedir. `-NoArduino` Arduino derleyicisini atlar, `-Repo sahip/ad` kurulumun indireceği yeri belirler. Bkz. [Derleme ve sürüm](DERLEME.md).

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.7.0
```

## Uygulamanın çağırdığı dış araçlar

| Araç | Çağıran | Ne için |
| --- | --- | --- |
| `arduino-cli` | `Studio/RobotWin.Studio/Firmware/ArduinoCompiler.cs` | `arduino-cli compile --fqbn arduino:avr:uno` (ya da `arduino:avr:nano`) → `%LOCALAPPDATA%\RobotWinStudio\Build` içinde `sketch.ino.hex` |
| MSVC `cl.exe`, `lib.exe` (`vcvars64.bat` üzerinden) | `CoreSim/.../Firmware/Esp32/Esp32SketchCompiler.cs` | ESP32 çizim DLL'leri ve `rw_esp32hal.lib` HAL kütüphanesi, `%LOCALAPPDATA%\RobotWin\esp32hal-cache` içinde önbellekte |
| `dotnet build` | `CoreSim/.../Sdk` | Dünyalardaki C# betikleri (bkz. [Fizik betikleri](SDK.md)) |

## Eski araçlar

`tools/`'un geri kalanı 1.x Unity tabanlı tasarıma aittir ve bugünkü uygulama için gerekmez: `rt_tool.py` (Unity ve C++ motorları için kurulum/derleme yönetimi), `scripts/` (yerel/yazılım derleme betikleri, Blender/FBX/GLB dönüştürme, kalite yardımcıları), `debug_console/`, `RobotWinFirmwareMonitor/`, `GoldenTraceRecorder/`, `RpiSmokeTest/`, `rpi/`, `fixtures/`, `issue_bodies/`. Belgeleri [arşivde](https://github.com/Kinin-Code-Offical/robotwin-studio/tree/main/docs/archive).
