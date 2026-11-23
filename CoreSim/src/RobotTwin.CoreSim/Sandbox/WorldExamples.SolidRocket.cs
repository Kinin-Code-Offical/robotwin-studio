using System;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>Tangent-ogive nose cone (mm): base radius <paramref name="r"/>, length <paramref name="length"/>, base at z = 0.</summary>
        private static PolyMesh Ogive(double r, double length, int segments = 32, int stations = 14)
        {
            double rho = (r * r + length * length) / (2 * r);
            var m = new PolyMesh();
            var rings = new int[stations][];
            for (int s = 0; s < stations; s++)
            {
                double x = length * (s + 1) / stations;            // from the tip
                double y = Math.Sqrt(Math.Max(0, rho * rho - (length - x) * (length - x))) + r - rho;
                rings[s] = new int[segments];
                for (int i = 0; i < segments; i++)
                {
                    double a = 2 * Math.PI * i / segments;
                    rings[s][i] = m.AddVertex(new Vec3(y * Math.Cos(a), y * Math.Sin(a), length - x));
                }
            }

            int tip = m.AddVertex(new Vec3(0, 0, length));
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                m.AddFace(rings[0][i], rings[0][j], tip);
                for (int s = 0; s + 1 < stations; s++) m.AddFace(rings[s + 1][i], rings[s + 1][j], rings[s][j], rings[s][i]);
            }

            var bottom = new int[segments];
            for (int i = 0; i < segments; i++) bottom[i] = rings[stations - 1][segments - 1 - i];
            m.AddFace(bottom);
            return m;
        }

        /// <summary>
        /// [gas][launch] A model rocket on its pad, fired from the ground: a 41 mm kraft-paper airframe with an
        /// ogive nose and three plywood fins around a KNSB motor (two BATES grains Ø25 / core 8 × 60 mm,
        /// 6 → 14 mm nozzle, ≈ 127 N·s: a G-class motor). The launch controller — a 9 V battery, an arming
        /// switch that a 2 s countdown closes, and the e-match up the nozzle — fires the igniter through the
        /// circuit; the igniter's gas soaks the grain to its ignition temperature, the flame spreads, the
        /// rocket climbs on the thrust the chamber pressure gives, coasts to apogee where the altimeter
        /// throws a 45 cm parachute, and drifts down under it. The heat of the casing and nozzle shows in the
        /// thermal camera.
        /// </summary>
        public static WorldDocument SolidRocket(PartLibrary lib)
        {
            var w = new WorldDocument { Title = "Katı yakıtlı roket (e-kibritle fırlatma)" };
            // The swept fins' tips reach below their roots (0.065·tan 30° − (0.09 − 0.045)·¾ ≈ 3.8 mm): the rocket stands on its fin tips.
            const double padTop = 0.012, tipDrop = 0.004, finChord = 0.09, tube = 0.041, tubeLength = 0.50, tubeBottom = padTop + tipDrop + 0.02, motorBottom = tubeBottom - 0.01;

            var pad = w.AddBody("box", 0.35, 0.35, padTop, new WorldPose(), "ÇELİK");
            pad.Name = "Fırlatma rampası";
            pad.IsStatic = true;
            pad.Color = "#5A5E66";

            // The rocket stands on its three fins (a cylinder's end is no stable footing); the motor's aft closure and
            // nozzle stick 1 cm out of the airframe, 1 cm above the pad.
            var motor = w.AddBody("cylinder", 0.032, 0.032, 0.16, new WorldPose { Z = motorBottom }, "ALÜMİNYUM");
            motor.Name = "KNSB motor (G sınıfı)";
            motor.MassKg = 0.12;
            motor.Color = "#B8BCC4";
            motor.Parameters["vessel"] = "true";
            motor.Parameters["wall"] = "1.5";
            motor.Parameters["grain"] = "KNSB";
            motor.Parameters["grain.diameter"] = "25";
            motor.Parameters["grain.core"] = "8";
            motor.Parameters["grain.length"] = "60";
            motor.Parameters["grain.segments"] = "2";
            motor.Parameters["nozzle"] = "6";
            motor.Parameters["nozzle.exit"] = "14";
            motor.Parameters["nozzle.axis"] = "-z";
            motor.Parameters["igniter.charge"] = "0.5";

            var body = w.AddBody("cylinder", tube, tube, tubeLength, new WorldPose { Z = tubeBottom }, "KARTON");
            body.Name = "Gövde borusu (kraft, Ø41 mm)";
            body.MassKg = 0.14;   // tube 90 g + parachute and shock cord 50 g
            body.Color = "#E8E2D4";
            body.Parameters["parachute"] = "0.45";
            body.Parameters["parachute.deploy"] = "apogee";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = motor.Id, ObjectB = body.Id });

            var nose = w.AddModel(Ogive(tube * 1000 / 2, 110).ToMeshModel(), "Burun konisi (ogive)", new WorldPose { Z = tubeBottom + tubeLength }, "PLA");
            nose.MassKg = 0.05;
            nose.Color = "#E5484D";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = nose.Id, ObjectB = body.Id });

            for (int k = 0; k < 3; k++)
            {
                double a = k * 120.0, ca = Math.Cos(a * Math.PI / 180), sa = Math.Sin(a * Math.PI / 180);
                // Chord along the airframe (leading edge up), span straight out from the tube, the thin side across.
                var fin = w.AddBody("wing", finChord, 0.065, 0.003, new WorldPose { X = ca * tube / 2, Y = sa * tube / 2, Z = padTop + tipDrop + finChord / 2, Pitch = -90, Yaw = a - 90 }, "AHŞAP");
                fin.Name = "Kanatçık " + (k + 1) + " (3 mm kontrplak)";
                fin.MassKg = 0.016;
                fin.Color = "#2F2F35";
                fin.Parameters["symmetric"] = "false";   // one panel, not a pair
                fin.Parameters["airfoil"] = "flat";
                fin.Parameters["tipchord"] = "0.045";
                fin.Parameters["sweep"] = "30";
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = fin.Id, ObjectB = body.Id });
            }

            // Launch controller: battery, arming switch closed by the countdown, e-match up the nozzle.
            var ematch = w.AddComponent(lib, AvionicsParts.EMatchId, new WorldPose { X = 0.035, Z = padTop });   // its head is up the nozzle; the leads come out beside it
            ematch.Name = "E-kibrit (lüleye sokulu)";
            ematch.IsStatic = true;
            motor.Parameters["igniter"] = ematch.Id;
            var bat = w.AddComponent(lib, "bat-9v", new WorldPose { X = 0.55, Y = -0.05 });
            bat.Name = "Fırlatma pili 9 V";
            bat.IsStatic = true;
            var arm = w.AddComponent(lib, "toggle-spst", new WorldPose { X = 0.55, Y = 0.05 });
            arm.Name = "Fırlatma anahtarı (geri sayım: 2 s)";
            arm.IsStatic = true;
            arm.Parameters["close.at"] = "2.0";
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = arm.Id, Pin = "A" }, 22, "#E5484D").Length = 0.15;
            w.Connect(new CableEnd { ObjectId = arm.Id, Pin = "B" }, new CableEnd { ObjectId = ematch.Id, Pin = "A" }, 22, "#E5484D").Length = 0.7;
            w.Connect(new CableEnd { ObjectId = ematch.Id, Pin = "B" }, new CableEnd { ObjectId = bat.Id, Pin = "-" }, 22, "#1F1F23").Length = 0.7;
            return w;
        }
    }
}
