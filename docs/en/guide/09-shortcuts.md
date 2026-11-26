# 9. Keyboard shortcuts

The app has its own shortcut lists too: the **Ctrl+K** command palette shows every command with its shortcut; on the Circuit and PCB pages **F1** or **?** opens that page's list; **Settings → Keyboard shortcuts** has a list as well.

The keys on this page are the defaults. In **Settings → Keyboard shortcuts** you can change the window's shortcuts and the keys of the Circuit, PCB, World, Code and Part pages.

- Page keys only work on their own page; on the Circuit, PCB, World and Part pages they can be single letters. On the Code page they also work while you type, so they need Ctrl/Alt/Shift or an F key.
- When you move a tool to another key, its old key does nothing any more.
- The short list in the schematic page's right panel shows the keys you chose.

## General

| Shortcut | Function |
| --- | --- |
| Ctrl+N | New circuit / board |
| Ctrl+Shift+N | New world |
| Ctrl+O | Open |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save as |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+K | Command palette: search everything |
| Ctrl+, | Settings |
| Ctrl+0 | Home |
| Ctrl+1 | Circuit |
| Ctrl+2 | PCB |
| Ctrl+3 | Code |
| Ctrl+4 | Part |
| Ctrl+5 | Assembly |
| Ctrl+6 | World |
| Ctrl+7 | Library |
| Ctrl+B | Expand / collapse the design line |
| Ctrl+\\ | Split view on / off |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+W | Close the tab |
| Ctrl+Shift+T | Open the closed file again |
| F5 | Start / pause / resume the simulation |
| Shift+F5 | Stop the simulation |
| F10 | Menu bar |
| Ctrl+Space | Full view: hide / bring back the panels |
| F11 | Full screen (Esc leaves it) |
| Alt+F4 | Exit |

## Circuit

| Shortcut | Function |
| --- | --- |
| A | Add a part (searchable list) |
| P | Power symbol (GND, +5V…) |
| W / Shift+W | Wire tool / start a wire at the cursor |
| L / Ctrl+L | Net label / global label |
| Q | No-connect (NC) marker |
| T | Text |
| / | Wire corner direction |
| Shift+Space | Wire mode: 90° → 45° → free |
| Backspace | Undo the last corner |
| Double-click or Esc | Finish the wire |
| M / G | Move / drag (wires stretch) |
| R / Shift+R | Rotate |
| X / Y | Mirror horizontally / vertically |
| Del | Delete |
| Ctrl+D or C | Duplicate |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste |
| Arrows, Shift+arrows | Nudge the selection 1 / 10 grid steps |
| E | Properties |
| V, F2 or double-click | Edit the value in place |
| D | Datasheet |
| Ctrl+F / Ctrl+H | Find / find and replace |
| Ctrl+A / Esc | Select all / clear selection |
| U | Select the connection |
| Tab / Shift+Tab | Step through parts |
| F or Home | Fit |
| + / − or wheel | Zoom |
| Middle button or Space + drag | Pan |
| The key left of 1 | Highlight the net under the cursor |
| Ctrl+Shift+X | Crosshair cursor |
| Ctrl+P | Print / PDF |
| F1 or ? | Shortcut list |

## PCB

| Shortcut | Function |
| --- | --- |
| X | Track tool |
| V | Place a via and go to the other layer of the layer pair (while routing); make the pair's other layer active (otherwise); the pair is set in the Katmanlar tab, top ↔ bottom by default |
| PgUp / PgDn | Active layer (while routing: change layer through a via) |
| / | Angle direction |
| Space | 45° / 90° mode |
| W / Shift+W | Track width: next / previous |
| Enter or double-click | Finish the track |
| Z | Draw a pour area |
| B / Ctrl+B | Fill / empty pours |
| T | Silkscreen text |
| M | Move (tracks stay put) |
| G | Drag (track ends follow) |
| D | Drag a track segment keeping its angle |
| R / Shift+R | Rotate |
| F | Flip to bottom / top (fit when nothing is selected) |
| L | Lock / unlock |
| E | Properties |
| U | Select the whole track |
| Del | Delete the selection (a part is removed from the schematic too) |
| Ctrl+C / X / V / D | Copy / cut / paste / duplicate |
| H | Active layer only / all |
| Home | Fit |
| Ctrl+Shift+M | Measure |
| Ctrl+Shift+H | Dimension line |
| F8 / Shift+F8 | Next / previous DRC marker |
| F1 or ? | Shortcut list |

## Code

| Shortcut | Function |
| --- | --- |
| F7 | Compile |
| F5 | Compile, load into the board and run (continue, when the board stopped at a breakpoint) |
| F10 | (Board stopped) next line |
| F11 | (Board stopped) into the called function |
| Shift+F11 | (Board stopped) out of the function |

## Part

| Shortcut | Function |
| --- | --- |
| Tab | Object mode / edit mode |
| 1 / 2 / 3 | (Edit mode) vertex / edge / face selection |
| G / R / S | Move / rotate / scale; then X / Y / Z axis lock and a number |
| Enter / Esc | Confirm / cancel |
| Shift+A | Add menu |
| Shift+D | Duplicate |
| A / Alt+A / Ctrl+I | Select all / clear selection / invert selection |
| X or Del | Delete |
| Ctrl+J | Join |
| Ctrl+P / Alt+P | Set parent / clear parent |
| H / Shift+H / Alt+H | Hide / hide others / show all |
| E / I / F / M / P | (Edit mode) extrude / inset / fill face / merge / separate |
| K | Knife (edit mode) · slice (object mode); C switches the mode |
| Ctrl+R | (Edit mode) loop cut; the wheel sets the number of cuts |
| Ctrl+B | (Edit mode) bevel |
| Alt+click · L · Ctrl+Numpad +/− | (Edit mode) loop select · linked piece · grow / shrink the selection |
| Shift+N | (Edit mode) recalculate normals |
| Z / Shift+Z / Alt+Z | Solid / wireframe / X-ray |
| Numpad 1 / 3 / 7 | Front / right / top |
| Numpad 5 | Perspective / orthographic |
| Home / Numpad . | Fit all / focus on the selection |
| W | Select tool |
| F2 | Rename |
| Ctrl+Z / Ctrl+Shift+Z | Undo / redo |
| Ctrl+N / Ctrl+O / Ctrl+S | New model / open / save |
| Middle button, Shift + middle button, wheel | Orbit, pan, zoom |

## World

| Shortcut | Function |
| --- | --- |
| K | Draw cable |
| B | Connect |
| Esc | Cancel / select tool |
| W / E / R / Y / Q | Move / rotate / scale / all-in-one handles / no handles (as in Unity) |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste (with what is mounted) |
| Ctrl+Shift+V | Paste mounted on the selected part |
| F2 | Rename |
| Ctrl+Alt+F | Move to view |
| Ctrl+E / Ctrl+Shift+E | Rotate by the rotate step (15°, set in Grid and snap) / the other way |
| Alt+Shift+A | Activate / deactivate the selection |
| Ctrl+D | Duplicate |
| Ctrl+click / Shift+click | Add to or take out of the selection / select a range in the tree |
| Shift+drag (on empty space) | Rectangle select (Ctrl+Shift: add to the selection) |
| Ctrl+Shift (while dragging a part) | Snap onto the surface |
| Ctrl+A | Select all |
| Ctrl+Alt+N / Alt+Shift+N / Ctrl+Shift+G | Empty object / empty child of the selection / put the selection under an empty parent |
| Del | Delete |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| T | Camera follows the selected object |
| F | Fit |
| F5 / Shift+F5 | Run-pause / stop and rewind |
| Ctrl+R | Start / end recording |
| Ctrl+F5 | Render |
| Shift + drag | Height |
| Alt + drag | Ignore the grid |

---

[← Previous: 8. Instruments](08-instruments.md) · [Contents](README.md) · [Next: 10. FAQ →](10-faq.md)
