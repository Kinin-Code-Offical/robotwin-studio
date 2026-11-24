# 10. FAQ

## Installing and starting

**The app does not start or asks for .NET.**
The lite package (`-lite.exe`) needs the .NET 9 Desktop Runtime. Install the full package (`RobotWinStudio-Setup-<version>.exe`), which carries .NET, or run the setup again and accept its offer to install .NET, or install ".NET 9 Desktop Runtime (x64)" from Microsoft's site.

**Windows warns about the setup file.**
Make sure you downloaded it from the project's GitHub Releases page, then choose **More info → Run anyway**.

**Can I run the app from a USB stick?**
Yes. Run the setup from the command line with `/S /PORTABLE /D=E:\RobotWin`; only the files are unpacked, no shortcuts or registry entries are created.

**The interface is too small / too large.**
Set it between 75 % and 150 % with **View → Interface scale**.

**Can the interface be in English?**
The interface is Turkish only for now. The documentation is complete in both Turkish and English.

## Code and compiling

**It says "arduino-cli not found".**
In a terminal run `winget install ArduinoSA.CLI` and then `arduino-cli core install arduino:avr`, then restart the app. If it still cannot find it, enter the path of `arduino-cli.exe` under **Settings → Compilers → arduino-cli path**.

**It says "… not found (library not installed)".**
An Arduino library your code uses is not installed. For example: `arduino-cli lib install "Adafruit SSD1306"`.

**ESP32 code does not compile: "MSVC (vcvars64.bat) not found".**
Install Visual Studio 2022 or Build Tools with the **"Desktop development with C++"** workload. If you installed it somewhere else, fill in **Settings → Compilers → vcvars64.bat path**.

**The first ESP32 build took very long.**
That is normal. The first build also compiles and caches the ESP32 hardware layer; later builds are fast.

**My ESP32 code cannot reach the Internet.**
Wi-Fi is simulated only inside the world, between boards; there is no real Internet connection.

**My SPI display / sensor does not work.**
SPI is not supported yet (there is only an empty stub). Use I²C modules: SSD1306 OLED, 16×2 / 20×4 LCD (PCF8574), MPU-6050.

## Simulation

**A part burned and the circuit no longer works.**
The simulation computes part damage as in real life. See the reason with **Fault finding → Why?**, fix the circuit and press **Repair damaged parts**. To switch damage off, turn off **Simulation → Part damage (burning out, fuses)**.

**The status bar says "…% of real time".**
The PC cannot keep up with the simulation in real time. Lower the speed, reduce **Settings → 3D quality** or close tabs you are not using. The results are still correct; it just advances more slowly.

To see what slows it, open **View → Performance overlay** (Ctrl+Shift+F). While a world runs, it lists how many milliseconds each solver (circuit, board firmware, rigid bodies, liquids, cables, heat…) takes per simulated second, and its share. When they add up to more than a thousand, the world cannot keep up with real time. On the water glass, for example, most of it is the liquid; a coarser particle spacing makes it faster.

**My board in the world does nothing.**
If the **Events** tab says "No firmware loaded (the board is idle)", the board has no code: select the board and write code with **Write the board's code**. Also make sure the board is powered (a battery cable or **Plug in USB**).

**The signal between two boards does not work.**
Boards in the world are electrically separate until a cable connects them. Connect their GNDs too, next to the signal cable.

**A cable became "YANDI" (burned).**
More current flowed through it than its gauge can carry. Choose a thicker cable (smaller AWG) or limit the current.

**I cannot drag an object in the world.**
Objects can only be moved while the simulation is stopped. Stop it with **Stop and rewind** (Shift+F5).

**My controller does not show up.**
Select the controller and press **Rescan gamepads**. Without a real controller use the on-screen one with **Open the virtual gamepad (mouse / keyboard)**. Make sure the controller is paired with the ESP32 (**Pairing mode (PS + Create held)**).

**A part collides oddly or does not roll.**
**View → Collision shapes (world)** shows the body the physics really uses: the outline of the part's own 3D model.

**There is no sound.**
Check that **Simulation → Simulation sounds** is on and **Settings → Volume** is not zero.

## Files

**I forgot to save; is my work gone?**
The app backs up your work at intervals to `Documents\RobotWin\Yedekler`. Look at the **Automatic backups** list on the home page or **File → Automatic backups folder**. Change the interval under **Settings → Saving and backup**.

**Double-clicking a file does not open the app.**
Press **Settings → File types → Open with this app in Windows**.

**Can I open my project in KiCad?**
Yes. **Export to KiCad** on the **PCB** page makes a KiCad 8 project. For a board maker, **Manufacturing** gives the Gerber files. Details: [3. PCB](03-pcb.md).

**I want to 3D-print a part.**
Make it on the **Part** page and export it as STL: [5. Part](05-model.md).

## Reporting a bug

If you find a bug, open an issue on GitHub with **Help → Report a problem…**. If possible include the steps, a screenshot and the `%APPDATA%\RobotWinStudio\crash.log` file (**Settings → About → Open settings and log folder**).

---

[← Previous: 9. Keyboard shortcuts](09-shortcuts.md) · [Contents](README.md)
