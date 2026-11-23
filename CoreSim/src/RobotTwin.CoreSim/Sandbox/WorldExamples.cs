using System;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>Worlds built in code (used by the app's templates and by tests).</summary>
    public static partial class WorldExamples
    {
        /// <summary>Two-wheel car: plate chassis, two TT motors on wheels, a ball caster, a 4×AA pack on top.</summary>
        /// <param name="lib">Part library.</param>
        /// <param name="motorHeight">Height of the TT gearbox body (m).</param>
        /// <param name="shaftX">Output shaft position along the motor (m, motor frame).</param>
        /// <param name="shaftZ">Output shaft height above the motor base (m).</param>
        /// <param name="shaftTip">Where the wheel hub sits on the shaft, sideways from the motor centre (m).</param>
        /// <summary>
        /// The car laid out for the app's detailed TT gear motor (gearbox 22.4 mm high, output shaft
        /// 22.7 mm behind the motor origin, 11.2 mm up, shaft end 18.4 mm to the side) — the geometry
        /// the Studio and the headless runner use, so the wheels sit exactly on their axles.
        /// </summary>
        /// <summary>
        /// [aero] A 1 m RC thermal glider built the way a real one is: a balsa fuselage pod carrying a
        /// 2S LiPo, an Arduino Nano and two SG90 servos (elevator, rudder) with lead in the nose, a
        /// carbon tail boom with the pushrods running beside it, a 6° dihedral Clark Y wing on the
        /// pod's wing saddle (rudder-steered, no ailerons: dihedral turns yaw into roll), and the
        /// tailplane and fin on the end of the boom. CG ≈ 33 % of the chord, tail volume ≈ 0.57.
        /// Hand-launched at 7 m/s from 40 m; the sketch holds trim, then flies a gentle turn.
        /// </summary>
        /// <summary>
        /// [shell] An electronics box: 120 × 80 × 40 mm PLA enclosure with 2 mm walls and a screw-on
        /// lid, an Arduino Uno screwed to its floor, a 9 V battery standing upright beside it (48 mm:
        /// too tall, the lid cannot close) and a servo outside, wired through the rear cable hole.
        /// </summary>
        public static WorldDocument ShellBox(PartLibrary lib)
        {
            var w = new WorldDocument { Title = "Elektronik kutusu" };
            var box = w.AddBody("box", 0.12, 0.08, 0.04, new WorldPose(), "PLA");
            box.Name = "Kutu (içi boş)";
            box.IsStatic = true;
            box.Parameters["shell"] = "true";
            box.Parameters["wall"] = "0.002";
            box.Parameters["holes"] = "-x";
            var uno = w.AddComponent(lib, "arduino-uno", new WorldPose { X = -0.0506, Y = 0.0266, Z = OnPins(0.002) });
            uno.Name = "Arduino Uno";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = uno.Id, ObjectB = box.Id });   // M3 standoffs
            var bat = w.AddComponent(lib, "bat-9v", new WorldPose { X = 0.036, Z = 0.002 });   // standing on its end
            bat.Name = "9 V pil (dik)";
            var lid = w.AddBody("box", 0.12, 0.08, 0.002, new WorldPose { Z = 0.04 + 0.06 }, "PLA");
            lid.Name = "Kutu kapağı";
            lid.IsStatic = true;   // held open above the box until it is closed
            lid.Parameters["lid.of"] = box.Id;
            lid.Parameters["screwed"] = "true";
            var servo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = -0.11, Y = 0.0, Z = 0 });
            servo.Name = "Servo (kutu dışında)";
            servo.IsStatic = true;
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = uno.Id, Pin = "VIN" }, 24, "#E5484D");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = uno.Id, Pin = "GND1" }, 24, "#1F1F23");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "5V" }, new CableEnd { ObjectId = servo.Id, Pin = "V+" }, 26, "#E5484D");   // lengths left to the pins (+ slack)
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND2" }, new CableEnd { ObjectId = servo.Id, Pin = "GND" }, 26, "#5A3A22");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D9" }, new CableEnd { ObjectId = servo.Id, Pin = "SIG" }, 26, "#F2A33B");
            return w;
        }

        public static WorldDocument Glider(PartLibrary lib)
        {
            var w = new WorldDocument { Title = "Planör" };
            const double z0 = 40;                    // pod bottom
            const double podTop = z0 + 0.050;
            const double boomZ = z0 + 0.025;          // boom axis
            const double tailX = -0.50;

            var pod = w.AddBody("box", 0.26, 0.045, 0.050, new WorldPose { X = 0.07, Z = z0 }, "BALSA");
            pod.Name = "Gövde (balsa kabin)";
            pod.MassKg = 0.035;                       // hollow 2 mm balsa box with a ply former
            pod.Parameters["launch"] = "5.5";   // a gentle throw near the trim speed (~5 m/s)
            pod.Parameters["shell"] = "true";        // [shell] really hollow: 2 mm balsa walls the electronics sit between
            pod.Parameters["wall"] = "0.002";
            pod.Parameters["holes"] = "-x";           // pushrod and cable exit at the rear
            var boom = w.AddBody("cylinder", 0.008, 0.008, 0.50, new WorldPose { X = tailX - 0.04, Z = boomZ, Pitch = 90 }, "KARBON");
            boom.Name = "Kuyruk borusu (karbon Ø8)";
            boom.MassKg = 0.012;

            var wing = w.AddBody("wing", 0.15, 1.0, 0.012, new WorldPose { X = 0.005, Z = podTop }, "BALSA");
            wing.Name = "Kanat (Clark Y, 6° dihedral)";
            wing.MassKg = 0.060;
            wing.Parameters["airfoil"] = "clarky";
            wing.Parameters["incidence"] = "2";
            wing.Parameters["dihedral"] = "6";

            // Electronics inside the pod, lengthwise between its 2 mm walls (the fit check holds them to it):
            // battery and lead forward for the CG, servos under the wing.
            var bat = w.AddComponent(lib, "bat-2s-lipo", new WorldPose { X = 0.165, Z = z0 + 0.004, Yaw = 180 });   // leads aft, inside the pod
            bat.Name = "LiPo 2S 450 mAh";
            bat.MassKg = 0.028;
            bat.Parameters["capacity"] = "0.45";   // a 2S 450 mAh pack: ≈ 62 × 20 × 10 mm
            var nano = w.AddComponent(lib, "arduino-nano", new WorldPose { X = 0.080, Y = 0.0025, Z = OnPins(z0 + 0.002), Yaw = 90 });   // on its pins in foam tape, beside the battery's plug
            nano.Name = "Uçuş kartı (Arduino Nano)";
            nano.MassKg = 0.007;
            nano.Firmware = new Formats.FirmwareSource
            {
                FileName = "planor.ino",
                Target = "arduino:avr:nano",
                Source = GliderSketch
            };
            var elevServo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = 0.055, Y = 0.011, Z = z0 + 0.004 });
            elevServo.Name = "İrtifa servosu";
            var rudServo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = 0.055, Y = -0.011, Z = z0 + 0.004 });
            rudServo.Name = "Yön servosu";
            var lead = w.AddBody("box", 0.020, 0.020, 0.008, new WorldPose { X = 0.185, Z = z0 + 0.034 }, "KURŞUN");
            lead.Name = "Burun kurşunu";
            lead.MassKg = 0.038;

            // Pushrods: 1 mm carbon rods from the pod's rear exit along the boom to the control horns.
            var rodE = w.AddBody("cylinder", 0.001, 0.001, 0.43, new WorldPose { X = tailX + 0.01, Y = 0.006, Z = boomZ + 0.0025, Pitch = 90 }, "KARBON");
            rodE.Name = "İtme çubuğu (irtifa)";
            rodE.MassKg = 0.0008;
            var rodR = w.AddBody("cylinder", 0.001, 0.001, 0.43, new WorldPose { X = tailX + 0.01, Y = -0.006, Z = boomZ + 0.0025, Pitch = 90 }, "KARBON");
            rodR.Name = "İtme çubuğu (yön)";
            rodR.MassKg = 0.0008;

            var stab = w.AddBody("wing", 0.08, 0.30, 0.006, new WorldPose { X = tailX, Z = boomZ + 0.004 }, "BALSA");
            stab.Name = "Yatay kuyruk + irtifa dümeni";
            stab.MassKg = 0.008;
            stab.Parameters["airfoil"] = "naca0012";
            stab.Parameters["incidence"] = "-2";
            stab.Parameters["control"] = "elevator";
            stab.Parameters["control.chord"] = "0.35";
            stab.Parameters["control.servo"] = elevServo.Id;
            // [linkage] real pushrod geometry: 12 mm servo horn hole, 24 mm control horn, 450 mm carbon rod.
            stab.Parameters["control.horn"] = "0.012";
            stab.Parameters["control.arm"] = "0.024";
            stab.Parameters["control.rod"] = "0.45";
            stab.Parameters["downwash"] = wing.Id;
            var fin = w.AddBody("wing", 0.08, 0.11, 0.005, new WorldPose { X = tailX, Z = boomZ + 0.010 }, "BALSA");
            fin.Name = "Dikey kuyruk + yön dümeni";
            fin.MassKg = 0.005;
            fin.Parameters["airfoil"] = "naca0012";
            fin.Parameters["symmetric"] = "false";
            fin.Parameters["control"] = "rudder";
            fin.Parameters["control.chord"] = "0.4";
            fin.Parameters["control.servo"] = rudServo.Id;
            fin.Parameters["control.horn"] = "0.012";
            fin.Parameters["control.arm"] = "0.024";
            fin.Parameters["control.rod"] = "0.45";

            foreach (var part in new WorldObject[] { boom, wing, bat, nano, elevServo, rudServo, lead })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = pod.Id });
            foreach (var part in new WorldObject[] { stab, fin, rodE, rodR })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = boom.Id });

            // Wiring: LiPo → Nano VIN (on-board regulator), servos from the Nano's 5 V, signals D9 / D10.
            WorldCable C(WorldObject a, string pa, WorldObject b, string pb, string color)
            {
                var c = w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, 26, color);
                c.Length = 0;   // cut to the pins with a little slack (a fixed 10 cm is shorter than some pin distances)
                return c;
            }

            C(bat, "+", nano, "VIN", "#E5484D");
            C(bat, "-", nano, "GND1", "#1F1F23");
            C(nano, "5V", elevServo, "V+", "#E5484D");
            C(nano, "GND2", elevServo, "GND", "#5A3A22");
            C(nano, "D9", elevServo, "SIG", "#F2A33B");
            C(nano, "5V", rudServo, "V+", "#E5484D");
            C(nano, "GND1", rudServo, "GND", "#5A3A22");
            C(nano, "D10", rudServo, "SIG", "#F2A33B");
            return w;
        }

        /// <summary>
        /// [esc] 5" X quad flown by real firmware: carbon X frame (220 mm motor to motor), four 2204
        /// 2300KV brushless motors on 20 A ESCs with 5045 props (diagonal CCW / CW), 3S 850 mAh LiPo,
        /// an ESP32 running <see cref="QuadSketch"/> and an MPU-6050 that feels the frame's motion.
        /// <paramref name="batteryX"/> moves the pack fore/aft (m): the centre of mass moves with it and the
        /// controller has to trim the motors against it, as on a real build.
        /// </summary>
        public static WorldDocument Quad(PartLibrary lib, double batteryX = 0)
        {
            var w = new WorldDocument { Title = "ESP32 dörtlü (quad)" };
            const double th = 0.004, d = 0.0778;   // arm thickness; motor offset along x and y (110 mm from centre)
            // One 220 mm arm through the middle; the other diagonal is two half arms butted against its sides (the two
            // bars cannot pass through each other), all held between the arms and the top plate by the frame screws.
            var a1 = w.AddBody("box", 0.22, 0.02, th, new WorldPose { Yaw = 45 }, "KARBON");
            a1.Name = "Kol (ön sol – arka sağ)";
            a1.MassKg = 0.012;
            WorldObject HalfArm(double angle, string name)
            {
                double r = 0.01 + 0.05, c = Math.Cos(angle * Math.PI / 180), sn = Math.Sin(angle * Math.PI / 180);
                var half = w.AddBody("box", 0.10, 0.02, th, new WorldPose { X = r * c, Y = r * sn, Yaw = angle }, "KARBON");
                half.Name = name;
                half.MassKg = 0.006;
                return half;
            }

            var a2 = HalfArm(-45, "Kol (ön sağ)");
            var a3 = HalfArm(135, "Kol (arka sol)");
            var plate = w.AddBody("plate", 0.09, 0.09, 0.003, new WorldPose { Z = th }, "KARBON");
            plate.Name = "Üst plaka";
            plate.MassKg = 0.010;
            foreach (var arm in new[] { a1, a2, a3 })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = arm.Id, ObjectB = plate.Id });
            double deck = th + 0.003;
            var bat = w.AddComponent(lib, "bat-3s-lipo", new WorldPose { X = batteryX, Z = deck });
            bat.Name = "LiPo 3S 850 mAh";
            bat.MassKg = 0.075;
            bat.Parameters["capacity"] = "0.85";   // 88 × 28 mm: clears the 5" prop discs on a 220 mm frame (a 1300 mAh pack reaches into them)
            // The pack lies along x in the middle; the flight board (turned lengthwise) and the IMU sit on their header
            // pins in foam tape in the strips beside it.
            var esp = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = -0.01345, Y = 0.0162, Z = OnPins(deck), Yaw = 90 });
            esp.Name = "Uçuş kartı (ESP32)";
            esp.Firmware = new Formats.FirmwareSource { FileName = "esp32-quad-fc.ino", Target = "esp32:esp32:esp32", Source = QuadSketch };
            var imu = w.AddComponent(lib, "gy-521", new WorldPose { X = -0.0064, Y = -0.0162, Z = OnPins(deck) });
            imu.Name = "IMU (MPU-6050)";
            foreach (var part in new[] { bat, esp, imu })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = part.Id, ObjectB = plate.Id });

            // Motors: M1 front-left CCW, M2 front-right CW, M3 rear-right CCW, M4 rear-left CW (as in the sketch).
            var corners = new[] { (X: d, Y: d, Ccw: true, Arm: a1, Pin: "IO25", Name: "M1 ön sol"), (X: d, Y: -d, Ccw: false, Arm: a2, Pin: "IO26", Name: "M2 ön sağ"),
                                  (X: -d, Y: -d, Ccw: true, Arm: a1, Pin: "IO27", Name: "M3 arka sağ"), (X: -d, Y: d, Ccw: false, Arm: a3, Pin: "IO14", Name: "M4 arka sol") };
            WorldObject? firstEsc = null;
            void Wire(WorldObject a, string pa, WorldObject b, string pb, double awg, string colour, bool soldered = false)
            {
                var cable = w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, awg, colour);
                // Cut to the run plus a little slack: a loose loop of motor wire ends up in the prop.
                cable.Length = 0;   // cut to the pins with a little slack: a wire measured between part centres can be shorter than the pin distance
                cable.SolderedA = cable.SolderedB = soldered;   // power leads are soldered on a real quad: no plug contact resistance
            }
            foreach (var c in corners)
            {
                double yaw = Math.Atan2(c.Y, c.X) * 180 / Math.PI;
                var m = w.AddComponent(lib, "bldc-2204", new WorldPose { X = c.X, Y = c.Y, Z = th, Yaw = yaw });
                m.Name = c.Name;
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = m.Id, ObjectB = c.Arm.Id });
                var prop = ReadyParts.Add(w, c.Ccw ? "prop-5045-ccw" : "prop-5045-cw", new WorldPose());
                ReadyParts.AttachToPort(w, prop, m, "shaft", new Vec3(0, 0, 0.026), Vec3.UnitZ);
                var esc = w.AddComponent(lib, "esc-20a", new WorldPose { X = c.X * 0.62, Y = c.Y * 0.62, Z = th, Yaw = yaw });
                esc.Name = "ESC " + c.Name.Substring(0, 2);
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = esc.Id, ObjectB = c.Arm.Id });
                firstEsc ??= esc;
                Wire(bat, "+", esc, "B+", 16, "#E5484D", true);
                Wire(bat, "-", esc, "B-", 16, "#1F1F23", true);
                // A CW motor is a CCW one with two phases swapped.
                Wire(esc, "M+", m, c.Ccw ? "A" : "B", 18, "#1F1F23", true);
                Wire(esc, "M-", m, c.Ccw ? "B" : "A", 18, "#1F1F23", true);
                Wire(esp, c.Pin, esc, "SIG", 26, "#F2F2F2");
            }

            Wire(firstEsc!, "5V", esp, "VIN", 26, "#E5484D");   // the ESC's BEC powers the flight controller
            Wire(esp, "GND1", bat, "-", 26, "#1F1F23");
            Wire(esp, "3V3", imu, "VCC", 28, "#E5484D");
            Wire(esp, "GND2", imu, "GND", 28, "#1F1F23");
            Wire(esp, "IO21", imu, "SDA", 28, "#2F7FD8");
            Wire(esp, "IO22", imu, "SCL", 28, "#F2A33B");
            return w;
        }

        /// <summary>[esc] Flight controller sketch for <see cref="Quad"/>: MPU-6050 over I²C, level and gyro calibration, complementary filter, cascaded angle/rate PID, X mixer, ESC arming.</summary>
        public const string QuadSketch = @"// ESP32 uçuş kontrolcüsü: MPU-6050 (I²C) + 4 fırçasız ESC, X dörtlü (5"", 2204 2300KV, 3S).
// Gerçek bir kartta da çalışan Arduino-ESP32 kodu; RobotWin dünyasında aynı kod çalışır:
// IMU gövdenin gerçek dönüşünü ve ivmesini okur, ESC'ler gönderilen darbeyi gaz olarak alır.
//
// Eksenler (gövde): x ileri, y sol, z yukarı. Motorlar:
//   M1 ön sol  (IO25, CCW)   M2 ön sağ  (IO26, CW)
//   M3 arka sağ (IO27, CCW)  M4 arka sol (IO14, CW)
// Akış: ESC'ler 2 s düşük gazla kurulur → jiroskop sıfırı ve ivmeölçer düzlüğü ölçülür → gaz
// yavaşça askı değerine çıkar → yatış/yunuslama açısı ve yalpa hızı PID ile sıfırda tutulur.
// Pilin yeri ağırlık merkezini kaydırsa da kontrolcü motorları ayrı ayrı ayarlayarak gövdeyi
// düz tutar.

#include <Arduino.h>
#include <Wire.h>
#include <ESP32Servo.h>

const int ESC_PIN[4] = {25, 26, 27, 14};
const float POS_X[4] = { 1,  1, -1, -1};   // ön (+) / arka (-)
const float POS_Y[4] = { 1, -1, -1,  1};   // sol (+) / sağ (-)
const float SPIN[4]  = { 1, -1,  1, -1};   // +1 CCW (gövdeye saat yönünde tork verir)
Servo esc[4];

const uint8_t MPU = 0x68;
float gyroBias[3] = {0, 0, 0};
float rollTrim = 0, pitchTrim = 0;         // ivmeölçer düzlük kalibrasyonu (MPU-6050 ofseti ±0,05 g olabilir)
float roll = 0, pitch = 0;                 // rad
float iRoll = 0, iPitch = 0, iYaw = 0;
unsigned long lastUs = 0, startMs = 0, lastPrint = 0;

// Kazançlar (açı → hız → itki farkı, µs biriminde)
const float KA = 5.0;                      // açı hatası (rad) → istenen hız (rad/s)
const float KP = 55.0, KI = 30.0;
const float KYAW = 60.0, KIYAW = 20.0;
const float HOVER_US = 1400;               // ~askı gazı (3S, 5045, ~290 g)

void mpuWrite(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(MPU);
  Wire.write(reg);
  Wire.write(val);
  Wire.endTransmission();
}

bool mpuRead(float a[3], float g[3]) {
  Wire.beginTransmission(MPU);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(MPU, (uint8_t)14, (uint8_t)1) != 14) return false;
  int16_t raw[7];
  for (int i = 0; i < 7; i++) raw[i] = (int16_t)((Wire.read() << 8) | Wire.read());
  for (int i = 0; i < 3; i++) a[i] = raw[i] / 8192.0f;                       // ±4 g
  for (int i = 0; i < 3; i++) g[i] = raw[4 + i] / 65.5f * DEG_TO_RAD;        // ±500 °/s
  return true;
}

void writeAll(int us) {
  for (int i = 0; i < 4; i++) esc[i].writeMicroseconds(us);
}

void setup() {
  Serial.begin(115200);
  Wire.begin(21, 22, 400000);
  mpuWrite(0x6B, 0x00);   // uyan
  mpuWrite(0x1A, 0x03);   // 44 Hz alçak geçiren
  mpuWrite(0x1B, 0x08);   // ±500 °/s
  mpuWrite(0x1C, 0x08);   // ±4 g
  for (int i = 0; i < 4; i++) {
    esc[i].setPeriodHertz(400);
    esc[i].attach(ESC_PIN[i], 1000, 2000);
  }
  writeAll(1000);
  Serial.println(""ESC kuruluyor (1000 us)..."");
  // Jiroskop sıfırı ve düzlük: kurulum beklenirken gövde düz ve hareketsiz duruyor.
  float a[3], g[3];
  int n = 0;
  unsigned long t0 = millis();
  while (millis() - t0 < 2000) {
    if (mpuRead(a, g)) {
      for (int k = 0; k < 3; k++) gyroBias[k] += g[k];
      n++;
      rollTrim += atan2f(a[1], a[2]);
      pitchTrim += atan2f(-a[0], a[2]);
    }
    delay(4);
  }
  if (n > 0) {
    for (int k = 0; k < 3; k++) gyroBias[k] /= n;
    rollTrim /= n;
    pitchTrim /= n;
  }
  roll = pitch = 0;
  Serial.printf(""IMU hazir: %d ornek, jiroskop sifiri %.4f %.4f %.4f rad/s, duzluk %.2f %.2f derece\n"", n, gyroBias[0], gyroBias[1], gyroBias[2], rollTrim * RAD_TO_DEG, pitchTrim * RAD_TO_DEG);
  startMs = millis();
  lastUs = micros();
}

void loop() {
  unsigned long now = micros();
  if (now - lastUs < 2500) return;          // 400 Hz döngü
  float dt = (now - lastUs) * 1e-6f;
  lastUs = now;

  float a[3], g[3];
  if (!mpuRead(a, g)) { writeAll(1000); return; }
  for (int k = 0; k < 3; k++) g[k] -= gyroBias[k];

  // Tamamlayıcı süzgeç: jiroskop kısa vadede, ivmeölçer uzun vadede.
  float accRoll = atan2f(a[1], a[2]) - rollTrim, accPitch = atan2f(-a[0], a[2]) - pitchTrim;
  // ~5 s: in flight the accelerometer mostly feels the thrust, not gravity, so it may only correct slow drift.
  roll = 0.9995f * (roll + g[0] * dt) + 0.0005f * accRoll;
  pitch = 0.9995f * (pitch + g[1] * dt) + 0.0005f * accPitch;

  // Gaz: 0,5 s bekle, 1,5 s'de askıya çık, sonra hafif tırman.
  float t = (millis() - startMs) * 1e-3f;
  float thr = 1000;
  if (t > 0.5f) thr = 1100 + (HOVER_US + 40 - 1100) * fminf(1.0f, (t - 0.5f) / 1.5f);

  // Açı → hız → itki farkı (kaskad PID).
  float wantRollRate = -KA * roll, wantPitchRate = -KA * pitch;
  float eR = wantRollRate - g[0], eP = wantPitchRate - g[1], eY = 0 - g[2];
  bool flying = thr > 1150;                 // kontrol, motorlar gövdeyi taşımaya başlamadan devrede
  if (flying) {
    iRoll = constrain(iRoll + eR * dt, -2.0f, 2.0f);
    iPitch = constrain(iPitch + eP * dt, -2.0f, 2.0f);
    iYaw = constrain(iYaw + eY * dt, -2.0f, 2.0f);
  }
  float uR = KP * eR + KI * iRoll;          // +: x ekseninde + tork (sol taraf kalkar)
  float uP = KP * eP + KI * iPitch;         // +: y ekseninde + tork (burun iner)
  float uY = KYAW * eY + KIYAW * iYaw;      // +: z ekseninde + tork (sola dönüş)

  for (int i = 0; i < 4; i++) {
    // τx = Σ y·F, τy = -Σ x·F, τz = -Σ spin·k·F
    float us = thr + (flying ? (uR * POS_Y[i] - uP * POS_X[i] - uY * SPIN[i]) : 0);
    esc[i].writeMicroseconds((int)constrain(us, 1000.0f, 2000.0f));
  }

  if (millis() - lastPrint > 250) {
    lastPrint = millis();
    Serial.printf(""t=%.2f gaz=%.0f yatis=%.1f yunus=%.1f\n"", t, thr, roll * RAD_TO_DEG, pitch * RAD_TO_DEG);
  }
}
";

        /// <summary>
        /// [uav] The glider as an electric UAV, built the way a real 1 m motor glider is: a 2212 1000KV
        /// outrunner on the nose with an 8 × 4.5" prop (≈ 5 N static thrust on 3S, sized for a ≈ 300 g
        /// plane), a 20 A ESC, a 3S 850 mAh pack placed for a 33 % CG — no nose lead — and an ESP32 +
        /// MPU-6050 flight controller running <see cref="PlaneSketch"/>. It is held still for 2.5 s
        /// (ESC arming, IMU calibration) and then hand-launched.
        /// </summary>
        public static WorldDocument MotorGlider(PartLibrary lib, double batteryX = 0.073, double height = 1.6)
        {
            var w = new WorldDocument { Title = "Motorlu planör (İHA)" };
            double z0 = height, podTop = z0 + 0.050, boomZ = z0 + 0.025;
            const double tailX = -0.50;

            // Long enough aft to hold the servos' own 8 cm leads and plugs; the boom and both pushrods pass a 16 mm hole
            // in the rear bulkhead, the motor leads a 7 mm hole in the firewall beside the motor, low down.
            var pod = w.AddBody("box", 0.337, 0.045, 0.050, new WorldPose { X = 0.0315, Z = z0 }, "BALSA");
            pod.Name = "Gövde (balsa kabin)";
            pod.MassKg = 0.045;
            pod.Parameters["launch"] = "8";
            pod.Parameters["launch.delay"] = "2.5";   // held for ESC arming and IMU calibration, then thrown
            pod.Parameters["shell"] = "true";
            pod.Parameters["wall"] = "0.002";
            pod.Parameters["holes"] = "-x,+x";
            pod.Parameters["hole.-x.size"] = "0.016";
            pod.Parameters["hole.-x.z"] = "0.023";
            pod.Parameters["hole.+x.size"] = "0.007";
            pod.Parameters["hole.+x.z"] = "0.0035";
            pod.Parameters["hole.+x.u"] = "0.0185";
            var boom = w.AddBody("cylinder", 0.008, 0.008, 0.50, new WorldPose { X = tailX - 0.04, Z = boomZ, Pitch = 90 }, "KARBON");
            boom.Name = "Kuyruk borusu (karbon Ø8)";
            boom.MassKg = 0.012;
            var wing = w.AddBody("wing", 0.15, 1.0, 0.012, new WorldPose { X = 0.005, Z = podTop }, "BALSA");
            wing.Name = "Kanat (Clark Y, 6° dihedral)";
            wing.MassKg = 0.060;
            wing.Parameters["airfoil"] = "clarky";
            wing.Parameters["incidence"] = "2";
            wing.Parameters["dihedral"] = "6";

            // Nose: motor on the firewall, shaft forward; the prop on its shaft.
            var motor = w.AddComponent(lib, "motor-2212-1000kv", new WorldPose { X = 0.200, Z = z0 + 0.025, Pitch = 90 });
            motor.Name = "Motor 2212 1000KV";
            var prop = ReadyParts.Add(w, "prop-8045-ccw", new WorldPose());
            ReadyParts.AttachToPort(w, prop, motor, "shaft", new Vec3(0, 0, 0.040), Vec3.UnitZ);
            var esc = w.AddComponent(lib, "esc-20a", new WorldPose { X = 0.180, Z = OnPins(z0 + 0.002) });
            esc.Name = "ESC 20 A";
            var bat = w.AddComponent(lib, "bat-3s-lipo", new WorldPose { X = batteryX, Y = 0.006, Z = z0 + 0.002, Yaw = 180 });   // leads aft, ending between the tail servos
            bat.Name = "LiPo 3S 850 mAh";
            bat.MassKg = 0.075;
            bat.Parameters["capacity"] = "0.85";
            // The 52 mm DevKit lies lengthwise in the 45 mm pod (turned 90°, centred: its origin is off the board centre).
            var esp = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = 0.1316, Y = -0.01275, Z = OnPins(z0 + 0.002), Yaw = 90 });   // on its pins in foam tape
            esp.Name = "Uçuş kartı (ESP32)";
            esp.Firmware = new Formats.FirmwareSource { FileName = "esp32-plane-fc.ino", Target = "esp32:esp32:esp32", Source = PlaneSketch };
            var imu = w.AddComponent(lib, "gy-521", new WorldPose { X = -0.105, Y = 0.009, Z = OnPins(z0 + 0.002) });   // rear bay, under the boom
            imu.Name = "IMU (MPU-6050)";
            var elevServo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = -0.035, Y = 0.011, Z = z0 + 0.002 });
            elevServo.Name = "İrtifa servosu";
            var rudServo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = -0.035, Y = -0.011, Z = z0 + 0.002 });
            rudServo.Name = "Yön servosu";

            var rodE = w.AddBody("cylinder", 0.001, 0.001, 0.43, new WorldPose { X = tailX + 0.01, Y = 0.006, Z = boomZ + 0.0025, Pitch = 90 }, "KARBON");
            rodE.Name = "İtme çubuğu (irtifa)";
            rodE.MassKg = 0.0008;
            var rodR = w.AddBody("cylinder", 0.001, 0.001, 0.43, new WorldPose { X = tailX + 0.01, Y = -0.006, Z = boomZ + 0.0025, Pitch = 90 }, "KARBON");
            rodR.Name = "İtme çubuğu (yön)";
            rodR.MassKg = 0.0008;
            var stab = w.AddBody("wing", 0.08, 0.30, 0.006, new WorldPose { X = tailX, Z = boomZ + 0.004 }, "BALSA");
            stab.Name = "Yatay kuyruk + irtifa dümeni";
            stab.MassKg = 0.008;
            stab.Parameters["airfoil"] = "naca0012";
            stab.Parameters["incidence"] = "-2";
            stab.Parameters["control"] = "elevator";
            stab.Parameters["control.chord"] = "0.35";
            stab.Parameters["control.servo"] = elevServo.Id;
            stab.Parameters["control.horn"] = "0.012";
            stab.Parameters["control.arm"] = "0.024";
            stab.Parameters["control.rod"] = "0.45";
            stab.Parameters["downwash"] = wing.Id;
            var fin = w.AddBody("wing", 0.08, 0.11, 0.005, new WorldPose { X = tailX, Z = boomZ + 0.010 }, "BALSA");
            fin.Name = "Dikey kuyruk + yön dümeni";
            fin.MassKg = 0.005;
            fin.Parameters["airfoil"] = "naca0012";
            fin.Parameters["symmetric"] = "false";
            fin.Parameters["control"] = "rudder";
            fin.Parameters["control.chord"] = "0.4";
            fin.Parameters["control.servo"] = rudServo.Id;
            fin.Parameters["control.horn"] = "0.012";
            fin.Parameters["control.arm"] = "0.024";
            fin.Parameters["control.rod"] = "0.45";

            foreach (var part in new WorldObject[] { boom, wing, motor, esc, bat, esp, imu, elevServo, rudServo })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = pod.Id });
            foreach (var part in new WorldObject[] { stab, fin, rodE, rodR })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = boom.Id });

            void Wire(WorldObject a, string pa, WorldObject b, string pb, double awg, string colour, bool soldered = false)
            {
                var cable = w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, awg, colour);
                cable.Length = 0;   // cut to the pins with a little slack: a wire measured between part centres can be shorter than the pin distance
                cable.SolderedA = cable.SolderedB = soldered;
            }

            Wire(bat, "+", esc, "B+", 18, "#E5484D", true);
            Wire(bat, "-", esc, "B-", 18, "#1F1F23", true);
            Wire(esc, "M+", motor, "A", 18, "#1F1F23", true);
            Wire(esc, "M-", motor, "B", 18, "#1F1F23", true);
            Wire(esc, "5V", esp, "VIN", 26, "#E5484D");
            Wire(esp, "GND1", bat, "-", 26, "#1F1F23");
            Wire(esp, "IO25", esc, "SIG", 26, "#F2F2F2");
            Wire(esc, "5V", elevServo, "V+", 26, "#E5484D");
            Wire(esp, "GND2", elevServo, "GND", 26, "#5A3A22");
            Wire(esp, "IO26", elevServo, "SIG", 26, "#F2A33B");
            Wire(esc, "5V", rudServo, "V+", 26, "#E5484D");
            Wire(esp, "GND2", rudServo, "GND", 26, "#5A3A22");
            Wire(esp, "IO27", rudServo, "SIG", 26, "#F2A33B");
            Wire(esp, "3V3", imu, "VCC", 28, "#E5484D");
            Wire(esp, "GND1", imu, "GND", 28, "#1F1F23");
            Wire(esp, "IO21", imu, "SDA", 28, "#2F7FD8");
            Wire(esp, "IO22", imu, "SCL", 28, "#F2A33B");
            return w;
        }

        /// <summary>[uav] Flight controller sketch for <see cref="MotorGlider"/>: IMU attitude, throttle schedule, pitch hold on the elevator, yaw damper / wing leveller on the rudder.</summary>
        public const string PlaneSketch = @"// ESP32 motorlu planör uçuş kartı: MPU-6050 (I²C), 20 A ESC + 2212 1000KV, irtifa ve yön servoları.
// Gerçek bir kartta da çalışan Arduino-ESP32 kodu; RobotWin dünyasında aynı kod çalışır.
//
// Eksenler (gövde): x ileri, y sol, z yukarı. yunuslama + = burun aşağı, yatış + = sol kanat yukarı.
// Akış: elde tutulurken ESC 1000 us ile kurulur, jiroskop sıfırı ve düzlük ölçülür; 2,5 s'de atılır.
// Motor tırmanışta %70 gaz, irtifa dümeni burnu 8° yukarıda tutar; 9 s'de motor durur, süzülüşte 2°.
// Yön dümeni ortada kalır: 6° dihedral kanatları kendiliğinden düzler.

#include <Arduino.h>
#include <Wire.h>
#include <ESP32Servo.h>

const int ESC_PIN = 25, ELEV_PIN = 26, RUD_PIN = 27;
Servo esc, elev, rud;

const uint8_t MPU = 0x68;
float gyroBias[3] = {0, 0, 0};
float rollTrim = 0, pitchTrim = 0;
float roll = 0, pitch = 0;
unsigned long lastUs = 0, startMs = 0, lastPrint = 0;

void mpuWrite(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(MPU);
  Wire.write(reg);
  Wire.write(val);
  Wire.endTransmission();
}

bool mpuRead(float a[3], float g[3]) {
  Wire.beginTransmission(MPU);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(MPU, (uint8_t)14, (uint8_t)1) != 14) return false;
  int16_t raw[7];
  for (int i = 0; i < 7; i++) raw[i] = (int16_t)((Wire.read() << 8) | Wire.read());
  for (int i = 0; i < 3; i++) a[i] = raw[i] / 8192.0f;
  for (int i = 0; i < 3; i++) g[i] = raw[4 + i] / 65.5f * DEG_TO_RAD;
  return true;
}

void setup() {
  Serial.begin(115200);
  Wire.begin(21, 22, 400000);
  mpuWrite(0x6B, 0x00);
  mpuWrite(0x1A, 0x03);
  mpuWrite(0x1B, 0x08);
  mpuWrite(0x1C, 0x08);
  esc.setPeriodHertz(400);
  esc.attach(ESC_PIN, 1000, 2000);
  esc.writeMicroseconds(1000);
  elev.attach(ELEV_PIN);
  rud.attach(RUD_PIN);
  elev.write(90);
  rud.write(90);
  // Elde tutulurken: jiroskop sıfırı ve düzlük.
  float a[3], g[3];
  int n = 0;
  unsigned long t0 = millis();
  while (millis() - t0 < 1500) {
    if (mpuRead(a, g)) {
      for (int k = 0; k < 3; k++) gyroBias[k] += g[k];
      rollTrim += atan2f(a[1], a[2]);
      pitchTrim += atan2f(-a[0], a[2]);
      n++;
    }
    delay(4);
  }
  if (n > 0) {
    for (int k = 0; k < 3; k++) gyroBias[k] /= n;
    rollTrim /= n;
    pitchTrim /= n;
  }
  Serial.printf(""Hazir: %d ornek, duzluk %.1f %.1f derece\n"", n, rollTrim * RAD_TO_DEG, pitchTrim * RAD_TO_DEG);
  startMs = millis();
  lastUs = micros();
}

void loop() {
  unsigned long now = micros();
  if (now - lastUs < 5000) return;   // 200 Hz
  float dt = (now - lastUs) * 1e-6f;
  lastUs = now;
  float a[3], g[3];
  if (!mpuRead(a, g)) return;
  for (int k = 0; k < 3; k++) g[k] -= gyroBias[k];
  float accRoll = atan2f(a[1], a[2]) - rollTrim, accPitch = atan2f(-a[0], a[2]) - pitchTrim;
  roll = 0.999f * (roll + g[0] * dt) + 0.001f * accRoll;
  pitch = 0.999f * (pitch + g[1] * dt) + 0.001f * accPitch;

  float t = (millis() - startMs) * 1e-3f;   // atış ~1 s sonra
  bool motor = t > 0.8f && t < 7.5f;
  esc.writeMicroseconds(motor ? 1700 : 1000);

  float pitchTarget = (motor ? -8.0f : -2.0f) * DEG_TO_RAD;   // burun yukarı = eksi
  float e = -1.6f * (pitch - pitchTarget) * RAD_TO_DEG - 0.12f * g[1] * RAD_TO_DEG;
  e = constrain(e, -35.0f, 35.0f);
  elev.write(90 + (int)e);

  // Yön dümeni ortada: 6° dihedral kanatları kendiliğinden düzler (polihedral planörlerde olduğu gibi).
  float r = 0;
  rud.write(90);

  if (millis() - lastPrint > 500) {
    lastPrint = millis();
    Serial.printf(""t=%.1f motor=%d yunus=%.1f yatis=%.1f irtifa_dumeni=%.0f yon=%.0f\n"", t, motor, pitch * RAD_TO_DEG, roll * RAD_TO_DEG, e, r);
  }
}
";

        /// <summary>Glider flight sketch: trim for 3 s, a gentle rudder turn, then straight again.</summary>
        public const string GliderSketch = @"#include <Servo.h>
// Planör uçuş kartı: irtifa dümeni D9, yön dümeni D10.
Servo elevator, rudder;
const int ELEV_TRIM = 90;   // derece; 90 = nötr
const int RUD_TRIM = 90;

void setup() {
  elevator.attach(9);
  rudder.attach(10);
  elevator.write(ELEV_TRIM);
  rudder.write(RUD_TRIM);
}

void loop() {
  unsigned long t = millis();
  // 3 s düz süzül, 4 s hafif sola dönüş (dihedral yalpayı yatışa çevirir), sonra düz.
  rudder.write(t > 3000 && t < 7000 ? RUD_TRIM + 20 : RUD_TRIM);
  elevator.write(ELEV_TRIM);
  delay(20);
}
";

        public static WorldDocument CarDetailed(PartLibrary lib) => Car(lib, 0.0224, -0.0227, 0.0112, 0.0184);

        public static WorldDocument Car(PartLibrary lib, double motorHeight = 0.0188, double shaftX = -0.011, double shaftZ = 0.0094, double shaftTip = 0.0185)
        {
            var w = new WorldDocument { Title = "Araba" };
            // Motors hang under the plate with their shafts at wheel-radius height, so the 65 mm wheels touch the floor.
            const double r = 0.0325;
            double motorZ = r - shaftZ, plateZ = motorZ + motorHeight;
            // 90 mm wide: the wheels run 5 mm clear of the chassis edge (they collide with it like in reality).
            var plate = w.AddBody("plate", 0.16, 0.09, 0.003, new WorldPose { X = 0, Z = plateZ }, "PLA");
            // The shaft sits off the gearbox centre: the right motor is the same motor flipped upside down
            // (rolled 180°), as in a real kit, so both shafts share one axle and the motors mirror each other.
            const double axleX = -0.03 - 0.011;
            var ml = w.AddComponent(lib, "motor-tt", new WorldPose { X = axleX - shaftX, Y = 0.03, Z = motorZ });
            var mr = w.AddComponent(lib, "motor-tt", new WorldPose { X = axleX - shaftX, Y = -0.03, Z = motorZ + motorHeight, Roll = 180 });
            var wl = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = axleX, Y = 0.03 + shaftTip + 0.015, Z = 0 });
            var wr = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = axleX, Y = -0.03 - shaftTip - 0.015, Z = motorHeight - 2 * shaftZ });
            // Ball caster on a post under the front of the plate.
            double ball = 0.016;
            var caster = w.AddBody("sphere", ball, ball, ball, new WorldPose { X = 0.06, Z = 0 }, "ÇELİK");
            var post = w.AddBody("box", 0.012, 0.012, plateZ - ball * 0.8, new WorldPose { X = 0.06, Z = ball * 0.8 }, "PLA");
            post.Name = "Teker ayağı";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = post.Id, ObjectB = plate.Id });
            var bat = w.AddComponent(lib, "bat-4aa", new WorldPose { X = 0.01, Z = plateZ + 0.003 });
            foreach (var o in new[] { ml, mr, caster, bat }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wl.Id, ObjectB = ml.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wr.Id, ObjectB = mr.Id, PortB = "shaft" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = ml.Id, Pin = "A" }, 22);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = ml.Id, Pin = "B" }, 22);
            // The right motor is mounted mirrored, so it is wired the other way round to drive forward too.
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = mr.Id, Pin = "B" }, 22);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = mr.Id, Pin = "A" }, 22);
            return w;
        }
    }
}
