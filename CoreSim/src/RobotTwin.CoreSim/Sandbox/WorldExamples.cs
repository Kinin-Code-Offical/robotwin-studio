using System;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>Worlds built in code (used by the app's templates and by tests).</summary>
    public static class WorldExamples
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
            var uno = w.AddComponent(lib, "arduino-uno", new WorldPose { X = -0.0506, Y = 0.0266, Z = 0.002 });
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
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = uno.Id, Pin = "VIN" }, 24, "#E5484D").Length = 0.10;
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = uno.Id, Pin = "GND1" }, 24, "#1F1F23").Length = 0.10;
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "5V" }, new CableEnd { ObjectId = servo.Id, Pin = "V+" }, 26, "#E5484D").Length = 0.15;
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND2" }, new CableEnd { ObjectId = servo.Id, Pin = "GND" }, 26, "#5A3A22").Length = 0.15;
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D9" }, new CableEnd { ObjectId = servo.Id, Pin = "SIG" }, 26, "#F2A33B").Length = 0.15;
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
            pod.Parameters["launch"] = "7";
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
            var bat = w.AddComponent(lib, "bat-2s-lipo", new WorldPose { X = 0.165, Z = z0 + 0.004 });
            bat.Name = "LiPo 2S 450 mAh";
            bat.MassKg = 0.028;
            bat.Parameters["capacity"] = "0.45";   // a 2S 450 mAh pack: ≈ 62 × 20 × 10 mm
            var nano = w.AddComponent(lib, "arduino-nano", new WorldPose { X = 0.080, Y = -0.0076, Z = z0 + 0.004, Yaw = 90 });
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
            var rodE = w.AddBody("cylinder", 0.001, 0.001, 0.45, new WorldPose { X = tailX + 0.01, Y = 0.006, Z = boomZ + 0.004, Pitch = 90 }, "KARBON");
            rodE.Name = "İtme çubuğu (irtifa)";
            rodE.MassKg = 0.0008;
            var rodR = w.AddBody("cylinder", 0.001, 0.001, 0.45, new WorldPose { X = tailX + 0.01, Y = -0.006, Z = boomZ + 0.004, Pitch = 90 }, "KARBON");
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
                c.Length = 0.10;
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
            var a1 = w.AddBody("box", 0.22, 0.02, th, new WorldPose { Yaw = 45 }, "KARBON");
            var a2 = w.AddBody("box", 0.22, 0.02, th, new WorldPose { Yaw = 135 }, "KARBON");
            a1.Name = "Kol (ön sol – arka sağ)";
            a2.Name = "Kol (ön sağ – arka sol)";
            a1.MassKg = a2.MassKg = 0.012;
            var plate = w.AddBody("plate", 0.09, 0.09, 0.003, new WorldPose { Z = th }, "KARBON");
            plate.Name = "Üst plaka";
            plate.MassKg = 0.010;
            foreach (var arm in new[] { a1, a2 })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = arm.Id, ObjectB = plate.Id });
            double deck = th + 0.003;
            var bat = w.AddComponent(lib, "bat-3s-lipo", new WorldPose { X = batteryX, Z = deck });
            bat.Name = "LiPo 3S 850 mAh";
            bat.MassKg = 0.075;
            bat.Parameters["capacity"] = "0.85";   // 88 × 28 mm: clears the 5" prop discs on a 220 mm frame (a 1300 mAh pack reaches into them)
            var esp = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = 0.0, Y = 0.034, Z = deck });
            esp.Name = "Uçuş kartı (ESP32)";
            esp.Firmware = new Formats.FirmwareSource { FileName = "esp32-quad-fc.ino", Target = "esp32:esp32:esp32", Source = QuadSketch };
            var imu = w.AddComponent(lib, "gy-521", new WorldPose { X = 0.0, Y = -0.034, Z = deck });
            imu.Name = "IMU (MPU-6050)";
            foreach (var part in new[] { bat, esp, imu })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = part.Id, ObjectB = plate.Id });

            // Motors: M1 front-left CCW, M2 front-right CW, M3 rear-right CCW, M4 rear-left CW (as in the sketch).
            var corners = new[] { (X: d, Y: d, Ccw: true, Arm: a1, Pin: "IO25", Name: "M1 ön sol"), (X: d, Y: -d, Ccw: false, Arm: a2, Pin: "IO26", Name: "M2 ön sağ"),
                                  (X: -d, Y: -d, Ccw: true, Arm: a1, Pin: "IO27", Name: "M3 arka sağ"), (X: -d, Y: d, Ccw: false, Arm: a2, Pin: "IO14", Name: "M4 arka sol") };
            WorldObject? firstEsc = null;
            void Wire(WorldObject a, string pa, WorldObject b, string pb, double awg, string colour, bool soldered = false)
            {
                var cable = w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, awg, colour);
                // Cut to the run plus a little slack: a loose loop of motor wire ends up in the prop.
                double run = Math.Sqrt(Math.Pow(a.Pose.X - b.Pose.X, 2) + Math.Pow(a.Pose.Y - b.Pose.Y, 2) + Math.Pow(a.Pose.Z - b.Pose.Z, 2));
                cable.Length = Math.Max(0.04, 1.15 * run + 0.02);
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
            var ml = w.AddComponent(lib, "motor-tt", new WorldPose { X = -0.03, Y = 0.03, Z = motorZ });
            var mr = w.AddComponent(lib, "motor-tt", new WorldPose { X = -0.03, Y = -0.03, Z = motorZ, Yaw = 180 });
            var wl = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.03 + shaftX, Y = 0.03 + shaftTip + 0.015, Z = 0 });
            var wr = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.03 - shaftX, Y = -0.03 - shaftTip - 0.015, Z = 0 });
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
