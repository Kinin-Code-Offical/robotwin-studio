# Proje yapısı

| Yol | İçindekiler |
| --- | --- |
| `RobotWinStudio.sln` | Uygulama + çekirdek + testler (CI'ın derlediği çözüm) |
| `global.json` | .NET SDK sabitlemesi (10.0.100, latestMajor; C# 14) |
| `Directory.Build.props` | bin/obj çıktısını `builds/<Proje>/` altına gönderir |
| `Studio/RobotWin.Studio/` | Masaüstü uygulaması (WPF, net10.0-windows) |
| `CoreSim/` | Simülasyon çekirdeği, yerel ESP32 HAL'i, testler |
| `Installer/` | Yerel Win32 kurulum programı (`setup.cpp`, `setup.rc`) |
| `tools/build-installer.ps1` | Tek kurulum programı `RobotWinStudio-Setup-<sürüm>.exe`'yi ve kurduğu bileşenleri (`*.rwpack`) derler |
| `tools/RobotWin.Headless/` | Platformlar arası başsız API sunucusu ve toplu koşu aracı |
| `python/`, `ros2/` | Python istemcisi (Gymnasium ortamı, öğrenme araçları), ROS 2 köprüsü |
| `sdk/templates/` | Eklenti şablonu ve örnekler |
| `docs/` | Belge sitesi: kullanım kılavuzu, fizik başvurusu, geliştirici belgeleri, ekran görüntüleri, arşiv |
| `.github/workflows/` | `ci.yml`, `release.yml`, `pages.yml` (+ labeler, stale) |
| `FirmwareEngine/`, `NativeEngine/` | Eski C++ motorları (1.x, uygulama kullanmaz) |
| `tests/` | Eski test takımları (1.x, çözümde değil) |
| `builds/` | Derleme çıktısı (git yok sayar) |

## Studio/RobotWin.Studio

| Yol | İçindekiler |
| --- | --- |
| `App.xaml(.cs)` | Başlangıç, Türkçe kültür, çökme günlüğü, komut satırı kipleri (`--selftest`, `--snapshot`, `--gallery`, `--export-logo`, dosya türü kaydı) |
| `MainWindow.xaml(.cs)`, `MainWindow.Menu.cs`, `MainWindow.Tabs.cs` | Pencere, menü çubuğu (Dosya, Düzen, Görünüm, Simülasyon, Pencere, Yardım), kısayollar, belge sekmeleri, bölünmüş görünüm, ayırma, odak kipi, tam ekran |
| `Shell/` | `FlowRail` (sol gezinme), `CommandPalette` (Ctrl+K), `Workspace` (açık belgeler, kaydet/aç, otomatik kayıt), `AppSettings`, `FileAssociations`, `UserLibrary`, `WindowBehavior`, iletişim kutuları |
| `Pages/` | `HomePage`, `SchematicPage`, `PcbPage`, `CodePage`, `ModelPage`, `WorldPage` (+ `.SceneTree`, `.Radio`, `.Gas`, `.Collision`, `.Thermal` …), `LibraryPage`, `SettingsPage` |
| `Editor/` | Şema tuvali (düzenleme, giriş, menüler, kısayollar, dışa aktarım), sembol çizimi, kod düzenleyici, ekran çizici |
| `Simulation/` | `SimulationSession` (gerçek zamanlı devre döngüsü), osiloskop, multimetre, histogram görünümleri |
| `Pcb/` | Kart tuvali, 3B kart görünümü, ayak izi düzenleyicisi, PCB iletişim kutuları |
| `Modeling/` | 3B modelleyici görünümü, düzenleyici, kaplamalar, denetimler |
| `Assembly3D/` | Montaj sayfasının 3B sahnesi ve efektleri (parçacıklar: duman, ateş, kıvılcım, jetler) |
| `World/` | `WorldSession` (çalışan dünya), `WorldScene` (3B görünüm + düzenleme, ısı kamerası, çarpışma şekilleri), `ObjectModels`, `ModelHulls` (modellerden çarpışma zarfları), radyo modelleri ve kaplaması |
| `CoreSim/.../Firmware/Xtensa/` | ESP32'nin donanım uyumlu modu için kendi Xtensa LX6 çekirdeğimiz: `XtensaCore` (kayıtlar, pencereler, istisnalar, kesmeler, zamanlayıcılar), `.Execute` (komut kümesi), `.Float` (FPU); çevresindeki yonga `Esp32Soc` (bellek haritası, yükleme, çalışma döngüsü), `.Rom` (C#'ta karşılanan maske ROM), `.Flash` (SPI flaş ve önbellek MMU'su), `.Peripherals` (kesme matrisi, çekirdekler arası kesmeler, UART0, DPORT), `.Timers` (yonganın saati, zamanlayıcı grupları ve bekçileri), `.Ledc`, `.Adc` (SAR ADC'ler, Espressif'in karakterizasyonu), `.Efuse`, `.Gpio` (GPIO, matris, IO_MUX, pad sürüşü), `.Uart` (UART0–2), `.I2c` (kartın veriyollarındaki I2C denetleyicileri), `.Spi` (kartın SPI veriyolundaki SPI2/SPI3). |
| `Render/` | Kendi GPU çizicimiz: `SceneMirror` (WPF 3B sahnesini her karede okur: ağlar, dünya dönüşümleri, fiziksel tabanlı malzemeye çevrilen malzemeler, ışıklar), `GpuRenderer` (Direct3D 11: güneşin üç kademeli gölgesi, GGX gölgelendirme, piksel başına lambalar, 4× çoklu örnekleme, ACES ton eşleme; ağlar ve resimler önbellekte ve havuzda), `Shaders` (HLSL), `GpuViewport` (resmi D3DImage ile gösterir), `GpuHost` (onu herhangi bir WPF 3B görünümünün önüne koyar: dünya, oyun görünümü, modelleyici, kart 3B, önizlemeler), `RenderSelfTest` (`--render-test`). Vortice yalnız Direct3D bağlantısıdır. |
| `Models3D/` | Kartların, modüllerin, kılıfların, mekanik parçaların kodla üretilen 3B modelleri; özel model geçersiz kılmaları; galeri |
| `Examples/` | `ExampleProjects` (devre/robot örnekleri), `WorldTemplates` (dünya şablonları) |
| `Firmware/` | `ArduinoCompiler` (arduino-cli), çizim şablonları, hazır `.ino` + önceden derlenmiş `.hex` örnekleri |
| `Audio/` | Gerçek zamanlı ses sentezi (`SoundEngine`) ve simülasyon sesleri (`SimSounds`) |
| `Input/` | XInput, Sony HID (DualSense/DualShock 4) ve ekrandaki sanal kumandalar |
| `Theme/`, `Branding/` | Tasarım belirteçleri, denetimler, simgeler; logo, açılış ekranı, dosya türü simgeleri |

## CoreSim

| Yol | İçindekiler |
| --- | --- |
| `src/RobotTwin.CoreSim/Analog/` | MNA çözücü, seyrek LU, eleman modelleri (`Devices/`) |
| `src/RobotTwin.CoreSim/Design/` | Şema modeli, parça kütüphanesi ve katalogları, devre derleyicisi, ERC, BOM, SPICE, toleranslar, Monte Carlo, tanılama |
| `src/RobotTwin.CoreSim/Instruments/` | Osiloskop ve multimetre modelleri |
| `src/RobotTwin.CoreSim/Firmware/` | ATmega328P öykünücüsü, Intel HEX, I²C veri yolu ve cihazları, AVR eş simülasyonu; `Esp32/` çizim derleyicisi, yerel yükleyici, eş simülasyon, radyo |
| `src/RobotTwin.CoreSim/Pcb/` | Kart, katmanlar, ayak izleri, bölgeler, yönlendiriciler, otomatik yerleşim, DRC, Gerber, KiCad dışa aktarımı |
| `src/RobotTwin.CoreSim/Mechanics/` | Katı cisim fizik sahnesi, çarpışma şekilleri ve dışbükey zarflar, düzenek sürüklemesi, ağ G/Ç ve BVH, malzemeler, kütle özellikleri, araçlar ve dronlar |
| `src/RobotTwin.CoreSim/Modeling/` | Ağ modelleme: temel şekiller, CSG, düzenleme işlemleri, robot parçaları, `.rwmodel` |
| `src/RobotTwin.CoreSim/Sandbox/` | Açık dünya: `WorldDocument`, `WorldCompiler`, `WorldPhysics` (+ kanatlar, kabuklar, ısı, gaz), `WorldExamples`, `Radio/` |
| `src/RobotTwin.CoreSim/Sdk/` | Ortam modeli, kuvvet alanları, davranışlar, betik derleme, eklenti yükleme |
| `src/RobotTwin.CoreSim/Formats/` | `.rwproj/.rwpart/.rwlib/.rwsim` kapları, KiCad kütüphane içe aktarımı, S-ifadeleri |
| `src/RobotTwin.CoreSim/Host`, `IPC`, `Runtime`, `Specs`, `Contracts`, `Engine`, `Infrastructure`, `Serialization`, `Models` | 1.x tasarımından kalma eski yapı taşları; Studio uygulaması kullanmaz |
| `native/esp32hal/` | C++ Arduino-ESP32 HAL katmanı (Arduino.h, WiFi, BLE, Wire, Servo, FreeRTOS, Bluepad32…), CoreSim'e gömülür ve gerektiğinde MSVC ile derlenir |
| `tests/RobotTwin.CoreSim.Tests/` | xUnit testleri (bkz. [Testler](TESTLER.md)) |
| `CoreSim.sln` | Yalnız CoreSim çözümü (kütüphane + testler) |

## docs

| Yol | İçindekiler |
| --- | --- |
| `index.html`, `en/index.html` | Sitenin ana sayfaları (Türkçe `/`, İngilizce `/en/`) |
| `kullanim/`, `en/guide/` | Kullanım kılavuzu, Türkçe ve İngilizce, konu başına bir dosya |
| `fizik/`, `en/physics/` | Fizik ve matematik başvurusu, Türkçe ve İngilizce |
| `gelistirici/` | Türkçe geliştirici belgeleri |
| kökteki `*.md` | İngilizce geliştirici belgeleri (`README.md` dizinidir) |
| `images/` | README'nin ve kılavuzun ekran görüntüleri |
| `_data/nav.yml` | Sitenin dil başına menüleri ve dil düğmesinin kullandığı sayfa eşleri |
| `archive/` | 1.x (Unity tabanlı) tasarım belgeleri, raporlar ve planlar; tarihçe için saklanır |

## Eski klasörler

`FirmwareEngine/` (adlandırılmış boru iletişimli C++ AVR öykünücüsü), `NativeEngine/` (C++ fizik) ve `tools/` ile `tests/`'in çoğu önceki Unity tabanlı tasarıma aittir. Bugünkü uygulama onları ne derler ne yükler. Bkz. [arşiv](https://github.com/Kinin-Code-Offical/robotwin-studio/tree/main/docs/archive).
