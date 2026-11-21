# 2. Circuit

The **Devre** (circuit) page (Ctrl+1) is where you draw schematics and simulate them live. The simulation runs on a real analog circuit solver: voltages, currents, heating and part damage are computed.

![Circuit page: line-follower schematic and oscilloscope](../../images/schematic.png)

The parts of the page:

- **Left:** a searchable part list.
- **Centre:** the schematic canvas and the toolbar above it (**Simüle et** (simulate) button, speed selector).
- **Right:** the properties panel: the selected part, project info, part tolerance, shortcuts.
- **Bottom:** measurement and info tabs: **Osiloskop** (oscilloscope), **Multimetre** (multimeter), **Arıza tespiti** (fault finding), **Monte Carlo**, **Kart pinleri** (board pins), **Seri monitör** (serial monitor), **Olaylar** (events).

## A new circuit

1. **Dosya → Yeni devre / kart** (File → new circuit / board, Ctrl+N).
2. The empty canvas says: pick a part on the left and click on the canvas; start at a pin and click another pin to draw a wire.

## Adding parts

1. Type the part's name in the search box on the left (e.g. `direnç 10k` (resistor 10k), `LED`, `LM358`, `arduino`).
2. Pick it from the list and click on the canvas to place it.
3. The **A** key also opens the searchable part list.
4. The **P** key adds a power symbol (GND, +5V…).

The library has passives, diodes and LEDs, transistors and MOSFETs, op-amps, 74HC logic ICs, regulators, batteries and power supplies, switches and relays, DC motors and servos, sensors, displays and I²C modules, motor drivers, Arduino Uno / Nano, ESP32 boards and wireless devices.

## Wiring

1. Click a pin; the wire starts.
2. Click where you want corners.
3. Click the target pin; the wire ends. A double-click or **Esc** also ends it.

Helper keys:

| Key | Function |
| --- | --- |
| W | Wire tool |
| / | Corner direction (horizontal first / vertical first) |
| Shift+Space | Wire mode: 90° → 45° → free |
| Backspace | Undo the last corner |
| L / Ctrl+L | Net label / global label |
| Q | No-connect (NC) marker |

Labels with the same name are connected; the name `GND` means ground. Right-clicking a wire offers **Böl** (split), **segment sil** (delete segment) and **etiket** (label).

## Editing

| Key | Function |
| --- | --- |
| M / G | Move / drag (with G the wires stretch) |
| R / Shift+R | Rotate |
| X / Y | Mirror horizontally / vertically |
| Ctrl+D | Duplicate |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste (also between tabs) |
| Del | Delete |
| E | Properties (also with several parts selected) |
| V, F2 or double-click | Edit the value in place (e.g. `4k7`) |
| D | The part's datasheet |
| Ctrl+F / Ctrl+H | Find / find and replace (`*` and `?` wildcards) |
| F1 or ? | All shortcuts |

- **Numaralandır** (annotate): numbers part names (R1, R2, C1…) by row or column; the names on the PCB and in the assembly follow.
- **Elektriksel kural denetimi (ERC)** (electrical rule check): finds unconnected pins, single-pin nets, output conflicts and unpowered power inputs.
- **PCB kılıfı** (PCB footprint): choose the footprint the part uses on the PCB.

## Simulation

1. Press **Simüle et** (simulate) on the toolbar, or **F5**.
2. While it runs:
   - Click buttons and switches.
   - Turn potentiometers with the mouse wheel.
   - Click a wire: the oscilloscope starts measuring there.
3. **F5** again pauses, **Shift+F5** stops. **1 ms ilerlet** (step 1 ms) advances one step.
4. Speed: **0.01×**, **0.1×**, **Gerçek zaman** (real time), **5×**, **20×**.

The status bar shows simulation time. If the PC cannot keep up with real time it shows "gerçek zamanın %…'i" (…% of real time).

### Part damage

A circuit that would burn in real life burns here too: an LED without a resistor, a motor drawing too much current, an electrolytic capacitor fitted backwards… A burned part is marked and written to the **Olaylar** (events) tab.

- In the **Arıza tespiti** (fault finding) tab, **Neden?** (why?) explains why the part burned.
- After fixing the circuit, renew the parts with **Hasarlı parçaları onar** (repair damaged parts).
- To switch damage off entirely, turn off **Simülasyon → Parça hasarı (yanma, sigorta)** (Simulation → part damage: burning, fuses).

### Board pins

You can drive the pins of an Arduino or ESP32 that has no code by hand in the **Kart pinleri** (board pins) tab: input, pull-up, LOW, HIGH or PWM.

## Exporting

From the circuit page you can export:

- Bill of materials (BOM, `.csv`)
- KiCad netlist (`.net`)
- SPICE netlist (`.cir`)
- Image (PNG / SVG)
- Print / PDF (Ctrl+P)

## Next step

When your circuit is ready, go to the **PCB** page (Ctrl+2) or write the board's code (Ctrl+3). The instruments are covered in [8. Instruments](08-instruments.md). How the solver works: [Circuit solver](../physics/01-circuit.md).

---

[← Previous: 1. Getting started](01-getting-started.md) · [Contents](README.md) · [Next: 3. PCB →](03-pcb.md)
