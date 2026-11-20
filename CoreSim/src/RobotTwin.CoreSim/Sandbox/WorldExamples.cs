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
        /// [aero] 1 m model glider: Clark Y wing (2° incidence), NACA 0012 tail with an elevator linked
        /// to an SG90 servo by a 0.5 pushrod, fin, nose weight for a stable CG; hand-launched at
        /// 12 m/s from 40 m. Static margin ≈ 25 % of the chord: it settles into a ~7:1 glide.
        /// </summary>
        public static WorldDocument Glider(PartLibrary lib)
        {
            var w = new WorldDocument { Title = "Planör" };
            double z = 40;
            var fus = w.AddBody("box", 0.60, 0.05, 0.05, new WorldPose { Z = z }, "PLA");
            fus.Name = "Gövde";
            fus.MassKg = 0.25;
            fus.Parameters["launch"] = "12";
            var nose = w.AddBody("box", 0.05, 0.04, 0.04, new WorldPose { X = 0.25, Z = z + 0.005 }, "ÇELİK");
            nose.Name = "Burun ağırlığı";
            nose.MassKg = 0.07;
            var wing = w.AddBody("wing", 0.15, 1.0, 0.012, new WorldPose { X = -0.02, Z = z + 0.05 }, "AHŞAP");
            wing.Name = "Kanat";
            wing.MassKg = 0.08;
            wing.Parameters["airfoil"] = "clarky";
            wing.Parameters["incidence"] = "2";
            var servo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = 0.10, Y = 0.0, Z = z + 0.05 });
            var tail = w.AddBody("wing", 0.08, 0.30, 0.006, new WorldPose { X = -0.28, Z = z + 0.05 }, "AHŞAP");
            tail.Name = "Yatay kuyruk";
            tail.MassKg = 0.015;
            tail.Parameters["airfoil"] = "naca0012";
            tail.Parameters["incidence"] = "-3";
            tail.Parameters["control"] = "elevator";
            tail.Parameters["control.chord"] = "0.35";
            tail.Parameters["control.servo"] = servo.Id;
            tail.Parameters["control.gain"] = "0.5";
            tail.Parameters["downwash"] = wing.Id;
            var fin = w.AddBody("wing", 0.08, 0.10, 0.006, new WorldPose { X = -0.28, Z = z + 0.056 }, "AHŞAP");
            fin.Name = "Dikey kuyruk";
            fin.MassKg = 0.008;
            fin.Parameters["airfoil"] = "naca0012";
            fin.Parameters["symmetric"] = "false";
            foreach (var part in new WorldObject[] { nose, wing, tail, fin, servo })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = fus.Id });
            return w;
        }

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
