# 11. What's new

What each release brought, changed and fixed. After an update, the first start shows this release's news on the start page; this page is always under **Yardım → 11. Yenilikler** (Help). Every technical detail: [CHANGELOG.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/CHANGELOG.md).

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
