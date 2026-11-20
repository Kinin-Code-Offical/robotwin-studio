# Accessibility

RobotWin Studio is a dense engineering tool (schematic, PCB, 3D world, plots). This page states
plainly what works today, what does not yet, and how to tell us when something blocks you.

## What works today

### Text and contrast

The dark theme's text colours meet WCAG 2.1 AA (4.5:1) on every surface they are used on:

| Text | Colour | On canvas `#0E0E10` | On panels `#161618` | On raised `#1C1C1F` |
| --- | --- | --- | --- | --- |
| Primary | `#EDEDEF` | 16.5:1 | 15.5:1 | 14.5:1 |
| Secondary | `#B4B4BC` | 9.4:1 | 8.8:1 | 8.3:1 |
| Muted (captions, hints) | `#8E8E97` | 5.9:1 | 5.6:1 | 5.2:1 |
| Disabled | `#5C5C63` | 2.9:1 | 2.7:1 | 2.6:1 |

Disabled controls are intentionally dimmer (WCAG exempts inactive components). No UI text is
smaller than 12 px, and inspector labels wrap instead of being cut off.

### Keyboard

- Every menu command is reachable from the keyboard. **Ctrl+K** opens the command palette, which
  searches all commands, pages and templates. *Yardım → Klavye kısayolları* lists every shortcut.
- Common shortcuts: Ctrl+N / O / S (file), Ctrl+Z / Y (undo / redo), Ctrl+C / X / V / D (clipboard,
  duplicate), Ctrl+F (find), F5 / Shift+F5 (start / stop the circuit simulation), F7 (compile the
  code), Esc (leave full screen or the current tool).
- Schematic: W draws a wire, L places a label, R rotates, X mirrors, Del deletes.
- PCB routing: click a ratsnest line to start, **F** finishes to the nearest pad of the net,
  **V** places a via.

### Not relying on colour alone

- Simulation problems (burned parts, overloaded cables, a lid that will not close) are written out
  as text in the inspector, the event log and the status bar, not only shown as colours.
- Cable diagnostics list measured values (current, voltage drop, length) next to the colour coding.
- The PCB editor has a *Yüksek kontrast* option that dims every layer except the active one.

### Tooltips

Most toolbar buttons, switches and inspector fields carry a tooltip that explains them.

## Known gaps

These are real limitations today. They are listed so you can plan around them.

- **Screen readers.** Standard controls such as menus, buttons, text boxes and lists expose their
  text to Narrator and NVDA through WPF. The drawing surfaces do not: the schematic, the PCB, the 3D
  world and the plots are custom-drawn, so they are invisible to screen readers. No UI Automation
  names have been added for them yet.
- **Windows High Contrast themes** are not followed. The app always uses its own dark theme.
- **Colour maps.** Stress, temperature and speed colouring use a blue→red "jet" scale. It is hard to
  read for some kinds of colour blindness and is not perceptually uniform. The numbers are available
  in the legend, the plots and the inspector, but not yet by hovering over a point on the 3D model.
- **Language.** The interface is in Turkish only. English (and other languages) are planned.
- **Motion.** Renders and replays animate continuously. There is no "reduce motion" setting yet,
  but playback can always be paused.
- **Mouse-heavy editing.** Placing and wiring parts, routing tracks and orbiting the 3D view need
  a mouse or touchpad. Keyboard alternatives exist for many commands, but not for every drag operation.

## Planned

- UI Automation names and descriptions for the canvases and the inspector.
- Following the Windows High Contrast theme and a light theme.
- A colour-blind-safe sequential colour map (viridis-type) as the default, plus exact values on
  hover over the 3D model.
- An English interface.
- A "reduce motion" option for replays and the 3D view.

## Reporting a problem

If something in RobotWin Studio is hard or impossible for you to use, please
[open an issue](https://github.com/Kinin-Code-Offical/robotwin-studio/issues). Tell us what you were
trying to do, what got in the way, and which assistive technology you use (screen reader,
magnifier, keyboard only, high-contrast theme …). Accessibility reports are treated as bugs, not
feature requests.
