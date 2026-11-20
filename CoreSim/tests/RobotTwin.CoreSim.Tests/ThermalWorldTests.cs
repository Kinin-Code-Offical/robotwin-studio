using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
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
    }
}
