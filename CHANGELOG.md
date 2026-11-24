# RobotWin Studio - Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.6.1] - 2026-11-24

### Added

- **Document tabs that hold many files:** the strip scrolls with the wheel and with arrows that appear when the tabs do not fit, the active tab is always scrolled into view, and a list button shows every open tab (the ones out of view too). Tabs can be pinned (Sabitle in the tab menu): a pinned tab stays at the left, shows a pin instead of the close button, is left open by a middle click and by "close others / to the right / all unpinned", and a saved file opens pinned again next time. Ctrl+Shift+T opens the last closed file again (also in the tab menu and the list). The tooltip says the kind, the file and whether it is unsaved.

### Changed

- **A tab reads its file's name:** a saved project or world shows its file name, not the title stored inside (a file saved as "osiloskop test.rwproj" no longer shows "Yeni Devre"); Save As gives the document that name inside too, as the Parça page already did. Two tabs of the same name are told apart by their folder, two new ones by a number. Unsaved changes are a dot where the close button is (the close button comes back under the pointer) instead of a "•" added to the name.

## [1.6.0] - 2026-11-24

### Added

- **Source-level debugging for the Arduino Uno and Nano, on one clock with the circuit and the world.** A click on a line number in the code page puts a breakpoint there (a line without code stops at the next line that has some; a ring marks one that can never stop). When the emulated ATmega328P reaches it, it stops before the line's first instruction at an exact cycle, and the circuit simulation or the world it runs in stops at that same instant, so the code, the voltages, the cables and the mechanics are seen at one moment. Devam (F5), Adım (F10, over calls), İçine (F11), Dışına (Shift+F11) and Satırda dur go on from there; the Değişkenler tab shows the sketch's globals and statics with their types and addresses, live while it runs, changed values in colour. Line tables, functions (LTO-inlined `loop()` included) and variables come from the ELF avr-gcc writes beside the HEX (`Firmware/Debug`: a DWARF 2–4 reader). Checked end to end in the app on the servo arm world and the Blink circuit (`--fault debug-e2e`, `debug-e2e-circuit`).

### Fixed

- **The Uno's clock ran ahead of the circuit's:** each slice ran its cycles from where the last one's final instruction ended, so the overrun piled up (about 0.05 % fast). The CPU now runs to the cycle that belongs to the slice's end time; after one simulated second it is at exactly 16 000 000 cycles.
- **A running world stood still while its page was out of view:** on the code page (or any other) the world's simulation did not advance at all, though it said it was running, and its serial output stopped. It now goes on in the background; the 3D view catches up when shown.

## [1.5.1] - 2026-11-24

### Added

- **The Arduino compiler comes with the installers and the portable app:** `tools\arduino\` carries arduino-cli, the Arduino AVR core (1.8.6, with avr-gcc and avrdude) and the Arduino library list, downloaded from arduino.cc when the release is built and proven there by compiling a sketch. Uno / Nano code builds right after installing; the app runs the bundled compiler on its own data folder (copied once to `%LOCALAPPDATA%\RobotWinStudio\arduino-data` when the install folder is read-only), completion reads its AVR core and the libraries tab its library list. A compiler chosen in Settings still wins. The self-test builds a sketch with it. Licenses: THIRD_PARTY_NOTICES.md, `tools\arduino\OKUBENI.txt`.
- **"İlk robotun", an interactive first-run guide** (offered once on the first start; Başlangıç and Yardım open it again): the ready servo arm, its Arduino code changed (`step = 2` → 6), loaded onto the Uno, the arm seen moving, the current measured in the cables table, the world saved — 5 to 10 minutes. A card in the corner says what to do and outlines the control to press; each step notices by itself when it is done, and the card's button does it for you.

### Fixed

- **A heavy simulation starved the rest of the app:** a world or circuit that needs more than a frame's time filled every frame, and nothing below render priority ran while it did: saving code, autosave, the rail, sensor views, panel-layout saves, the guide's checks and the automation API waited until the simulation stopped (the line follower, at 0.7× real time, was one). Once such work has waited 0.2 s the next frame now simulates only a sliver, so it gets its turn; a heavy simulation runs about a tenth slower, a light one as before. The line follower's world now draws 49 frames a second instead of 36.
- **Snapshots, the benchmark and the self-test wrote into the user's settings:** they saved their own window placement (off screen), panel layout and recent files over the user's and showed the user's recent files and backups in documentation screenshots. Runs from the command line now start from the default settings and save nothing. The benchmark (`--bench`) writes its log as it goes, so a run that is stopped still shows what it measured and where it stopped, and `--bench-filter id,id` measures only the templates named.

## [1.5.0] - 2026-11-24

### Changed

- **The rail in three groups:** ELEKTRONİK (circuit, PCB, code), MEKANİK (part, assembly), TEST (world), each group on its own trace. Home is no longer a step on the line: it sits with the tools at the bottom and on the logo. Montaj has its own mark (it shared the cube with Modelle); expanded, every stage says what it is for and its live status.
- **Modelle is Parça (part), and a file holds as many parts as you like:** every top-level object is a part, objects parented under it (Ctrl+P) are one part with it. **Montaja gönder** (send to assembly) sends each part as a body of its own instead of merging everything into one lump; "one body into a test world" stays in the context menu and never targets a Montaj.
- **Montaj is the robot, owned by its project:** a world tab that is saved inside the project file (`montaj/montaj.rwworld`), saved with Ctrl+S on the project, reopened from it, and marked as Montaj (badge, tab mark, "Montajı çalıştır"). Setting it up asks what goes in: the open projects or projects from files, each **with its PCB** (what is placed on the board soldered on one board, the rest cabled to it), **without** (every part separate, cabled pin to pin) or automatic, and the Parça page's parts. Several projects share one Montaj side by side; the inspector changes a project's form, adds or removes projects and picks an environment to try the robot in.
- **The Montaj follows its projects:** opening it (or "Projeden güncelle") brings circuit, PCB and code changes in: added parts are added and cabled, removed ones removed, changed parts and code updated where they stand, the board takes the current PCB and code, the Montaj's cables follow the nets. Where parts were put, how they were joined and cables laid by hand are kept. A project is linked by a stable id (`Schematic.ProjectId`), not its title.
- **Rover projects are built as real rovers:** a project whose mechanics say "rover" (the obstacle-avoiding and line-following examples) is put together from real parts: a plate, the gear motors under it with the wheels on their output shafts (the right motor mirrored on the same axle, its leads swapped as on a real kit, unless its mount says reversed), a ball caster, the boards, battery and modules packed on the deck by their real footprints without touching, the sensors where its mechanics put them, at its arena's start. It drives on its own code (1.29 m in 2 s in the test, level, wheels on their shafts).
- **Parts sent again keep their place:** the world shows a model about its bounding box, so a part that grew on one side used to shift against its joints; its own origin is now kept (`ModelOrigin`) and the pose compensated. New parts arrive arranged as modelled, beside what is there, on the floor (never inside another body). Parts deleted from the model are offered for removal; parts inside each other that are not one part (a tyre and its hub) are pointed out.
- **ESP32 sketches use installed Arduino libraries:** a header the HAL and the sketch do not provide is looked up in the library folders the way the Arduino builder does (the library named after the header and supporting the architecture wins; `time.h` never picks a library's `Time.h`), the library's own includes are followed, and its sources compile with the sketch (1.0 and 1.5 layouts, `utility/`, examples left out). A library that does not compile is named. The libraries tab lists installed libraries and searches the index for ESP32 too; a cached build is rebuilt when a library it uses changes.
- **PCBs of up to 32 copper layers** (In1.Cu … In30.Cu, as in KiCad), with Gerber and KiCad output; the board set-up window wraps the layer chips, scrolls when the screen is short and gives the least thickness a stack needs.
- **The line-following example uses real optical TCRT5000 modules** (IR LED, phototransistor, LM393; DO high over the tape): the same part reads the floor in the vehicle model (through its own phototransistor) and in a world. In its Montaj it follows the oval track on real wheels (a lap in about 8 s, at most 22 mm off the line).
- **Dünya while a Montaj is open** goes to a test world (the last one, or a new one) instead of showing the Montaj again.

### Fixed

- **Headless runs and the render had blind sensors:** the world's sensors (line modules, cameras, LiDAR, GPS, encoders) were built only when an IMU was on an I²C bus, so a render of a line follower saw nothing while the app's run did. They are built as in the app.
- **An arena turned into a world lost its start and its floor:** the oval's start (on its bottom straight) and the figure eight's heading were dropped, and the floor read dark grey (0.3) to optical sensors instead of the arena's white (0.85).

- **The Montaj was a one-time copy of its project:** later circuit, PCB and code changes never reached it, renaming the project made a new empty Montaj, two projects of the same title shared one, and closing the app lost the assembly work. Boards off the PCB shared the project's firmware object while the PCB board was a copy.
- **Sending parts to the Montaj pulled you to the world page** and put them where the circuit's board stood (bodies started inside each other); each part was lifted on its own, breaking the arrangement.
- **The code page listed a Montaj's boards as boards of their own** beside the project's; "Kartı düzenle" on a Montaj board opened a copy. Both now lead to the project.
- **The robot examples opened a Montaj of loose parts** and their own vehicle (chassis, wheels, arena) was ignored there; the start page told you to press a "Başlat" button that did not exist.
- **MSVC messages came out garbled on non-English Windows** ("i�erme dosyas�"): cl.exe writes in the console code page, which is now read; the vcvars environment is captured as UTF-16, so paths with non-ASCII characters survive. A missing header is recognised in any language.
- **Two sketch sources of the same name in different `src/` folders overwrote each other's object file.**
- **The board set-up window could run off the bottom of a laptop screen.**
- **A long status message ran over the counts on the right of the status bar.**

## [1.4.2] - 2026-11-24

### Added

- **The code page is a code workspace.** The explorer lists every programmable board of the project and of the open world (each with its own code folder) and code folders on disk (`Documents\RobotWin\Kod` and any folder opened from disk, e.g. an Arduino IDE sketch folder). Folders take new files and subfolders (`src/`), imported files and folders (also by drag and drop), and can be saved to disk anywhere as an Arduino sketch folder, exported as a zip, renamed, deleted or removed from the list; Ctrl+S saves files on disk.
- **Code loads onto any board:** the board list holds every Arduino Uno, Nano and ESP32 of the project and of the open world. **Karta yükle ve çalıştır** copies the folder's code into the board, compiles it and runs the project's simulation or the world.
- **Multi-file sketches** for the Uno / Nano and the ESP32: the main `.ino` with `.h` / `.cpp` files and `src/**` subfolders compile together as in the Arduino IDE. A board's files are saved in the project under `firmware/<ref>/files/`; a single-file program keeps its old form, so existing builds stay current.
- **Code completion from the board's real headers:** the Arduino AVR core, the installed libraries and the sketchbook for the Uno / Nano, the simulation layer for the ESP32. Members after `.`, `->` and `::` (by the variable's type: `kol.` lists `Servo`'s members), headers after `#include <`, the sketch's own functions, variables and `#define`s, ranked by prefix, word starts and subsequence; a detail panel shows the signature and a description. Signature help shows the parameters of the call being typed with the current one in bold. Ctrl+Space and Ctrl+Shift+Space.
- **Arduino libraries tab:** searches the local Arduino library index (about 10 000 libraries), installs (`arduino-cli lib install`) and adds the `#include`, uninstalls, adds an installed library's headers, downloads the index on request; a header no installed library provides is flagged and the library that provides it is suggested.
- **Adjustable AC/DC power supply** (0-30 V DC / 0-24 V AC, 3 A): DC (CV/CC), AC (RMS voltage, frequency, phase) or AC on top of DC; on AC the current limit holds in both directions (it sources and sinks), OVP looks at |V|, and the report gives true RMS voltage and current over whole cycles. Presets: 12 V~ 50 Hz, 24 V~ 60 Hz, 230 V~ 50 Hz mains, 12 V DC + 1 V RMS 100 Hz ripple. The symbol shows the setting (`12V~`, `12+1~`); AC nets read their RMS value (`V~`).
- **Two-level part catalogue:** every part has a main category and a sub-category from its simulation model (Pasif → Direnç, Kondansatör …; Ölçüm & Kaynak → Güç kaynağı, Sinyal jeneratörü …), with part counts, in the circuit page's list and as two rows of chips on the library page. The library page's part sheet has a specification table: every value of the part's model (bold where this part number differs from the default), its pins and its model.

### Fixed

- **The servo-arm template's second arm spun around its own joint:** its firmware drove the elbow the opposite way to the shoulder across the whole range. The elbow now follows the shoulder at half range, in 2° steps.
- **Pin labels on module boards could name the wrong pins:** board prints were cached by a key without the pin names, so a module with another pin order (or an edited pin map) showed the first one's VCC / GND labels over pins that carry other signals. The key now carries the printed names; the GY-521 prints its mapped names instead of a fixed list.
- **The HC-SR04's front print was mirrored against its pins** ("Vcc" stood over the GND pin seen from the front); it now names the pins in the order they stand.
- **Simple world models:** the servo's pigtail is three wires (brown GND, red V+, orange SIG) in the plug's pin order, and motors show red / black terminal tabs where their A / B pins are.

## [1.4.1] - 2026-11-24

### Changed

- **The render's air is the real air, solved over time:** one 3D lattice-Boltzmann run through the whole recording in a box fixed to the world. The parts move through it exactly as recorded and push and drag the air with their own surface velocity (moving walls, Ladd; curved walls from each part's signed distance, re-placed whenever a part has moved a third of a cell); the world's wind with its gusts blows in at the faces, everywhere else the faces are open at ambient pressure; the ground is a wall. The box is fitted around the parts that really move, with cells a quarter of the thinnest moving part's width (coarser only if the accuracy's budget says so). Nothing else drives the air: with no wind and nothing moving there is no flow. Force on the moving parts per frame is the gauge force (ambient pressure removed), by Wen et al.'s Galilean-invariant momentum exchange.
- **The air is shown in 3D as smoke:** particles carried by the solved air, each drawn as a short streak along its velocity and coloured by speed; still air stays invisible. The flat velocity cut plane is gone.
- **Wind-tunnel test is an explicit option** with its own speed, direction (0° = +X, 90° = +Y) and elevation; it replaces the "test speed" the render used to blow at any assembly that stood still.
- **Stress for servos, motors and boards:** the render's part list now includes components, and the ones something is fastened to (a servo carrying a second servo) are marked by default.
- **The circuit page's bottom tabs** (oscilloscope, multimeter, fault finding, Monte Carlo, board pins, serial monitor, events) work like the world page's panels: resized by a 7 px handle, hidden, docked beside the drawing or opened in their own window; buttons at the right end of the tab strip float, place or close the picked tab or fold the pane. **Görünüm → Devre panelleri** brings them back.
- **The rail sets the world apart:** the project's stages (circuit → PCB → code → model → assembly) share the trace; the world, a document of its own, sits below a divider.
- **Montaj explains itself:** with nothing in the project's circuit it says what it does instead of opening an empty world tab, and points to where a world's boards are edited.
- **A world's boards from the circuit page:** an empty circuit page lists the open world's Arduino / ESP32 boards; one click opens a board's circuit and code (a plain board becomes a one-board design whose built-in program is editable code).
- **Attaching to a servo horn or a motor shaft** offers "…ne tak": the part's face lies flat on the output and a long part (an arm) reaches out from it with its end over the pivot; "olduğu yerde sabitle" keeps the old behaviour.

### Fixed

- **Render cables were drawn from the editor's layout** while the parts took their recorded poses, which stretched them into long streaks; and the render did not lay the cables as ropes at all (their pin positions were not passed to it). Renders now simulate the same ropes as the live world, record them at up to 60 frames per second and replay them exactly (sag, tension, a pulled plug).
- **Render scenes could be edited:** parts and cables could be dragged and rewired in a replay. The render scene is read-only now.
- **Render air frames were not saved** in `.rwanalysis` files; they are now, together with the cable ropes.
- **Space and the arrow keys did not type in the render's channel search** (they played / stepped the replay).
- **Stress could read 10¹³ MPa from a 0.02 mm displacement** (a servo case): moving mid-edge nodes onto the true surface next to an already snapped thin element nearly folded some curved (T10) elements. Each curved element is now checked at its corners, mid-edges and centre, and where its Jacobian drops below a fifth of the straight element's, its mid-edge nodes go back onto the straight edges. A part's cells that are not face-connected to its largest piece (edge- or corner-only contacts, separate bits) are left out of the mesh instead of making it singular.
- **A saved render opened with the window's default settings** shown on the left; it now shows the settings it was computed with.
- **Stray scope-probe badges and meter leads on an empty circuit page** (left from another document); probes and leads are cleared when the document changes and not drawn on an empty page.

## [1.4.0] - 2026-11-23

### Added

- **Liquids:** water, sea water, ethanol, milk, sunflower oil, glycerol and honey as particles (position-based fluids) at their real density, viscosity (implicit SPH Laplacian, no-slip walls) and surface tension (continuum surface force). Walls carry the liquid's pressure through density maps from each body's signed distance, and their reaction acts on the body: a glass carries its water's weight, a part in water feels buoyancy. No particle crosses a wall within a step, judged in the turning body's own frame. A liquid object is a box or cylinder the liquid fills when the world starts.
- **Density maps for every rigid body:** the share of the kernel a body's solid covers (Koschier & Bender) is precomputed on a grid in the body's frame, so the inside corner of a glass, its thin wall and a part behind it press on the liquid as they really are; flat analytic faces use the half-space formula, and a flat face behind a nearer one facing the same way adds no pressure.
- **Surface tension as cohesion, wetting by contact angle:** a pair attraction between particles whose strength follows from the liquid's real σ (Kirkwood–Buff: σ = (π n²/8)∫F(r)r⁴dr) holds sheets and streams together at any particle spacing; each wall pulls with Young–Dupré's work of adhesion σ(1 + cos θ), θ from its material (glass 25°, PLA 75°, PTFE 110° …), and carries the reaction. Replaces the continuum surface force, which only acted on resolved surfaces.
- **Air drag on spray:** exposed particles are dragged by the world's air and wind as spheres of their volume (Schiller–Naumann).
- **Anisotropic surface kernels** (Yu & Turk): each particle's kernel follows the shape of its neighbourhood, so a sheet one particle thick and a thin stream are drawn continuous; only particles with fewer than two neighbours are drops.
- **Renders iterate the pressure to a tolerance:** until no particle is compressed by more than 0.2 % ("Hassas", 40 000 particles) or 0.05 % ("Referans", 120 000), instead of a fixed number of iterations.
- **Liquid surface:** smoothed particles, a density field and surface nets give a closed surface with smooth normals; Fresnel reflectance per vertex (clear looking in, sky at grazing angles), a sun highlight, drops for loose particles. Glass and acrylic are drawn after the liquid, so water shows through a glass.
- **Glass and cups:** a "CAM" (soda-lime glass) material, and `tube.bottom` closes a hollow cylinder into a cup; the editor draws hollow tubes and cups.
- **Template "Su bardağı":** an ESP32 tips a glass of water held on a sideways SG90's horn and pours it into a bowl.
- **Renders compute liquids in full:** substeps no longer than the accuracy's physics step (every 0.1 ms on "Hassas"), no sleeping, 6000–30 000 particles and 5–12 pressure iterations by accuracy, recorded at 60 frames per second and replayed with a finer surface; falling drops are drawn with the streak they make between two frames. The live view solves about 3000 particles every 1 ms (a coarser step let a resting column compress by 8 %) and lets still water sleep.
- **A liquid starts with its exact mass, at rest:** a lattice that fills the whole volume (a whole number of layers in height), the mass set to ρ·V, then the liquid poured to rest beforehand (stepped under gravity with its motion damped, at the step it will run with), so the water stands still at its real density, in hydrostatic balance, when the world starts.
- **Open a saved render:** **Kayıtlı render'ı aç…** in the render window replays a `.rwanalysis` without computing it again.

- **Validation page:** **Yardım → 12. Doğrulama** (and the README) lists, for every solver, the theory or benchmark value, what the simulation gives, the deviation and the tolerance its test enforces.
- **Flow benchmarks:** the Schäfer–Turek DFG cylinder benchmarks 2D-1 (Re 20, steady) and 2D-2 (Re 100, periodic shedding) are tests now, along with sphere drag at Re 20 and Re 100 against the standard drag curve.
- **Trust notes on the 3D flow analysis:** each result states the real Reynolds number, cell size, blockage ratio, whether the boundary layer is resolved (y+), whether the force settled, and a grid uncertainty (GCI) from solving the first instant again on a coarser grid.

### Changed

- **CFD at the real Reynolds number:** the lattice-Boltzmann solvers use a regularized collision with Smagorinsky LES, stable down to τ → ½, instead of lowering the Reynolds number to 1000–1500 on the grid. An absorbing layer along the domain edges keeps pressure waves and eddies from reflecting back at the body.
- **Curved walls are second order:** where the true surface is known (from each part's signed distance), the wall sits at its real position on every lattice link (Bouzidi interpolated bounce-back) instead of a staircase of cells.
- **Inflow and far field:** the inlet is a velocity boundary that delivers the exact flow rate (it used to lose about 7 % in a channel), and the sides of the 3D domain are open at ambient pressure rather than held at the free-stream speed, which over-confined the flow. The 3D domain is wider (3.2 body sizes) and the run stops once the force settles.

### Fixed

- **Sphere drag was 30 % high** at Re 20; with the changes above it is within about 10 % of the standard drag curve, and the DFG cylinder drag is within 1 % of the benchmark.
- **ESP32 boards were idle in renders:** the render's runner had no sketch compiler, so an ESP32 world (the gamepad car, the plane, the traffic light) recorded its boards doing nothing; the render now compiles and runs the same firmware as the live world.
- **Help tables with many columns:** a wide table squeezed its last column to one letter per line; column widths are now normalized.

## [1.3.0] - 2026-11-23

### Added

- **What's new in the app:** after an update, the start page shows the new version's headlines once; **Yardım → 11. Yenilikler** keeps every release's news in Turkish, and the documentation site has a news page fed by the same `news.json` as the app.
- **New world templates:** an enclosed rover (electronics in a closed box, motor leads out through a grommet, an ESP32 driving a square), a traffic light with a pedestrian button on an ESP32, and a compound pendulum whose period matches theory within 0.1 %.
- **Cables run where a real wire can:** each cable is routed through free space between its plugs (A* over a grid of cells around them, pulled taut, checked with exact ray tests) and its automatic length is that run plus 10 % and 15 mm. A wire between two parts in a closed fuselage stays in it, one that must leave goes out through a real opening, and a plug whose straight exit is blocked by another part leaves the first clear way.
- **Real cable holes:** a shell wall with a hole is the frame around the opening, in the physics and in the editor's model, with the hole's size, height and offset settable per side (`hole.+x.size`, `hole.+x.z`, `hole.+x.u`).
- **Hollow tubes:** a cylinder with `tube.wall` is a tube open at both ends (a rocket's body tube, a pipe), with a tube's inertia.
- **Consistency audit:** joined parts sunk into each other (which the collision filter used to hide), wires through solids, plugs inside other parts and wires with no way between their plugs are reported when a run starts, in the snapshot log and in the all-templates overlap report.
- **Headless runs simulate the app's cables:** a baked geometry file now holds the cables' pin positions, and the headless runner lays and steps the same ropes as the app.
- **Wiring carried with its body:** a wire at rest inside one rigid body (both plugs on it, nothing else near it) is carried with it while the body's effective gravity stays within 15 % of g, instead of being stepped. Standing on the runway, the PS5 plane's 20 wires now take 0.2 s of computing per simulated second instead of 1.5 s (headless run).
- **Hollow parts collide as their real shape:** a closed model mesh whose convex hull clearly exceeds it (tray, cup, tube, U bracket) is split into convex pieces (greedy plane cuts: along the faces at inside corners for flat-walled parts, the plane that removes the most hull volume for curved insides; separate parts on one side of a cut become separate pieces). Bodies collide piece by piece, rays, cables and the start overlap check use the pieces, and the collision view draws them. Mass and inertia still come from the mesh. New world template **Kap ve bilyeler**.
- **Real part models:** the TT gear motor, the 130 motor and the 9 g micro servos (SG90, MG90S) are drawn from the makers' CAD models (Adafruit, MIT) instead of approximations: real gearbox and case shapes, smooth shading, the turning shaft and servo spline, the servo cable leaving under the output shaft. Heat glow and charring still work on their bodies. Licences in THIRD_PARTY_NOTICES.md.
- **Rocket ignition the way it happens:** firing an e-match lights the igniter (match head + pyrogen, "Ateşleyici barutu"), not the grain. The grain is split into axial zones; each unlit surface soaks up heat from the gas flowing past it (Dittus–Boelter convection from the local port mass flux plus radiation, implicit 1-D conduction into the propellant, checked against the semi-infinite-solid solution within 1 %) and lights at the propellant's ignition temperature, so ignition delay and flame spread from the head end come out of the physics. A bare e-match head fails to light a sugar motor and the log says why.
- **Erosive burning** (Lenoir–Robillard) from each zone's port mass flux: a narrow port burns faster towards the nozzle, the start-up pressure spikes, the core reaches the casing first at the aft end.
- **Real gas and nozzle:** Noble–Abel co-volume for hot products; over-expanded nozzles separate at Schmucker's pressure instead of a fixed 40 % rule; air, propellant and igniter products are mixed as three components.
- **Motor heating:** the casing heats only where the grain does not cover it (closures, burned-through zones), by convection and radiation; the nozzle throat by Bartz's correlation.
- **Launch switches and parachutes:** a switch or button in the world can close on a countdown ("close.at", "open.at") or by hand while running (inspector **Kapat / Aç**); any body can carry a parachute thrown at apogee or at a set time, opening by Knacke's filling model and drawn in the scene.
- **New world template Katı yakıtlı roket:** a 41 mm model rocket on a G-class KNSB motor fired through a 9 V battery, a countdown switch and an e-match; it climbs to ≈ 1 km (270 m/s) and lands at 7 m/s under a 45 cm canopy.

- **Exhaust in the 3D scene:** flames and smoke are drawn inside the scene, so bodies in front hide them (they used to be painted over everything). A layered flame (white-hot core, orange body, pink afterburning envelope) about fifteen exit diameters long; gas leaves at the exit velocity on top of the vessel's velocity and the still air stops it within a few flame lengths, so a climbing rocket leaves an unbroken smoke trail behind. The smoke's thickness follows the condensed share of the products (KNSB 44 % K₂CO₃, KNDX 43 %, aluminised APCP 32 % Al₂O₃, black powder 56 %), now part of the gas readout. The follow camera keeps up with fast objects.
- **Lift-off and jet heating:** the event log reports when the thrust lifts the motor off the pad, with the thrust, the weight of everything joined to it and their ratio. A hot jet heats the first surface it hits (impinging round-jet convection into that part's heat-network node).
- **Rockets in the render:** recordings carry chamber pressure, gas temperature, thrust, mass flow, exit velocity, burning surface, grain surface temperature, grain left, smoke flow and parachute opening; the replay draws flame, smoke and parachute from the recording.

- **Materials, colours and surface finishes in the world:** the material list shows every material the physics knows (plywood, PET, cardboard, copper were missing) with a card of its numbers; colour swatches and a colour box for bodies and imported models; a **Yüzey** (finish) for each part: matte/gloss paint, polished, brushed, anodised, rusty metal, wood grain, carbon twill, kraft, rubber, 3D-print layers. Textures are drawn at real size on any shape (per-triangle planar projection). The finish sets the thermal emissivity used by the heat network: a black anodised block cools clearly faster than a polished one (51 °C against 72 °C after 10 minutes from 150 °C). New template **Malzeme ve yüzey vitrini**.

- **Screwing and gluing as real physics:** a fixed joint gets a fastening (M3 screw + nut, screw into plastic, super glue, epoxy, hot glue, foam tape, zip tie, solder). It sets the joint's break force (per screw, or lap-shear strength over the bonded area), its heat path in the heat network (glue conducts far better than screw heads) and its softening with temperature: a hot-glued 1 kg block holds at 25 °C and falls at 95 °C.
- **User guide:** a sensor table (what each sensor measures, how it is computed, its errors), how the camera pipeline works, and the fastenings with their numbers.

- **Movable panels in the world:** the palette, the inspector and every bottom tab (events, serial monitor, cables, RF, sensors) can sit on the left or right (stacked, with resizable heights and column widths), be a bottom tab, or tear off into their own window by dragging their header or tab. Dragging a panel window over the page shows **Sol / Sağ / Alt** guides and highlights where it will dock; closing the window docks it back. Each panel has a menu (left, right, bottom, own window, move up / down, hidden), and the toolbar's **Panel düzeni** button and **Görünüm → Dünya panelleri** list every panel, swap the sides, dock all windows and reset the layout. The layout, including window positions, is kept between sessions (%APPDATA%\RobotWinStudio\layout.json).

- **New world template PS5 kumandalı uçak:** a 1 m trainer on a model airfield, flown from a DualSense: balsa pod, carbon boom, 4° dihedral Clark Y wing with ailerons, elevator and rudder on pushrods, 2212 1000KV + 8×4.5" prop + 20 A ESC + 3S LiPo (402 g, CG at 30 % from the battery position), tricycle gear with free-rolling wheels. Its ESP32 + MPU-6050 flight controller (Bluepad32) arms on Options, flies in a self-levelling or manual mode, mixes a little nose-down with low throttle so a power-off glide keeps its speed, and on a lost Bluetooth link (≈ 90 m) cuts the motor and circles down. It takes off from the runway, turns, glides at ≈ 7 m/s (6:1) and lands on its wheels, tested end to end through the controller.
- **Live inputs are replayed in the render:** while recording, every controller report (when it changes), controller power and pairing, serial text, USB plugging, IR key, conveyor command, light-curtain test and launch switch is stored with its simulation time; the render feeds them back at exactly those instants (each step is split at the input), and its log says how many were replayed.
- **Pilot's-eye camera:** a world with a controller and an aircraft opens the game view from where the pilot stands, zooming with the distance. The chase camera sits back by the size of what it follows (a 1 m plane, not 45 cm behind a car's chassis).
- **Hinge friction:** a hinge or slider joint can carry Coulomb friction (a hub on an axle, a bushing).
- **Impacts name what was hit** ("· zemin", or the other part).
- **Welded parts are one rigid body:** parts joined by unbreakable fixed bonds (no break force or fastening set, shell walls, screwed lids) are solved as a single rigid body: summed mass, centre of mass and full inertia tensor, the group's motion from the parts' momentum, each part still colliding with its own shape. A chain through a light part no longer bends (a 6 g wire leg let a plane's wheel toe in by 1–4°; now under 0.03°). The welds' loads come from inverse dynamics, so recordings and FEM loads keep them (a weld under a 0.5 kg part carries 4.905 N, a cantilever's weld moment matches m·g·d within 0.1 %). Breakable fastenings stay joints. Fewer solver bodies also make worlds faster (the plane runs at real time, the test suite 40 % quicker).
- **The PS5 plane's wheels sit on their wire legs** again, now that welded legs hold their alignment. Its aileron servo moved from on top of the wing into the pod under it (its horn was caught in the dihedral wing's convex collision hull, which locked the ailerons at −8.5° in the app), and a screwed balsa hatch closes the pod under the wing.
- **Snapshot log:** `ROBOTWIN_SNAPSHOT_LOG=<file>` writes a snapshot run's events, serial output, body states and the app's collision geometry (as a baked-geometry file a headless run can load), so a world can be checked without a screen.

### Changed

- **The world inspector shows what an object has, not everything it could have:** position, physics, size and look come first; a pressure vessel, a parachute, its own air settings and behaviours appear only when they are on, each with **Kaldır** (remove), and the rest are under **Özellik ekle ▾** (add feature). Every box, cylinder and sphere used to open with a gas-vessel switch and a parachute field.
- **Automatic cable length** is the routed run plus 10 % and 15 mm (it was the straight pin distance plus 15 % and 30 mm).

### Fixed

- **Wires passed through walls and thin parts:** a wire particle could move through a 2 mm wall in one step and be pushed out on the far side, and a segment between two particles could cut through a wall's edge. Moves are now checked continuously in each body's frame, and segments are capsules pushed out the shortest way. The PS5 plane had 9 wires leaving its pod through the side walls; it has none, from take-off to landing.
- **Wires trailed moving parts by a step:** ropes were stepped before the bodies, so their contacts used where the parts had been (12 mm behind at 12 m/s); they are now stepped after them.
- **Wires were damped against the world:** the jacket's damping acted on absolute velocity, an invented drag of about 4 g on the wiring of a plane at 12 m/s. It now acts on motion relative to the plugged ends, and air drag on a wire is computed from the air around it.
- **Rocket fins stood tangent to the body** (a pinwheel) in both rocket templates, half their thickness inside the tube; they now stand straight out. The solid rocket's fins are turned 60° so none stands over the e-match on the pad.
- **The solid rocket's motor was inside a solid body tube:** the tube is now hollow, and two plywood centering rings hold the motor in its bore.
- **Parts sunk into each other in the templates:** dev boards and modules had their header pins in the floor or plate under them (they now stand on them); the PS5 plane's battery lay in its aileron servo, the servo floated on nothing (it now sits on a balsa block) and its battery leads ended inside the rudder servo; the quad's flight board lay in its battery; the two arms of both X frames passed through each other (one is now two half arms); the sensor rover's LiDAR mast stood in its Uno and battery; pushrods ran into the stabiliser; the line follower's battery lay in its Uno; the SCARA's flange disc reached 1 mm into its gripper.
- **Servo plugs hung outside the aircraft:** the SG90 lead ends 8 cm behind the servo, behind the pod's rear wall, so the wires to them left the pod. The PS5 plane's and the motor glider's pods are now long enough to hold them, with the boom and pushrods through a 16 mm hole in the rear bulkhead and the motor leads through a 7 mm hole beside the motor.
- **Multi-line text boxes** (serial monitor) filled from the middle of their height; their text now starts at the top.
- **Propeller thrust in forward flight:** world propellers used the static k_T·ω² at any airspeed (the physics guide already described the advance-ratio model, the code did not use it). Thrust and torque now follow J = V/(n·D): an 8×4.5" prop that pushes 4.2 N on the ground gives ≈ 1.3 N at 12 m/s and brakes beyond its pitch speed.
- **A propeller dragged like a solid disc** (≈ 1.1 N at 8 m/s on an 8" prop). A spinning prop's forces are its thrust model; standing still, only its blades' area (solidity ≈ 0.1) meets the flow, edgewise their thickness.
- **Parts inside a closed shell had air drag** (a battery inside a fuselage); they are now out of the airflow.
- **Part masses differed between the app and the headless runner:** the app gave a 2212 motor 5 g instead of 50 g (and other parts generic masses), which moved an aircraft's balance by 15 % of the chord. Both now use the datasheet masses.
- **Cables shorter than the distance between their pins** started stretched and yanked the parts they joined: the glider, the motor glider, the ESP32 quad and the electronics box had wires measured between part centres, too short for the pins of the app's part models. Their lengths are now left to the pins (+ slack), and a run warns about any cable that could not be plugged in.
- **Pushrods ran into the servo models** in the glider and motor glider; they now stop at the pod's rear wall.
- **The model rocket stood inside its launch pad:** its swept fins' tips reach 3.8 mm below their roots; it now stands on them.
- **The game view and the camera sensors did not draw the terrain** (grass, runway, roads); they do, with a sky backdrop outdoors.
- **Cables pushed on nothing:** a wire resting against a body was pushed out of it while the body felt no reaction; a resting quad was lifted off the floor by its own wiring before its motors ran. The body now gets the reaction. A wire whose plugs both sit on one rigid body still rests on it, but those forces are internal and not applied; propeller discs are swept volumes, not solids, for wires. New cables are laid with their slack bowed whichever way runs through the fewest parts, so wiring inside a closed fuselage stays inside.
- **2D flow missed thin parts:** a 3 mm plate lying level on a 3.3 mm lattice fell between the cell centres and showed no drag; cells are now solid when the part fills any of 3 × 3 points across them.
- **Mecanum roller traction** followed the contact force of the last step, which flickers between 0 and 6 N as the rollers' facets touch; it now uses the load smoothed over 20 ms.
- **The chase camera could not stay selected:** the game view switched back to its default view whenever the scene refreshed.

- **Drag of model meshes:** a mesh body's drag came from its bounding box (an ogive nose was a flat 41 mm plate); it now comes from its faces (0.8·sin²θ on faces meeting the flow, Hoerner; base suction behind).
- **Joined parts' drag on slender bodies:** face coverage was sampled over the bounding sphere and missed a long thin tube entirely; it is now sampled over each body's own extent.
- **Cross-flow drag:** drag grew with |sin α| of the angle of attack; each body axis now sees the dynamic pressure of its own velocity component (Allen & Perkins), so cross-flow drag grows with sin²α.
- **Rocket fins** in the water-rocket template were mounted tangent to the bottle; they now stand straight out as single panels.
- The igniter of a motor was not found when an e-match was joined to it.

## [1.2.0] - 2026-11-22

The modeller gets Blender's cutting and selection tools, and every message is in one language.

### Added

- **Modeller cutting tools:** knife (K) along a line drawn on the screen, with cut / delete one side and capping; slice an object into two closed pieces (K in object mode); loop cut (Ctrl+R) with a live preview; bevel (Ctrl+B) with corner patches; solidify (even wall thickness, hollow shells); extrude from edges in vertex/edge mode; loop select (Alt+click), select linked (L) and grow/shrink (Ctrl+Numpad +/−). Boolean union/difference/intersection and slicing have their own section in the properties panel.

### Changed

- One language per message: KiCad import log, solver errors, ESP32 events and loader errors, mesh import errors and API messages are in Turkish only (no more "Türkçe / English" pairs).
- Part names: "Erkek Header 1×N" and "Çekirdeksiz Motor" instead of English names; the board's reset button reads "Sıfırla".

## [1.1.0] - 2026-11-21

One shared physics now drives everything in the world: circuits, rigid bodies, heat, air, gas and radio feed each other, so robots, aircraft, rockets and industrial cells are built from the same parts and behave as they would in reality.

### Added

- **Help, news and updates:** in-app Help page (F1) with the user guide, News, one-click automatic updates that keep projects and settings, older versions list, performance indicator.
- **Real Montaj:** the assembly is built from the circuit itself. Parts on the PCB stay on the board; every other part (motor, servo, battery, sensor, module) arrives as its real 3D part, nets become real jumper cables. No ready-made vehicle; build chassis, wheels and shells with the world tools.
- **Model-shaped rigid bodies:** every part, board and model collides with the convex outline of its own 3D model (round things roll, wheels stand on their tyres). *Görünüm → Çarpışma şekilleri* shows the shapes the physics uses.
- **Flight:** drawn wings, tails and fins generate lift and drag with stall, validated stability and elevator authority; balsa glider, motor glider UAV and ESP32 quadcopter flown by its own firmware (brushless outrunners, ESC current limit, IMU that feels the motion).
- **Gas physics and rockets:** ideal-gas pressure vessels with species mixing, isentropic choked and supersonic nozzle flow, water expulsion, solid propellant grains (KNSB, KNDX, APCP) with Saint-Robert burn rate and BATES geometry, wall heating, burst at the material's hoop-stress limit; ISA atmosphere. Templates: water rocket and sugar-motor test stand.
- **Air drag of assemblies:** joined parts shield each other's faces, base drag and skin friction are separated, oblique flow blends by projected area.
- **USB-C lithium batteries:** 18650, 2S pack, AA and 4×AA Li-ion with protection board (over-charge, over-discharge, over-current), CC/CV charger from a world USB cable, regulated 1.5 V output.
- **PS5-controlled car rebuilt:** designed controller board (schematic, routed PCB, firmware), real 2WD kit geometry with mirrored motors, battery powered, drives straight and turns from the DualSense over Bluetooth.
- **Heat:** thermal network across parts and boards, heat changes materials (softening, melting, thermal stress), honest motor and battery heating, thermal camera on the 3D models, board heat map in the render.
- **Analysis and render:** offline analysis runs with full recording, FEM stress with mesh convergence and `.inp` export, 2D CFD, 3D streamlines, replay of a recorded run with stress on every part, follow camera, electrical render.
- **Honest measurements:** per-part variance of cloned parts, uncertainty on readings, Monte Carlo bands, bench comparison against an imported measured log.
- **Mechanics:** hollow shells with screwed lids and fit check, X-ray and exploded views, pushrod linkages, brushless ESC, ready wheels and propellers, rope cables, tyre slip, tipping, motors that stall under load, interpenetration prevented inside assemblies.
- **Industrial robot cell:** 6-axis arm, SCARA, inverse kinematics, gripper, vacuum, conveyor, teach pendant.
- **Sensors and cameras:** real line sensing, cameras, LiDAR, IMU, GPS; Scene + Game views.
- **Terrain and world editor:** sculpting, surface layers, real ground mechanics; free lights and 3D positional sound.
- **ESP32:** real SPI bus to wired devices (MCP3008, BMP280), real task watchdog.
- **Cables:** length check (enough / taut / too short), rope tension, voltage drop, plug contact resistance, cable diagnostics.
- **PCB:** hand routing from a ratsnest line (F finishes to the target pad), live lengths, the board's real outline in 3D and in the world, ready board shapes.
- **Automation API and SDK:** Python/Gymnasium, ROS 2 bridge, headless runs (with radio between boards); environment SDK with atmosphere, wind, force fields and physics scripts.
- **External libraries** and a part creator.
- **Documentation site:** every page fully Turkish or fully English, physics and maths reference with formulas, API and physics-script how-tos with code, drawn diagrams and tables.

### Changed

- Installers: `RobotWinStudio-Setup-<version>.exe` is the full package and carries .NET; `RobotWinStudio-Setup-<version>-lite.exe` needs the .NET 9 Desktop Runtime.
- Core simulation messages (battery, regulator, ESP32 build errors) are in Turkish like the rest of the interface.
- Text contrast raised to WCAG level, no text under 12 px, labels wrap (see `docs/ACCESSIBILITY.md`).
- Wood density corrected; balsa, carbon and lead materials added; PET, cardboard and acrylic strength data.

### Fixed

- Serial monitor freeze and a model lost when closing the app.
- Solver collapse when a battery ran completely empty.
- Wheels and other turned bodies drawn lying flat in the render.

## [1.0.0] - 2026-11-20

RobotWin Studio is now a single Windows desktop application: C# .NET 9 WPF (`Studio/RobotWin.Studio`) on top of the C# simulation core `CoreSim`. The Turkish user interface follows one "design-to-real line": Devre → PCB → Kod → Modelle → Montaj → Dünya.

### Added

- **Circuit (Devre):** schematic editor with a searchable library of 256 real parts, wires, labels, power symbols, annotation, find/replace, ERC; live analog simulation (MNA + Newton-Raphson, adaptive steps, thermal model and part damage); BOM, KiCad netlist, SPICE, PNG/SVG and print/PDF export.
- **Instruments:** 4-channel oscilloscope (triggers, math, cursors, automatic measurements, probe loading), 6000-count true-RMS multimeter with fused current inputs, CV/CC bench supply with OVP, component tolerances, Monte Carlo analysis with histogram, fault diagnosis with "why did it burn" explanations.
- **PCB:** 2/4/6-layer boards, stackup and net classes, copper zones, interactive router, auto-placement and auto-routing, DRC, Gerber RS-274X X2 + Excellon + BOM + CPL, KiCad 8 project export, footprint editor, board-as-module parts, 3D board view.
- **Code (Kod):** per-board firmware; Arduino Uno/Nano sketches built with `arduino-cli` and run on a cycle-counted ATmega328P emulator (timers/PWM, USART, ADC, EEPROM, watchdog, TWI with SSD1306, PCF8574 LCD and MPU-6050 models); ESP32 sketches compiled natively with MSVC and co-simulated at Arduino-API level (GPIO, ADC, LEDC, DAC, timers, FreeRTOS, Wire, Servo, simulated Wi-Fi, BLE subset, Bluepad32); serial monitor.
- **Model (Modelle):** Blender-like 3D modeller with primitives, profiles and revolves, edit mode (extrude, inset, subdivide, merge…), CSG union/difference/intersection, mirror and arrays, parametric robot parts, materials and mass, STL/OBJ/PLY import/export, `.rwmodel` files and *send to world*.
- **Assembly (Montaj):** automatic rover or X-quadcopter from the circuit, arenas, cameras, drone controls, fault experiments, `.rwsim` recordings.
- **World (Dünya):** open sandbox with designed boards, catalogue parts, mechanical bodies and imported meshes; real cables (AWG, copper resistance, burn-out); fixed, servo-horn/motor-shaft, hinge and slider joints; rigid-body physics with gravity presets, materials and collisions, coupled both ways to servos and DC motors; ultrasonic sensors that ray-cast the scene; live I²C displays; 2.4 GHz radio (BLE, Wi-Fi, Bluetooth gamepads), IR remotes, PC + USB power and serial; DualSense/DualShock 4/Xbox and on-screen gamepads; scene tree, cable measurements, RF panel; simulation sounds; eight world templates.
- **Library:** custom parts (`.rwpart`), part libraries (`.rwlib`), custom 3D models per part, KiCad symbol and footprint import.
- **Shell:** menu bar, document tabs with drag reordering, split view, detachable pages, focus mode, full screen, command palette (Ctrl+K), autosave backups, recent files, UI scale, splash screen, file-type registration.
- **File formats:** versioned ZIP containers `.rwproj`, `.rwworld`, `.rwpart`, `.rwlib`, `.rwsim`, `.rwmodel`, `.rwfp`.
- **Command line:** `--selftest`, `--snapshot`, `--gallery`, `--export-logo`, `--register-file-types` / `--unregister-file-types`.
- **Installer:** native `RobotWinStudio-Setup-<version>.exe` (wizard, `/S`, `/ALLUSERS`, `/PORTABLE /D=<dir>`; offers the .NET 9 Desktop Runtime) built by `tools/build-installer.ps1`; the release workflow builds it on `v*` tags.
- **Docs:** new README, developer documentation and a Turkish step-by-step user guide in `docs/kullanim/`.

### Changed

- CI builds `RobotWinStudio.sln` with .NET 9, runs the unit tests and the app self-test.
- Documentation of the 1.x design moved to `docs/archive/`.

### Removed

- The Unity front end (`RobotWin/` Unity project) and its documentation. `NativeEngine/` and `FirmwareEngine/` (C++) remain in the repository as legacy code and are not used by the app.

## [0.x] - Unity-based design (history)

### Added

- 🧪 Physics deep validation suite with energy conservation tests
- 🧪 Sensor internal validation suite for line sensor algorithms
- ⚡ SIMD optimization with AVX2 instructions
- ⚡ Fast floating-point math (`/fp:fast`) for performance
- ⚡ Precomputed effective mass in contact resolution
- ⚡ Angular inertia integration in collision response
- 📚 Comprehensive README with badges and feature highlights
- 📚 Enhanced CONTRIBUTING.md with detailed guidelines
- 📚 Expanded CODE_OF_CONDUCT.md with clear expectations
- 📚 Improved SECURITY.md with vulnerability reporting process
- 🔧 Updated .gitignore with comprehensive exclusion patterns

### Fixed

- 🐛 Physics damping time-step dependency (now `damping * dt`)
- 🐛 Ground friction model (switched to proper Coulomb friction)
- 🐛 Restitution coefficient application in collisions
- 🐛 Energy conservation in elastic collisions (now exact)
- 🐛 Sensor test compilation conflicts with Arduino macros

### Changed

- 🎨 Improved build system with Release configuration defaults
- 🎨 Cleaned up binary artifacts from repository
- 🎨 Enhanced PR template with comprehensive checklist

## [0.1.0] - Initial Release

### Added

- 🎯 CoreSim deterministic scheduler (.NET 8)
- 🎯 NativeEngine C++ physics simulation
- 🎯 FirmwareEngine ATmega328P emulation
- 🎯 Unity 6 LTS visualization frontend
- 🎯 Named pipe IPC between components
- 🎯 Basic rigid body physics (sphere and box collision)
- 🎯 Vehicle simulation with Pacejka tire model
- 🎯 Ground plane support with friction
- 🎯 Distance constraints (springs/ropes)
- 🎯 Circuit simulation (nodal solver)
- 🎯 Line sensor array implementation

---

### Emoji Key

- ✨ New feature
- 🐛 Bug fix
- ⚡ Performance improvement
- 📚 Documentation
- 🎨 Code style/refactoring
- 🧪 Tests
- 🔧 Configuration
- 🚀 Deployment
- 💥 Breaking change
- 🔒 Security fix
- 🎯 Core functionality
