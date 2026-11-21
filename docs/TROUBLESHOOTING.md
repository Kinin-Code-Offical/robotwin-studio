# Troubleshooting

Problems with the installed app first, then problems when building from source. The user guide has a user-facing FAQ: [10. FAQ](en/guide/10-faq.md).

Where to look first: the status bar at the bottom of the window, the **Olaylar** (events) tab, and the crash log `%APPDATA%\RobotWinStudio\crash.log` (Settings → *Hakkında* → *Ayar ve günlük klasörünü aç*).

## Installing and starting

| Symptom | Fix |
| --- | --- |
| The app does not start, or asks for .NET | Install the **.NET 9 Desktop Runtime** (x64) from Microsoft, or re-run the setup and accept its offer, or use the full package `RobotWinStudio-Setup-<version>.exe`, which carries .NET (the `-lite` one does not). |
| Windows SmartScreen warns about the setup | Check that the file came from the project's GitHub Releases page; then *More info → Run anyway*. |
| All-users install fails | `/ALLUSERS` writes to `Program Files` and needs administrator rights; use the default per-user install otherwise. |
| Double-clicking a `.rwproj`/`.rwworld` file does not open the app | Settings → *Dosya türleri* → *Windows'ta bu uygulamayla aç* (or File → *Dosya türlerini bu uygulamayla ilişkilendir*). |
| Text or controls are too small / too large | View → *Arayüz ölçeği* (75–150 %), or Settings → *Görünüm*. |

## Compiling firmware

| Symptom | Fix |
| --- | --- |
| "arduino-cli bulunamadı" when pressing **Derle** (F7) | `winget install ArduinoSA.CLI`, open a new terminal, `arduino-cli core install arduino:avr`. If it is installed somewhere unusual, set Settings → *Derleyiciler* → *arduino-cli yolu*. Restart the app after installing so it sees the new `PATH`. |
| Build fails with "platform not installed" / missing `arduino:avr` | Run `arduino-cli core install arduino:avr`. |
| A library (`#include <...>`) is not found | Install it with arduino-cli, e.g. `arduino-cli lib install "Adafruit SSD1306"`. |
| ESP32: "MSVC (vcvars64.bat) bulunamadı" | Install Visual Studio 2022 or Build Tools with **Desktop development with C++**, or set Settings → *vcvars64.bat yolu*. |
| ESP32: the first run takes long | The HAL library and the sketch are compiled with MSVC on first use, then cached in `%LOCALAPPDATA%\RobotWin\esp32hal-cache`. Later runs start quickly. Deleting that folder forces a rebuild. |
| ESP32: SPI device does not respond | SPI is only a stub in the ESP32 HAL (transfers return `0xFF`). Use I²C devices. |
| ESP32: `WiFi` cannot reach the Internet | Wi-Fi is simulated inside the world only (UDP and stream sockets between boards); there is no Internet or DNS. |

## Simulation

| Symptom | Fix |
| --- | --- |
| Status shows "gerçek zamanın %…'i" | The PC cannot keep up with real time. Pick a slower speed, close other heavy pages, lower Settings → *3D kalitesi*, or increase *En büyük zaman adımı*. |
| A part burned / a fuse blew and nothing works | That is simulated damage. Fix the cause (see **Arıza tespiti** → *Neden?*), then *Hasarlı parçaları onar* (circuit) or *Tümünü onar* (Montaj). To ignore damage, switch off Simulation → *Parça hasarı*. |
| A board in the world does nothing; log says "Yüklü yazılım yok (kart boşta)" | The board has no program. Open it with *Kartın kodunu yaz*, build and run. Also check it is powered (battery cable or a PC + USB cable plugged in). |
| Two boards do not see each other's signals | Boards are electrically isolated until a cable connects them; also connect GND between them. |
| A cable shows **YANDI** (burned) | The current exceeded its rating. Use a thicker gauge (lower AWG) or limit the current. |
| Objects cannot be dragged | Objects can only be moved while the world simulation is stopped (Shift+F5). |
| Gamepad not found | Select the controller → *Kumandaları yeniden tara*; or use *Sanal kumandayı aç (fare / klavye)*. Pair it first with *Eşleştirme modu*. |
| A part collides oddly or does not roll | View → *Çarpışma şekilleri (dünya)* shows the bodies the physics really uses. |
| No sound | Simulation → *Simülasyon sesleri*, and Settings → *Ses düzeyi*. |

## Building from source

| Symptom | Fix |
| --- | --- |
| `A compatible .NET SDK was not found` | Install the .NET 9 SDK (9.0.112 or a later 9.0 feature band), as pinned in `global.json`. |
| Build fails because `RobotWinStudio.exe` is in use | Close the running app (and any `--snapshot`/`--selftest` process) before rebuilding. |
| Many `RobotWin.Studio_*_wpftmp` folders in `builds\` | WPF temporary projects; safe to delete with the whole `builds\` folder. |
| `build-installer.ps1`: "Visual Studio C++ araçları bulunamadı" | Install the C++ workload; the script finds it with `vswhere`. |
| PowerShell blocks the script | Run it as `powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 ...`. |
| Turkish characters become garbage after editing a file from PowerShell | Windows PowerShell 5.1 `Get-Content`/`Set-Content` use the ANSI code page. Edit files in an editor, or pass `-Encoding utf8`. |
