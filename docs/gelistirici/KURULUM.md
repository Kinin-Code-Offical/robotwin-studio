# Windows'ta kurulum

RobotWin Studio'yu kaynaktan derlemek, çalıştırmak ve sınamak için bir Windows makinesini nasıl hazırlayacağınız. Uygulamayı yalnızca *kullanmak* için onu [Sürümler](https://github.com/Kinin-Code-Offical/robotwin-studio/releases) sayfasından kurun (bkz. [README](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/README.md#indirme-ve-kurulum)).

## Gerekenler

| Araç | Ne için | Notlar |
| --- | --- | --- |
| Windows 10 ya da 11, x64 | her şey | Uygulama WPF'dir ve yalnız Windows'ta çalışır. |
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | uygulamayı ve testleri derlemek | `global.json` 9.0.112'yi `rollForward: latestFeature` ile sabitler. |
| Git | depoyu klonlamak | |
| Visual Studio 2022 ya da Build Tools, **C++ ile masaüstü geliştirme** iş yükü | ESP32 çizimleri, kurulum paketini derlemek | `cl.exe`, `lib.exe`, `rc.exe` ve `vcvars64.bat`'ı sağlar. Uygulamanın kendisini derlemek ya da çalıştırmak için gerekmez. |
| `arduino-cli` + `arduino:avr` çekirdeği | kendi Arduino Uno/Nano çizimlerinizi derlemek | Hazır örnekler önceden derlenmiş HEX dosyaları olarak gelir. |

İsteğe bağlı araçları winget ile kurun:

```powershell
winget install Microsoft.DotNet.SDK.9
winget install ArduinoSA.CLI
arduino-cli core install arduino:avr
# C++ iş yüküyle Visual Studio Build Tools:
winget install Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive"
```

## Klonla, derle, çalıştır

```powershell
git clone https://github.com/Kinin-Code-Offical/robotwin-studio.git
cd robotwin-studio
dotnet build RobotWinStudio.sln -c Debug
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe
```

`Directory.Build.props` bütün `bin/` ve `obj/` çıktısını `builds\<ProjeAdı>\` altına yönlendirir; depo ağacı temiz kalır. İyileştirilmiş derleme için `-c Release` kullanın (`builds\RobotWin.Studio\Release\net9.0-windows\`).

Visual Studio 2022 (.NET 9 için 17.12 ya da sonrası) ya da C# Dev Kit'li VS Code `RobotWinStudio.sln`'yi doğrudan açar; başlangıç projesi olarak **RobotWin.Studio**'yu seçin.

## Uygulama derleyicileri nasıl bulur

- **arduino-cli:** Ayarlar → *Derleyiciler* → *arduino-cli yolu*, sonra `%ProgramFiles%\Arduino CLI\`, `%LOCALAPPDATA%\Programs\Arduino CLI\`, `%LOCALAPPDATA%\Microsoft\WinGet\Links\`, sonra `PATH`.
- **MSVC (ESP32):** Ayarlar → *vcvars64.bat yolu*, sonra `vswhere.exe`, sonra standart `Microsoft Visual Studio\{18,2022,2019}\{BuildTools,Community,Professional,Enterprise}\VC\Auxiliary\Build\vcvars64.bat` konumları.

## Kurulumu doğrulayın

```powershell
dotnet test RobotWinStudio.sln
builds\RobotWin.Studio\Debug\net9.0-windows\RobotWinStudio.exe --selftest --selftest-out selftest.txt
Get-Content selftest.txt -Tail 1   # SELFTEST PASS
```

Sonra uygulamayı açın, Ana sayfadan *Arduino: Gerçek Firmware* örneğini yükleyin, **Kod**'a geçin (Ctrl+3) ve arduino-cli'yi denemek için **Derle**'ye (F7) basın; ESP32 için *ESP32: PWM Işık Ayarı*'nı açıp simülasyonu başlatın (F5): ilk çalıştırma çizimi MSVC ile derler, sonrakiler önbelleği kullanır.

Sonraki: [Testler](TESTLER.md), [Derleme ve sürüm](DERLEME.md), [Sorun giderme](SORUN_GIDERME.md).
