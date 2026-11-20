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
    /// <summary>[thermal] Parts heat from their losses, heat flows through joints, plastics soften and melt.</summary>
    public class ThermalWorldTests
    {
        private readonly ITestOutputHelper _out;

        public ThermalWorldTests(ITestOutputHelper output) => _out = output;

        private static PartLibrary Lib => PartLibrary.Default;

        private static WorldBodyGeometry Geometry(WorldObject o)
        {
            if (o.Kind == WorldObjectKind.Component)
                return new WorldBodyGeometry { Min = new Vec3(-0.035, -0.011, 0), Max = new Vec3(0.035, 0.011, 0.019), MassKg = 0.03, PortName = o.PartId == "dc-motor" || (o.PresetId ?? "").Contains("motor") ? "shaft" : null, PortPivot = new Vec3(-0.011, 0.0185, 0.0094), PortAxis = Vec3.UnitY };
            return new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = o.MassKg ?? 0.02 };
        }

        [Fact]
        public void StalledMotorHeatsAndWarmsItsPlasticBracket()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.2 });
            var bracket = w.AddBody("box", 0.08, 0.04, 0.03, new WorldPose(), "PLA");
            bracket.IsStatic = true;
            var motor = w.AddComponent(Lib, "motor-tt", new WorldPose { Z = 0.03 });
            var stop = w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose { X = 0.15 }, "ÇELİK");
            stop.IsStatic = true;
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = motor.Id, ObjectB = bracket.Id });
            var wheel = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.011, Y = 0.0335, Z = 0.0394 - 0.0325 });
            w.Joints.Add(new WorldJoint { Id = "J2", ObjectA = wheel.Id, ObjectB = motor.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = "J3", ObjectA = wheel.Id, ObjectB = stop.Id });   // locked wheel: the motor stalls
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 22);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 22);
            var wc = WorldCompiler.Compile(w, Lib, c => 0.25);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            for (int i = 0; i < 20000; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
            }

            double tm = p.TemperatureOf(motor.Id), tb = p.TemperatureOf(bracket.Id), amb = w.Environment.AmbientC;
            _out.WriteLine($"20 s stalled: motor {tm:F2} °C ({p.HeatOf(motor.Id):F2} W), PLA bracket {tb:F3} °C, ambient {amb:F1} °C");
            Assert.True(p.HeatOf(motor.Id) > 1, "a stalled TT motor dissipates watts in its winding");
            Assert.True(tm > amb + 0.8, "the motor heats");
            Assert.True(tb > amb + 0.001, "heat flows through the screw joint into the bracket");
            Assert.True(tb < tm, "the bracket stays cooler than the motor");
        }

        [Fact]
        public void HotPlasticSoftensAndAMeltedBracketLetsGo()
        {
            foreach (double t0 in new[] { 65.0, 170.0 })
            {
                var w = new WorldDocument();
                var bracket = w.AddBody("box", 0.06, 0.04, 0.02, new WorldPose { Z = 0.3 }, "PLA");
                bracket.IsStatic = true;
                bracket.Parameters["temperature"] = t0.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var weight = w.AddBody("box", 0.03, 0.03, 0.03, new WorldPose { Z = 0.32 }, "ÇELİK");
                w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = weight.Id, ObjectB = bracket.Id });
                var p = WorldPhysics.Build(w, Geometry, null);
                for (int i = 0; i < 400; i++) p.Advance(1e-3, null);
                var events = p.DrainEvents().ToList();
                foreach (var e in events) _out.WriteLine($"{t0} °C: {e.Message}");
                double z = p.PoseOf(weight.Id)!.Value.Position.Z;
                _out.WriteLine($"{t0} °C: PLA {p.MaterialStateOf(bracket.Id)}, weight z = {z:F3} m");
                if (t0 < 100)
                {
                    Assert.Equal("yumuşadı", p.MaterialStateOf(bracket.Id));
                    Assert.True(z > 0.3, "softened but still holding");
                }
                else
                {
                    Assert.Equal("eriyor", p.MaterialStateOf(bracket.Id));
                    Assert.Contains(events, e => e.Kind == PhysicsEventKind.JointBroken);
                    Assert.True(z < 0.25, "the melted bracket lets the weight fall");
                }
            }
        }

        /// <summary>
        /// A yellow TT motor stalled on 4×AA, screwed (through its plastic gearbox) to a PLA bracket, for
        /// two minutes: the winding is hottest, the steel can next, the ABS gearbox far behind, the PLA
        /// bracket barely warms. The current falls as the copper heats (+0.39 %/°C) and the alkaline
        /// cells polarise, so the heat is well below the cold-start V²/R.
        /// </summary>
        [Fact]
        public void StalledTtMotorKeepsItsHeatBehindThePlasticGearbox()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.2 });
            var bracket = w.AddBody("box", 0.08, 0.04, 0.03, new WorldPose(), "PLA");
            bracket.IsStatic = true;
            var motor = w.AddComponent(Lib, "motor-tt", new WorldPose { Z = 0.03 });
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = motor.Id, ObjectB = bracket.Id });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 22);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 22);
            var wc = WorldCompiler.Compile(w, Lib, c => 0.25);
            var dc = wc.Compiled.DevicesOf(wc.ComponentRef(motor.Id)!).OfType<DcMotor>().Single();
            var cells = wc.Compiled.DevicesOf(wc.ComponentRef(bat.Id)!).OfType<Battery>().Single();
            dc.Jammed = true;   // shaft locked
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            double i1 = 0, v1 = 0, p1 = 0, amb = w.Environment.AmbientC;
            void Run(int ms, int start)
            {
                for (int k = start + 1; k <= start + ms; k++)
                {
                    sim.Step(1e-3);
                    p.Advance(1e-3, sim);
                    if (k == 1000)
                    {
                        i1 = Math.Abs(dc.Current);
                        v1 = Math.Abs(dc.Voltage);
                        p1 = p.HeatOf(motor.Id);
                    }
                }
            }

            Run(40000, 0);
            double i40 = Math.Abs(dc.Current), v40 = Math.Abs(dc.Voltage), pol40 = cells.PolarizationVoltage, rHot = dc.HotResistance;
            var parts = p.PartTemperatures(motor.Id);
            foreach (var (label, t) in parts) _out.WriteLine($"  40 s {label}: {t:F1} °C");
            _out.WriteLine($"1 s: {i1:F2} A at {v1:F2} V, {p1:F2} W · 40 s: {i40:F2} A at {v40:F2} V, {p.HeatOf(motor.Id):F2} W, R hot {rHot:F2} Ω (cold {dc.Resistance:F2} Ω), cell polarization {pol40:F2} V, PLA bracket {p.TemperatureOf(bracket.Id):F3} °C");
            double winding = parts.First(x => x.Label == "sargı").T, can = parts.First(x => x.Label.StartsWith("motor gövdesi")).T, gearbox = parts.First(x => x.Label.StartsWith("dişli")).T;
            Assert.True(winding > can && can > gearbox, "heat flows winding → can → gearbox");
            Assert.True(can > amb + 20, "a stalled TT's can gets hot");
            Assert.True(gearbox < can - 15, "the plastic gearbox lags far behind the can");
            Assert.True(p.TemperatureOf(bracket.Id) < amb + 1, "the PLA bracket the screws hold barely warms");
            Assert.True(i40 < 0.92 * i1, "hot copper and polarised cells cut the stall current");
            Assert.True(rHot > 1.1 * dc.Resistance, "the winding resistance rose with temperature");
            Assert.True(pol40 > 0.2, "alkaline cells sag under a long heavy load");

            // Held stalled for two minutes the winding cooks — a real TT does that too — while the bracket stays cool.
            Run(80000, 40000);
            var burn = sim.DrainEvents().FirstOrDefault(e => e.Message.Contains("yandı"));
            _out.WriteLine($"120 s: {burn.Time:F0} s → {burn.Message}; PLA bracket {p.TemperatureOf(bracket.Id):F2} °C");
            Assert.True(dc.IsFailed, "two minutes stalled burns the winding");
            Assert.True(p.TemperatureOf(bracket.Id) < amb + 3, "even then the PLA bracket stays near ambient");

            Run(60000, 120000);
            _out.WriteLine($"60 s later, no current: cell polarization {cells.PolarizationVoltage:F3} V");
            Assert.True(cells.PolarizationVoltage < 0.1 * pol40, "the cells recover once the load is gone");
        }

        /// <summary>A coreless motor behind its own prop: the slipstream (actuator-disc v_i … 2·v_i) cools what sits in it.</summary>
        [Fact]
        public void PropellerSlipstreamBlowsOverTheMotorBehindIt()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-1s-lipo-300", new WorldPose { X = -0.1 });
            var stand = w.AddBody("box", 0.03, 0.03, 0.05, new WorldPose(), "AHŞAP");
            stand.IsStatic = true;
            var motor = w.AddComponent(Lib, "motor-8520", new WorldPose { X = 0.03, Z = 0.05 });   // overhanging the stand: the prop disc clears it
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = motor.Id, ObjectB = stand.Id });
            var geo = new RobotTwin.CoreSim.Automation.ApproximateGeometry(w, Lib);
            var g = geo.GeometryOf(motor);
            var prop = ReadyParts.Add(w, "prop-55-ccw", new WorldPose());
            ReadyParts.AttachToPort(w, prop, motor, "shaft", g.PortPivot, g.PortAxis);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 24);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 24);
            var wc = WorldCompiler.Compile(w, Lib, c => 0.2);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-4 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, geo.GeometryOf, wc);
            double still = p.AirSpeedOver(motor.Id);
            for (int k = 0; k < 20000; k++)
            {
                sim.Step(1e-4);
                p.Advance(1e-4, sim);
            }

            var pr = p.PropReadout(prop.Id)!.Value;
            double wash = p.AirSpeedOver(motor.Id);
            _out.WriteLine($"prop {pr.Rpm:F0} rpm, thrust {pr.Thrust * 1000 / 9.81:F1} gf; air over the motor: still {still:F2} m/s, running {wash:F2} m/s");
            Assert.True(Math.Abs(pr.Thrust) > 0.05, "the prop pulls");
            Assert.True(wash > still + 1.5, "the motor sits in its prop's slipstream");
        }
    }
}
