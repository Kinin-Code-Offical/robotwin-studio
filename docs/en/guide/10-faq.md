# 10. FAQ

## Installing and starting

**The app does not start or asks for .NET.**
The lite package (`-lite.exe`) needs the .NET 9 Desktop Runtime. Install the full package (`RobotWinStudio-Setup-<version>.exe`), which carries .NET, or run the setup again and accept its offer to install .NET, or install ".NET 9 Desktop Runtime (x64)" from Microsoft's site.

**Windows warns about the setup file.**
Make sure you downloaded it from the project's GitHub Releases page, then choose **More info → Run anyway**.

**Can I run the app from a USB stick?**
Yes. Run the setup from the command line with `/S /PORTABLE /D=E:\RobotWin`; only the files are unpacked, no shortcuts or registry entries are created.

**The interface is too small / too large.**
Set it between 75 % and 150 % with **Görünüm → Arayüz ölçeği** (View → interface scale).

**Can the interface be in English?**
The interface is Turkish only for now. The documentation is complete in both Turkish and English.

## Code and compiling

**It says "arduino-cli bulunamadı" (arduino-cli not found).**
In a terminal run `winget install ArduinoSA.CLI` and then `arduino-cli core install arduino:avr`, then restart the app. If it still cannot find it, enter the path of `arduino-cli.exe` under **Ayarlar → Derleyiciler → arduino-cli yolu** (Settings → compilers → arduino-cli path).

**It says "… bulunamadı (kütüphane kurulu değil)" (not found: library not installed).**
An Arduino library your code uses is not installed. For example: `arduino-cli lib install "Adafruit SSD1306"`.

**ESP32 code does not compile: "MSVC (vcvars64.bat) bulunamadı" (not found).**
Install Visual Studio 2022 or Build Tools with the **"Desktop development with C++"** workload. If you installed it somewhere else, fill in **Ayarlar → Derleyiciler → vcvars64.bat yolu** (vcvars64.bat path).

**The first ESP32 build took very long.**
That is normal. The first build also compiles and caches the ESP32 hardware layer; later builds are fast.

**My ESP32 code cannot reach the Internet.**
Wi-Fi is simulated only inside the world, between boards; there is no real Internet connection.

**My SPI display / sensor does not work.**
SPI is not supported yet (there is only an empty stub). Use I²C modules: SSD1306 OLED, 16×2 / 20×4 LCD (PCF8574), MPU-6050.

## Simulation

**A part burned and the circuit no longer works.**
The simulation computes part damage as in real life. See the reason with **Arıza tespiti → Neden?** (fault finding → why?), fix the circuit and press **Hasarlı parçaları onar** (repair damaged parts). To switch damage off, turn off **Simülasyon → Parça hasarı (yanma, sigorta)** (Simulation → part damage).

**The status bar says "gerçek zamanın %…'i" (…% of real time).**
The PC cannot keep up with the simulation in real time. Lower the speed, reduce **Ayarlar → 3D kalitesi** (Settings → 3D quality) or close tabs you are not using. The results are still correct; it just advances more slowly.

**My board in the world does nothing.**
If the **Olaylar** (events) tab says "Yüklü yazılım yok (kart boşta)" (no firmware loaded, board idle), the board has no code: select the board and write code with **Kartın kodunu yaz** (write the board's code). Also make sure the board is powered (a battery cable or **USB'yi tak**, plug in USB).

**The signal between two boards does not work.**
Boards in the world are electrically separate until a cable connects them. Connect their GNDs too, next to the signal cable.

**A cable became "YANDI" (burned).**
More current flowed through it than its gauge can carry. Choose a thicker cable (smaller AWG) or limit the current.

**I cannot drag an object in the world.**
Objects can only be moved while the simulation is stopped. Stop it with **Durdur ve başa dön** (stop and rewind, Shift+F5).

**My controller does not show up.**
Select the controller and press **Kumandaları yeniden tara** (rescan controllers). Without a real controller use the on-screen one with **Sanal kumandayı aç (fare / klavye)** (mouse / keyboard). Make sure the controller is paired with the ESP32 (**Eşleştirme modu**, pairing mode).

**A part collides oddly or does not roll.**
**Görünüm → Çarpışma şekilleri (dünya)** (View → collision shapes) shows the body the physics really uses: the outline of the part's own 3D model.

**There is no sound.**
Check that **Simülasyon → Simülasyon sesleri** (simulation sounds) is on and **Ayarlar → Ses düzeyi** (volume) is not zero.

## Files

**I forgot to save; is my work gone?**
The app backs up your work at intervals to `Documents\RobotWin\Yedekler`. Look at the **Otomatik yedekler** (autosaves) list on the home page or **Dosya → Otomatik yedekler klasörü** (File → autosave folder). Change the interval under **Ayarlar → Dosyalar ve otomatik kayıt** (Settings → files and autosave).

**Double-clicking a file does not open the app.**
Press **Ayarlar → Dosya türleri → Windows'ta bu uygulamayla aç** (Settings → file types → open with this app in Windows).

**Can I open my project in KiCad?**
Yes. **KiCad'e aktar** (export to KiCad) on the **PCB** page makes a KiCad 8 project. For a board maker, **Üretim** (manufacturing) gives the Gerber files. Details: [3. PCB](03-pcb.md).

**I want to 3D-print a part.**
Make it on the **Parça** (part) page and export it as STL: [5. Part](05-model.md).

## Reporting a bug

If you find a bug, open an issue on GitHub with **Yardım → Sorun bildir…** (Help → report a problem). If possible include the steps, a screenshot and the `%APPDATA%\RobotWinStudio\crash.log` file (**Ayarlar → Hakkında → Ayar ve günlük klasörünü aç**, Settings → about → open the settings and log folder).

---

[← Previous: 9. Keyboard shortcuts](09-shortcuts.md) · [Contents](README.md)
