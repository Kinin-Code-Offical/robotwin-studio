using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// Arduino Uno drives two SG90s with its own pulses (no library): the shoulder servo on a free plywood base, the
        /// elbow servo screwed onto the shoulder's arm, so the arm turns as a chain (template "servo-arm").
        /// </summary>
        public static WorldDocument ServoArm(PartLibrary lib)
        {
            var w = new WorldDocument { Title = "Servo kol" };
            var uno = w.AddComponent(lib, "arduino-uno", new WorldPose { X = -0.15, Y = 0 });   // [phys] beside the wider base
            uno.Parameters["firmware"] = "builtin:servo-sweep.hex";
            // [phys] A free, heavy plywood base (140×100×18 mm ≈ 150 g): the arm's centre of mass stays
            // inside its footprint at every angle, so it stands by its own weight — nothing is clamped.
            // (The former 60×40×12 mm block weighed 17 g and only stood because it was marked "Sabit".)
            const double baseH = 0.018;
            var table = w.AddBody("box", 0.14, 0.10, baseH, new WorldPose { X = 0.04, Y = 0 }, "AHŞAP");
            table.Name = "Taban (18 mm kontrplak)";
            var s1 = w.AddComponent(lib, "servo-sg90", new WorldPose { X = 0.04, Y = 0, Z = baseH });
            s1.Name = "Omuz servosu";
            // Horn pivot of an SG90 sits 5.4 mm from the body centre, 31 mm above its base.
            double pivotX = s1.Pose.X - 0.0054, pivotZ = s1.Pose.Z + 0.031;
            var arm1 = w.AddBody("box", 0.11, 0.018, 0.003, new WorldPose { X = pivotX + 0.045, Z = pivotZ + 0.0002 }, "PLA");
            arm1.Name = "Kol 1";
            arm1.Color = "#E87A2C";
            var s2 = w.AddComponent(lib, "servo-sg90", new WorldPose { X = pivotX + 0.085, Z = arm1.Pose.Z + 0.003 });
            s2.Name = "Dirsek servosu";
            double pivot2X = s2.Pose.X - 0.0054, pivot2Z = s2.Pose.Z + 0.031;
            var arm2 = w.AddBody("box", 0.08, 0.014, 0.003, new WorldPose { X = pivot2X + 0.032, Z = pivot2Z + 0.0002 }, "PLA");
            arm2.Name = "Kol 2";
            arm2.Color = "#2E7DB8";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = s1.Id, ObjectB = table.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = arm1.Id, ObjectB = s1.Id, PortB = "horn" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = s2.Id, ObjectB = arm1.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = arm2.Id, ObjectB = s2.Id, PortB = "horn" });
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "5V" }, new CableEnd { ObjectId = s1.Id, Pin = "V+" }, 24, "#E5484D");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND1" }, new CableEnd { ObjectId = s1.Id, Pin = "GND" }, 24, "#5A3A22");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D9" }, new CableEnd { ObjectId = s1.Id, Pin = "SIG" }, 24, "#F2A33B");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "5V" }, new CableEnd { ObjectId = s2.Id, Pin = "V+" }, 24, "#E5484D");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND2" }, new CableEnd { ObjectId = s2.Id, Pin = "GND" }, 24, "#5A3A22");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D10" }, new CableEnd { ObjectId = s2.Id, Pin = "SIG" }, 24, "#F2A33B");
            return w;
        }
    }
}
