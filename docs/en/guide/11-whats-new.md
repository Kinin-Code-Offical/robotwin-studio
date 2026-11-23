# 11. What's new

What each release brought, changed and fixed. After an update, the first start shows this release's news on the start page; this page is always under **Yardım → 11. Yenilikler** (Help). Every technical detail: [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).

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
