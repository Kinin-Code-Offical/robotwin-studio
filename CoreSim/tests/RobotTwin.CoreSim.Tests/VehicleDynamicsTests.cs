using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Drivetrain, tyres, environment and structural failures of whole vehicles (circuit + mechanics).</summary>
    public class VehicleDynamicsTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public VehicleDynamicsTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static (CompiledCircuit Circuit, AnalogSimulator Sim, MechatronicSystem Mech) Start(Schematic sch)
        {
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var mech = new MechatronicSystem(sch.Assembly!, sch, Lib, compiled, sim);
            return (compiled, sim, mech);
        }

        private static void Run(AnalogSimulator sim, MechatronicSystem mech, double seconds, Action<double>? each = null)
        {
            const double dt = 1e-3;
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++)
            {
                sim.Step(dt);
                mech.Advance(dt);
                each?.Invoke(sim.Time);
            }
        }

        private static void Rel(double expected, double actual, double tol, string what) =>
            Assert.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:G5}, got {actual:G5} ({(actual - expected) / expected:P1})");

        /// <summary>The line follower drives straight at full power on a floor without tape.</summary>
        private static Schematic Runner(WorldSpec world)
        {
            var sch = VehicleTemplates.LineFollower(Lib);
            sch.Assembly!.Arena = "custom";
            sch.Assembly.World = world;
            return sch;
        }

        // ================================================================== tyre and gears

        [Fact]
        public void BrushTyreReachesTheSteadyStateCharacteristicAndRelaxesOverItsLength()
        {
            var tyre = new BrushTyre { SlipStiffness = 12, CorneringStiffness = 10, RelaxationLength = 0.006 };
            double n = 2.0, mu = 0.9, muK = 0.75, v = 0.5;
            // Small longitudinal slip: F = C_κ·κ·N (the brush curve is linear to 0.5 % at κ = 0.001).
            double kappa = 0.001, f = 0;
            for (int i = 0; i < 4000; i++) f = tyre.Update(n, v, 0, v * (1 + kappa), mu, muK, 1e-4).Fx;
            Rel(12 * kappa * n, f, 0.01, "linear slip stiffness");
            // Large slip: the brush characteristic, then sliding friction.
            tyre.Reset();
            kappa = 0.08;
            for (int i = 0; i < 4000; i++) f = tyre.Update(n, v, 0, v * (1 + kappa), mu, muK, 1e-4).Fx;
            Rel(BrushTyre.Characteristic(kappa, 12, mu, muK) * n, f, 0.01, "brush characteristic");
            tyre.Reset();
            for (int i = 0; i < 4000; i++) f = tyre.Update(n, v, 0, v * 2, mu, muK, 1e-4).Fx;
            Rel(muK * n, f, 0.02, "full sliding at kinetic friction");
            // Relaxation: after rolling one relaxation length the deflection reaches 1 − 1/e of steady state.
            tyre.Reset();
            double steps = tyre.RelaxationLength / v / 1e-4;
            for (int i = 0; i < (int)Math.Round(steps); i++) f = tyre.Update(n, v, 0, v * 1.005, mu, muK, 1e-4).Fx;
            Rel(1 - Math.Exp(-1), tyre.DeflectionX / (tyre.RelaxationLength * 0.005), 0.02, "first-order lag over σ");
            // Cornering: small slip angle (0.1°) → F_y = C_α·tan α·N; friction circle never exceeded.
            tyre.Reset();
            double alpha = 0.1 * Math.PI / 180, fy = 0;
            for (int i = 0; i < 4000; i++) fy = tyre.Update(n, v, -v * Math.Tan(alpha), v, mu, muK, 1e-4).Fy;
            Rel(10 * Math.Tan(alpha) * n, fy, 0.01, "cornering stiffness");
            var both = tyre.Update(n, v, -v * 0.5, v * 3, mu, muK, 1e-4);
            Assert.True(Math.Sqrt(both.Fx * both.Fx + both.Fy * both.Fy) <= mu * n + 1e-9);
        }

        [Fact]
        public void GearboxBacklashLosesExactlyItsFreePlayOnReversal()
        {
            // The gear turns forward 20°, then backwards; the wheel carries a Coulomb friction load.
            // Once the gear has crossed the gap the teeth drive the other flank: wheel − gear = backlash.
            var gb = new Gearbox { Backlash = 3 * Math.PI / 180 };
            gb.Reset();
            const double h = 1e-3, j = 1e-5, friction = 0.01;
            double gearAngle = 0, wheelAngle = 0, prev = 0, forwardEnd = 20 * Math.PI / 180;
            bool reversed = false;
            for (int i = 0; i < 300; i++)
            {
                if (gearAngle >= forwardEnd) reversed = true;
                double w = reversed ? -2.0 : 2.0;
                // Coulomb friction that stops the free wheel without reversing it.
                double stop = gb.WheelSpeed * j / h;
                double load = Math.Abs(stop) <= friction ? stop : friction * Math.Sign(gb.WheelSpeed);
                double ww = gb.Step(w, prev, load, j, h);
                prev = w;
                gearAngle += w * h;
                wheelAngle += ww * h;
            }

            _out.WriteLine($"gear {gearAngle * 180 / Math.PI:F2}°, wheel {wheelAngle * 180 / Math.PI:F2}°, engaged {gb.Engaged}");
            Assert.Equal(-1, gb.Engaged);
            // Within one step of gear travel (2 rad/s × 1 ms ≈ 4 % of the gap).
            Rel(gb.Backlash, wheelAngle - gearAngle, 0.05, "lost motion = backlash");
            Assert.Equal(0.9 * 0.9 * 0.9, Gearbox.TypicalEfficiency(48), 9);
            Assert.Equal(1.0, Gearbox.TypicalEfficiency(1), 9);
        }
        // ================================================================== load fed back to the motors

        [Fact]
        public void ClimbingASlopeRaisesMotorCurrentByTheGravityLoad()
        {
            double Current(double slopeDeg, out MechatronicSystem mech, out double speed)
            {
                var (_, sim, m) = Start(Runner(new WorldSpec { Width = 4, Depth = 2, Terrain = new WorldTerrain { Kind = "slope", SlopeDeg = slopeDeg } }));
                var motors = sim.Circuit.Devices.OfType<DcMotor>().ToList();
                double sum = 0, v = 0;
                int k = 0;
                Run(sim, m, 1.6, t =>
                {
                    if (t < 0.8) return;
                    sum += motors.Average(x => Math.Abs(x.Current));
                    v += m.Speed;
                    k++;
                });
                mech = m;
                speed = v / k;
                return sum / k;
            }

            double flat = Current(0, out var mech0, out double v0);
            double up = Current(10, out var mech, out double v1);
            var motor = mech.Wheels[0].Motor!;
            double theta = 10 * Math.PI / 180;
            // Extra traction per wheel m·g·sinθ/2 → output torque · R → motor torque /(G·η) → current /Kt.
            double expected = mech.TotalMass * 9.81 * Math.Sin(theta) / 2 * mech.Wheels[0].Wheel.Radius /
                              (motor.GearRatio * motor.GearEfficiency * motor.MotorConstant);
            _out.WriteLine($"flat {flat:F3} A at {v0:F2} m/s, 10° up {up:F3} A at {v1:F2} m/s, Δ {up - flat:F3} A (expected {expected:F3} A), η {motor.GearEfficiency:F2}");
            Rel(expected, up - flat, 0.25, "current rise on the slope");
            Assert.True(v1 < v0, "slower uphill (back-EMF)");
        }

        [Fact]
        public void PushingAMovableBoxLoadsTheMotorsWithItsSlidingFriction()
        {
            var world = new WorldSpec { Width = 4, Depth = 2, FloorMaterial = "wood" };
            world.Obstacles.Add(new WorldObstacle { X = 0.45, Y = 0, SizeX = 0.1, SizeY = 0.2, Height = 0.06, Dynamic = true, Mass = 0.2, Material = "wood" });
            var (_, sim, mech) = Start(Runner(world));
            var motors = sim.Circuit.Devices.OfType<DcMotor>().ToList();
            var box = mech.World.Movables.Single();
            double x0 = box.Position.X, free = 0, push = 0;
            int nf = 0, np = 0;
            Run(sim, mech, 2.0, t =>
            {
                double i = motors.Average(m => Math.Abs(m.Current));
                bool touching = box.Position.X - x0 > 0.01;
                if (t > 0.3 && !touching && box.Velocity.Length < 1e-3)
                {
                    free += i;
                    nf++;
                }
                else if (t > 1.0)
                {
                    push += i;
                    np++;
                }
            });
            free /= nf;
            push /= np;
            var motor = motors[0];
            double mu = Materials.Pair("wood", "wood").KineticFriction;
            double expected = mu * 0.2 * 9.81 / 2 * mech.Wheels[0].Wheel.Radius / (motor.GearRatio * motor.GearEfficiency * motor.MotorConstant);
            _out.WriteLine($"box moved {box.Position.X - x0:F2} m; current free {free:F3} A, pushing {push:F3} A (Δ {push - free:F3}, expected {expected:F3})");
            Assert.True(box.Position.X - x0 > 0.3, "the robot pushes the box");
            Rel(expected, push - free, 0.35, "extra current while pushing");
        }

        [Fact]
        public void ParkedRoverHoldsOnASlopeThroughTyreGripAndGearFriction()
        {
            var sch = new Schematic();
            var bat = sch.AddPart(Lib, "bat-4aa", new GridPoint(0, 0));
            VehicleTemplates.Tie(sch, Lib, bat, "+", "V5");
            VehicleTemplates.Tie(sch, Lib, bat, "-", "GND");
            sch.Parts.Add(new SchematicPart { RefDes = "M1", PartId = "motor-tt", Position = new GridPoint(40, 0) });
            sch.Parts.Add(new SchematicPart { RefDes = "M2", PartId = "motor-tt", Position = new GridPoint(50, 0) });
            sch.Assembly = AssemblyDocument.AutoConfigure(sch, Lib, new AssemblyDocument
            {
                Kind = VehicleKind.Rover, Arena = "custom",
                World = new WorldSpec { FloorMaterial = "wood", Terrain = new WorldTerrain { Kind = "slope", SlopeDeg = 8 } }
            });
            var (_, sim, mech) = Start(sch);
            Run(sim, mech, 0.5);
            var p0 = mech.Body.Position;
            Run(sim, mech, 2.0);
            double crept = (mech.Body.Position - p0).Length;
            _out.WriteLine($"crept {crept * 1000:F2} mm in 2 s on 8°");
            Assert.True(crept < 3e-3, $"crept {crept * 1000:F1} mm");
        }

        /// <summary>Two TT motors wired straight to a 4×NiMH pack: drives regardless of what the floor looks like.</summary>
        private static Schematic DirectDrive(string arena)
        {
            var sch = new Schematic();
            var bat = sch.AddPart(Lib, "bat-4nimh", new GridPoint(0, 0));
            VehicleTemplates.Tie(sch, Lib, bat, "+", "VBAT");
            VehicleTemplates.Tie(sch, Lib, bat, "-", "GND");
            foreach (int x in new[] { 30, 40 })
            {
                var m = sch.AddPart(Lib, "motor-tt", new GridPoint(x, 0));
                VehicleTemplates.Tie(sch, Lib, m, "A", "VBAT");
                VehicleTemplates.Tie(sch, Lib, m, "B", "GND");
            }

            sch.Assembly = AssemblyDocument.AutoConfigure(sch, Lib, new AssemblyDocument { Kind = VehicleKind.Rover, Arena = arena });
            return sch;
        }

        [Fact]
        public void RoverDrivesUpTheRampOntoThePlatform()
        {
            var (_, sim, mech) = Start(DirectDrive("ramps"));
            double zStart = mech.Body.Position.Z, maxPitch = 0;
            Run(sim, mech, 3.0, _ => maxPitch = Math.Max(maxPitch, Math.Abs(mech.Body.Orientation.ToEuler().Pitch)));
            var p = mech.Body.Position;
            _out.WriteLine($"pos {p}, rose {(p.Z - zStart) * 1000:F0} mm, max pitch {maxPitch * 180 / Math.PI:F1}°");
            Assert.True(p.X > 0.05, "reached the platform");
            Assert.InRange(p.Z - zStart, 0.07, 0.09);
            Assert.InRange(maxPitch, 5 * Math.PI / 180, 12 * Math.PI / 180);
            Assert.DoesNotContain(mech.DrainEvents(), e => e.Severe);
        }

        // ================================================================== servo-driven links

        private static Schematic ServoArm(double pulseMs, out AssemblyDocument asm)
        {
            var sch = new Schematic();
            var bat = sch.AddPart(Lib, "bat-4aa", new GridPoint(0, 0));
            VehicleTemplates.Tie(sch, Lib, bat, "+", "V5");
            VehicleTemplates.Tie(sch, Lib, bat, "-", "GND");
            var servo = sch.AddPart(Lib, "servo-sg90", new GridPoint(20, 0));
            VehicleTemplates.Tie(sch, Lib, servo, "V+", "V5");
            VehicleTemplates.Tie(sch, Lib, servo, "GND", "GND");
            VehicleTemplates.Tie(sch, Lib, servo, "SIG", "PWM");
            var gen = sch.AddPart(Lib, "signal-generator", new GridPoint(10, 10));
            gen.Parameters["frequency"] = "50";
            gen.Parameters["amplitude"] = "5";
            gen.Parameters["duty"] = (pulseMs / 20).ToString(System.Globalization.CultureInfo.InvariantCulture);
            gen.Parameters["rs"] = "1";
            VehicleTemplates.Tie(sch, Lib, gen, "+", "PWM");
            VehicleTemplates.Tie(sch, Lib, gen, "-", "GND");
            sch.Parts.Add(new SchematicPart { RefDes = "M1", PartId = "motor-tt", Position = new GridPoint(40, 0) });
            sch.Parts.Add(new SchematicPart { RefDes = "M2", PartId = "motor-tt", Position = new GridPoint(50, 0) });
            asm = AssemblyDocument.AutoConfigure(sch, Lib, new AssemblyDocument { Kind = VehicleKind.Rover, Arena = "empty" });
            asm.Links.Add(new LinkMount
            {
                Name = "kol", X = 0.08, Y = 0, Z = 0.03, AxisY = 1, Length = 0.12, Width = 0.02, Thickness = 0.006, Mass = 0.03, ServoRefDes = servo.RefDes,
                ServoOffsetDeg = 90
            });
            sch.Assembly = asm;
            return sch;
        }

        [Fact]
        public void ServoArmFollowsThePulseAndStallsAgainstTheFloor()
        {
            // 1.0 ms → 44° servo angle → joint −46° (arm raised); the arm holds there at idle-ish current.
            var (_, sim, mech) = Start(ServoArm(1.0, out _));
            var link = mech.Links.Single();
            var servo = link.Servo!;
            Run(sim, mech, 1.0);
            double target = (servo.TargetAngleDeg - 90);
            _out.WriteLine($"pulse 1.0 ms: servo target {servo.TargetAngleDeg:F1}°, joint {link.JointValue:F1}°, current {servo.SupplyCurrent:F3} A, horn {servo.AngleDeg:F1}°");
            Assert.InRange(link.JointValue, target - 2.0, target + 2.0);
            Assert.InRange(servo.SupplyCurrent, servo.IdleCurrent, 0.5 * servo.StallCurrent);
            Assert.Equal(servo.AngleDeg, 90 + link.JointValue, 6);

            // 2.4 ms → 180° → joint +90°: the arm is driven down into the floor and the servo stalls.
            var (_, sim2, mech2) = Start(ServoArm(2.4, out _));
            var link2 = mech2.Links.Single();
            var servo2 = link2.Servo!;
            var battery = sim2.Circuit.Devices.OfType<Battery>().First();
            Run(sim2, mech2, 1.0);
            _out.WriteLine($"pulse 2.4 ms: joint {link2.JointValue:F1}°, servo current {servo2.SupplyCurrent:F3} A, battery {battery.Current:F3} A, tip z {link2.Body.ToWorld(new Vec3(0.06, 0, 0)).Z * 1000:F1} mm");
            Assert.True(link2.JointValue < 85, "blocked by the floor");
            // Stall current scales with the supply (4×AA ≈ 6 V against the 4.8 V rating).
            double v = Math.Min(1.4, link2.ServoMotor!.SupplyVoltage / link2.ServoMotor.RatedVoltage);
            double stall = servo2.IdleCurrent + (servo2.StallCurrent - servo2.IdleCurrent) * v;
            Rel(stall, servo2.SupplyCurrent, 0.05, "stall current");
            Assert.True(Math.Abs(battery.Current) > 0.9 * stall, "the stall current flows in the circuit");
        }

        // ================================================================== structural failures

        [Fact]
        public void QuadcopterArmSnapsInAHighFallButNotAHop()
        {
            foreach (double height in new[] { 0.3, 6.0 })
            {
                var (_, sim, mech) = Start(VehicleTemplates.MicroQuadcopter(Lib));
                Run(sim, mech, 0.05);
                mech.Body.Position = new Vec3(0, 0, height);
                Run(sim, mech, Math.Sqrt(2 * height / 9.81) + 0.3);
                var events = mech.DrainEvents();
                foreach (var e in events) _out.WriteLine($"{height} m: {e.Time:F2} {e.Source}: {e.Message}");
                _out.WriteLine($"{height} m: peak {mech.PeakAcceleration / 9.81:F0} g, {mech.PeakImpactForce:F1} N, arms broken {mech.Props.Count(p => p.ArmBroken)}");
                if (height < 1)
                {
                    Assert.DoesNotContain(mech.Props, p => p.ArmBroken);
                }
                else
                {
                    Assert.Contains(mech.Props, p => p.ArmBroken);
                    Assert.Contains(events, e => e.Message.Contains("kol kırıldı"));
                    Assert.True(mech.PeakAcceleration > 50 * 9.81);
                    Assert.NotEmpty(mech.Impacts);
                }
            }
        }

        [Fact]
        public void RoverDroppedFromHeightLosesAWheelAndItsBattery()
        {
            foreach (double height in new[] { 0.1, 5.0 })
            {
                var (_, sim, mech) = Start(VehicleTemplates.LineFollower(Lib));
                Run(sim, mech, 0.05);
                double mass0 = mech.Body.Mass;
                mech.Body.Position += new Vec3(0, 0, height);
                Run(sim, mech, Math.Sqrt(2 * height / 9.81) + 0.3);
                var events = mech.DrainEvents();
                foreach (var e in events) _out.WriteLine($"{height} m: {e.Time:F2} {e.Source}: {e.Message}");
                _out.WriteLine($"{height} m: peak {mech.PeakAcceleration / 9.81:F0} g, {mech.PeakImpactForce:F0} N, wheels off {mech.Wheels.Count(w => w.Detached)}, modules off {mech.DetachedModules.Count}");
                if (height < 1)
                {
                    Assert.DoesNotContain(events, e => e.Severe);
                    continue;
                }

                Assert.Contains(mech.Wheels, w => w.Detached);
                Assert.Contains(mech.DetachedModules, d => d.Mount.RefDes.StartsWith("BT"));
                Assert.True(mech.Body.Mass < mass0, "the battery's mass left the chassis");
                mech.Reset();
                Assert.DoesNotContain(mech.Wheels, w => w.Detached);
                Assert.Empty(mech.DetachedModules);
                Assert.Equal(mass0, mech.Body.Mass, 9);
            }
        }

        // ================================================================== air and environment

        [Fact]
        public void QuadcopterCannotTakeOffInMartianAir()
        {
            var sch = VehicleTemplates.MicroQuadcopter(Lib);
            sch.Assembly!.Arena = "custom";
            sch.Assembly.World = new WorldSpec { Gravity = 3.71, AirDensity = 0.020 };
            var (_, sim, mech) = Start(sch);
            mech.Flight!.Mode = FlightMode.AltitudeHold;
            mech.Flight.TargetAltitude = 0.8;
            mech.Flight.Armed = true;
            Run(sim, mech, 2.0);
            double thrust = mech.Props.Sum(p => p.Prop.Thrust);
            _out.WriteLine($"z {mech.Body.Position.Z:F3} m, total thrust {thrust * 1000:F1} mN vs weight {mech.TotalMass * 3.71 * 1000:F1} mN");
            Assert.True(mech.Body.Position.Z < 0.05, "stays on the ground");
            Assert.True(thrust < mech.TotalMass * 3.71);
        }

        [Fact]
        public void CrosswindBlowsAHoveringQuadcopterDownwind()
        {
            var sch = VehicleTemplates.MicroQuadcopter(Lib);
            sch.Assembly!.Arena = "custom";
            sch.Assembly.World = new WorldSpec { Width = 20, Depth = 20, WindY = 2.0 };
            var (_, sim, mech) = Start(sch);
            mech.Flight!.Mode = FlightMode.AltitudeHold;
            mech.Flight.Throttle = 0.5;
            mech.Flight.TargetAltitude = 0.8;
            mech.Flight.Armed = true;
            Run(sim, mech, 4.0);
            var p = mech.Body.Position;
            double rotorDrag = mech.Props.Sum(x => x.Prop.RotorDrag);
            _out.WriteLine($"pos {p}, vy {mech.Body.Velocity.Y:F2} m/s, rotor drag {rotorDrag * 1000:F1} mN");
            Assert.True(p.Y > 1.0, "drifted downwind");
            Assert.InRange(mech.Body.Velocity.Y, 0.5, 2.2);
            Assert.InRange(p.Z, 0.6, 1.0);
        }

        // ================================================================== documents

        [Fact]
        public void NewAssemblyAndWorldFieldsRoundTripAndOldFilesStillLoad()
        {
            var sch = VehicleTemplates.LineFollower(Lib);
            var a = sch.Assembly!;
            a.WheelMaterial = "foam";
            a.Breakable = false;
            a.Motors[0].GearEfficiency = 0.6;
            a.Motors[0].BacklashDeg = 4;
            a.Modules[0].HoldForce = 80;
            a.Links.Add(new LinkMount { Name = "kepçe", Joint = JointKind.Slider, Lower = 0, Upper = 0.05, ServoRefDes = "", Material = "aluminium" });
            a.Arena = "custom";
            a.World = new WorldSpec
            {
                FloorMaterial = "asphalt", AirDensity = 1.1, WindX = 1, Gust = 0.5, WallHeight = 0.2,
                Terrain = new WorldTerrain { Kind = "hills", Amplitude = 0.03, Wavelength = 0.8 }
            };
            a.World.Regions.Add(new WorldRegion { X = 1, Y = 0, SizeX = 0.5, SizeY = 0.5, Material = "ice" });
            a.World.Obstacles.Add(new WorldObstacle { Shape = "ramp", X = 0.5, SizeX = 0.4, SizeY = 0.3, Height = 0.05, Yaw = 30 });
            a.World.Obstacles.Add(new WorldObstacle { Shape = "sphere", X = -0.5, SizeX = 0.06, Dynamic = true, Mass = 0.05, Material = "rubber" });
            var back = Schematic.FromJson(sch.ToJson()).Assembly!;
            Assert.Equal("foam", back.WheelMaterial);
            Assert.False(back.Breakable);
            Assert.Equal(0.6, back.Motors[0].GearEfficiency);
            Assert.Equal(4, back.Motors[0].BacklashDeg);
            Assert.Equal(80, back.Modules[0].HoldForce);
            Assert.Equal(JointKind.Slider, back.Links.Single().Joint);
            Assert.Equal("asphalt", back.World!.FloorMaterial);
            Assert.Equal("hills", back.World.Terrain!.Kind);
            Assert.Single(back.World.Regions);
            Assert.Equal(2, back.World.Obstacles.Count);
            var arena = back.CreateArena();
            Assert.IsType<HeightMapTerrain>(arena.Terrain);
            Assert.Equal(2, arena.Shapes.Count);
            Assert.Empty(arena.Obstacles);
            Assert.Single(arena.Regions);
            Assert.Equal("ice", arena.Regions[0].Material);

            // A file from before these fields existed loads with the old behaviour.
            const string old = "{\"kind\":\"Rover\",\"arena\":\"custom\",\"world\":{\"width\":3,\"depth\":2,\"friction\":0.7,\"obstacles\":[{\"x\":0.5,\"y\":0,\"sizeX\":0.2,\"sizeY\":0.2,\"height\":0.1}]},\"motors\":[{\"refDes\":\"M1\",\"role\":\"L\"}]}";
            var legacy = System.Text.Json.JsonSerializer.Deserialize<AssemblyDocument>(old, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            })!;
            Assert.Equal("rubber", legacy.WheelMaterial);
            Assert.True(legacy.Breakable);
            Assert.Empty(legacy.Links);
            Assert.Null(legacy.Motors[0].GearEfficiency);
            var legacyArena = legacy.CreateArena();
            Assert.Null(legacyArena.FloorMaterial);
            Assert.Null(legacyArena.Terrain);
            Assert.Single(legacyArena.Obstacles);
            Assert.Equal(0.7, legacyArena.Friction);
            // [air] Its air is the air of its temperature (25 °C unless set), as in every world: 1.184, not a fixed 1.225.
            Assert.Equal(RobotTwin.CoreSim.Sdk.Atmosphere.Density(25, RobotTwin.CoreSim.Sdk.Atmosphere.SeaLevelPressure), legacyArena.AirDensity, 9);
        }
    }
}
