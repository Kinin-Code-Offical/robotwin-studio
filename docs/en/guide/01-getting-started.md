# 1. Getting started

In this chapter you install RobotWin Studio, learn the parts of the screen and open your first project.

## Installing

1. From the [GitHub Releases page](https://github.com/Kinin-Code-Offical/robotwin-studio/releases/latest) download one of the two packages:
   - `RobotWinStudio-Setup-<version>.exe`: carries .NET, nothing else to install (recommended).
   - `RobotWinStudio-Setup-<version>-lite.exe`: smaller, but needs the **.NET 9 Desktop Runtime**; the wizard offers to install it if it is missing.
2. Run the file and follow the wizard.
3. When it finishes, open **RobotWin Studio** from the Start menu.

By default it installs for your user only and does not ask for administrator rights. To uninstall, use Windows **Settings → Apps**.

### To compile your own code

| Board | Tool needed | How to install |
| --- | --- | --- |
| Arduino Uno / Nano | The Arduino compiler: `arduino-cli`, the AVR core and the library list | **Comes with the installer**; nothing else to install |
| ESP32 | Visual Studio C++ tools (MSVC) | Visual Studio 2022 or Build Tools with the **"Desktop development with C++"** workload |

To use an `arduino-cli` of your own, enter its path under **Settings → Compilers**. The app finds MSVC itself; if it cannot, enter its path there too.

## First launch

A splash screen appears while the app opens: it shows what is being loaded, how far it has got and the version. Then the **Start** page.

![Splash screen](../../images/en/splash.png)

The home page has:

- The **New world**, **New circuit / board** and **Open…** buttons.
- **Worlds**: ready-made world templates (empty workshop, servo arm, two-wheel car, ultrasonic sensor, remote-controlled car, water rocket, solid-propellant motor…).
- **Circuit and robot examples**: ready circuits at beginner, intermediate and robot level (LED and button, Arduino Blink, LCD + OLED, ESP32 PWM, motor with a MOSFET, op-amp, 7805, RC filter, relay, NAND, line follower, obstacle avoider, micro quadcopter).
- On the right: **Recent**, **Automatic backups** and common **Shortcuts**.

![Home page](../../images/en/home.png)

> Tip: clicking an example opens it in a new tab. Do not be afraid of breaking examples; the original stays unchanged unless you save over it.

## Your first robot: an interactive guide

The first time you open the app it offers a 5–10 minute guide. You can open it again with the **Your first robot** button on the **Start** page or with **Help → Your first robot: interactive guide (5–10 min)**.

The guide stays in the bottom right corner as a small card. At each step it says what to press and draws an orange outline around that control. It notices by itself when you have done the step and moves on. The card's button (for example **Open servo arm** or **Upload and run**) does the step for you; **Skip this step** moves on.

1. **Open the ready servo arm:** an Arduino Uno drives two SG90 servos.
2. **Run it and watch:** the arm sweeps.
3. **Open the code:** the arm's Arduino code opens on the Code page.
4. **Change the code:** the 2 in `step = 2` on line 23 comes selected; type 6. The arm sweeps three times as fast.
5. **Load it onto the board:** the code is compiled, loaded onto the Uno, and the world starts over.
6. **Measure the current:** the **Cables and measurements** tab shows the current in the servos' cables live.
7. **Save:** the world is kept with the code you changed.

![The first-robot guide](../../images/en/guide.png)

## The parts of the screen

| Part | Where | What it does |
| --- | --- | --- |
| Menu bar | Top | **File**, **Edit**, **View**, **Simulation**, **Window**, **Help**. **F10** opens it from the keyboard. |
| Document tabs | Next to the menu | Open circuits and worlds; a saved file shows its own name. Unsaved changes show as a dot where the close button is. When the tabs do not fit, the strip scrolls with the mouse wheel or its arrows; **▾** lists every open tab. Right-click and **Sabitle** (pin) keeps a tab at the left, out of the bulk closes, and the file opens pinned next time too. Drag tabs to reorder them. **Ctrl+Tab** next tab, **Ctrl+W** closes a tab, **Ctrl+Shift+T** opens the closed file again. |
| Design-to-real line | Left edge | The pages in three groups: **ELECTRONICS** (Circuit → PCB → Code), **MECHANICAL** (Part → Assembly), **TEST** (World). Below: Start, Library, Settings and Help. The logo at the top left also returns to Start. **Ctrl+B** widens the line to show what each page is for and its status. |
| Page | Centre | The editor of the selected stage. |
| Status bar | Bottom | Latest message, simulation state, part and cable counts. |

![The rail in groups (expanded)](../../images/en/rail-groups.png)

### Window features

- **Command palette (Ctrl+K):** find any command, page or part by typing its name.
- **Split view (Ctrl+\\):** shows two pages side by side, e.g. the circuit and the PCB together.
- **Detach:** **Window → Move this page to a separate window** moves a page to a second window (e.g. a second monitor).
- **Focus mode:** **View → Focus mode** hides the navigation rail.
- **Moving panels (world):** on the world page, drag the palette, the inspector or a bottom tab by its header into its own window; drop the window on an edge to dock it left, right or in the bottom panel. The layout is kept. See [World → Arranging the panels](07-world.md#arranging-the-panels).
- **Full screen (F11):** leave with **Esc** or **F11**.
- **Interface scale:** **View → Interface scale**, 75–150 %.

## Files

| Extension | What it stores |
| --- | --- |
| `.rwproj` | A circuit / board project: schematic, PCB, assembly, the boards' code |
| `.rwworld` | A world: objects, cables, joints and the boards inside it |
| `.rwmodel` | A 3D model you made on the Model page |
| `.rwsim` | A simulation recording (measurements, events, serial output) |
| `.rwpart`, `.rwlib` | Your own part / part library |

- **Save:** Ctrl+S. **Save as:** Ctrl+Shift+S. **Open:** Ctrl+O.
- The app backs up unsaved work at intervals to `Documents\RobotWin\Yedekler`. Open backups from the **Automatic backups** list on the home page.
- The installer registers these file types with Windows; double-clicking a file opens RobotWin Studio.

## Library

The **Library** (Ctrl+7) page shows every part with its schematic symbol, PCB footprint and 3D preview.

- **New part from this**: creates a new part (`.rwpart`) with your own values using an existing part's simulation model.
- **Change 3D model…**: replaces a part's 3D look with your own STL / OBJ / PLY file. **Revert to the generated model** restores it.
- **Import…**: `.rwpart` / `.rwlib` files and KiCad symbol (`.kicad_sym`) and footprint (`.kicad_mod`, `.pretty` folder) files.
- **Export my library**: share your parts in one `.rwlib` file.

## Settings

The **Settings** (Ctrl+,) page is split into categories. Pick one in the list on the left, or type a word into the **Search settings** box at the top; the search looks through every category.

| Category | What it holds |
| --- | --- |
| General | Language, home page at startup, the first-robot guide, project folder, recent files, length unit |
| Appearance and theme | Theme gallery (9 dark, 7 light themes and a custom one), accent colour, interface scale, performance overlay, effects and sound, 3D quality, 3D renderer (our own GPU renderer or WPF) |
| Saving and backup | Autosave interval, keeping the previous version on save, crash recovery, backup folder, copies kept per document, the folder's size limit |
| Keyboard shortcuts | Change the window's and every page's shortcuts, clash warnings, reset one or all |
| Simulation and physics | Part damage, pause on failure, ambient temperature, time step, starting speed |
| World and editing | Gravity of new worlds, world editor grid |
| Compilers | `arduino-cli` path, `vcvars64.bat` path |
| Updates, Plugins | Version checks; plug-in folders and scripts |
| Advanced | Export settings to a file and import them, error log, file types |

**Language:**

- The app starts in English. Choose English or Türkçe in **General → Language**. The language applies everywhere once the app starts again; the card has a **Restart now** button.
- The language changes:
  - every text of the windows, canvases and instruments;
  - number and date formats (1.5 mm in English, 1,5 mm in Turkish);
  - the guide on the Help page and the News;
  - the part catalogue's names and descriptions;
  - the examples' names and their code's comments.
- Searches (parts, settings, the command palette) work in both languages: "resistor" and "direnç" both find it.
- **To make a language pack of your own:**
  1. Copy `Lang/en.json` and name it like `de.json`. Its keys are the interface's source texts (Turkish) and its values their English: translate the values into your language. In patterns with `{…}`, numbers and names go into `{0}`, `{1}` in order.
  2. Set `"language"` at the top of the file to the language's name, and `"culture"` to its number and date format (for example `"de-DE"`).
  3. Put the new file in the folder the **Language packs folder** button opens (`%APPDATA%\RobotWin\lang`). The file name is the language's id.
  4. The pack shows in the list as "(your own pack)". Texts you have not translated show in English, not Turkish, so a pack can grow a piece at a time.

**Themes:** the gallery in **Appearance and theme** shows each theme as a small picture of the window. The theme you click is applied at once.

![The theme gallery: dark, light and custom themes; accent colours](../../images/en/settings-themes.png)

- **Dark:** Graphite (the default), Black (OLED), Midnight blue, Deep ocean, Forest, Plum, Dusty rose night, Espresso, High contrast.
- **Light:** Light, Paper, Pink, Peach, Lavender, Mint, Sky. On a light theme, the schematic is drawn in dark ink as if printed on paper, the PCB shows a pale board with dark silkscreen (copper keeps its colours), the code editor turns light too, and the syntax colours switch to tones that read on a light background.
- **Custom:**
  - **Ton (hue):** 0 red, 120 green, 200 blue, 330 pink.
  - **Doygunluk (tint):** how coloured the backgrounds are.
  - The **Light background** switch.
  - The window changes colour while you drag; the setting is saved when you let go.
- **Accent colour:**
  - Ten colours for selected items, focus and links; the one marked **T** is the theme's own colour.
  - On light themes the accent turns darker for contrast.
  - 3D views and instrument screens keep their own lighting.

**How backups work:**

- Unsaved tabs are copied into the backup folder at the interval you choose; your own file is not changed.
- When you save over a file, its old version is kept as `name-önceki-date`.
- Each document's newest copies are kept and the folder stays under its size limit; the oldest go first.
- If the app closes unexpectedly, the home page shows that run's backups at the next start.

**Changing a shortcut:** in **Keyboard shortcuts**, click the button and press the new keys. The keys of the Circuit, PCB, World, Code and Part pages are there too; they can be single letters. **Esc** cancels, **Backspace** removes the keys. A key that another command uses is marked with an orange note. A single letter cannot be a window shortcut, because it would fire while you type; press it with **Ctrl**, **Alt** or **Shift**. **F1–F12** work on their own.

---

[Contents](README.md) · [Next: 2. Circuit →](02-circuit.md)
