using System;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Examples
{
    /// <summary>[objs] Worlds with ready propellers / mecanum wheels and free-standing lights.</summary>
    public static partial class WorldTemplates
    {
        private static Vec3 V(System.Windows.Media.Media3D.Point3D p) => new Vec3(p.X, p.Y, p.Z);

        private static Vec3 V(System.Windows.Media.Media3D.Vector3D v) => new Vec3(v.X, v.Y, v.Z);

        private static void Wire(WorldDocument w, WorldObject a, string pa, WorldObject b, string pb, double awg = 24, string? colour = null) =>
            w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, awg, colour ?? (pa.Contains('-') || pb.Contains('-') || pb == "K" ? "#1F1F23" : "#E5484D"));

        /// <summary>Two 8520 motors on wooden stands with 55 and 65 mm props, one 1S LiPo.</summary>
        private static WorldDocument PropStand()
        {
            var w = new WorldDocument { Title = "Pervane itki standı" };
            var (pivot, axis, _) = World.ObjectModels.Mount(Lib, "motor-8520");
            var bat = w.AddComponent(Lib, "bat-1s-lipo-300", new WorldPose { X = 0, Y = -0.11 });
            string[] props = { "prop-55-ccw", "prop-65-ccw" };
            for (int i = 0; i < 2; i++)
            {
                double x = i == 0 ? -0.08 : 0.08;
                var stand = w.AddBody("box", 0.04, 0.04, 0.09, new WorldPose { X = x }, "AHŞAP");
                stand.Name = "Stand " + (i + 1);
                stand.IsStatic = true;
                var motor = w.AddComponent(Lib, "motor-8520", new WorldPose { X = x, Z = 0.09 });
                motor.Name = "8520 motor " + (i + 1);
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = motor.Id, ObjectB = stand.Id });
                var prop = ReadyParts.Add(w, props[i], new WorldPose { X = x, Y = 0.1 });
                ReadyParts.AttachToPort(w, prop, motor, "shaft", V(pivot), V(axis));
                Wire(w, bat, "+", motor, "A", 24, "#E5484D");
                Wire(w, bat, "-", motor, "B", 24, "#1F1F23");
            }

            return w;
        }

        /// <summary>X-frame micro quad: four 8520 motors, diagonal CCW / CW props, 1S LiPo on top. No controller: all motors on.</summary>
        private static WorldDocument MiniDrone()
        {
            var w = new WorldDocument { Title = "Mini drone" };
            var (pivot, axis, _) = World.ObjectModels.Mount(Lib, "motor-8520");
            const double arm = 0.13, th = 0.002, r = 0.0424;
            var a1 = w.AddBody("box", arm, 0.010, th, new WorldPose { Yaw = 45 }, "KARBON");
            var a2 = w.AddBody("box", arm, 0.010, th, new WorldPose { Yaw = 135 }, "KARBON");
            a1.Name = "Kol 1";
            a2.Name = "Kol 2";
            a1.MassKg = a2.MassKg = 0.003;
            a1.Color = a2.Color = "#2A2A2E";
            var plate = w.AddBody("plate", 0.04, 0.04, th, new WorldPose { Z = th }, "KARBON");
            plate.Name = "Gövde";
            plate.MassKg = 0.004;
            plate.Color = "#2A2A2E";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = a1.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = a2.Id, ObjectB = plate.Id });
            var bat = w.AddComponent(Lib, "bat-1s-lipo-300", new WorldPose { Z = 2 * th, Yaw = 90 });
            bat.Name = "1S LiPo";
            // Without a flight controller nothing corrects a tilt: the pack's centre of mass must sit on the frame's centre.
            var body = World.ObjectModels.Build(bat, w, Lib);
            var cb = body.CollisionBounds.IsEmpty ? body.Bounds : body.CollisionBounds;
            var off = bat.Pose.Orientation.Rotate(new Vec3(cb.X + cb.SizeX / 2, cb.Y + cb.SizeY / 2, 0));
            bat.Pose.X -= off.X;
            bat.Pose.Y -= off.Y;
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = bat.Id, ObjectB = plate.Id });
            // Front-right and rear-left spin CCW, the other diagonal CW (reversed leads, CW props): yaw torques cancel.
            var corners = new[] { (X: r, Y: r, Ccw: true, Arm: a1, Name: "Ön sol"), (X: r, Y: -r, Ccw: false, Arm: a2, Name: "Ön sağ"), (X: -r, Y: -r, Ccw: true, Arm: a1, Name: "Arka sağ"), (X: -r, Y: r, Ccw: false, Arm: a2, Name: "Arka sol") };
            foreach (var c in corners)
            {
                // Leads toward the middle of the frame, where the battery is.
                var m = w.AddComponent(Lib, "motor-8520", new WorldPose { X = c.X, Y = c.Y, Z = th, Yaw = Math.Atan2(-c.Y, -c.X) * 180 / Math.PI });
                m.Name = c.Name + " motor";
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = m.Id, ObjectB = c.Arm.Id });
                var prop = ReadyParts.Add(w, c.Ccw ? "prop-55-ccw" : "prop-55-cw", new WorldPose());
                ReadyParts.AttachToPort(w, prop, m, "shaft", V(pivot), V(axis));
                Wire(w, bat, c.Ccw ? "+" : "-", m, "A", 26, c.Ccw ? "#E5484D" : "#1F1F23");
                Wire(w, bat, c.Ccw ? "-" : "+", m, "B", 26, c.Ccw ? "#1F1F23" : "#E5484D");
            }

            return w;
        }

        /// <summary>Four TT motors with A/B mecanum wheels wired for strafing (front motors forward, rear reversed).</summary>
        private static WorldDocument Mecanum()
        {
            var w = new WorldDocument { Title = "Mekanum araba" };
            var (pivot, axis, _) = World.ObjectModels.Mount(Lib, "motor-tt");
            var wheel = ReadyParts.Find("mecanum-65-a")!;
            double R = wheel.Radius;
            double top = R + 0.0112;
            var chassis = w.AddBody("plate", 0.19, 0.07, 0.003, new WorldPose { Z = top }, "AKRİLİK");   // wheels run clear of the edge
            chassis.Name = "Şase";
            chassis.Color = "#3E7CC8";
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { Z = top + 0.003, Yaw = 90 });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = bat.Id, ObjectB = chassis.Id });
            // Seen from above the rollers of the four wheels form an X: A front-left / rear-right, B front-right / rear-left.
            var corners = new[]
            {
                (Name: "Sol ön", X: 0.06, Left: true, Wheel: "mecanum-65-a", Forward: true),
                (Name: "Sağ ön", X: 0.06, Left: false, Wheel: "mecanum-65-b", Forward: true),
                (Name: "Sol arka", X: -0.06, Left: true, Wheel: "mecanum-65-b", Forward: false),
                (Name: "Sağ arka", X: -0.06, Left: false, Wheel: "mecanum-65-a", Forward: false)
            };
            foreach (var c in corners)
            {
                // Gearboxes mirrored front/back so the motor cans point to the middle.
                bool flip = c.Left == (c.X > 0);
                var pose = new WorldPose { Yaw = c.Left ? 0 : 180, Pitch = flip ? 180 : 0 };
                var tip = new Vec3(c.X, (c.Left ? 1 : -1) * (0.045 + pivot.Y), R);
                var p = tip - pose.Orientation.Rotate(V(pivot));
                pose.X = p.X;
                pose.Y = p.Y;
                pose.Z = p.Z;
                var m = w.AddComponent(Lib, "motor-tt", pose);
                m.Name = c.Name + " motor";
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = m.Id, ObjectB = chassis.Id });
                var wh = ReadyParts.Add(w, c.Wheel, new WorldPose());
                wh.Name = c.Name + " tekerlek";
                ReadyParts.AttachToPort(w, wh, m, "shaft", V(pivot), V(axis));
                Wire(w, bat, c.Forward ? "+" : "-", m, "A", 22, c.Forward ? "#E5484D" : "#1F1F23");
                Wire(w, bat, c.Forward ? "-" : "+", m, "B", 22, c.Forward ? "#1F1F23" : "#E5484D");
            }

            return w;
        }

        /// <summary>Dark room: indicator LEDs with resistors, an RGB LED, a 3 W LED, a flashlight bulb and a 12 V LED strip.</summary>
        private static WorldDocument Lights()
        {
            var w = new WorldDocument { Title = "Işıklar", RoomLight = 0.06 };
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.26, Y = 0.1, Yaw = 90 });
            void Indicator(string led, string res, double x)
            {
                var r = w.AddComponent(Lib, res, new WorldPose { X = x, Y = 0.04, Yaw = 90 });
                var d = w.AddComponent(Lib, led, new WorldPose { X = x, Y = -0.03 });
                Wire(w, bat, "+", r, "A", 26, "#E5484D");
                Wire(w, r, "B", d, "A", 26, "#F2A33B");
                Wire(w, d, "K", bat, "-", 26, "#1F1F23");
            }

            Indicator("led5-red", "res-220", -0.17);
            Indicator("led5-green", "res-180", -0.11);
            Indicator("led5-blue", "res-150", -0.05);
            // RGB LED: red and blue dies on → purple.
            var rgb = w.AddComponent(Lib, "led-rgb-5mm", new WorldPose { X = 0.02, Y = -0.03 });
            var rr = w.AddComponent(Lib, "res-220", new WorldPose { X = 0.0, Y = 0.04, Yaw = 90 });
            var rb = w.AddComponent(Lib, "res-150", new WorldPose { X = 0.04, Y = 0.04, Yaw = 90 });
            Wire(w, bat, "+", rr, "A", 26, "#E5484D");
            Wire(w, bat, "+", rb, "A", 26, "#E5484D");
            Wire(w, rr, "B", rgb, "R", 26, "#E5484D");
            Wire(w, rb, "B", rgb, "B", 26, "#3E7CC8");
            Wire(w, rgb, "K", bat, "-", 26, "#1F1F23");
            // 3 W LED on an 18650 through a 1 Ω resistor.
            var cell = w.AddComponent(Lib, "bat-18650", new WorldPose { X = 0.13, Y = 0.12 });
            var r1 = w.AddComponent(Lib, "res-1R-1W", new WorldPose { X = 0.13, Y = 0.05, Yaw = 90 });
            var power = w.AddComponent(Lib, "led-3w-white", new WorldPose { X = 0.13, Y = -0.04 });
            Wire(w, cell, "+", r1, "A", 22, "#E5484D");
            Wire(w, r1, "B", power, "A", 22, "#E5484D");
            Wire(w, power, "K", cell, "-", 22, "#1F1F23");
            // Flashlight bulb on a second cell.
            var cell2 = w.AddComponent(Lib, "bat-18650", new WorldPose { X = 0.26, Y = 0.12 });
            var bulb = w.AddComponent(Lib, "lamp-3v8-0w3", new WorldPose { X = 0.26, Y = -0.03 });
            Wire(w, cell2, "+", bulb, "A", 22, "#E5484D");
            Wire(w, bulb, "B", cell2, "-", 22, "#1F1F23");
            // 12 V LED strip on a lead-acid battery.
            var sla = w.AddComponent(Lib, "bat-12v-sla", new WorldPose { X = -0.05, Y = 0.3 });
            var strip = w.AddComponent(Lib, "led-strip-12v", new WorldPose { X = -0.12, Y = -0.17 });
            Wire(w, sla, "+", strip, "+", 22, "#E5484D");
            Wire(w, strip, "-", sla, "-", 22, "#1F1F23");
            return w;
        }
    }
}
