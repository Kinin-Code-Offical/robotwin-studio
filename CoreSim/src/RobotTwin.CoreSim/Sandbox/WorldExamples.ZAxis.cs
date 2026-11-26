using System.Collections.Generic;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// [stepper] A Z axis as on a 3D printer (template "z-axis"): an Arduino Uno runs the built-in z-axis-stepper sketch
        /// (D2 DIR, D3 STEP, D8 EN) into an A4988 fed from a 12 V supply; the NEMA 17 turns a T8×8 lead screw through a
        /// coupler, the screw's top runs in a 608 bearing, and a brass nut lifts an aluminium carriage riding two Ø8 rods on
        /// LM8UUs. Only the carriage's screws are joined by hand: the assembler finds the rest from where the parts sit.
        /// With <paramref name="stepRate"/> above zero a square wave steps the driver instead of the Uno.
        /// </summary>
        public static WorldDocument ZAxis(PartLibrary lib, double stepRate = 0)
        {
            var w = new WorldDocument { Title = "Z ekseni" };
            WorldObject Component(string id, double x, double y, Dictionary<string, string>? parameters = null)
            {
                var o = w.AddComponent(lib, id, new WorldPose { X = x, Y = y });
                o.IsStatic = true;
                if (parameters != null)
                    foreach (var kv in parameters) o.Parameters[kv.Key] = kv.Value;
                return o;
            }

            WorldObject Put(string id, double x, double y, double z, bool frame = false)
            {
                var o = DriveParts.Add(w, id, new WorldPose { X = x, Y = y, Z = z });
                o.IsStatic = frame;
                return o;
            }

            void Wire(WorldObject a, string pa, WorldObject b, string pb, string colour) =>
                w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, 22, colour);

            const string red = "#E5484D", black = "#2B2B2B", yellow = "#F2A33B", green = "#3FA34D", blue = "#2E7DB8";
            var vmot = Component("dc-source", 0.15, 0.1, new Dictionary<string, string> { ["voltage"] = "12" });
            vmot.Name = "12 V";
            var vdd = Component("dc-source", 0.2, 0.1, new Dictionary<string, string> { ["voltage"] = "5" });
            vdd.Name = "5 V";
            var drv = Component("a4988", 0.15, -0.1);
            var motor = Component("nema17-17hs4401", 0, 0);
            Wire(vmot, "+", drv, "VMOT", red);
            Wire(vmot, "-", drv, "GND", black);
            Wire(vdd, "+", drv, "VDD", red);
            Wire(vdd, "-", drv, "GND2", black);
            if (stepRate > 0)
            {
                var gen = Component("signal-generator", 0.25, 0.1, new Dictionary<string, string>
                {
                    ["frequency"] = stepRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["amplitude"] = "5",
                    ["duty"] = "0.1"
                });
                gen.Name = "STEP";
                Wire(vdd, "+", drv, "DIR", blue);
                Wire(gen, "+", drv, "STEP", yellow);
                Wire(gen, "-", drv, "GND2", black);
            }
            else
            {
                // The sketch steps it; the Uno runs from the same 5 V as the driver's logic.
                var uno = Component("arduino-uno", 0.25, -0.1, new Dictionary<string, string> { ["firmware"] = "builtin:z-axis-stepper.hex" });
                Wire(vdd, "+", uno, "5V", red);
                Wire(vdd, "-", uno, "GND1", black);
                Wire(uno, "D2", drv, "DIR", blue);
                Wire(uno, "D3", drv, "STEP", yellow);
                Wire(uno, "D8", drv, "EN", green);
            }

            Wire(drv, "1A", motor, "A+", black);
            Wire(drv, "1B", motor, "A-", green);
            Wire(drv, "2A", motor, "B+", red);
            Wire(drv, "2B", motor, "B-", blue);
            // The mechanics, placed: the coupler over the motor's shaft (42–64 mm up) and the screw's foot in it.
            Put("coupler-5-8", 0, 0, 0.048);
            Put("screw-t8x8-300", 0, 0, 0.062);
            Put("bearing-608zz", 0, 0, 0.33, true);
            Put("shaft-8-300", 0, 0.035, 0.062, true);
            Put("shaft-8-300", 0, -0.035, 0.062, true);
            var nut = Put("nut-t8x8", 0, 0, 0.15);
            var lm1 = Put("lm8uu", 0, 0.035, 0.15);
            var lm2 = Put("lm8uu", 0, -0.035, 0.15);
            var plate = new WorldObject
            {
                Id = w.NextId("O"), Name = "Taşıyıcı", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.02, SizeY = 0.1, SizeZ = 0.024,
                MassKg = 0.2, Material = "aluminium", Pose = new WorldPose { X = 0.021, Z = 0.15 }
            };
            w.Objects.Add(plate);
            foreach (var part in new[] { nut, lm1, lm2 })
            {
                w.Joints.Add(new WorldJoint
                {
                    Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = part.Id, ObjectB = plate.Id, Fastening = "screws",
                    AnchorX = part.Pose.X, AnchorY = part.Pose.Y, AnchorZ = part.Pose.Z
                });
            }

            return w;
        }
    }
}
