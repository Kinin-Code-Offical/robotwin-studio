using System;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// [ps5plane] A 1 m park flyer flown from a PS5 DualSense over Bluetooth, built like a real one:
        /// a hollow balsa pod with a carbon tail boom, a 4° dihedral Clark Y wing with ailerons on its outer
        /// halves (one SG90 in the pod under the wing, torque rods), elevator and rudder on pushrods along the boom,
        /// a 2212 1000KV outrunner with an 8 × 4.5" prop on a 20 A ESC, a 3S 850 mAh pack placed for the CG,
        /// and an ESP32 + MPU-6050 flight controller running <see cref="Ps5PlaneSketch"/> (Bluepad32).
        /// It stands on a tricycle gear (steel wire legs, free-rolling wheels on axles) at the end of an
        /// asphalt runway on a grass field; the pilot holds the controller beside the runway. Nothing is
        /// scripted: the pilot arms the motor, opens the throttle, the plane rolls, lifts off when the
        /// elevator rotates it, and flies wherever the sticks put it — within Bluetooth range.
        /// </summary>
        public static WorldDocument Ps5Plane(PartLibrary lib)
        {
            var w = new WorldDocument
            {
                Title = "PS5 kumandalı uçak",
                Description = "ESP32 uçuş kartı ve DualSense ile pistten kalkan 1 m'lik eğitim uçağı."
            };
            const double z0 = 0.115;                 // pod bottom: the gear keeps the 8" prop 3 cm off the runway
            const double podTop = z0 + 0.052, boomZ = z0 + 0.025, tailX = -0.50;   // the wing sits on the 2 mm hatch

            // The pod runs back far enough to hold the servos' own 8 cm leads and their plugs; the boom enters it through
            // a 16 mm hole in the rear bulkhead (the two pushrods beside it), and the motor leads come in through a 7 mm
            // hole in the firewall beside the motor, low down.
            var pod = w.AddBody("box", 0.337, 0.045, 0.050, new WorldPose { X = 0.0315, Z = z0 }, "BALSA");
            pod.Name = "Gövde (balsa kabin)";
            pod.MassKg = 0.045;
            pod.Parameters["shell"] = "true";
            pod.Parameters["wall"] = "0.002";
            pod.Parameters["holes"] = "-x,+x";
            pod.Parameters["hole.-x.size"] = "0.016";
            pod.Parameters["hole.-x.z"] = "0.023";
            pod.Parameters["hole.+x.size"] = "0.007";
            pod.Parameters["hole.+x.z"] = "0.0035";
            pod.Parameters["hole.+x.u"] = "0.0185";
            // The hatch closing the pod under the wing, screwed onto the rim: the wiring stays inside.
            var hatch = w.AddBody("box", 0.337, 0.045, 0.002, new WorldPose { X = 0.0315, Z = z0 + 0.050 }, "BALSA");
            hatch.Name = "Kabin kapağı (balsa)";
            hatch.MassKg = 0.0065;
            hatch.Parameters["lid.of"] = pod.Id;
            hatch.Parameters["screwed"] = "true";
            var boom = w.AddBody("cylinder", 0.008, 0.008, 0.50, new WorldPose { X = tailX - 0.04, Z = boomZ, Pitch = 90 }, "KARBON");
            boom.Name = "Kuyruk borusu (karbon Ø8)";
            boom.MassKg = 0.012;
            var wing = w.AddBody("wing", 0.15, 1.0, 0.012, new WorldPose { X = 0.005, Z = podTop }, "BALSA");
            wing.Name = "Kanat (Clark Y, 4° dihedral, kanatçıklı)";
            wing.MassKg = 0.062;
            wing.Parameters["airfoil"] = "clarky";
            wing.Parameters["incidence"] = "2";
            wing.Parameters["dihedral"] = "4";
            wing.Parameters["control"] = "aileron";   // outer halves, opposite deflection
            wing.Parameters["control.chord"] = "0.25";

            // Nose: motor on the firewall, shaft forward; the prop on its shaft.
            var motor = w.AddComponent(lib, "motor-2212-1000kv", new WorldPose { X = 0.200, Z = z0 + 0.025, Pitch = 90 });
            motor.Name = "Motor 2212 1000KV";
            var prop = ReadyParts.Add(w, "prop-8045-ccw", new WorldPose());
            ReadyParts.AttachToPort(w, prop, motor, "shaft", new Vec3(0, 0, 0.040), Vec3.UnitZ);
            var esc = w.AddComponent(lib, "esc-20a", new WorldPose { X = 0.180, Z = z0 + 0.002 });
            esc.Name = "ESC 20 A";
            esc.Pose.Z = OnPins(z0 + 0.002);
            // 6 mm to the left: its two leads end in the 10 mm gap between the tail servos, not inside the rudder servo.
            var bat = w.AddComponent(lib, "bat-3s-lipo", new WorldPose { X = 0.073, Y = 0.006, Z = z0 + 0.002, Yaw = 180 });
            bat.Name = "LiPo 3S 850 mAh";
            bat.MassKg = 0.075;
            bat.Parameters["capacity"] = "0.85";
            // The flight board stands on its header pins on a strip of foam tape (pins in the foam, not in the floor).
            var esp = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = 0.1316, Y = -0.01275, Z = z0 + 0.002, Yaw = 90 });
            esp.Pose.Z = OnPins(z0 + 0.002);
            esp.Name = "Uçuş kartı (ESP32)";
            esp.Firmware = new Formats.FirmwareSource { FileName = "esp32-ps5-plane.ino", Target = "esp32:esp32:esp32", Source = Ps5PlaneSketch };
            // The IMU sits flat on the floor in the rear bay, under the boom and clear of the servos' plugs.
            var imu = w.AddComponent(lib, "gy-521", new WorldPose { X = -0.105, Y = 0.009, Z = z0 + 0.002 });
            imu.Name = "IMU (MPU-6050)";
            imu.Pose.Z = OnPins(z0 + 0.002);
            var elevServo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = -0.035, Y = 0.011, Z = z0 + 0.002 });
            elevServo.Name = "İrtifa servosu";
            var rudServo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = -0.035, Y = -0.011, Z = z0 + 0.002 });
            rudServo.Name = "Yön servosu";
            // The aileron servo stands in the pod right under the wing's centre on a 6 mm balsa block glued to the floor,
            // its horn up to the wing's torque rods (as in pod-and-boom gliders): out of the airflow and clear of the wing.
            var riser = w.AddBody("box", 0.032, 0.013, 0.006, new WorldPose { X = 0.003, Z = z0 + 0.002 }, "BALSA");
            riser.Name = "Servo yatağı (balsa)";
            riser.MassKg = 0.0004;
            var ailServo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = 0.003, Z = z0 + 0.008 });
            ailServo.Name = "Kanatçık servosu";
            wing.Parameters["control.servo"] = ailServo.Id;
            wing.Parameters["control.horn"] = "0.010";
            wing.Parameters["control.arm"] = "0.012";
            wing.Parameters["control.rod"] = "0.03";

            // Under the stabiliser's root (its control horns hang below it) and through the rear bulkhead's hole.
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

            // ---- Tricycle gear: a bent aluminium strap with steel wire legs for the mains just behind
            // the CG (≈ 15° from it, so the plane neither sits on its tail nor needs a long run to rotate)
            // and a steel nose leg behind the firewall. The wheels turn freely on their axles.
            const double mainX = 0.0, track = 0.09, mainD = 0.055, noseX = 0.165, noseD = 0.045;
            var strap = w.AddBody("box", 0.025, 2 * track + 0.006, 0.003, new WorldPose { X = mainX, Z = z0 - 0.003 }, "ALÜMİNYUM");
            strap.Name = "Ana iniş takımı (alüminyum lama)";
            strap.MassKg = 0.016;
            var legs = new System.Collections.Generic.List<WorldObject>();
            var wheels = new System.Collections.Generic.List<(WorldObject Wheel, WorldObject Leg, double Y)>();
            foreach (double side in new[] { 1.0, -1.0 })
            {
                double axleZ = mainD / 2;
                var leg = w.AddBody("cylinder", 0.003, 0.003, z0 - 0.003 - axleZ, new WorldPose { X = mainX, Y = side * track, Z = axleZ }, "ÇELİK");
                leg.Name = side > 0 ? "Sol ana bacak (çelik tel)" : "Sağ ana bacak (çelik tel)";
                leg.MassKg = 0.006;
                legs.Add(leg);
                var wheel = w.AddBody("wheel", mainD, 0.018, 0.018, new WorldPose { X = mainX, Y = side * (track + 0.0115), Z = 0 }, "KAUÇUK");
                wheel.Name = side > 0 ? "Sol ana teker" : "Sağ ana teker";
                wheel.MassKg = 0.010;
                wheels.Add((wheel, leg, side * (track + 0.0115)));
            }

            var noseLeg = w.AddBody("cylinder", 0.003, 0.003, z0 - noseD / 2, new WorldPose { X = noseX, Y = 0.0125, Z = noseD / 2 }, "ÇELİK");
            noseLeg.Name = "Burun bacağı (çelik tel)";
            noseLeg.MassKg = 0.005;
            var noseWheel = w.AddBody("wheel", noseD, 0.014, 0.014, new WorldPose { X = noseX, Z = 0 }, "KAUÇUK");
            noseWheel.Name = "Burun tekeri";
            noseWheel.MassKg = 0.007;

            foreach (var part in new WorldObject[] { boom, wing, motor, esc, bat, esp, imu, elevServo, rudServo, riser, strap, noseLeg })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = pod.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = ailServo.Id, ObjectB = riser.Id });
            foreach (var leg in legs)
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = leg.Id, ObjectB = strap.Id });   // wire legs bent into the strap
            foreach (var part in new WorldObject[] { stab, fin, rodE, rodR })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = boom.Id });
            // Each wheel's nylon hub turns on the end of its steel wire leg (μ ≈ 0.3 under ≈ 2 N on a 1.25 mm radius:
            // ≈ 0.8 mN·m). The legs are welded into the strap and the strap into the pod, so the whole airframe is one
            // rigid body and the axles hold their alignment as real 2.5 mm steel does.
            foreach (var (wheel, leg, y) in wheels)
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Hinge, ObjectA = wheel.Id, ObjectB = leg.Id, AnchorX = mainX, AnchorY = y, AnchorZ = mainD / 2, AxisX = 0, AxisY = 1, AxisZ = 0, Friction = 0.0008 });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Hinge, ObjectA = noseWheel.Id, ObjectB = noseLeg.Id, AnchorX = noseX, AnchorY = 0, AnchorZ = noseD / 2, AxisX = 0, AxisY = 1, AxisZ = 0, Friction = 0.0005 });

            void Wire(WorldObject a, string pa, WorldObject b, string pb, double awg, string colour, bool soldered = false)
            {
                var cable = w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, awg, colour);
                cable.Length = 0;   // cut to the pins with a little slack (the rope model measures the real pin distance)
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
            Wire(esc, "5V", ailServo, "V+", 26, "#E5484D");
            Wire(esp, "GND1", ailServo, "GND", 26, "#5A3A22");
            Wire(esp, "IO14", ailServo, "SIG", 26, "#F2A33B");
            Wire(esp, "3V3", imu, "VCC", 28, "#E5484D");
            Wire(esp, "GND1", imu, "GND", 28, "#1F1F23");
            Wire(esp, "IO21", imu, "SDA", 28, "#2F7FD8");
            Wire(esp, "IO22", imu, "SCL", 28, "#F2A33B");

            // ---- The field: the plane at the threshold of a 40 m asphalt runway on mown grass, facing +x.
            const double startX = -16;
            foreach (var o in w.Objects)
                o.Pose.X += startX;
            foreach (var j in w.Joints)
                if (j.AnchorX != 0 || j.AnchorY != 0 || j.AnchorZ != 0) j.AnchorX += startX;

            w.Environment.Name = "Model uçak sahası";
            w.Environment.Width = 90;
            w.Environment.Depth = 60;
            w.Environment.Walls = false;
            var field = new TerrainMap(90, 60, 0.5, 0, 0, "grass");
            TerrainSculpt.Road(field, new[] { (-20.0, 0.0), (0.0, 0.0), (22.0, 0.0) }, 3.0, TerrainLayers.IndexOf("asphalt"), false, 0.3);
            w.Terrain = field;
            foreach (double x in new[] { -19.0, -10.0, 0.0, 10.0, 20.0 })
            {
                WorldBuilding.AddProp(w, "cone", x, 2.0);
                WorldBuilding.AddProp(w, "cone", x, -2.0);
            }

            foreach (var (x, y) in new[] { (-38.0, 24.0), (-30.0, 26.0), (-12.0, 27.0), (14.0, 26.0), (33.0, 24.0), (40.0, -22.0), (24.0, -26.0), (-36.0, -25.0) })
                WorldBuilding.AddProp(w, "tree", x, y, x * 13);

            // The pilot stands beside the runway holding the controller (≈ 1 m up), bonded to the plane's ESP32.
            var pad = w.AddComponent(lib, "dualsense", new WorldPose { X = -10, Y = -6, Z = 1.0, Yaw = 90, Pitch = -20 });
            pad.Name = "DualSense (pilot)";
            pad.IsStatic = true;
            pad.Parameters["pairedWith"] = esp.Id;
            pad.Parameters["source"] = "otomatik";
            return w;
        }
    }
}
