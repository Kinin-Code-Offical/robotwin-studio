# Araçlar

Günlük geliştirme için yalnız .NET CLI, uygulamanın kendi komut satırı kipleri ve bir PowerShell betiği gerekir.

## .NET CLI

| İş | Komut |
| --- | --- |
| Derleme (hata ayıklama) | `dotnet build RobotWinStudio.sln -c Debug` |
| Derleme (sürüm) | `dotnet build RobotWinStudio.sln -c Release` |
| Birim testleri | `dotnet test RobotWinStudio.sln` |
| Uygulamayı çalıştırma | `builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe` |
| Temizleme | `dotnet clean RobotWinStudio.sln` (ya da `builds\`'i silin) |
| Güvenlik açığı olan paketler | `dotnet list RobotWinStudio.sln package --vulnerable` |

## Uygulamanın komut satırı kipleri

`RobotWinStudio.exe` aynı zamanda bir araçtır. Bütün kipler işini bitirince kapanır. Ekran görüntüsü, ölçüm, öz test, galeri ve çakışma çalıştırmaları varsayılan ayarlar, varsayılan panel düzeni ve boş bir son dosyalar listesiyle başlar ve bunların hiçbirini kaydetmez: kullanıcının kendi ayarları olduğu gibi kalır.

| Kip | Kullanım |
| --- | --- |
| `--selftest [--selftest-out dosya]` | Her örneğin arayüzsüz uçtan uca denetimi (bkz. [Testler](TESTLER.md)). |
| `--snapshot cikti.png --page <sayfa> [...]` | Belgeler ve görsel denetim için bir sayfayı PNG'ye çizer (bkz. [Testler](TESTLER.md)). |
| `--overlap-report <dosya>` | Her dünya şablonunu kurar ve iç içe geçen ya da kabuğuna sığmayan parçaları listeler. |
| `--bench <dosya> [--bench-seconds 4] [--bench-filter id,id]` | Kare hızı ölçümü: her dünya şablonunu (ya da adı verilenleri) açar, dururken ve çalışırken ölçer. Kare hızını, kare sürelerini, karenin her bölümünün maliyetini ve şablonun açılışının nereye gittiğini yazar. Günlük ölçüm sürerken yazılır; son satırı o anda yapılan adımı söyler. |
| `--startup-report <dosya>` | Normal bir açılış (açılış ekranı dahil); her adımın işlem başından itibaren zamanını yazar ve kapanır. |
| `--snapshot cikti.png --page splash` | Açılış ekranını açılışın ortasındaki hâliyle çizer. |
| `--snapshot cikti.png --page <sayfa> --theme pembe:gul` | Sayfayı bir tema ve vurgu rengiyle çizer; yalnız bu çalıştırma için, hiçbir şey kaydedilmez. |
| `--lang en` | Bu çalıştırmayı bir dil paketinin diliyle açar (`--snapshot` ile sayfayı o dilde çizer). |
| `--lang-missing <dosya>` | `--snapshot` ile: sayfada paketin çevirmediği metinleri dosyaya, her biri bir JSON satırı olarak ekler (`--page library` bütün parça kataloğunu ve örnekleri de ekler). |
| `--api <port> [--world f.rwworld]` | Uygulama, pencere açmadan otomasyon sunucusu olarak (bkz. [Otomasyon API'si](API.md)). |
| `--bake-geometry f.rwworld [--out g.json]` | Uygulamanın gövde geometrisini (model zarfları dahil) başsız araç için dünyanın yanına yazar. |
| `--gallery <klasör> [--gallery-filter metin] [--gallery-columns 4] [--gallery-tile 460x340]` | Kodla üretilen her 3B bileşen modelinin toplu görüntüsü. |
| `--export-logo <klasör>` | Logoyu PNG boyutları + ICO olarak dışa aktarır (ayrıca Ayarlar → *Hakkında*). |
| `--register-file-types`, `--unregister-file-types` | `.rwproj`, `.rwworld`, `.rwsim`, `.rwpart`, `.rwlib`, `.rwfp`, `.rwmodel` türlerini geçerli kullanıcı için kaydeder / kaldırır (HKCU). Kurulum programı kullanır. |
| `<dosya>` | Bir RobotWin dosyasını açar. |

### README ekran görüntülerini yenilemek

```powershell
$exe = "builds\RobotWin.Studio\Release\net9.0-windows\RobotWinStudio.exe"
& $exe --snapshot docs\images\home.png --page home
& $exe --snapshot docs\images\world-ps5-car.png --page world --fault ps5-car
& $exe --snapshot docs\images\world-sonar.png --page world --fault sonar
& $exe --snapshot docs\images\world-servo-arm.png --page world --fault servo-arm
& $exe --snapshot docs\images\modeler.png --page model
```

Özelliği en iyi gösteren `--example`, `--simulate` ve `--size` değerlerini seçin; depodaki görüntüler için kullanılan argümanlar kayıtlı değildir.

## tools/build-installer.ps1

Uygulamayı yayımlar, yerel kurulum koçanını MSVC ile derler ve her şeyi `builds\installer\RobotWinStudio-Setup-<sürüm>-lite.exe` (çerçeveye bağımlı) ya da `-SelfContained` ile tam `RobotWinStudio-Setup-<sürüm>.exe` içine paketler. Bkz. [Derleme ve sürüm](DERLEME.md).

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.1.0
```

## Uygulamanın çağırdığı dış araçlar

| Araç | Çağıran | Ne için |
| --- | --- | --- |
| `arduino-cli` | `Studio/Firmware/ArduinoCompiler.cs` | `arduino-cli compile --fqbn arduino:avr:uno` (ya da `arduino:avr:nano`) → `%LOCALAPPDATA%\RobotWinStudio\Build` içinde `sketch.ino.hex` |
| MSVC `cl.exe`, `lib.exe` (`vcvars64.bat` üzerinden) | `CoreSim/.../Firmware/Esp32/Esp32SketchCompiler.cs` | ESP32 çizim DLL'leri ve `rw_esp32hal.lib` HAL kütüphanesi, `%LOCALAPPDATA%\RobotWin\esp32hal-cache` içinde önbellekte |
| `dotnet build` | `CoreSim/.../Sdk` | Dünyalardaki C# betikleri (bkz. [Fizik betikleri](SDK.md)) |

## Eski araçlar

`tools/`'un geri kalanı 1.x Unity tabanlı tasarıma aittir ve bugünkü uygulama için gerekmez: `rt_tool.py` (Unity ve C++ motorları için kurulum/derleme yönetimi), `scripts/` (yerel/yazılım derleme betikleri, Blender/FBX/GLB dönüştürme, kalite yardımcıları), `debug_console/`, `RobotWinFirmwareMonitor/`, `GoldenTraceRecorder/`, `RpiSmokeTest/`, `rpi/`, `fixtures/`, `issue_bodies/`. Belgeleri [arşivde](https://github.com/Kinin-Code-Offical/robotwin-studio/tree/main/docs/archive).
