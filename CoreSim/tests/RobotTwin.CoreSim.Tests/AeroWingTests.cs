using System;
using System.Linq;
using RobotTwin.CoreSim.Aero;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[aero] Drawn wings and tails fly: lift-curve slope, static stability, elevator authority.</summary>
    public class AeroWingTests
    {
        private readonly ITestOutputHelper _out;

        public AeroWingTests(ITestOutputHelper output) => _out = output;

        private static WorldBodyGeometry Geometry(WorldObject o) => new WorldBodyGeometry
        {
            Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0),
            Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ),
            MassKg = o.MassKg ?? 0.05
        };

        [Fact]
        public void RectangularWingLiftSlopeMatchesLiftingLineTheory()
        {
            // AR 6 rectangular wing, NACA 0012 (symmetric: zero lift at α = 0).
            var wing = new LiftingSurface { Span = 0.9, RootChord = 0.15, TipChord = 0.15, Airfoil = ParametricAirfoil.Naca0012, StripsPerSide = 12, Oswald = 1.0 };
            double v = 15, rho = 1.225, alpha = 4 * Math.PI / 180;
            var loads = wing.Evaluate(new AeroState(new Vec3(v * Math.Cos(alpha), 0, -v * Math.Sin(alpha)), Vec3.Zero, rho, 343, 1.81e-5));
            double q = 0.5 * rho * v * v;
            double lift = loads.Force.Z * Math.Cos(alpha) + loads.Force.X * Math.Sin(alpha);
            double cl = lift / (q * wing.Area);
            double a0 = ParametricAirfoil.Naca0012.LiftSlope;
            double expected = a0 / (1 + a0 / (Math.PI * wing.AspectRatio)) * alpha;   // Prandtl lifting line, elliptic loading
            _out.WriteLine($"AR {wing.AspectRatio:F1}: C_L = {cl:F3} at 4°, lifting line {expected:F3}");
            Assert.InRange(cl / expected, 0.93, 1.07);
        }

        [Fact]
        public void InducedDragFollowsLiftingLineTheory()
        {
            // Drag polar of an AR 6 wing: C_D = C_d(section) + C_L² / (π·e·AR).
            var wing = new LiftingSurface { Span = 0.9, RootChord = 0.15, TipChord = 0.15, Airfoil = ParametricAirfoil.Naca0012, StripsPerSide = 12, Oswald = 1.0 };
            double v = 15, rho = 1.225, q = 0.5 * rho * v * v;
            (double Cl, double Cd) At(double deg)
            {
                double a = deg * Math.PI / 180;
                var l = wing.Evaluate(new AeroState(new Vec3(v * Math.Cos(a), 0, -v * Math.Sin(a)), Vec3.Zero, rho, 343, 1.81e-5));
                var flow = new Vec3(-Math.Cos(a), 0, Math.Sin(a));   // direction the air moves (body frame)
                double d = l.Force.Dot(flow);
                double lift = (l.Force - flow * d).Length * Math.Sign(l.Force.Z);
                return (lift / (q * wing.Area), d / (q * wing.Area));
            }

            var (_, cd0) = At(0);
            var (cl, cd) = At(6);
            double induced = cl * cl / (Math.PI * wing.AspectRatio);
            _out.WriteLine($"C_D0 {cd0:F4}; at 6°: C_L {cl:F3}, C_D {cd:F4}, induced part {cd - cd0:F4} vs theory {induced:F4}");
            Assert.True(cd0 > 0.004 && cd0 < 0.03, "profile drag of a thin section at Re ≈ 150 000");
            Assert.InRange((cd - cd0) / induced, 0.8, 1.4);   // section drag also rises a little with α
        }

        private (WorldDocument World, WorldObject Fuselage, WorldObject Tail) Glider(double elevator = 0, double noseKg = 0.07, double tailIncidence = -3)
        {
            var w = new WorldDocument();
            double z = 80;
            var fus = w.AddBody("box", 0.60, 0.05, 0.05, new WorldPose { Z = z }, "PLA");
            fus.MassKg = 0.25;
            var nose = w.AddBody("box", 0.05, 0.04, 0.04, new WorldPose { X = 0.25, Z = z + 0.005 }, "ÇELİK");
            nose.MassKg = noseKg;
            var wing = w.AddBody("wing", 0.15, 1.0, 0.012, new WorldPose { X = -0.02, Z = z + 0.05 }, "AHŞAP");
            wing.MassKg = 0.08;
            wing.Parameters["airfoil"] = "clarky";
            wing.Parameters["incidence"] = "2";
            var tail = w.AddBody("wing", 0.08, 0.30, 0.006, new WorldPose { X = -0.28, Z = z + 0.05 }, "AHŞAP");
            tail.MassKg = 0.015;
            tail.Parameters["airfoil"] = "naca0012";
            tail.Parameters["incidence"] = tailIncidence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tail.Parameters["control"] = "elevator";
            tail.Parameters["control.chord"] = "0.35";
            tail.Parameters["control.deflection"] = elevator.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tail.Parameters["downwash"] = wing.Id;
            var fin = w.AddBody("wing", 0.08, 0.10, 0.006, new WorldPose { X = -0.28, Z = z + 0.056 }, "AHŞAP");
            fin.MassKg = 0.008;
            fin.Parameters["airfoil"] = "naca0012";
            fin.Parameters["symmetric"] = "false";
            foreach (var part in new[] { nose, wing, tail, fin })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = part.Id, ObjectB = fus.Id });
            return (w, fus, tail);
        }

        private static (double Pitch, Vec3 Position) Fly(WorldDocument w, WorldObject fus, double seconds, Action<double, WorldPhysics>? each = null)
        {
            var p = WorldPhysics.Build(w, Geometry, null);
            foreach (var b in p.Scene.Bodies)
                if (!b.IsStatic) b.Velocity = new Vec3(12, 0, 0);   // hand launch at 12 m/s, level
            int steps = (int)(seconds / 1e-3);
            for (int i = 0; i < steps; i++)
            {
                p.Advance(1e-3, null);
                each?.Invoke((i + 1) * 1e-3, p);
            }

            var pose = p.PoseOf(fus.Id)!.Value;
            var nose = pose.Orientation.Rotate(Vec3.UnitX);
            return (Math.Asin(Math.Max(-1, Math.Min(1, nose.Z))) * 180 / Math.PI, pose.Position);
        }

        [Fact]
        public void GliderSettlesIntoAStableGlide()
        {
            // Launched faster than its trim speed, a statically stable glider pitches into a phugoid
            // (slow speed/height exchange, period ≈ π·√2·V/g ≈ 5 s) that decays into a steady glide.
            var (w, fus, _) = Glider();
            double early = 0, late = 0;
            Vec3 at1 = Vec3.Zero;
            var (_, end) = Fly(w, fus, 9.0, (t, p) =>
            {
                var pose = p.PoseOf(fus.Id)!.Value;
                double pc = Math.Asin(Math.Max(-1, Math.Min(1, pose.Orientation.Rotate(Vec3.UnitX).Z))) * 180 / Math.PI;
                if (t > 1 && t < 4) early = Math.Max(early, Math.Abs(pc));
                if (t > 6) late = Math.Max(late, Math.Abs(pc));
                if (Math.Abs(t - 1.0) < 5e-4) at1 = pose.Position;
            });
            double dx = end.X - at1.X, dz = at1.Z - end.Z;
            _out.WriteLine($"pitch swing 1–4 s {early:F1}°, 6–9 s {late:F1}°; glide 1→9 s {dx:F1} m forward, {dz:F1} m down, L/D {dx / dz:F1}");
            Assert.True(late < early, "the phugoid decays");
            Assert.True(late < 25, "no tumbling or stall oscillation");
            Assert.InRange(dx / dz, 5, 15);   // 1 m span, AR 6.7 model glider with a box fuselage
        }

        [Fact]
        public void ServoLinkedElevatorFollowsTheServoHorn()
        {
            var w = new WorldDocument();
            var servo = w.AddComponent(RobotTwin.CoreSim.Design.PartLibrary.Default, "servo-sg90", new WorldPose());
            servo.IsStatic = true;
            var bat = w.AddComponent(RobotTwin.CoreSim.Design.PartLibrary.Default, "bat-4aa", new WorldPose { X = -0.2 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = servo.Id, Pin = "V+" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = servo.Id, Pin = "GND" });
            var tail = w.AddBody("wing", 0.08, 0.30, 0.006, new WorldPose { X = 0.3, Z = 0.5 }, "AHŞAP");
            tail.IsStatic = true;   // held in a 12 m/s wind tunnel
            tail.Parameters["airfoil"] = "naca0012";
            tail.Parameters["control"] = "elevator";
            tail.Parameters["control.servo"] = servo.Id;
            tail.Parameters["control.gain"] = "0.5";   // pushrod: surface moves half the horn angle
            w.Environment.WindX = -12;
            var wc = WorldCompiler.Compile(w, RobotTwin.CoreSim.Design.PartLibrary.Default, c => 0.2);
            var s = wc.Compiled.DevicesOf(wc.ComponentRef(servo.Id)!).OfType<RobotTwin.CoreSim.Analog.Devices.Servo>().Single();
            var sim = new RobotTwin.CoreSim.Analog.AnalogSimulator(wc.Compiled.Circuit, new RobotTwin.CoreSim.Analog.AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(RobotTwin.CoreSim.Analog.InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            typeof(RobotTwin.CoreSim.Analog.Devices.Servo).GetProperty("TargetAngleDeg")!.SetValue(s, 135.0);   // +45° from centre
            for (int i = 0; i < 1500; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
            }

            double horn = s.AngleDeg - 90;   // servo output relative to centre (pushrod: nothing mounted on the horn)
            double delta = p.WingDeflection(tail.Id);
            var loads = p.WingLoads(tail.Id)!.Value;
            _out.WriteLine($"horn {horn:F1}°, elevator {delta:F1}°, tail force z {loads.Force.Z:F3} N");
            Assert.InRange(horn, 40, 50);
            Assert.InRange(delta, horn * 0.5 - 0.5, horn * 0.5 + 0.5);
            Assert.True(loads.Force.Z > 0.05, "trailing edge down → the tail lifts");
        }

        [Fact]
        public void UpElevatorRaisesTheNoseAndClimbs()
        {
            var (w0, f0, _) = Glider(0);
            var (w1, f1, _) = Glider(-10);   // trailing edge up
            var (p0, x0) = Fly(w0, f0, 0.6);
            var (p1, x1) = Fly(w1, f1, 0.6);
            _out.WriteLine($"t = 0.6 s: neutral pitch {p0:F1}°, z {x0.Z:F3} m; up elevator pitch {p1:F1}°, z {x1.Z:F3} m");
            Assert.True(p1 > p0 + 5, "up elevator pitches the nose up");
            Assert.True(x1.Z > x0.Z, "and the aircraft climbs relative to neutral");
        }
    
        [Fact]
        public void GliderExampleFliesInTheHeadlessRunner()
        {
            {
                string inc = "-2";
                var w = WorldExamples.Glider(RobotTwin.CoreSim.Design.PartLibrary.Default);
                var pod = w.Objects.First(o => o.Name.StartsWith("Gövde"));
                using var r = new RobotTwin.CoreSim.Automation.WorldRunner(w, new RobotTwin.CoreSim.Automation.RunnerOptions { Firmware = false });
                Assert.True(r.TryGetBody(pod.Id, out var s0));
                double mass = 0;
                Vec3 cg = Vec3.Zero;
                foreach (var b in r.Physics!.Scene.Bodies.Where(b => !b.IsStatic))
                {
                    mass += b.Mass;
                    cg += b.Position * b.Mass;
                }

                cg /= mass;
                double maxPitch = 0;
                for (int k = 0; k < 50; k++)
                {
                    r.Step(0.1);
                    Assert.True(r.TryGetBody(pod.Id, out var sk));
                    double pitch = Math.Asin(Math.Max(-1, Math.Min(1, sk.Orientation.Rotate(Vec3.UnitX).Z))) * 180 / Math.PI;
                    if (k >= 20) maxPitch = Math.Max(maxPitch, Math.Abs(pitch));
                }

                Assert.True(r.TryGetBody(pod.Id, out var s1));
                double dx = s1.Position.X - s0.Position.X, dz = s0.Position.Z - s1.Position.Z;
                _out.WriteLine($"tail {inc}°: mass {mass * 1000:F0} g, CG x {cg.X * 1000:F1} mm (wing LE at 80 mm → {(0.08 - cg.X) / 0.15 * 100:F0} % chord); 5 s: {dx:F1} m fwd, {dz:F1} m down, L/D {dx / Math.Max(1e-6, dz):F1}, speed {s1.Velocity.Length:F1} m/s, max |pitch| 2–5 s {maxPitch:F0}°");
                Assert.InRange(mass, 0.20, 0.26);                       // a 1 m balsa/carbon glider with electronics
                Assert.InRange((0.08 - cg.X) / 0.15, 0.28, 0.38);       // CG at ~1/3 of the chord
                Assert.InRange(dx / dz, 7, 14);                          // model thermal glider glide ratio
                Assert.InRange(s1.Velocity.Length, 4, 8);
                Assert.True(maxPitch < 15, "settles into a steady glide");
            }
        }
}
}
