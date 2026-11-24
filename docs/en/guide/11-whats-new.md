# 11. What's new

What each release brought, changed and fixed. After an update, the first start shows this release's news on the start page; this page is always under **Yardım → 11. Yenilikler** (Help). Every technical detail: [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).

## 1.6.0 — 24 November 2026

**Debugging: follow Arduino code line by line; when the board stops on a line, the circuit and the world stop at that instant too.**

### New

- **Breakpoints, steps and variables (Arduino Uno / Nano):**
  - Click a line number on the code page to put a breakpoint on the line.
  - When the board reaches it, it stops. The circuit simulation or the world stops at the same simulated instant, so you see the code, the voltages, the cables and the parts at one moment.
  - Go on with **Devam** (continue, F5), **Adım** (step, F10), **İçine** (into, F11), **Dışına** (out, Shift+F11) and **Satırda dur** (stop on a line).
  - The **Değişkenler** (variables) tab shows your code's global and `static` variables with their types and addresses. The values also refresh while the board runs; changed ones are shown in colour.

  Details: [4. Code](04-code.md#debugging-breakpoints-steps-variables).

### Fixes

- **The Uno's clock ran ahead of the circuit's:** the cycles that ran over at the end of each time slice piled up (about 0.05 % fast). The program now runs to the cycle that belongs to the slice's end time; after one simulated second it is at exactly 16 000 000 cycles.
- **A running world stood still while its page was out of view:** on the code page the world said it was running but did not advance at all, and its serial output stopped. It now keeps running in the background; the 3D view catches up when you go back to it.

## 1.5.1 — 24 November 2026

**The Arduino compiler comes with the installer; an interactive guide gets your first robot running in 5–10 minutes.**

### New

- **The Arduino compiler comes with the installer:** arduino-cli, the Arduino AVR core and the library list are installed with the app. Uno / Nano code builds right after installing; there is nothing else to install. You can pick an arduino-cli of your own under **Ayarlar → Derleyiciler** (Settings → Compilers).
- **İlk robotun (your first robot), an interactive guide:** offered on the first start; **Başlangıç** (start) and the **Yardım** (help) menu open it again. The steps:
  - open the ready servo arm;
  - change its Arduino code and load it onto the board;
  - watch the arm move;
  - measure the current in **Kablolar ve ölçümler** (cables and measurements);
  - save.

  At each step the card shows what to press and notices by itself when you have done it. The card's button does the step for you. Details: [1. Getting started](01-getting-started.md#your-first-robot-an-interactive-guide).

### Fixes

- **A heavy simulation kept the rest of the app waiting:** a world or circuit that needs more than a frame's time filled every frame. While it ran, saving code, autosave, the rail, sensor views, panel-layout saves, the guide's checks and the automation API all waited (the line follower, running at 0.7× real time, was one such world). Now, once that work has waited 0.2 s, the next frame gives the simulation only a sliver and the waiting work runs. A heavy simulation runs about a tenth slower; light ones run as before. The line follower's world draws 49 frames a second instead of 36.
- **Snapshot, benchmark and self-test runs wrote into the user's settings:**
  - they saved their own window placement (off screen), panel layout and recent files over the user's;
  - documentation screenshots showed the user's recent files and backups.

  Runs from the command line now start from the default settings and save nothing.

## 1.5.0 — 24 November 2026

**The rail, Parça and Montaj redone: all of a robot's parts in one place, and the Montaj follows its project. Arduino libraries on the ESP32, PCBs of up to 32 layers.**

### New

- **The rail in three groups:** **ELEKTRONİK** (electronics: Devre, PCB, Kod), **MEKANİK** (mechanics: Parça, Montaj), **TEST** (Dünya, world).
  - Home no longer looks like a step of the flow; it is with the tools at the bottom and on the logo at the top left.
  - Montaj has its own mark.
  - Expanded (Ctrl+B), every page says what it is for and its status.
- **Modelle is now Parça (part), and one file holds as many parts as you like.** Every top-level object is a part. **Montaja gönder** (send to assembly) makes each part a body of its own:
  - The parts arrive arranged as modelled, beside what is there, touching nothing.
  - Sending a changed part again updates it where it stands; its own origin and joints stay put.
  - Deleted parts are offered for removal from the Montaj; separate parts inside each other get a warning.
- **The Montaj (assembly) is the robot itself:**
  - Setting it up asks which projects go in and how: **PCB'li** (with PCB: the soldered board plus separate motors and batteries), **PCB'siz** (without PCB: every part separate) or **Otomatik** (automatic).
  - Several projects can share one Montaj side by side, and the Parça page's parts can come too.
  - It is saved with its project (**Ctrl+S**).
  - An orange **MONTAJ** badge and a **Montajı çalıştır** (run the assembly) button set it apart from the world.
  - Details: [6. Assembly](06-assembly.md).
- **The Montaj follows its project:** each time it opens, circuit, PCB and code changes come in.
  - New parts are added, removed ones removed, changed parts and code updated where they stand.
  - Where you put things, the joints you made and your own cables are kept.
- **Robot examples are real vehicles in the Montaj:** the obstacle-avoiding and line-following robots are built from real parts:
  - a chassis, wheels on the motor shafts, a ball caster;
  - the board, battery and modules on the deck without touching;
  - in their own arena, on their own code.
- **Environment to try it in:** try the Montaj in a workshop, on a line track, or in an obstacle or ramp field.
- **Arduino libraries on the ESP32:** the installed libraries a sketch includes compile with it, with their own dependencies. The libraries tab searches for the ESP32 too.
- **PCBs of up to 32 copper layers** (as in KiCad). The board set-up window gives the least thickness the stack needs.
- **The line-following example uses real optical TCRT5000 modules** (IR LED, phototransistor, LM393). In its Montaj it follows the oval track on real wheels: a lap in 8 seconds, at most 22 mm off.
- **Dünya (world) on the rail while a Montaj is open** now goes to a test world instead of showing the Montaj again.

### Fixes

- **Sensors were blind in headless runs and in the render:** the world's sensors (line modules, cameras, LiDAR, GPS, encoders) were built only when an IMU was in the circuit, so a render of a line follower saw nothing. They are now built as in the app.
- **A world made from an arena lost its start and its floor:** the oval track's start (its bottom straight) was dropped, and the floor looked dark grey instead of white to optical sensors.

- **The Montaj was a one-time copy of its project:**
  - Later circuit and code changes never reached it.
  - Renaming the project made an empty Montaj.
  - Two projects with the same title shared one Montaj.
  - Closing the app lost the assembly work.
- **Sending parts from the Parça page jumped to the world page.** The parts landed on the circuit's board and started inside each other.
- **The code page listed a Montaj's board as a separate board.** "Kartı düzenle" (edit board) in the Montaj opened a copy.
- **Robot examples opened a Montaj of scattered parts.** The start page also described a "Başlat" (start) button that did not exist.
- **MSVC messages came out garbled on Turkish Windows** ("i�erme dosyas�").
- **Two `.cpp` files of the same name under `src/` overwrote each other's output.**
- **The board set-up window could run off small screens.**
- **Long status messages ran over the counts on the right.**

## 1.4.2 — 24 November 2026

**The code page works like a code editor: folders, loading onto any board, completion from the real headers and Arduino libraries. An adjustable AC/DC power supply and a part library in categories.**

### New

- **An explorer on the code page:**
  - **KARTLAR** (boards) lists every programmable board of the project and of the open world, each with its own code folder.
  - **KOD KLASÖRLERİM** (my code folders) lists folders on disk: `Documents\RobotWin\Kod` and every folder you open from disk (e.g. an Arduino IDE sketch folder).
  - Folders take files and subfolders (`src/`); files and folders can be imported (drag and drop works too).
  - A folder can be saved anywhere as an Arduino sketch folder, exported as a zip, renamed and deleted.
- **Code loads onto any board:** the **Kart** (board) list holds every Arduino Uno, Nano and ESP32 of the project and of the open world. **Karta yükle ve çalıştır** (load onto the board and run) copies the folder's code into the board, compiles it and runs the project or the world.
- **Multi-file sketches:** the main `.ino`, the `.h` / `.cpp` files next to it and `src/` subfolders compile together as in the Arduino IDE, on the Uno / Nano and on the ESP32 alike.
- **Code completion** comes from the board's real header files: it opens after `.`, `->`, `::` and `#include <`, or with **Ctrl+Space**. It knows a variable's type: `kol.` lists the `Servo` members. The panel on the right shows the signature and a description. Opening a parenthesis shows the parameters above the line.
- **Libraries tab:** searches about 10 000 Arduino libraries. **Kur ve ekle** (install and add) installs a library and adds its `#include` line; installed libraries can be removed. A header no installed library provides is flagged and the library that provides it is suggested.
- **Adjustable AC/DC power supply:** DC (CV/CC), AC (RMS voltage, frequency, phase) or AC on top of DC. On AC the current limit holds in both directions and the report gives true RMS values. Presets: 12 V~ 50 Hz, 24 V~ 60 Hz, 230 V~ mains, 12 V DC + 1 V ripple. Details: [8. Instruments](08-instruments.md#adjustable-acdc-power-supply).
- **A part library in categories:** every part has a main category and a sub-category (e.g. Pasif → Direnç, Kondansatör; Ölçüm & Kaynak → Güç kaynağı, Sinyal jeneratörü), with part counts. On the **Kütüphane** (library) page every part has an **Özellikler** (specifications) table.

### Fixed

- **The servo-arm template's second arm spun around its own joint:** the firmware drove the elbow the opposite way to the shoulder across the whole range. The elbow now follows the shoulder at half range, in 2° steps.
- **Pin labels on module boards could name the wrong pins:** board prints were cached without the pin names, so a module with another pin order (or an edited pin map) could show the previous one's VCC / GND labels over pins that carry other signals.
- **The HC-SR04's front print was mirrored against its pins:** seen from the front, "Vcc" stood over the GND pin.
- **Simple world models:** the servo cable has three colours (brown GND, red V+, orange SIG); motors' A / B terminals are red / black.

## 1.4.1 — 24 November 2026

**The render's air is the real air; cables in a render are the simulated ropes; the circuit page's bottom panels can be moved, closed and opened in their own windows.**

### Changed

- **The render's air is the real air**, solved over the whole recording. The parts move as recorded and push the air with their own surface velocity; the world's wind blows in at the box's faces; the ground is a wall. With no wind and nothing moving there is no flow. Before, a 2 m/s "test speed" was blown at an assembly that stood still.
- **The air is shown as 3D smoke:** particles carried by the air leave short streaks along their velocity (blue slow, red fast). The flat cut plane is gone.
- **The wind-tunnel test** is a separate option with its own speed, direction (0° = +X, 90° = +Y) and elevation.
- **Stress for servos, motors and boards:** components are in the render's list; the ones something is fastened to (a servo carrying a second servo) are marked by default.
- **The circuit page's bottom tabs** (oscilloscope, multimeter, fault finding, Monte Carlo, board pins, serial monitor, events) work like the world page's panels:
  - resized by dragging their top edge;
  - closed, docked beside the drawing or opened in their own window;
  - buttons at the right of the strip float, place or close the picked tab, or fold the pane.

  Closed ones come back with **Görünüm → Devre panelleri**.
- **The rail:** the project's stages (Circuit → PCB → Code → Model → Assembly) share the line; the world, a document of its own, sits below a divider.
- **Montaj explains itself** when the project's circuit is empty instead of opening an empty world.
- **A world's boards from the circuit page:** an empty circuit page lists the open world's Arduino / ESP32 boards; one click opens a board's circuit and code, and a template's built-in program becomes editable code.
- **Attach to a servo horn / motor shaft:** "…ne tak" lays the part flat on the output; a long part such as an arm reaches out with its end over the pivot. "Olduğu yerde sabitle" is the old behaviour.

### Fixed

- **Cables in a render stretched into long streaks:** they were drawn from the editor's layout, and the render did not lay them as ropes at all. Renders now simulate, record and replay the same ropes as the live world.
- **Parts and cables could be dragged in a render;** the render is for watching only now.
- **A saved render lost its air;** the air is written to the file now, with the cables.
- **Space and the arrow keys did not type in the render's channel search.**
- **A servo case read 10¹³ MPa:** mid-edge nodes moved onto the curved surface nearly folded some elements. Curved elements that would be distorted now stay straight, and loose bits not face-connected to the part are left out of the mesh.
- **A saved render showed the window's default settings** on the left; it now shows the ones it was computed with.
- **Stray probe badges and meter leads on an empty circuit page.**

## 1.4.0 — 23 November 2026

**Liquids flow with real physics; flow analysis at the real Reynolds number; every solver's deviation from theory on one page.**

### New

- **Liquids:** water, sea water, ethanol, milk, sunflower oil, glycerol and honey flow as particles. Each one's real density, viscosity and surface tension are used. A liquid presses on its container's walls with its real pressure; a container carries its water's weight, and a part dropped in water feels buoyancy. Water wets by material: glass very well, PLA less, PTFE not at all. Drops in the air are dragged by the world's air.
- **Glass and containers:** the **CAM** (glass) material is drawn see-through, so water shows through a glass. A cylinder with a wall thickness (**tube.wall**) and a bottom (**tube.bottom**) becomes a glass or a bowl. **Mekanik → Sıvılar** adds a glass, full or empty, and a volume of water.
- **Glass of water template:** an ESP32 tips a glass held on a sideways SG90's horn by 90°; 193 ml of water pours into a bowl.
- **Water that looks like water:** the surface is built continuously from the particles. A thin sheet and a falling stream read as continuous. Fresnel reflection (clear looking in, sky at grazing angles), a sun highlight and drops streaked by a 1/60 s exposure.
- **Renders compute everything in full:**
  - the liquid is solved at the accuracy's physics step (0.1 ms on Hassas);
  - the pressure is iterated until the compression is under 0.2 %;
  - up to 40 000 particles are used (120 000 on Reference);
  - the replay draws a finer surface.
- **Open a saved render:** the render window replays a `.rwanalysis` without computing it again.
- **Flow analysis at the real Reynolds number:** a regularized collision with LES, and curved walls at their true position. Each result states how far to trust it: grid uncertainty (GCI), boundary layer (y+) and blockage.
- **Validation page:** **Yardım → 12. Doğrulama** and the README list every solver's deviation from theory and benchmarks, with tolerances. For example: the Schäfer–Turek cylinder 0.8 %, hydrostatic water pressure 2.1 %, the dam-break experiment 4–8 %.

### Fixed

- **ESP32 boards sat idle in renders:** the render's runner had no sketch compiler. A render now runs the same firmware as the live world.
- **Sphere drag was 30 % high** (Re 20); it is now within 8–11 % of the standard curve.
- **A channel inlet delivered 93 % of its flow rate;** it is now a velocity boundary with the exact rate.
- **Help tables with many columns** squeezed their last column to one letter.

## 1.3.0 — 23 November 2026

**Cables run where a real wire can; joined parts no longer sink into each other; a PS5-controlled plane; rocket ignition along its real chain.**

### New

- **Cable routing:** each cable is laid along the shortest way between its plugs that touches no solid. A wire inside a closed body stays inside, and one that must leave goes out through a real hole. The automatic length is that run plus 10 % and 15 mm.
- **Real cable holes:** a shell wall marked with a cable hole is the frame around the opening, so wires and pushrods pass through the opening only. Size, height and offset can be set per side.
- **No tunnelling:** a fast wire cannot jump a thin wall in one step, and a wire drawn against a hole's edge slides back into the hole instead of through the wall.
- **Consistency audit:** when a run starts, the **Olaylar** (events) tab reports joined parts sunk into each other (a board with its pins in the floor), wires through solids, plugs inside other parts, and wires with no way between their ends.
- **Hollow tubes:** a cylinder can have a wall thickness. A rocket's motor sits in the body tube's bore on centering rings.
- **PS5-controlled plane:** a 1 m trainer takes off from a model airfield's runway and is flown with a DualSense, with stabilisation, failsafe out of range and a chase camera.
- **Live inputs replayed in the render:** controller, serial, USB, IR, conveyor and switch events are recorded with their times, and the analysis recomputes the run with the same inputs.
- **Welded parts as one rigid body**, **rocket ignition along its real chain** (igniter, grain heating, flame spread, erosive burning, real gas, nozzle separation, casing heating, countdown switch, parachute), **screws and glue as real physics**, **materials and finishes**, **movable panels**.
- **A tidier inspector:** it shows only what the object uses; a pressure vessel, a parachute, own air settings and behaviours appear when on (with **Kaldır**, remove), the rest are under **Özellik ekle ▾** (add feature).
- **Headless runs simulate the app's cables**, using the plug positions stored in the exported geometry file.
- **New templates:** enclosed rover (cable holes; an ESP32 drives a square), traffic light with a pedestrian button (ESP32), pendulum (period within 0.1 % of theory), PS5-controlled plane, solid-fuel rocket.

### Fixed

- **Wires went through walls:** 9 wires left the PS5 plane's pod through its side walls. From take-off to landing, none goes through a wall now.
- **Wires trailed moving parts by a step** (12 mm in a plane at 12 m/s), and they were damped against the world (a false pull of about 4 g in flight).
- **Rocket fins stood tangent to the body;** they now stand straight out.
- **Parts sunk into each other in the templates:** boards on their pins, a battery inside a servo, a floating servo, crossed X-frame arms, a LiDAR mast inside an Uno, and pushrods inside the tail are all fixed.
- **Propeller thrust falls with the advance ratio in forward flight,** the part masses of the app and the headless runner match, and the game view draws the terrain.

## 1.2.0 — 22 November 2026

Cutting tools in the modeller: knife, slice, difference/union/intersection, loop cut, bevel, solidify, edge extrude, ring and linked selection. The app's messages are in one language.

## 1.1.0 — 21 November 2026

One shared physics for circuit, rigid bodies, heat, air, gas and radio: real assemblies, model-shaped collision, flight, gas and rockets, heat network, analysis and render, an industrial robot cell, sensors and cameras, a terrain editor, automation API and SDK, in-app help, news and automatic updates.

## 1.0.0 — 20 November 2026

First public release: the design-to-reality pipeline (Circuit → PCB → Code → Model → Assembly → World), a KiCad-level schematic editor, PCB, Arduino and ESP32 emulation, a Blender-like modeller, an open world, radio and controllers, and instruments.
