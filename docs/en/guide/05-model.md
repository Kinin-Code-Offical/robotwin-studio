# 5. Model

The **Modelle** (model) page (Ctrl+4) is where you model your robot's mechanical parts in 3D (chassis, bracket, box, wheel hub…). It works much like Blender: object mode, edit mode and quick keyboard transforms. Send a finished part to the world with one button.

![Model page](../../images/modeler.png)

## Navigation

| Mouse / key | Function |
| --- | --- |
| Drag with the middle button | Orbit |
| Shift + drag with the middle button | Pan |
| Wheel | Zoom in / out |
| Numpad 1 / 3 / 7 | Front / right / top view |
| Numpad 5 | Perspective / orthographic |
| Home | Fit everything |
| Numpad . | Focus on the selection |

## Adding objects

With **Ekle** (add) or **Shift+A**:

- **Temel şekiller** (primitives): box, cylinder, cone, sphere, torus, tube, wedge, perforated plate.
- **Profil** (profile): extrude a rectangle, circle, polygon, channel or a free sketch to a thickness.
- **Döndürme** (revolve): turn a profile around an axis into a solid.
- **Robot parçaları** (robot parts): 2WD chassis plate, SG90 servo bracket, L bracket, wheel hub, enclosure + lid. Their dimensions can be changed later.

## Move, rotate, scale

1. Click an object to select it (**A** selects all, **Alt+A** clears the selection).
2. **G** move, **R** rotate, **S** scale.
3. Optionally lock to an axis with **X**, **Y** or **Z** and type a number (e.g. `G` `X` `20` → 20 mm along X).
4. **Enter** confirms, **Esc** cancels.

You can also drag the on-screen move / rotate / scale handles. With **Yapış** (snap) on it snaps to the grid; holding **Ctrl** turns snapping off for the moment.

## Boolean (CSG) operations

To drill holes, cut pockets or join parts:

1. Select the "tool" objects first and the main object last (the last selected is the active one; Shift+click adds to the selection).
2. Pick in the **Kes, kırp, birleştir** (cut, trim, join) section of the properties panel (or on the toolbar or the right-click menu):
   - **Fark (kırp)** (difference, trim): carves the volume of the overlapping tools out of the active object (hole, pocket, trim).
   - **Birleşim** (union): joins the tools with the active object into one closed body.
   - **Kesişim** (intersection): keeps only the shared volume.

The tool objects are removed afterwards; the result is again a closed (watertight) body.

## Slicing

To split an object in two, select it and press **K** (or **Dilimle**, slice). Click the start and the end of the cut line on the screen: the object is cut along that line, in the viewing direction. Both pieces are capped with the section, so both are closed bodies. Holes in the section stay open (a sliced tube shows a ring). Then select one piece and move it with **G** or turn it with **R**.

![A sliced tube, a bevelled block and a solidified tray](../../images/modeler-cut.png)

Other object operations: **Ctrl+J** join, mirror copy (X / Y / Z), linear and circular array, **Ctrl+P** set parent, **Alt+P** clear parent, **Shift+D** duplicate, **H** hide, **Alt+H** show all.

## Edit mode

Enter edit mode with **Tab**. **1**, **2**, **3** select vertices, edges and faces.

| Key | Function |
| --- | --- |
| E | Extrude: pulls faces out along their normal; in vertex/edge mode new faces grow from the selected edges and move with the mouse |
| I | Inset |
| K | Knife: cuts along a line drawn on the screen (only the selected faces when there is a selection). **C** switches the mode: cut / delete the right side / delete the left side; the opening of the deleted side is capped |
| Ctrl+R | Loop cut: hover over an edge and new edge loops are added along its ring of quads; the wheel or +/− changes the number of cuts |
| Ctrl+B | Bevel: flat chamfer faces in place of the selected edges; set the width with the mouse or type mm. A corner face closes the corner where three chamfers meet |
| F | Fill a face |
| M | Merge |
| P | Separate the selection into its own object |
| X or Del | Delete |
| Shift+N | Recalculate normals |

**Kalınlık ver** (solidify) gives the surface a wall thickness inward: an open surface becomes a thick wall, a closed body becomes a hollow shell (for example a box without its top face → a tray). The thickness stays even at the corners.

**Alt böl** (subdivide), **Yumuşat** (smooth, Catmull–Clark), merge by distance, triangulate and flip normals are in the panel and the right-click menu.

![Edit mode: loop cuts and a solidified tray](../../images/modeler-cut-edit.png)

## Material and mass

Give each object a material (PLA, PETG, ABS, nylon, acrylic, plywood, MDF, FR4, aluminium, steel, carbon, rubber) and an infill ratio (for 3D printing). The mass is computed from the volume; it is used when you send the part to the world.

## View modes

- **Katı** (solid, Z), **Tel** (wireframe, Shift+Z), **X-ray** (Alt+Z), **Kenarlar** (edges).
- **Ölç** (measure) tool: click two points to see the distance (snaps to vertices).

## Saving and exporting

- The model is saved as a `.rwmodel` file (Ctrl+S).
- **İçe aktar** (import): STL, OBJ, PLY.
- **Dışa aktar** (export): STL (mm), OBJ, PLY. Open (not closed) meshes are warned about. Use STL for 3D printers.

## Send to the world

**Dünyaya gönder** (send to world) adds the selected objects (or the whole model when nothing is selected) to the active world as one object with its computed mass. Without an open world a new one is made. Then fix the part to motors, servos and other parts in the world: [7. World](07-world.md). Its collision body is the part's own shape ([Rigid bodies and contact](../physics/02-mechanics.md)).

---

[← Previous: 4. Code](04-code.md) · [Contents](README.md) · [Next: 6. Assembly →](06-assembly.md)
