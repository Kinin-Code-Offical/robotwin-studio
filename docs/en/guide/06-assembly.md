# 6. Assembly

**Montaj** (assembly, Ctrl+5) turns your project's circuit into a real assembly and opens it in a world tab. There is no ready-made vehicle template: every part of the circuit arrives as its real 3D part, and you build the chassis, wheels, joints and shells yourself. Everything runs on the real physics of the [World](07-world.md) page.


## What goes where?

| In the circuit | In the world |
| --- | --- |
| Parts placed on the PCB | Stay on the board; the board is one object with its circuit, PCB and code inside |
| Parts not on the PCB (motor, servo, battery, sensor, Arduino/ESP32 module…) | Each arrives as its own real 3D part around the board |
| Nets | Cables between the board's pins and the parts' pins; jumper wires at real lengths in 5 cm steps |

The board's pins appear on cables as `RefDes.Pin` (e.g. `J2.1`).

## Step by step

1. Open a circuit or pick a robot example on the **Başlangıç** (home) page (**Robot: Çizgi İzleyen** (line follower), **Robot: Engelden Kaçan (Arduino)** (obstacle avoider), **Drone: Mikro Quadcopter**).
2. Go to **Montaj** (Ctrl+5). The assembly is built once; afterwards the same world tab comes back.
3. From the **Mekanik** (mechanical) tab add a chassis (a plate or a [shell](07-world.md#mechanical-bodies)), wheels and a ball caster; fix the parts to the chassis and the wheels to the motor shafts with **Bağla** (connect, B).
4. Run with **F5**. The motors turn at the speed the circuit computes, the sensors measure the world, loads and impacts reflect back into the circuit.

> Example: from a submarine to a biplane, everything is built with the same tools; there is no template limit. For wings, shells, pressure vessels and pushrods see [7. World](07-world.md).

## Looking inside the board

Select the board object and press **Kartı düzenle (devre · PCB · kod)** (edit board: circuit · PCB · code): its circuit, PCB and code open in their own tab; your changes go into the board in the world.

## Recording and fault experiments

The assembly is a world, so all the world's tools work here too: record the simulation with **Kayıt** (record) and replay it in the render window, try faults such as a battery short or a jammed motor, and look inside with **Görünüm → Isı kamerası** (View → thermal camera) and **Görünüm → Çarpışma şekilleri** (View → collision shapes).

---

[← Previous: 5. Model](05-model.md) · [Contents](README.md) · [Next: 7. World →](07-world.md)
