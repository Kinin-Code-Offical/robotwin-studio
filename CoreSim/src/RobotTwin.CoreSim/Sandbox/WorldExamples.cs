using RobotTwin.CoreSim.Design;

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
