using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[objs] Ready wheels, casters and propellers; free-standing lights; positional audio maths.</summary>
    public class ReadyPartsTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public ReadyPartsTests(ITestOutputHelper output)
        {
            _out = output;
        }

        // TT gear motor 37 × 22.5 × 18.8 mm with its output shaft along +Y; 8520 coreless can standing up, shaft along +Z.
        private static readonly Vec3 TtPivot = new Vec3(-0.011, 0.0185, 0.0094);
        private static readonly Vec3 CorelessPivot = new Vec3(0, 0, 0.0255);

        private static WorldBodyGeometry Geometry(WorldObject o)
        {
            if (ReadyParts.Geometry(o) is { } g) return g;
            if (o.Kind == WorldObjectKind.Body)
            {
                return new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = o.MassKg ?? 0.05 };
            }

            if (o.PartId == "dc-motor" && (o.PresetId ?? string.Empty).Contains("8520"))
            {
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.00425, -0.00425, 0), Max = new Vec3(0.00425, 0.00425, 0.0208), MassKg = 0.0045,
                    PortName = "shaft", PortPivot = CorelessPivot, PortAxis = Vec3.UnitZ
                };
            }

            if (o.PartId == "dc-motor")
            {
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0185, -0.01125, 0), Max = new Vec3(0.0185, 0.01125, 0.0188), MassKg = 0.03,
                    PortName = "shaft", PortPivot = TtPivot, PortAxis = Vec3.UnitY
                };
            }

            return new WorldBodyGeometry { Min = new Vec3(-0.03, -0.02, 0), Max = new Vec3(0.03, 0.02, 0.015), MassKg = 0.05 };
        }

        private static (WorldCircuit Circuit, AnalogSimulator Sim, WorldPhysics Physics) Run(WorldDocument w, double seconds, double dt = 1e-3)
        {
            var wc = WorldCompiler.Compile(w, Lib, c => 0.2);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = dt });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            for (int i = 0; i < (int)Math.Round(seconds / dt); i++)
            {
                sim.Step(dt);
                p.Advance(dt, sim);
            }

            return (wc, sim, p);
        }

        // ================================================================ catalogue

        [Fact]
        public void CatalogueHasRealSizesAndMasses()
        {
            var tt = ReadyParts.Find("wheel-tt-65")!;
            Assert.Equal(0.065, tt.Diameter, 6);
            Assert.Equal(0.026, tt.Width, 6);
            Assert.InRange(tt.MassKg, 0.020, 0.040);
            Assert.Equal(0.043, ReadyParts.Find("wheel-43")!.Diameter, 6);
            Assert.Equal(0.085, ReadyParts.Find("wheel-offroad-85")!.Diameter, 6);
            Assert.Equal(0.127, ReadyParts.Find("prop-5045-ccw")!.Diameter, 6);
            Assert.Equal(0.1143, ReadyParts.Find("prop-5045-cw")!.Pitch, 4);
            Assert.Equal(3, ReadyParts.Find("prop-5040x3-ccw")!.Blades);
            Assert.Equal(45, ReadyParts.Find("mecanum-65-a")!.RollerAngleDeg);
            Assert.Equal(90, ReadyParts.Find("omni-60")!.RollerAngleDeg);
            foreach (var s in ReadyParts.All)
            {
                Assert.True(s.MassKg > 0 && s.Diameter > 0 && s.Width > 0, s.Id);
                if (s.IsWheel)
                {
                    // A real wheel is lighter than a solid rubber disc of its size, and heavier than 5 % of it.
                    double solid = Math.PI * s.Radius * s.Radius * s.Width * 1100;
                    Assert.InRange(s.MassKg, 0.05 * solid, solid);
                }
            }

            // Every propeller comes as a CCW/CW pair.
            foreach (var p in ReadyParts.All.Where(x => x.Kind == ReadyPartKind.Propeller && x.Hand > 0))
            {
                Assert.NotNull(ReadyParts.Find(p.Id.Replace("-ccw", "-cw")));
            }
        }

        [Fact]
        public void AddedPartCarriesItsCatalogueMassAndGeometry()
        {
            var w = new WorldDocument();
            var o = ReadyParts.Add(w, "wheel-tt-65", new WorldPose());
            Assert.Equal(ReadyParts.Shape, o.Shape);
            Assert.Equal(0.028, o.MassKg!.Value, 6);
            var g = ReadyParts.Geometry(o)!;
            Assert.Equal("cylinder-y", g.Shape);
            Assert.Equal(0.065, g.Max.Z - g.Min.Z, 6);
            Assert.Equal(0.026, g.Max.Y - g.Min.Y, 6);
            Assert.Equal("prop-z", ReadyParts.Geometry(ReadyParts.Add(w, "prop-55-ccw", new WorldPose()))!.Shape);
            Assert.Equal("mecanum-y", ReadyParts.Geometry(ReadyParts.Add(w, "mecanum-65-b", new WorldPose()))!.Shape);
            Assert.Equal("caster", ReadyParts.Geometry(ReadyParts.Add(w, "caster-15", new WorldPose()))!.Shape);
            var json = w.ToJson();
            var back = WorldDocument.FromJson(json);
            Assert.Equal("wheel-tt-65", ReadyParts.Of(back.Objects[0])!.Id);
        }

        // ================================================================ propeller aerodynamics

        [Theory]
        [InlineData("prop-5045-ccw", 10000)]
        [InlineData("prop-5040x3-cw", 12000)]
        [InlineData("prop-55-ccw", 30000)]
        [InlineData("prop-65-cw", 22000)]
        public void PropellerThrustAndTorqueFollowTheCoefficientLaws(string id, double rpm)
        {
            var p = ReadyParts.Find(id)!;
            double n = rpm / 60.0, rho = 1.225;
            double t = p.ThrustCoefficient * rho * n * n * Math.Pow(p.Diameter, 4);
            double q = p.PowerCoefficient / (2 * Math.PI) * rho * n * n * Math.Pow(p.Diameter, 5);
            Assert.Equal(t, PropellerAero.Thrust(p, rpm), 9);
            Assert.Equal(q, PropellerAero.Torque(p, rpm), 12);
            // Square law in speed, density proportional, k·ω² form identical.
            Assert.Equal(4 * t, PropellerAero.Thrust(p, 2 * rpm), 9);
            Assert.Equal(0.5 * t, PropellerAero.Thrust(p, rpm, rho / 2), 9);
            double w = rpm * 2 * Math.PI / 60;
            Assert.Equal(t, PropellerAero.ThrustPerOmega2(p) * w * w, 9);
            Assert.Equal(q, PropellerAero.TorquePerOmega2(p) * w * w, 12);
            Assert.Equal(rpm, PropellerAero.RpmForThrust(p, t), 6);
            // P = 2π n Q = C_P ρ n³ D⁵
            Assert.Equal(p.PowerCoefficient * rho * n * n * n * Math.Pow(p.Diameter, 5), PropellerAero.Power(p, rpm), 9);
            _out.WriteLine($"{p.Name}: {rpm:0} rpm → {t:0.000} N ({t / 9.81 * 1000:0} g), {q * 1000:0.000} mN·m, {PropellerAero.Power(p, rpm):0.0} W");
        }

        [Fact]
        public void A5045At10000RpmMakesAboutAHundredGrams()
        {
            // Thrust-stand data for 5×4.5" props: ≈ 100 g at 10 000 rpm, ≈ 400 g at 20 000 rpm.
            var p = ReadyParts.Find("prop-5045-ccw")!;
            double grams = PropellerAero.Thrust(p, 10000) / 9.81 * 1000;
            Assert.InRange(grams, 80, 130);
            Assert.InRange(PropellerAero.Thrust(p, 20000) / 9.81 * 1000, 330, 520);
        }

        // ================================================================ mounting

        [Theory]
        [InlineData(0, 1, 0)]
        [InlineData(0, -1, 0)]
        [InlineData(-1, 0, 0)]
        [InlineData(0, 0, 1)]
        [InlineData(0.6, 0, 0.8)]
        public void SnapPosePutsTheHubOnTheShaft(double ax, double ay, double az)
        {
            var axis = new Vec3(ax, ay, az).Normalized();
            var tip = new Vec3(0.1, -0.05, 0.04);
            foreach (var id in new[] { "wheel-tt-65", "prop-5045-ccw", "mecanum-65-a" })
            {
                var s = ReadyParts.Find(id)!;
                var pose = ReadyParts.SnapPose(s, tip, axis);
                var hubAxis = pose.Orientation.Rotate(ReadyParts.HubAxis(s));
                Assert.True(hubAxis.Dot(axis) > 0.9999, $"{id}: hub axis {hubAxis} vs shaft {axis}");
                var hub = s.Kind == ReadyPartKind.Propeller ? pose.Position : pose.ToWorld(new Vec3(0, 0, s.Radius));
                // On the shaft line …
                var off = hub - tip;
                Assert.True((off - axis * off.Dot(axis)).Length < 1e-9, $"{id}: hub off the shaft line");
                // … at the catalogue depth.
                double along = off.Dot(axis);
                double expected = s.Kind == ReadyPartKind.Propeller ? -(s.Width - s.MountOffset) : s.MountOffset;
                Assert.Equal(expected, along, 9);
            }
        }

        [Fact]
        public void AttachToPortMakesTheShaftJoint()
        {
            var w = new WorldDocument();
            var motor = w.AddComponent(Lib, "motor-tt", new WorldPose { X = 0.1, Z = 0.02, Yaw = 90 });
            var wheel = ReadyParts.Add(w, "wheel-tt-65", new WorldPose { X = -0.3 });
            var j = ReadyParts.AttachToPort(w, wheel, motor, "shaft", TtPivot, Vec3.UnitY)!;
            Assert.Equal("shaft", j.PortB);
            Assert.Equal(motor.Id, j.ObjectB);
            var tip = motor.Pose.ToWorld(TtPivot);
            var hub = wheel.Pose.ToWorld(new Vec3(0, 0, 0.0325));
            // Yaw 90°: the shaft points along −X; the hub sits 6.5 mm beyond the tip.
            Assert.Equal(tip.X - 0.0065, hub.X, 6);
            Assert.Equal(tip.Z, hub.Z, 6);
            Assert.Single(w.Joints);
        }

        // ================================================================ physics

        [Fact]
        public void PropellerLoadsItsMotorAndPushesAlongTheShaft()
        {
            WorldDocument Build(bool withProp)
            {
                var w = new WorldDocument();
                var stand = w.AddBody("box", 0.04, 0.04, 0.03, new WorldPose());
                stand.IsStatic = true;
                var motor = w.AddComponent(Lib, "motor-8520", new WorldPose { Z = 0.03 });
                var bat = w.AddComponent(Lib, "bat-1s-lipo-300", new WorldPose { X = 0.15 });
                w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = motor.Id, ObjectB = stand.Id });
                if (withProp)
                {
                    var prop = ReadyParts.Add(w, "prop-55-ccw", new WorldPose { X = -0.2 });
                    ReadyParts.AttachToPort(w, prop, motor, "shaft", CorelessPivot, Vec3.UnitZ);
                }

                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 22);
                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 22);
                return w;
            }

            var w = Build(true);
            var (wc, _, p) = Run(w, 0.6);
            var motor = wc.Compiled.DevicesOf(wc.ComponentRef(w.Objects[1].Id)!).OfType<DcMotor>().Single();
            var prop = w.Objects.First(o => ReadyParts.Of(o) != null);
            var spec = ReadyParts.Of(prop)!;
            var r = p.PropReadout(prop.Id)!.Value;
            _out.WriteLine($"8520 (48 000 rpm / 0.15 A unloaded) with 55 mm prop: {motor.OutputRpm:0} rpm {motor.Current:0.00} A, " +
                           $"prop {r.Rpm:0} rpm, thrust {r.Thrust:0.000} N ({r.Thrust / 9.81 * 1000:0.0} g), Q {r.Torque * 1000:0.000} mN·m, load {motor.LoadTorque * 1000:0.000} mN·m");

            // The prop turns with the shaft, in its working direction.
            Assert.InRange(r.Rpm, motor.OutputRpm * 0.95, motor.OutputRpm * 1.05);
            // Thrust and torque from the coefficient laws at the simulated speed.
            // At the scene's air density (the world environment computes it from temperature/pressure).
            Assert.Equal(PropellerAero.Thrust(spec, r.Rpm, p.Scene.AirDensity), r.Thrust, 6);
            Assert.Equal(PropellerAero.Torque(spec, r.Rpm, p.Scene.AirDensity), r.Torque, 9);
            // The motor feels the drag torque: well below its 48 000 rpm no-load speed, many times its 0.15 A no-load current.
            Assert.True(motor.OutputRpm < 0.75 * 48000, "the prop must load the motor");
            Assert.True(motor.Current > 5 * 0.15, "loaded motor draws more current");
            Assert.InRange(motor.LoadTorque, 0.9 * r.Torque, 1.1 * r.Torque);
            // 8520 on a 1S LiPo with a 55 mm prop: ≈ 25 000 rpm, 1.5–2.5 A, ≈ 20–30 g of thrust.
            Assert.InRange(motor.OutputRpm, 18000, 34000);
            Assert.InRange(motor.Current, 1.0, 3.0);
            Assert.InRange(r.Thrust / 9.81 * 1000, 12, 40);
        }

        [Fact]
        public void FreePropellerAssemblyLiftsOffWhenThrustBeatsWeight()
        {
            // Two 8520 motors on a light 10 g plate with a CCW and a CW 55 mm prop (their reaction
            // torques cancel, as on every real multirotor), powered through long leads by a fixed
            // battery: ≈ 2 × 23 g of thrust against ≈ 22 g of plate, motors and props.
            // (A single free motor spins itself up against the prop's reaction torque on a real floor,
            // which the material-pair floor friction now reproduces — so it is not a lift-off case.)
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-1s-lipo-300", new WorldPose { X = 0.25 });
            bat.IsStatic = true;
            var plate = w.AddBody("plate", 0.10, 0.02, 0.002, new WorldPose(), "PLA");
            plate.MassKg = 0.010;
            foreach (var (x, propId) in new[] { (-0.035, "prop-55-ccw"), (0.035, "prop-55-cw") })
            {
                var motor = w.AddComponent(Lib, "motor-8520", new WorldPose { X = x, Z = 0.002 });
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = motor.Id, ObjectB = plate.Id });
                var prop = ReadyParts.Add(w, propId, new WorldPose { X = x, Y = -0.2 });
                ReadyParts.AttachToPort(w, prop, motor, "shaft", CorelessPivot, Vec3.UnitZ);
                // CW props are mounted on motors turning the other way: swap the leads.
                bool cw = propId.EndsWith("-cw");
                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = cw ? "-" : "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 26);
                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = cw ? "+" : "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 26);
            }

            var (_, _, p) = Run(w, 0.5);
            var pose = p.PoseOf(plate.Id)!.Value;
            _out.WriteLine($"plate after 0.5 s: z = {pose.Position.Z * 1000:0} mm, tilt {Math.Acos(pose.Orientation.Rotate(Vec3.UnitZ).Z) * 180 / Math.PI:0}°");
            Assert.True(pose.Position.Z > 0.05, "thrust larger than weight must lift the plate");
        }

        [Fact]
        public void MecanumRobotStrafesSideways()
        {
            var w = new WorldDocument();
            double motorZ = 0.0325 - TtPivot.Z;
            var chassis = w.AddBody("plate", 0.17, 0.08, 0.003, new WorldPose { Z = motorZ + 0.0188 });   // wheels run clear of the edge
            chassis.MassKg = 0.06;
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { Z = chassis.Pose.Z + 0.003 });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = bat.Id, ObjectB = chassis.Id });
            // Front +X, left +Y. A wheels front-left and rear-right, B wheels front-right and rear-left.
            var corners = new[] { (Name: "FL", X: 0.055, Left: true, Wheel: "mecanum-65-a", Polarity: 1), (Name: "FR", X: 0.055, Left: false, Wheel: "mecanum-65-b", Polarity: 1),
                                  (Name: "RL", X: -0.055, Left: true, Wheel: "mecanum-65-b", Polarity: -1), (Name: "RR", X: -0.055, Left: false, Wheel: "mecanum-65-a", Polarity: -1) };
            foreach (var c in corners)
            {
                var m = w.AddComponent(Lib, "motor-tt", new WorldPose { X = c.X, Y = c.Left ? 0.03 : -0.03, Z = motorZ, Yaw = c.Left ? 0 : 180 });
                m.Name = c.Name;
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = m.Id, ObjectB = chassis.Id });
                var wheel = ReadyParts.Add(w, c.Wheel, new WorldPose());
                ReadyParts.AttachToPort(w, wheel, m, "shaft", TtPivot, Vec3.UnitY);
                // Strafing to +Y: front motors forward-left pattern, rear motors reversed.
                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = c.Polarity > 0 ? "+" : "-" }, new CableEnd { ObjectId = m.Id, Pin = "A" }, 22);
                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = c.Polarity > 0 ? "-" : "+" }, new CableEnd { ObjectId = m.Id, Pin = "B" }, 22);
            }

            var (_, _, p) = Run(w, 1.5);
            var pose = p.PoseOf(chassis.Id)!.Value;
            double dx = pose.Position.X, dy = pose.Position.Y;
            double yaw = pose.Orientation.ToEuler().Yaw * 180 / Math.PI;
            _out.WriteLine($"mecanum chassis after 1.5 s: dx = {dx * 1000:0} mm, dy = {dy * 1000:0} mm, yaw {yaw:0.0}°, z {pose.Position.Z * 1000:0} mm");
            Assert.True(Math.Abs(dy) > 0.05, "it must move sideways");
            Assert.True(Math.Abs(dy) > 3 * Math.Abs(dx), "sideways, not forward");
            Assert.InRange(Math.Abs(yaw), 0, 20);
        }

        // ================================================================ lights

        [Fact]
        public void RgbLedLightsOnlyTheDrivenColour()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-2aa", new WorldPose());
            var res = w.AddComponent(Lib, "res-47", new WorldPose { X = 0.05 });
            var led = w.AddComponent(Lib, "led-rgb-5mm", new WorldPose { X = 0.1 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = res.Id, Pin = "A" });
            w.Connect(new CableEnd { ObjectId = res.Id, Pin = "B" }, new CableEnd { ObjectId = led.Id, Pin = "R" });
            w.Connect(new CableEnd { ObjectId = led.Id, Pin = "K" }, new CableEnd { ObjectId = bat.Id, Pin = "-" });
            var wc = WorldCompiler.Compile(w, Lib, c => 0.2);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            for (int i = 0; i < 20; i++) sim.Step(1e-3);
            var leds = wc.Compiled.DevicesOf(wc.ComponentRef(led.Id)!).OfType<Led>().ToList();
            Assert.Equal(3, leds.Count);
            var red = leds.Single(l => l.Id.EndsWith(".R", StringComparison.Ordinal));
            _out.WriteLine(string.Join(", ", leds.Select(l => $"{l.Id}: {l.Current * 1000:0.0} mA")));
            Assert.InRange(red.Current, 0.005, 0.03);
            Assert.All(leds.Where(l => l != red), l => Assert.True(l.Current < 1e-4));
        }

        [Fact]
        public void PowerLedRunsAtItsRatedCurrentWithoutBurning()
        {
            // 3 W LED on a Li-ion cell through 1 Ω (the usual flashlight circuit): ≈ 0.7 A, 2.4 W in the die, survives on its heat sink.
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-18650", new WorldPose());
            var res = w.AddComponent(Lib, "res-1R-1W", new WorldPose { X = 0.05 });
            var led = w.AddComponent(Lib, "led-3w-white", new WorldPose { X = 0.1 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = res.Id, Pin = "A" }, 22);
            w.Connect(new CableEnd { ObjectId = res.Id, Pin = "B" }, new CableEnd { ObjectId = led.Id, Pin = "A" }, 22);
            w.Connect(new CableEnd { ObjectId = led.Id, Pin = "K" }, new CableEnd { ObjectId = bat.Id, Pin = "-" }, 22);
            var wc = WorldCompiler.Compile(w, Lib, c => 0.2);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3, EnableDamage = true });
            sim.Initialize(InitialState.OperatingPoint);
            for (int i = 0; i < 2000; i++) sim.Step(1e-3);
            var d = wc.Compiled.DevicesOf(wc.ComponentRef(led.Id)!).OfType<Led>().Single();
            _out.WriteLine($"3 W LED: {d.Current * 1000:0} mA, brightness {d.Brightness:0.00}, failed {d.IsFailed}");
            Assert.False(d.IsFailed);
            Assert.InRange(d.Current, 0.45, 0.95);
        }

        // ================================================================ positional audio

        [Fact]
        public void AttenuationFollowsTheInverseDistanceLaw()
        {
            double r = SpatialAudio.ReferenceDistance;
            Assert.Equal(1.0, SpatialAudio.Attenuation(0.1), 9);   // clamped inside the reference distance
            Assert.Equal(1.0, SpatialAudio.Attenuation(r), 9);
            Assert.Equal(0.5, SpatialAudio.Attenuation(2 * r), 9);
            Assert.Equal(0.25, SpatialAudio.Attenuation(4 * r), 9);
            Assert.True(SpatialAudio.Attenuation(3) < SpatialAudio.Attenuation(2));
        }

        [Fact]
        public void PanningIsConstantPowerAndFollowsTheCamera()
        {
            foreach (double p in new[] { -1, -0.5, 0, 0.3, 1 })
            {
                var (l, rr) = SpatialAudio.Pan(p);
                Assert.Equal(2.0, l * l + rr * rr, 9);
            }

            Assert.Equal(1.0, SpatialAudio.Pan(0).Left, 9);
            // Camera at the origin looking along +Y, Z up: +X is to the right.
            var ear = new AudioListener(Vec3.Zero, Vec3.UnitY, Vec3.UnitZ);
            var right = SpatialAudio.Compute(ear, new Vec3(1, 0, 0));
            var left = SpatialAudio.Compute(ear, new Vec3(-1, 0, 0));
            var ahead = SpatialAudio.Compute(ear, new Vec3(0, 1, 0));
            var behind = SpatialAudio.Compute(ear, new Vec3(0, -1, 0));
            var far = SpatialAudio.Compute(ear, new Vec3(0, 4, 0));
            Assert.True(right.Right > 2 * right.Left);
            Assert.True(left.Left > 2 * left.Right);
            Assert.Equal(ahead.Left, ahead.Right, 9);
            // Behind sounds quieter and duller than the same distance ahead.
            Assert.True(behind.Left < ahead.Left);
            Assert.True(behind.CutoffLeft < 0.5 * ahead.CutoffLeft);
            // The far ear is muffled (head shadow).
            Assert.True(right.CutoffLeft < right.CutoffRight);
            // Four times farther: a quarter of the level (beyond the reference distance) and fewer highs.
            Assert.Equal(ahead.Left * 0.25, far.Left, 9);
            Assert.True(far.CutoffLeft < ahead.CutoffLeft);
            // Turning the camera around swaps the ears.
            var turned = SpatialAudio.Compute(new AudioListener(Vec3.Zero, -Vec3.UnitY, Vec3.UnitZ), new Vec3(1, 0, 0));
            Assert.True(turned.Left > 2 * turned.Right);
        }

        [Fact]
        public void DopplerRaisesApproachingAndLowersReceding()
        {
            var ear = new AudioListener(Vec3.Zero, Vec3.UnitY, Vec3.UnitZ);
            var src = new Vec3(0, 2, 0);
            double toward = SpatialAudio.Compute(ear, src, new Vec3(0, -5, 0)).Pitch;
            double away = SpatialAudio.Compute(ear, src, new Vec3(0, 5, 0)).Pitch;
            Assert.Equal(343.0 / 338.0, toward, 9);
            Assert.Equal(343.0 / 348.0, away, 9);
            Assert.Equal(1.0, SpatialAudio.Compute(ear, src).Pitch, 9);
        }
    }
}
