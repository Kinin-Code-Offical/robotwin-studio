# 11. What's new

What each release brought, changed and fixed. After an update, the first start shows this release's news on the start page; this page is always under **Help → 11. What's new**. Every technical detail: [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).

## 1.7.0 — 26 November 2026

**A big release: an English interface, our own GPU renderer, the ESP32 emulator, mechanisms, the PLC and industrial cells, KiCad and FMU bridges.**

### New

- **Language and look:** the app starts in English, Turkish is one click away (Settings → General → Language), and more languages come as packs. A gallery of 16 themes and your own, accent colours, readable text in every theme. Settings in categories with search, changeable shortcuts, settings export and import. Backups per document and earlier versions one click away.
- **Speed:** every 3D view is drawn by our own GPU renderer (sun shadows in three cascades); simulations run on their own thread; the splash comes at once and the first window opens in 712 ms instead of 1174 ms. The performance overlay says which solver holds a world back.
- **ESP32, two run modes:** the fast mode (the sketch compiled on the PC) and the hardware-compatible mode, where **our own Xtensa LX6 core** runs real ESP32 firmware on both cores: UART, I²C, SPI, RMT (WS2812), PCNT (encoders), MCPWM, DAC, touch, deep and light sleep, watchdogs, Wi-Fi station and soft-AP with the firmware's own lwIP. The two modes were compared template by template. The fast mode has ESP-NOW, and warns when a sketch holds more memory than a real ESP32's heap. Building and uploading for real boards too.
- **Arduino Uno:** Registers and Timeline (logic analyzer) tabs in the debugger; the UART's TX and RX pins bit by bit; a bit-timed SPI master (an MCP3008, say); an Arduino compatibility matrix.
- **NeoPixel (WS2812B) strips** in circuit and world, with real firmware.
- **Mechanisms:** gears, pulleys, GT2 belts, bearings whose friction follows the load, shafts, lead screws, racks, backlash and belt stretch, springs and dampers in joints. Stepper motors with A4988, DRV8825 and TMC2209 drivers; an Arduino Z-axis example.
- **Robotics:** kinematics of the arms and wheeled robots you build, speed and position PID tuners, arm path and load checks. URDF and xacro import; a ROS 2 bridge (rosbridge: clock, joint states and commands, mobile robots; the world's LiDARs, IMUs, cameras, GPS and compasses publish to ROS too; the FollowJointTrajectory action for MoveIt).
- **Industrial cells:** a PLC running Structured Text (IEC 61131-3) with its own function blocks and functions, arrays (of more dimensions too), structures, enumerations, VAR_IN_OUT and STRING. A ladder diagram editor, an HMI operator panel, station cycle time and bottleneck. Modbus TCP and RTU (server, client, remote I/O) and an **OPC UA server and client** (UA_READ, UA_WRITE). Pneumatic cylinders, 5/2 and double-solenoid valves, compressor, air tank, vacuum ejector; photoelectric sensor, limit switch; equipment fault scenarios.
- **Circuit:** AC analysis (Bode), manufacturers' SPICE models, UART/I²C/SPI decoding and math channels on the oscilloscope, switching regulator modules, batteries from a value, line fault injection, pins damaged by heat, **FMU models** (FMI 2.0 co-simulation), **opening KiCad schematics** (connections identical to kicad-cli's).
- **PCB:** manufacture and test checks (DFM/DFT), differential pairs and length matching, an enclosure check, a Gerber cross-check, a layout imported back from KiCad, what the schematic's changes did to the board, a bill of materials with sourcing and costing.
- **Part and Montaj:** dimensioned sketches, parameters and formulas, assembly mates, a 3D print check, STEP and IGES (Open CASCADE).
- **Measuring and validating:** sensor logs and failure scenarios, temperature's effect on sensors, attitude fusion experiments, experiments with confidence intervals and Python, conservation and time-step convergence checks, each model's limits shown with its part.
- **Project check:** what a project needs that this computer lacks; parts keep their identity when renamed; design alternatives.

Every detail and fix: [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).

## 1.6.1 — 24 November 2026

**The tabs at the top: scrolling, pinning, a full list and file names.**

### New

- **Working with many tabs:**
  - When the tabs do not fit, the strip scrolls with the mouse wheel and with arrows that appear. The active tab is always brought into view.
  - The **▾** button lists every open tab, the ones out of view too.
  - Right-click and **Sabitle** (pin): the tab stays at the left with a pin where the close button is. A middle click and the bulk closes leave it open, and the file opens pinned next time too.
  - **Ctrl+Shift+T** opens the closed file again.

### Changes

- **A tab shows its file's name:** a saved project or world shows its file name instead of the title stored inside. For example, "osiloskop test.rwproj" no longer shows as "New Circuit". **Save as** gives the document that name inside too (the Part page already did).
- Two tabs of the same name are told apart by their folder, two new documents by a number.
- Unsaved changes are a dot where the close button is, instead of a "•" added to the name. The close button comes back under the pointer.

## 1.6.0 — 24 November 2026

**Debugging: follow Arduino code line by line; when the board stops on a line, the circuit and the world stop at that instant too.**

### New

- **Breakpoints, steps and variables (Arduino Uno / Nano):**
  - Click a line number on the code page to put a breakpoint on the line.
  - When the board reaches it, it stops. The circuit simulation or the world stops at the same simulated instant, so you see the code, the voltages, the cables and the parts at one moment.
  - Go on with **Continue** (F5), **Step** (F10), **Into** (F11), **Out** (Shift+F11) and **Stop at line**.
  - The **Variables** tab shows your code's global and `static` variables with their types and addresses. The values also refresh while the board runs; changed ones are shown in colour.

  Details: [4. Code](04-code.md#debugging-breakpoints-steps-variables).

### Fixes

- **The Uno's clock ran ahead of the circuit's:** the cycles that ran over at the end of each time slice piled up (about 0.05 % fast). The program now runs to the cycle that belongs to the slice's end time; after one simulated second it is at exactly 16 000 000 cycles.
- **A running world stood still while its page was out of view:** on the code page the world said it was running but did not advance at all, and its serial output stopped. It now keeps running in the background; the 3D view catches up when you go back to it.

## 1.5.1 — 24 November 2026

**The Arduino compiler comes with the installer; an interactive guide gets your first robot running in 5–10 minutes.**

### New

- **The Arduino compiler comes with the installer:** arduino-cli, the Arduino AVR core and the library list are installed with the app. Uno / Nano code builds right after installing; there is nothing else to install. You can pick an arduino-cli of your own under **Settings → Compilers**.
- **Your first robot, an interactive guide:** offered on the first start; **Start** and the **Help** menu open it again. The steps:
  - open the ready servo arm;
  - change its Arduino code and load it onto the board;
  - watch the arm move;
  - measure the current in **Cables and measurements**;
  - save.

  At each step the card shows what to press and notices by itself when you have done it. The card's button does the step for you. Details: [1. Getting started](01-getting-started.md#your-first-robot-an-interactive-guide).

### Fixes

- **A heavy simulation kept the rest of the app waiting:** a world or circuit that needs more than a frame's time filled every frame. While it ran, saving code, autosave, the rail, sensor views, panel-layout saves, the guide's checks and the automation API all waited (the line follower, running at 0.7× real time, was one such world). Now, once that work has waited 0.2 s, the next frame gives the simulation only a sliver and the waiting work runs. A heavy simulation runs about a tenth slower; light ones run as before. The line follower's world draws 49 frames a second instead of 36.
- **Snapshot, benchmark and self-test runs wrote into the user's settings:**
  - they saved their own window placement (off screen), panel layout and recent files over the user's;
  - documentation screenshots showed the user's recent files and backups.

  Runs from the command line now start from the default settings and save nothing.

## 1.5.0 — 24 November 2026

**The rail, Part and Assembly redone: all of a robot's parts in one place, and the Assembly follows its project. Arduino libraries on the ESP32, PCBs of up to 32 layers.**

### New

- **The rail in three groups:** **ELECTRONICS** (Circuit, PCB, Code), **MECHANICAL** (Part, Assembly), **TEST** (World).
  - Home no longer looks like a step of the flow; it is with the tools at the bottom and on the logo at the top left.
  - Assembly has its own mark.
  - Expanded (Ctrl+B), every page says what it is for and its status.
- **Model is now Part, and one file holds as many parts as you like.** Every top-level object is a part. **Send to assembly** makes each part a body of its own:
  - The parts arrive arranged as modelled, beside what is there, touching nothing.
  - Sending a changed part again updates it where it stands; its own origin and joints stay put.
  - Deleted parts are offered for removal from the Assembly; separate parts inside each other get a warning.
- **The Assembly is the robot itself:**
  - Setting it up asks which projects go in and how: **PCB'li** (with PCB: the soldered board plus separate motors and batteries), **PCB'siz** (without PCB: every part separate) or **Otomatik** (automatic).
  - Several projects can share one Assembly side by side, and the Part page's parts can come too.
  - It is saved with its project (**Ctrl+S**).
  - An orange **ASSEMBLY** badge and a **Run the assembly** button set it apart from the world.
  - Details: [6. Assembly](06-assembly.md).
- **The Assembly follows its project:** each time it opens, circuit, PCB and code changes come in.
  - New parts are added, removed ones removed, changed parts and code updated where they stand.
  - Where you put things, the joints you made and your own cables are kept.
- **Robot examples are real vehicles in the Assembly:** the obstacle-avoiding and line-following robots are built from real parts:
  - a chassis, wheels on the motor shafts, a ball caster;
  - the board, battery and modules on the deck without touching;
  - in their own arena, on their own code.
- **Environment to try it in:** try the Assembly in a workshop, on a line track, or in an obstacle or ramp field.
- **Arduino libraries on the ESP32:** the installed libraries a sketch includes compile with it, with their own dependencies. The libraries tab searches for the ESP32 too.
- **PCBs of up to 32 copper layers** (as in KiCad). The board set-up window gives the least thickness the stack needs.
- **The line-following example uses real optical TCRT5000 modules** (IR LED, phototransistor, LM393). In its Assembly it follows the oval track on real wheels: a lap in 8 seconds, at most 22 mm off.
- **World on the rail while an Assembly is open** now goes to a test world instead of showing the Assembly again.

### Fixes

- **Sensors were blind in headless runs and in the render:** the world's sensors (line modules, cameras, LiDAR, GPS, encoders) were built only when an IMU was in the circuit, so a render of a line follower saw nothing. They are now built as in the app.
- **A world made from an arena lost its start and its floor:** the oval track's start (its bottom straight) was dropped, and the floor looked dark grey instead of white to optical sensors.

- **The Assembly was a one-time copy of its project:**
  - Later circuit and code changes never reached it.
  - Renaming the project made an empty Assembly.
  - Two projects with the same title shared one Assembly.
  - Closing the app lost the assembly work.
- **Sending parts from the Part page jumped to the world page.** The parts landed on the circuit's board and started inside each other.
- **The code page listed an Assembly's board as a separate board.** "Edit board" in the Assembly opened a copy.
- **Robot examples opened an Assembly of scattered parts.** The start page also described a "Start" button that did not exist.
- **MSVC messages came out garbled on Turkish Windows** ("i�erme dosyas�").
- **Two `.cpp` files of the same name under `src/` overwrote each other's output.**
- **The board set-up window could run off small screens.**
- **Long status messages ran over the counts on the right.**

## 1.4.2 — 24 November 2026

**The code page works like a code editor: folders, loading onto any board, completion from the real headers and Arduino libraries. An adjustable AC/DC power supply and a part library in categories.**

### New

- **An explorer on the code page:**
  - **BOARDS** lists every programmable board of the project and of the open world, each with its own code folder.
  - **MY CODE FOLDERS** lists folders on disk: `Documents\RobotWin\Kod` and every folder you open from disk (e.g. an Arduino IDE sketch folder).
  - Folders take files and subfolders (`src/`); files and folders can be imported (drag and drop works too).
  - A folder can be saved anywhere as an Arduino sketch folder, exported as a zip, renamed and deleted.
- **Code loads onto any board:** the **Board** list holds every Arduino Uno, Nano and ESP32 of the project and of the open world. **Upload to board and run** copies the folder's code into the board, compiles it and runs the project or the world.
- **Multi-file sketches:** the main `.ino`, the `.h` / `.cpp` files next to it and `src/` subfolders compile together as in the Arduino IDE, on the Uno / Nano and on the ESP32 alike.
- **Code completion** comes from the board's real header files: it opens after `.`, `->`, `::` and `#include <`, or with **Ctrl+Space**. It knows a variable's type: `kol.` lists the `Servo` members. The panel on the right shows the signature and a description. Opening a parenthesis shows the parameters above the line.
- **Libraries tab:** searches about 10 000 Arduino libraries. **Install and add** installs a library and adds its `#include` line; installed libraries can be removed. A header no installed library provides is flagged and the library that provides it is suggested.
- **Adjustable AC/DC power supply:** DC (CV/CC), AC (RMS voltage, frequency, phase) or AC on top of DC. On AC the current limit holds in both directions and the report gives true RMS values. Presets: 12 V~ 50 Hz, 24 V~ 60 Hz, 230 V~ mains, 12 V DC + 1 V ripple. Details: [8. Instruments](08-instruments.md#adjustable-acdc-power-supply).
- **A part library in categories:** every part has a main category and a sub-category (e.g. Passive → Resistor, Capacitor; Measurement & Source → Power supply, Signal generator), with part counts. On the **Library** page every part has a **Properties** table.

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

  Closed ones come back with **View → Circuit panels**.
- **The rail:** the project's stages (Circuit → PCB → Code → Model → Assembly) share the line; the world, a document of its own, sits below a divider.
- **Assembly explains itself** when the project's circuit is empty instead of opening an empty world.
- **A world's boards from the circuit page:** an empty circuit page lists the open world's Arduino / ESP32 boards; one click opens a board's circuit and code, and a template's built-in program becomes editable code.
- **Attach to a servo horn / motor shaft:** "… · mount on …" lays the part flat on the output; a long part such as an arm reaches out with its end over the pivot. "… · fix to … where it is" is the old behaviour.

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
- **Glass and containers:** the **Glass (soda-lime)** material is drawn see-through, so water shows through a glass. A cylinder with a wall thickness (**tube.wall**) and a bottom (**tube.bottom**) becomes a glass or a bowl. **Mechanical → Liquids (real flow with particles)** adds a glass, full or empty, and a volume of water.
- **Glass of water template:** an ESP32 tips a glass held on a sideways SG90's horn by 90°; 193 ml of water pours into a bowl.
- **Water that looks like water:** the surface is built continuously from the particles. A thin sheet and a falling stream read as continuous. Fresnel reflection (clear looking in, sky at grazing angles), a sun highlight and drops streaked by a 1/60 s exposure.
- **Renders compute everything in full:**
  - the liquid is solved at the accuracy's physics step (0.1 ms on Hassas);
  - the pressure is iterated until the compression is under 0.2 %;
  - up to 40 000 particles are used (120 000 on Reference);
  - the replay draws a finer surface.
- **Open a saved render:** the render window replays a `.rwanalysis` without computing it again.
- **Flow analysis at the real Reynolds number:** a regularized collision with LES, and curved walls at their true position. Each result states how far to trust it: grid uncertainty (GCI), boundary layer (y+) and blockage.
- **Validation page:** **Help → 12. Validation** and the README list every solver's deviation from theory and benchmarks, with tolerances. For example: the Schäfer–Turek cylinder 0.8 %, hydrostatic water pressure 2.1 %, the dam-break experiment 4–8 %.

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
- **Consistency audit:** when a run starts, the **Events** tab reports joined parts sunk into each other (a board with its pins in the floor), wires through solids, plugs inside other parts, and wires with no way between their ends.
- **Hollow tubes:** a cylinder can have a wall thickness. A rocket's motor sits in the body tube's bore on centering rings.
- **PS5-controlled plane:** a 1 m trainer takes off from a model airfield's runway and is flown with a DualSense, with stabilisation, failsafe out of range and a chase camera.
- **Live inputs replayed in the render:** controller, serial, USB, IR, conveyor and switch events are recorded with their times, and the analysis recomputes the run with the same inputs.
- **Welded parts as one rigid body**, **rocket ignition along its real chain** (igniter, grain heating, flame spread, erosive burning, real gas, nozzle separation, casing heating, countdown switch, parachute), **screws and glue as real physics**, **materials and finishes**, **movable panels**.
- **A tidier inspector:** it shows only what the object uses; a pressure vessel, a parachute, own air settings and behaviours appear when on (with **Remove**), the rest are under **Add feature ▾**.
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
