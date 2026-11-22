# 7. World

The **Dünya** (world) page (Ctrl+6) is where everything comes together. Put the boards you designed, catalogue parts, mechanical bodies and 3D models into an environment with real physics, connect them with cables, join them with screws or hinges and run them all together. The circuit, the boards' firmware, the radio and the physics are computed at the same time, step by step.

![A two-axis servo arm driven by an Arduino](../../images/world-servo-arm.png)

## Opening a world

- **Yeni dünya** (new world, Ctrl+Shift+N): an empty 4 × 3 m workshop.
- **Bu devreyi yeni bir dünyaya koy** (put this circuit into a new world): puts your open circuit into a new world as a board.
- **Aç…** (open): a saved `.rwworld` file.
- **World templates** (home page or **Dosya → Dünya şablonundan**, File → from a world template):

| Template | Contents |
| --- | --- |
| Boş atölye (empty workshop) | 4 × 3 m floor, 25 °C |
| Kabloyla bağlı iki kart (two boards joined by cables) | A 9 V power board and an LED board on separate PCBs; the LEDs light once two cables connect them |
| Arduino ile iki eksenli servo kol (two-axis servo arm) | An Uno drives two SG90 servos; the second servo is screwed to the first one's horn |
| Arduino, LCD ve OLED (I²C kablolu) | Uno, 16×2 LCD, 128×64 OLED and a potentiometer on I²C cables |
| İki tekerlekli araba (two-wheel car) | PLA chassis, two TT motors and wheels on one axle, ball caster, 4×AA pack |
| Ultrasonik mesafe sensörü ve engeller (ultrasonic sensor and obstacles) | An HC-SR04 really measures the obstacle in front of it |
| Pil, motor ve tekerlek (battery, motor and wheel) | A 4×AA pack drives a TT motor with a wheel on its shaft |
| Fabrika hücresi (factory cell) | Six-axis arm, conveyor belt, light curtain |
| SCARA al-bırak (SCARA pick and place) | A SCARA arm carries parts with a parallel gripper |
| Pervane itki standı (propeller thrust stand) | 8520 motors with propellers: speed, thrust, torque, current live |
| Mini drone kalkışı (mini drone take-off) | A 40 g X frame with four 8520 motors lifts off on its thrust |
| ESP32 dörtlü (ESP32 quad) | Brushless motors, ESCs, MPU-6050; the flight firmware on the ESP32 keeps the frame level |
| Elektronik kutusu (electronics box) | A hollow body with a lid, an Uno and a 9 V battery inside; fit check, X-ray and exploded views |
| Motorlu planör (powered glider, UAV) | A 1 m glider, a 2212 brushless motor, an ESP32 flight board; balanced by the battery's position alone |
| Planör (glider) | Balsa pod, carbon tail boom, Nano + two servos, pushrods; hand-launched |
| Mekanum tekerlekli araba (mecanum car) | Four TT motors on mecanum wheels slide sideways |
| Işıklar (lights) | LEDs, an RGB LED, a power LED, a bulb and an LED strip in a dark room |
| Kumandalı araba (remote-controlled car, ESP32 + PS5 controller) | Its own ESP32 + DRV8833 PCB, a USB-C rechargeable 2S pack, a paired DualSense |
| Su roketi (water rocket) | 2 L PET bottle, 0.6 L water, 4 bar; water jet, air thrust, fins |
| Katı yakıtlı motor test standı (solid motor test stand, KNSB) | Two BATES sugar grains; pressure and thrust follow from the burn rate |
| Katı yakıtlı roket (solid rocket: e-match launch and parachute) | A 41 mm rocket on a G-class KNSB motor; a switch closed by a 2 s countdown fires the e-match, the rocket climbs to ~1 km and throws its parachute at apogee |
| Kap ve bilyeler: içi boş parçalar (containers: hollow parts) | A tray and a tube made in the modeller; balls and cubes fall into the tray, one ball drops down the tube's bore to the ground |
| Çizgi izleyen robot (line follower) | A 5-channel TCRT5000 over matte, faded and glossy tape |
| Sensör gezgini (sensor rover) | Camera, LiDAR, GPS, IMU, compass and encoders live |
| ESP32 + OV2640 kamera ile bant bulma (tape finding with a camera) | The camera sees the track from above, the ESP32 finds the tape |
| Rüzgâr, fan, konveyör ve ifadeler (wind, fan, conveyor and expressions) | Turbulent wind, a fan, a conveyor and forces written as expressions |
| Arazi parkuru, Kapalı atölye (terrain course, indoor workshop) | Sculpted terrain, painted floor layers |

## The parts of the screen

- **Top toolbar:** select and move, **Kablo çek** (draw cable, K), **Bağla** (connect, B), rotate, duplicate, delete, undo / redo; **Dünyayı çalıştır** (run world), **10 ms ilerlet** (step 10 ms), **Durdur ve başa dön** (stop and rewind), speed; camera follow, top view, fit all.
- **Left panel tabs:** **Sahne** (scene), **Kartlar** (boards), **Parçalar** (parts), **Mekanik** (mechanical), **Ortam** (environment).
- **Centre:** the 3D view.
- **Right:** the inspector: the selected object's name, position, physics, cables and joints.
- **Bottom tabs:** **Olaylar** (events), **Seri monitör** (serial monitor), **Kablolar ve ölçümler** (cables and measurements), **RF**.

## Step by step: your first world

In this example a battery turns a motor and the motor turns a wheel.

1. Open a **Yeni dünya** (new world, Ctrl+Shift+N).
2. In the **Parçalar** (parts) tab search for `pil` (battery) and add **4×AA Pil 6 V**. Then search `motor` and add **TT Redüktörlü Motor 1:48** (TT gear motor).
3. Select the motor. In the **Fizik** (physics) section on the right switch on **Sabit (zemine/masaya sabitlenmiş)** (fixed to the floor/table), so the motor does not run away while turning.
4. Add a **Tekerlek** (wheel) from the **Mekanik** (mechanical) tab.
5. With the wheel selected, take the **Bağla** (connect) tool (B) and click the motor. In the menu that opens choose **"Motor miline sabitle (birlikte döner)"** (fix to the motor shaft: turns with it).
6. Take the **Kablo çek** (draw cable) tool (K). Click the battery's `+` pin, then a pin of the motor. Do the same for the battery's `−` pin and the motor's other pin. With the mouse over a pin you see its name, live voltage and how many cables are connected.
7. **Dünyayı çalıştır** (run world, F5). The motor turns, the wheel turns, you hear the motor.
8. In the **Kablolar ve ölçümler** (cables and measurements) tab below, see each cable's current, power and load percentage.
9. **Durdur ve başa dön** (stop and rewind, Shift+F5) puts everything back to the start.

## Object types

### Boards

Your own circuit + PCB + code. In the **Kartlar** (boards) tab:

- **Açık sekmeden** (from an open tab): turns an open circuit into a board and adds it.
- **Proje dosyasından kart ekle (.rwproj)…** (add a board from a project file)
- **Yeni boş kart tasarla** (design a new empty board)
- **Hazır modül kartları** (ready module boards): Arduino Uno, Arduino Nano, ESP32 DevKit V1, ESP32 DevKitC, a brushed flight controller.

The same design can be placed several times; the copies share one circuit. With a board selected, the inspector has:

- **Kartı düzenle (devre · PCB · kod)** (edit board: circuit · PCB · code): the board opens in its own tab.
- **Kartın kodunu yaz** (write the board's code): opens its code on the Code page ([4. Code](04-code.md)).
- **Ayrı proje dosyası olarak dışa aktar…** (export as a separate project file)

### Parts

A single catalogue part: servo, motor, battery, sensor, display, Arduino… The **Parçalar** (parts) tab has a search box and categories: Geliştirme (development), Elektromekanik (electromechanical), Güç (power), Sensör (sensor), Ekran (display), Anahtarlama (switching), **Kablosuz ve USB** (wireless and USB). Change parameters in the inspector's **Parametreler** (parameters) section.

A loose Arduino or ESP32 becomes a programmable board with **Kart tasarımına dönüştür ve kodunu aç** (turn into a board design and open its code).

### Mechanical bodies

The **Temel şekiller** (basic shapes) in the **Mekanik** (mechanical) tab: box, plate / chassis, arm / bar, cylinder, wheel (with a rubber tyre), sphere / ball, table / fixed floor. Set size and colour in the inspector. **Gövde kabukları** (hollow shells with a lid) hold boards and batteries inside; **Uçuş yüzeyleri** (flying surfaces: wing, tailplane with elevator, fin with rudder) produce real lift, drag and moments.

### Collision shapes

Every part's rigid body comes from its own 3D model: a round motor can rolls, a wing collides with its tapered shape, a controller with its own outline (not a bounding box). Thin pieces such as leads, pins and cable ends are left out. **Görünüm → Çarpışma şekilleri (dünya)** (View → collision shapes) draws the bodies the physics really collides, translucent over the models; they move with the bodies while the simulation runs. Hollow models (a tray, cup, tube or U bracket made in the modeller or imported) are split into convex pieces, so things can be put inside them and a ball passes down a tube's bore. Example: the **Kap ve bilyeler** (containers) template. The maths: [Rigid bodies and contact](../physics/02-mechanics.md).

![Containers: the tray's floor and wall pieces, the tube's slices](../../images/world-containers.png)

### Pressure vessels and rockets

The **Mekanik → Basınçlı kaplar ve roketler** (mechanical → pressure vessels and rockets) section has a PET bottle (water rocket), an air tank, a solid-propellant motor and a rocket fin. Any cylinder, box or sphere body becomes a vessel with **İçinde gaz tutan kapalı kap** (a closed vessel holding gas) in the inspector:

- **Gas, pressure, inner volume, wall, water:** what the vessel holds. The gas follows the ideal gas law (p·V = m·R·T).
- **Nozzle:** throat and exit diameter, the face the jet leaves from. The flow is isentropic; at a high enough pressure ratio it reaches the speed of sound at the throat, and an expanding nozzle accelerates the gas supersonically. The thrust ṁ·vₑ + (pₑ − pₐ)·Aₑ acts on the body where the nozzle is.
- **Vana açılır (s)** (valve opens): the launcher holds the nozzle closed until then.
- **Water:** while it covers the nozzle, water leaves first (Bernoulli; the rocket's acceleration presses the water onto the nozzle), then the air.
- **Solid propellant:** a KNSB, KNDX or APCP grain; the burn rate depends on pressure (r = a·pⁿ). The chamber pressure comes out of the balance between the gas the burning surface makes and the gas leaving the nozzle; no ready thrust curve is read. It ignites when an **e-match** joined to the motor is fired from the circuit, or at the **Ateşleme zamanı** (ignition time).
- **Bursting:** if the pressure brings the wall's hoop stress (σ = p·r/t) to the material's strength, the vessel bursts.

The vessel's mass falls as gas, water and propellant leave; hot gas heats the wall and the nozzle throat (visible in the thermal camera). While running, the inspector shows pressure, temperature, thrust, the grain surface temperature, the burning share of the surface and the state live. Start with the **Su roketi** (water rocket), **Katı yakıtlı motor test standı** (solid motor test stand) and **Katı yakıtlı roket** (solid rocket) templates. The maths: [Gas and rockets](../physics/05-gas-and-rockets.md).

### Ignition and launch

A solid grain does not burn the moment the e-match fires. The match and the pyrogen on it burn; their hot gas heats the grain surface, a zone whose surface reaches the ignition temperature lights and the flame spreads towards the nozzle. In the inspector set **Ateşleyici barutu (g)** (igniter charge) and **Ateşleyici yanma süresi (ms)** (igniter burn time). With the charge at 0 only the match head is left; a sugar motor usually fails to light and the event log says why ("the grain surface reached at most 122 °C").

There are two ways to fire the e-match:

- **Launch controller:** battery → switch → e-match → battery. In the switch's inspector, **Kendiliğinden kapanır (s)** (closes by itself) gives a countdown. While the simulation runs, the **Kapat** (close) button closes the switch by hand.
- **From a board:** drive a MOSFET from a board to fire the e-match (for example from an altimeter or a radio).

The e-match is chosen in the motor's **Ateşleme** (ignition) setting or mechanically joined to the motor.

### Parachute

Give any body a **Kanopi çapı (m)** (canopy diameter) in its inspector. The parachute is thrown when the altimeter sees the climb end (or at **Açılma zamanı**, the deploy time). The canopy opens over its filling time once the lines are taut; the opening shock and the descent rate come out of the physics. While running, the canopy is drawn in the scene.

![The solid rocket on its pad: kraft tube, fins, battery and launch switch](../../images/world-rocket-pad.png)

![0.1 s after ignition: 24 bar, 1275 °C, 100 N of thrust; the whole grain surface is burning](../../images/world-rocket-ignite.png)

![Descending under the 45 cm parachute thrown at apogee](../../images/world-rocket-chute.png)

### 3D models

Add your own model with **Dışarıdan model → 3D model içe aktar (STL, OBJ, PLY)…** (import a 3D model). Mass, centre of mass and inertia are computed from the material. Parts you made on the Model page come here directly with **Dünyaya gönder** (send to world) ([5. Model](05-model.md)).

## Cables

- Every cable has a gauge (AWG), a length and a colour. The default is 24 AWG; 22 AWG when connecting a DC motor or a battery.
- A cable's resistance is real copper resistance. If its current limit is exceeded the cable heats and burns; the **Kablolar ve ölçümler** (cables and measurements) tab says **YANDI** (burned).
- Boards are electrically separate until a cable connects them. If you carry a signal between two boards, connect GND too.
- To remove a cable press **Kabloyu sök** (remove cable) in the inspector.

## Mechanical joints

With an object selected, click another object with the **Bağla** (connect, B) tool:

| Option | What happens |
| --- | --- |
| **Servo kolu / Motor miline sabitle (birlikte döner)** (fix to the servo horn / motor shaft) | The object is fixed to the servo horn or motor shaft and turns with it. Only offered if the target has a horn or shaft. |
| **… gövdesine sabitle (vida / yapıştırıcı)** (fix to the body: screw / glue) | The two objects move as one |
| **Menteşe ile bağla (serbest döner)** (hinge: turns freely) | Turns freely about one axis |
| **Kızakla bağla (doğrusal kayar)** (slider) | Slides along a line |

Parts joined by at most two joints do not collide with each other. Dragging an object brings everything attached to it. Remove a joint with **Bağlantıyı çöz** (release joint) in the inspector.

## Physics and environment

Every object's inspector has a **Fizik** (physics) section:

- **Sabit** (fixed): the object is fixed to the floor or table and does not move.
- **Malzeme** (material): PLA, PETG, ABS, acrylic, aluminium, steel, carbon, wood, MDF, rubber, FR4, PET, cardboard. Friction and bounce depend on the material.
- **Doluluk (%)** (infill) and **Kütle (g, 0 = otomatik)** (mass, 0 = automatic).

The **Ortam** (environment) tab:

- **Yerçekimi** (gravity): Earth 9.81, Moon 1.62, Mars 3.71, Jupiter 24.8, zero gravity.
- **Ortam sıcaklığı** (ambient temperature), floor width / depth, floor friction (μ), surrounding walls.
- **Engel ekle** / **Engelleri temizle** (add / clear obstacles).
- Air, wind, turbulence and force fields: see [Physics scripts (SDK)](../../SDK.md).

Servos read the pulse command from the circuit; the horn's angle and the current it draws go back to the circuit. DC motors turn at the speed the circuit computes; the load on the shaft reflects back into the motor. Hard impacts and broken joints are written to the **Olaylar** (events) tab. Air drag acts face by face: a part's face that a joined part covers sees no air ([Aerodynamics](../physics/04-aerodynamics.md)).

## Thermal camera

**Görünüm → Isı kamerası (dünya)** (View → thermal camera) colours the models by temperature like a real thermal camera (iron palette). With the mouse over a part you see the temperature of that point and the part's nodes (a TT motor's winding, can and gearbox). The maths: [Heat](../physics/03-heat.md).

## Sensors and displays

- The **HC-SR04 ultrasonic sensor** really measures by casting a beam at the objects in the world. The beam and its hit point are drawn, coloured by distance.

  ![Ultrasonic sensor and an obstacle](../../images/world-sonar.png)

- **I²C displays** (SSD1306 OLED, 16×2 LCD) show what the board writes, live on the 3D model.
- LEDs and lamps light up; overheated parts glow red, failing parts spark.

## Controllers, radio and USB

![ESP32 and the PS5-controlled car](../../images/world-ps5-car.png)

In **Parçalar → Kablosuz ve USB** (parts → wireless and USB): DualSense (PS5), DualShock 4 and Xbox controllers, **Bilgisayar + USB kablosu** (PC + USB cable), **Wi-Fi Modem (2.4 GHz)**, **BLE Beacon Etiketi** (BLE beacon tag), **IR Kumanda (NEC, 21 tuş)** (IR remote, 21 keys), **TSOP38238 IR Alıcı** (IR receiver).

### Controller

1. Select the controller. Pair it with the ESP32 with **Eşleştirme modu (PS + Create basılı)** (pairing mode: PS + Create held) in the inspector.
2. Use the on-screen controller with **Sanal kumandayı aç (fare / klavye)** (open the on-screen controller: mouse / keyboard; W/S throttle, A/D steering), or connect a real controller to the PC over USB or Bluetooth. If it does not show, **Kumandaları yeniden tara** (rescan controllers).
3. The **ESP32'ye uzaklık** (distance to the ESP32) slider (while running) shows how the signal weakens with distance.
4. **Kumandayı aç/kapat** (controller on/off) and **Eşleşmeyi unut** (forget pairing) are here too.

With an ESP32 selected, **Örnek kodu yükle: kumandalı araba (Bluepad32)** (load sample code: remote-controlled car) loads a ready car program.

### USB

Point the **Bilgisayar** (PC) object at a board and press **USB'yi tak** (plug in USB). While plugged, the board is powered with 5 V and its serial port is connected to the **Seri monitör** (serial monitor) tab. **USB'yi çıkar** (unplug USB) disconnects it.

**USB-C rechargeable lithium batteries** (**Parçalar → Güç**, parts → power): an 18650 cell with a USB-C input, a 2S 18650 pack (7.4 V) and USB-C AA cells with a steady 1.5 V. Real electronics run inside: a protection board (cuts at 2.4 V over-discharge, over-charge and over-current; a flat pack wakes only when a charger is connected) and a constant-current / constant-voltage charger (1 A up to 4.2 V, then the current tapers). Point the **Bilgisayar + USB kablosu** (PC + USB cable) object at the battery and plug it in: the battery charges; unplugged, charging stops.

### Radio environment

Wi-Fi, BLE and Bluetooth signals weaken with distance, obstacles and bodies. The **RF** tab below lists devices, signal strength (RSSI), channel and packets per second. **Sönümleme** (fading), **Dalgalar** (waves) and **Bağlantı çizgileri** (link lines) switch the radio visualisation on and off. IR remotes work only in line of sight; their keys are in the inspector's **IR kumanda tuşları (NEC)** (IR remote keys) section. The maths: [Radio](../physics/06-radio.md).

## Scene tree

The **Sahne** (scene) tab shows everything in the world as a tree of what is attached to what (e.g. "servo koluna takılı" on the servo horn, "mile takılı" on the shaft, "sabit" fixed). While running it shows temperatures above 60 °C and **ARIZALI** (failed) objects. The **Kablolar** (cables) list is below it. Right-click: **Odakla** (focus), **Alt nesneleri gizle/göster** (hide/show children); double-click focuses the object.

## Running

| Button / key | Function |
| --- | --- |
| **Dünyayı çalıştır** / **Duraklat** / **Devam** (run / pause / continue, F5) | Start, pause, resume |
| **10 ms ilerlet** (step 10 ms) | One step |
| **Durdur ve başa dön** (stop and rewind, Shift+F5) | Stops and puts everything back to the start |
| Speed | 0.01×, 0.1×, 0.5×, real time, 5× |

If a board has no firmware, the **Olaylar** (events) tab says "Yüklü yazılım yok (kart boşta)" (no firmware loaded, board idle).

## Recording and rendering

Real-time running has to keep up with the screen. A **render** recomputes a recording from start to end at the accuracy you choose, independent of the screen, then plays it like a video.

1. Press **● Kayıt** (record) on the toolbar (Ctrl+R). The world restarts from the beginning.
2. Press **Kaydı bitir** (end recording) whenever you like. The render window opens and starts computing that span. You can also enter the duration yourself with **Simülasyon › Render** (Ctrl+F5).

![Render: stress, forces and air flow](../../images/world-render.png)

**What is computed**

- **Every frame:** every object's position and orientation, servo horn and motor shaft angles, velocities, accelerations, contact and joint forces, every part's current, voltage, power and temperature signals, circuit nodes, serial output.
- **Stress:** every part you mark is solved with FEM at the "stress frame rate" (default 15 frames/s) and at its most loaded moments, with its real loads at that instant. Slender parts use beam elements.
- **Air flow:** all moving parts are solved together at their current positions with 3D lattice-Boltzmann. The wind is the world's wind minus the assembly's own velocity; a test speed is used if the assembly stands still. The result is the force on the assembly and its drag coefficient (Cd).
- **Heating:** conduction, convection and radiation from the components' power losses on the boards.

**Playback**

- **Play / pause:** Space. **Frame by frame:** ← →. **Start / end:** Home / End. Speed from ×0.05 to ×2 (slow motion).
- **Colour:** stress (von Mises, deformed shape exaggerated) or velocity arrows. **Scale:** the render's maximum, or each material's yield strength.
- **Forces:** contact forces as orange arrows, joint forces as cyan arrows. **Air:** a velocity slice through the middle of the assembly (blue still, yellow free stream, red accelerated air) and the aerodynamic force (purple arrow).
- **Graphs:** every recorded channel. Clicking or dragging on a graph jumps to that second.
- **Parçalar · gerilme** (parts · stress): each part's worst moment and safety factor. Clicking a row goes to that moment.
- **Isı** (heat) and **Günlük** (log) tabs; results are saved as `.rwanalysis` or CSV.

Manual controller inputs (gamepad, buttons in the scene) are not replayed in the render yet. The render recomputes the world's programmed behaviour (firmware, motors, physics) from the start.

## Comparing with the bench

The best way to see how close the simulation is to reality is to run the same setup on the bench and measure it. In the render window's **Grafikler** (graphs) tab open a measurement log with **Tezgâh ölçümü içe aktar (CSV)** (import a bench measurement), pick the simulation channel to compare on the left (e.g. the servo's current) and press **Seçili kanalla karşılaştır** (compare with the selected channel).

RobotWin aligns the two signals in time, draws the measurement in orange and reports:

- **gecikme** (delay): how far the log is shifted relative to the simulation;
- **ölçüm ≈ k × sim + c** (measurement ≈ k × sim + c): the real part's proportional difference from the model (e.g. a clone servo at 1.30×);
- RMSE, the largest difference and when;
- **uyum** (agreement): the share of time the difference stays within 10 % of the measured range;
- **ilk ayrılma** (first divergence): the first moment the two signals part for longer than 50 ms (e.g. when the real arm caught on something).

Log format: the first line is a header, the first column time. If the header has `ms` or `us` that unit is used, otherwise seconds. Separators may be commas, semicolons or tabs; decimal commas work too. Columns with `mA` / `mV` in their header are converted automatically when compared with A / V channels.

An example logging sketch (Arduino + INA219, in series with the servo's supply):

```cpp
#include <Wire.h>
#include <Adafruit_INA219.h>
#include <Servo.h>
Adafruit_INA219 ina;
Servo s;
void setup() {
  Serial.begin(115200);
  ina.begin();
  s.attach(9);
  Serial.println("t_ms,current_mA");
}
void loop() {
  unsigned long t = millis();
  s.write((t / 1000) % 2 ? 135 : 45);   // the same motion as the simulation's command
  Serial.print(t); Serial.print(','); Serial.println(ina.getCurrent_mA());
  delay(5);                             // 200 Hz
}
```

Save the serial monitor output as `.csv` and import it. Load the same sketch into the board in the simulation and render.

## Mouse and keyboard

| Mouse | Function |
| --- | --- |
| Drag an object with the left button | Move (with everything attached) |
| Shift + drag | Change height |
| Alt + drag | Ignore the 5 mm grid |
| Drag on empty space with the left button | Orbit |
| Drag with the right button | Pan |
| Wheel | Zoom |
| Double-click an object / empty space | Focus / top view |

| Key | Function |
| --- | --- |
| K | Draw cable |
| B | Connect |
| R / Shift+R | Rotate 15° / the other way |
| Ctrl+D | Duplicate |
| Del | Delete |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| T | Camera follows the selected object |
| F | Fit the selection / everything |
| Esc | Cancel, back to the select tool |

Objects can only be moved while the simulation is stopped. You can also type position and angle into the inspector's **Konum ve yön** (position and orientation) fields.

## Saving

**Kaydet** (save, Ctrl+S) stores the world as a `.rwworld` file. The boards inside are saved with their full projects (circuit, PCB, code); open the file on another PC and everything is ready.

---

[← Previous: 6. Assembly](06-assembly.md) · [Contents](README.md) · [Next: 8. Instruments →](08-instruments.md)
