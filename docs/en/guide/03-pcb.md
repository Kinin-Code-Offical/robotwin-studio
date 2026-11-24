# 3. PCB

The **PCB** page (Ctrl+2) turns your circuit into a printed circuit board: you place the parts, draw the copper tracks, check the rules and produce the files to send for manufacturing.

![PCB page](../../images/en/pcb.png)

## From schematic to board

1. Draw the circuit on the **Circuit** page.
2. Go to the **PCB** page. The schematic's parts come onto the board; pins that must be connected are shown with thin lines (ratsnest).
3. If you change the schematic later, press **Update**. The changes are carried over and your placement is kept.
4. **Add part** puts a part from the library straight onto the board; it is added to the schematic too.

## Board settings

**Board settings** opens:

- Copper layer count: any even number from **2** to **32** (top copper, inner 1 … inner 30, bottom copper), as in KiCad. When the stack does not fit the board thickness, the window gives the least thickness it needs: make the board thicker or the prepreg thinner.
- Board thickness, copper weights, dielectric.
- Blind / buried vias.
- Net classes (track width and clearance) and manufacturing rules.

Draw the board edge as a polygon with the **Board edge** tool on the left toolbar, and add **Mounting hole**s.

## Placement

- Drag parts with the mouse. **R** rotates, **F** flips a part to the bottom / top side, **L** locks it.
- **Place** does an automatic placement by the connections; locked parts stay where they are.

## Routing

### By hand (interactive)

1. Press **X** (track tool).
2. Click a pad and move the mouse towards the target. The track walks around obstacles and keeps the clearance rules.
3. Click to fix intermediate points; clicking the target pad finishes it.

| Key | Function |
| --- | --- |
| V | Place a via and change layer |
| PgUp / PgDn | Go to the top / bottom layer through a via |
| / | Angle direction (diagonal first / straight first) |
| Space | 45° / 90° mode |
| W / Shift+W | Track width: next / previous |
| Backspace | Undo the last segment |
| Enter or double-click | Finish |
| Esc | Cancel |

Drag a drawn track with **G**; its connected ends follow.

### Automatic

**Route** draws every connection automatically: it searches all layers, keeps the net classes' widths and clearances, and drops vias to inner planes when needed.

![Board settings: a 12-layer stack](../../images/en/pcb-stackup.png)

## Copper pours

1. Draw a pour area with **Z**, or use the ready **+ GND (top)**. Boards with 4 or more layers also have **+ GND plane (Inner 1)**.
2. Fill the areas with **Fill**. **B** fills, **Ctrl+B** empties the fill.

Filling follows the clearance, thermal relief, priority and island-removal rules.

## Design rule check (DRC)

**DRC** checks the board:

- Net-class clearances, track width, hole and annular ring sizes
- Distance to the board edge
- Courtyard overlaps, silkscreen on pads
- Unconnected nets, dangling tracks and vias
- Out-of-date pours
- Track current capacity

Errors appear in a list; clicking one shows the spot on the board.

## 3D view

**3D** shows the board with its parts in 3D. While simulating, LEDs light up and hot parts glow.

## Manufacturing files

| Button | What it produces |
| --- | --- |
| **Manufacturing** | `<name>-gerber.zip`: Gerber files of every copper layer, solder mask, paste, silkscreen, board edge, PTH / NPTH / blind drill (Excellon) files, BOM and pick-and-place file (CPL) |
| **Export to KiCad** | `<name>-kicad.zip`: a KiCad 8 project (`.kicad_pro`, `.kicad_sch`, `.kicad_pcb`), netlist, symbol and footprint libraries, SPICE and Gerber |

Upload the Gerber ZIP straight to a board maker.

## Footprints and modules

- **Footprint:** the footprint editor. Draw SMD and THT pads, silkscreen, courtyard and fabrication layers and save to your user library.
- **Make module**: saves this board to the library as one part. It is added to other circuits as a single part, and its inside is still fully simulated.

## View

| Key | Function |
| --- | --- |
| Home or F | Fit |
| + / − / wheel | Zoom in / out |
| Drag with the middle or right button | Pan |
| PgUp / PgDn | Active layer |
| H | Show only the active layer / show all |
| The key left of 1 | Highlight the net under the cursor |
| Ctrl+Shift+M | Measure |
| Ctrl+Shift+H | Dimension line |
| T | Silkscreen text |
| F1 or ? | All PCB shortcuts |

---

[← Previous: 2. Circuit](02-circuit.md) · [Contents](README.md) · [Next: 4. Code →](04-code.md)
