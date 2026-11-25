# Testler

Hepsi geliştirici makinesinde ve CI'da çalıştırılabilen üç denetim katmanı:

| Katman | Komut | Neyi kanıtlar |
| --- | --- | --- |
| Birim testleri | `dotnet test RobotWinStudio.sln` | CoreSim doğru davranır (çözücü, yazılım, PCB, fizik, dünya, biçimler). |
| Öz test | `RobotWinStudio.exe --selftest` | Her hazır örnek baştan sona derlenir, simüle edilir, yerleşir, yollanır ve DRC'den geçer. |
| Ekran görüntüsü | `RobotWinStudio.exe --snapshot …` | Bir sayfa olması gerektiği gibi çizilir (görsel denetim, belge ekran görüntüleri). |
| Çizim testi | `RobotWinStudio.exe --render-test dosya` | Kendi 3B çizicimiz WPF sahnelerini doğru okur ve ekran kartında doğru çizer (piksel piksel geri okunur). |

## Birim testleri

```powershell
dotnet test RobotWinStudio.sln
# yalnız bir alan:
dotnet test RobotWinStudio.sln --filter "FullyQualifiedName~WorldPhysicsTests"
```

xUnit projesi `CoreSim/tests/RobotTwin.CoreSim.Tests`'tir. Arayüz ve dış araç gerektirmez. Başlıca test dosyaları:

| Alan | Testler |
| --- | --- |
| Analog çözücü | `AnalogSimulatorTests`, `AnalogActiveDeviceTests`, `SparseSolverTests`, `ThermalModelTests`, `ComponentVariationTests`, `ToleranceAndSourceTests`, `DeterministicNoiseTests` |
| Ölçü aletleri, tanılama | `OscilloscopeTests`, `MultimeterTests`, `DiagnosticsTests` |
| Şema, PCB, KiCad | `SchematicEditorTests`, `DesignAndPcbTests`, `PcbEditorTests`, `PcbPolygonTests`, `KiCadExportTests`, `KiCadImportTests` |
| Yazılım | `FirmwareCoSimTests`, `I2cDeviceTests`, `Esp32Tests`, `Esp32RadioTests`, `FirmwareProtocolTests`, `ProtocolFramingTests` |
| Mekanik, modelleme | `PhysicsSceneTests`, `VehicleDynamicsTests`, `MechatronicsTests`, `MeshTests`, `ModelingTests`, `PartModel3DTests`, `ModelHullTests` (model biçimli gövdeler gerçek şekil gibi yuvarlanır), `AssemblyDragTests` (örtülen yüzler, taban sürüklemesi, eğik akış), `LiquidTests` (hidrostatik, serbest tank ve uykusu, Martin & Moyce'a karşı baraj yıkılması, gerçek dökme), `ConservationTests` (çarpışmalarda momentum ve açısal momentum, Newton'un çarpışma yasası, serbest cismin ara ekseninde devrilmesi, boşlukta enerji, sekmelerde enerji artışı olmaması), `WorkTeamTests` (çözücü iş parçacıkları: her geçişte her indis bir kez, hatalar çağırana döner, aynı anda tek çağıran) |
| ESP32 emülatörü | `XtensaCoreTests`: kendi Xtensa LX6 çekirdeğimiz; programlar Espressif'in gerçek `xtensa-esp32-elf-as`'ıyla derlenip çekirdekte çalıştırılır (aritmetik, kaydırmalar, yükleme/saklama ve sabit havuzu, S32C1I, dallar, sıfır ek yüklü döngü, gerçek pencere işleyicileriyle özyinelemeli fibonacci, CCOMPARE kesmesi ve WAITI, sıfıra bölme ve kapalı FPU istisnaları, FPU). ESP32 araç zinciri kurulu değilse atlanır; elle kodlanmış bir test her yerde çalışır. `Esp32SocPeripheralTests`: yonganın çevre birimleri yazmaçlarından, IDF'nin sürdüğü gibi (saat, LAC sayacı, zamanlayıcı alarmı ve kesmesi, çekirdekler arası kesme, LEDC görev oranı ve donanım geçişi, GPIO çıkışı/açık akaç, matristen pine LEDC, Schmitt girişi ve pin kesmesi, eFuse, Espressif'in ADC karakterizasyonu, SAR ADC dönüşümü); firmware gerekmez. `Esp32SocBringUpTests`: `RW_ESP32_ELF` verilince gerçek firmware'i çalıştırır (`RW_ESP32_PAD=34=1.0` pad gerilimleri, `RW_ESP32_PROFILE=1` çağıranlarıyla çağrı grafiği, `RW_ESP32_PEEK=ad,ad` firmware değişkenleri) ve raporunu ELF'in yanına yazar (`RW_ESP32_SEND=metin`, `RW_ESP32_SEND_AT=döngü`: UART0'ın alma hattına metin). `Esp32EmulatedBoardTests`: donanım uyumlu kart bir devrede: dimmer yazılımı (`RW_ESP32_ELF`) 10k/4,7k bölücüyü 5 mV içinde okur ve LED'in PWM'ini sürer; bir yankı yazılımı (`RW_ESP32_ECHO_ELF`) UART FIFO'sundan uzun bir satır basar ve seri monitörün gönderdiğini büyük harfle geri yollar; bir I2C yazılımı (`RW_ESP32_I2C_ELF`) veriyolunu tarar, bir modülün yazmaçlarını yazıp yinelenen START ile geri okur, 32 baytlık FIFO'yu aşan 40 bayt yazar ve olmayan bir modülden NACK alır; bir SPI yazılımı (`RW_ESP32_SPI_ELF`) GPIO'suyla seçilen bir cihazla bayt, 16 bitlik sözcük ve 40 baytlık tampon alışverişi yapar, seçili değilken 0xFF okur; takılan bir yazılım (`RW_ESP32_WDT_ELF`: kesmeler kapalı, sonsuz döngü) kesme bekçisini tetikler, bir çekirdek paniği raporlar, kart yeniden başlar (`SW_CPU_RESET`) ve çizim yeniden çalışır; bir derin uyku yazılımı (`RW_ESP32_SLEEP_ELF`) iki kez 200 ms uyur, `RTC_DATA_ATTR` sayacı korunarak zamanlayıcıyla uyanır. `Esp32SocPeripheralTests` UART2'yi de ayrıca sınar: metni, baud hızı, 9600 baud'da gelen baytlar. |
| Dünya, radyo | `WorldTests`, `WorldPhysicsTests`, `RadioTests`, `GamepadCarTests` (tasarlanmış kart, tek aks, düz gider ve kumandayla döner) |
| Uçuş, gaz, ısı, piller | `AeroWingTests`, `MotorGliderTests`, `QuadFlightTests`, `GasPhysicsTests` (boşalma, KNSB motor, su roketi, patlama), `ThermalWorldTests`, `UsbcBatteryTests` (CC/CV şarj, koruma kartı, sabit çıkışlı AA) |
| SDK | `SdkTests` (limit hız, rüzgâr, türbülans, ifadeler, eklentiler) |
| Dosyalar, depo düzeni | `FileFormatTests`, `RtwinRoundTripTests`, `TemplateTests`, `GoldenTraceHarnessTests`, `WorkflowReferenceTests` |

Studio'nun kendi testleri `Studio/RobotWin.Studio.Tests` içindedir (WPF'siz parçalar bağlanır). `SolverCostsTests` performans göstergesinin çözücü tablosunu (simülasyon saniyesi başına maliyet, paylar, çalışma hızı) ve her çözücü adının İngilizcesinin olmasını denetler. `LangTests` dil paketlerini denetler: tam metinler, kalıplar, birleşik metinler, sütun hizalı ölçümler, eskiz satırları, iki dilli arama ve Studio'nun XAML'ındaki her Türkçe etiketin İngilizcesinin olması (`EveryXamlLabelHasItsEnglish`). Böylece İngilizcesi olmadan eklenen bir etiket testlerde yakalanır.

ESP32 testleri gerçek çizimleri MSVC ile derler. Visual Studio C++ araçları olmayan bir makinede (`vcvars64.bat` bulunamazsa) başarısız değil **atlandı** olarak raporlanır.

## Öz test

```powershell
dotnet build RobotWinStudio.sln -c Debug
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
Get-Content selftest.txt
```

`Studio/RobotWin.Studio/Examples/ExampleProjects.cs` içindeki her örnek için:

1. şemayı derler ve analog simülatörü çalıştırır (2 ms'lik 100 adım; örnekte araç ya da dron varsa 1500 adım, kurulur ve gaz verilir);
2. 90 × 70 mm'lik bir PCB eşler, otomatik yerleştirir, otomatik yollar ve DRC çalıştırır.

Her örnek bir satır yazar (`parts`, `nets`, `warn`, `sim=OK`, `routed=x/y`, `vias`, `drcErrors`, ayrıca aracın kütlesi/mesafesi/irtifası). Bir istisna, tamamlanmamış yollama ya da herhangi bir DRC hatası örneği başarısız sayar. Son satır `SELFTEST PASS` ya da `SELFTEST FAIL (n)`'dir; çıkış kodu başarıda 0, başarısızlıkta 1'dir.

## Çizim testi

```powershell
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --render-test render-test.txt
Get-Content render-test.txt
```

Önce sahne aynası (`Render/SceneMirror.cs`) denetlenir: katmanlı gövde malzemesi (boş kömürleşme katmanı gövdeyi saydam yapmaz, yarı kömürleşmiş katman örtüsünü taşır), yalnız ışıyan malzeme (seçilen renk, arkasına eklenir), tam saydam seçim hedefi (çizilmez), donmuş geçişli fırça (resme çevrilir) ve değişen geçiş (ortalama renk), iç içe dönüşümler, güneş, lamba ve spot ışığı. Sonra küçük sahneler GPU çizicisiyle (`Render/GpuRenderer.cs`) ekran dışında çizilip piksel piksel geri okunur: güneşle aydınlanan kırmızı kutu ve zemin rengi, eğik güneşin kutu gölgesi (gölgedeki zemin ışıktakinin %60'ından karanlık), siyah üzerine kendi ışığı (tam seçilen renk), beyaz zemin üstünde yarı saydam mavi katman, resmin yönü (solda kırmızı, sağda yeşil), WPF'nin sonsuz uzak düzlemiyle dik (ortografik) kamera ve saydam zemin (kare dışında alfa 0), sonsuz uzak düzlemli perspektif, kademeli gölge (6 m yükseklikten bakarken 12 m'deki küçük kutunun da 40 m'deki büyük kutunun da gölgesi). `RW_RENDER_TEST_PNG=klasör` çizilen her sahneyi PNG olarak yazar; bir denetim düştüğünde bakmak için. Ekran kartı olmayan makinede (CI) çizim Windows'un yazılım çizicisi WARP'ta yapılır; `RW_RENDER_WARP=1` WARP'ı ekran kartı olan makinede de zorlar. Çıkış kodu başarıda 0'dır.

## Ekran görüntüsü testi

`--snapshot` bir sayfayı ekran dışında, kullanıcı etkileşimi olmadan PNG'ye çizer ve kapanır. `docs/images/` içindeki ekran görüntüleri böyle yapılır, arayüz değişiklikleri böyle denetlenir.

```powershell
$exe = "builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe"
& $exe --snapshot out\home.png --page home
& $exe --snapshot out\schematic.png --page schematic --example arduino-displays --simulate 1.5
& $exe --snapshot out\pcb.png --page pcb --example motor-driver
& $exe --snapshot out\world.png --page world --fault ps5-car --simulate 2
& $exe --snapshot out\model.png --page model --size 1600x960
```

| Seçenek | Anlamı |
| --- | --- |
| `--page` | `home`, `schematic`, `pcb`, `code`, `model`, `assembly`, `world`, `library`, `settings` |
| `--example <id>` | Önce bir devre örneği yükler (`led-button`, `arduino-blink`, `arduino-displays`, `esp32-dimmer`, `motor-driver`, `opamp-amp`, `power-7805`, `rc-filter`, `relay-lamp`, `nand-logic`, `line-follower`, `obstacle-avoider`, `quadcopter`) |
| `--simulate <s>` | Görüntüyü almadan önce simülasyonu bu kadar simüle saniye çalıştırır |
| `--fault <ad>` | Sayfaya özel senaryo. Dünya sayfası: dünya şablonunun kimliği (`empty`, `two-boards`, `servo-arm`, `displays`, `car`, `sonar`, `motor-wheel`, `ps5-car`, `esp32-quad`, `shell-box`, `motor-glider`, `glider`, `water-rocket`, `sugar-motor`, …). Devre: ör. `scope`, `dmm`, `burn`, `diag`, `montecarlo`, `tolerance`, `erc`, `find`, `help`. PCB: `3d`, `fpeditor`, `module`. Her sayfa: `split` (bölünmüş görünüm). |
| `--collision` | Dünya sayfası: çarpışma şekilleri gösterilirken alır |
| `--size GxY` | Pencere boyutu (varsayılan 1600x960); dar boyutlar bölünmüş görünümü / ayrılmış pencereleri taklit eder |
| `--wait <s>` | Görüntüden önce ek bekleme (saniye) |
| `--nav-expanded` | Gezinme şeridi açıkken alır |

PNG'leri gözle ya da bir görüntü karşılaştırma aracıyla karşılaştırın; CI'da otomatik piksel karşılaştırması yoktur.

İlgili: `--overlap-report <dosya>` her dünya şablonunu kurar ve iç içe geçen ya da kabuğuna sığmayan parçaları listeler; `--gallery <klasör>` kodla üretilen her 3B bileşen modelinin toplu görüntüsünü yazar (`--gallery-filter`, `--gallery-columns`, `--gallery-tile GxY`), `Models3D/` değişikliklerinden sonra işe yarar.

## Sürekli tümleştirme

`.github/workflows/ci.yml`, `main`'e yapılan gönderimlerde ve çekme isteklerinde çalışır (`windows-latest`):

1. `global.json`'ın varlığını ve hiçbir derleme çıktısının izlenmediğini denetler;
2. `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`;
3. `RobotWinStudio.exe --selftest` çalıştırır ve geçmezse işi başarısız sayar.

## Eski test takımları

`tests/native` (`NativeEngine` için C++ fizik/sensör doğrulaması), `tests/integration` (Node) ve `tests/python` 1.x Unity tabanlı tasarıma aittir. Çözüm ya da CI tarafından derlenmezler.
