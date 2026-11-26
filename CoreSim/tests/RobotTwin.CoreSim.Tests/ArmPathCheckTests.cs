using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[pathcheck] Arm moves checked along their whole path before they start.</summary>
    public class ArmPathCheckTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public ArmPathCheckTests(ITestOutputHelper output) => _out = output;

        private static (WorldDocument W, WorldPhysics P, AnalogSimulator Sim, MachineArm Arm) Cell(Action<WorldDocument>? edit = null)
        {
            var w = FactoryCell.Build();
            edit?.Invoke(w);
            var wc = WorldCompiler.Compile(w, Lib, _ => 1.0);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, wc);
            var arm = p.ArmOf(w.Objects.First(o => o.PartId == CellParts.ArmId).Id)!;
            return (w, p, sim, arm);
        }

        [Fact]
        public void TheFactoryCellsProgramRunsWithoutAPathWarning()
        {
            var (_, p, sim, arm) = Cell();
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < 20000; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
                foreach (var m in p.DrainMachineMessages()) log.AppendLine($"{m.Time:0.00} {m.Text}");
            }

            _out.WriteLine(log.ToString());
            _out.WriteLine($"cycles {arm.Controller.CycleCount}, last path warning '{arm.Controller.PathWarning}'");
            Assert.DoesNotContain("Yolda çarpışma", log.ToString());
            Assert.True(arm.Controller.CycleCount >= 1);
        }

        [Fact]
        public void APillarInTheSweepIsFoundBeforeTheArmMoves()
        {
            WorldObject? pillar = null;
            var (w, p, _, arm) = Cell(world =>
            {
                // Beside the arm, a quarter turn round from where its flange starts, out at the flange's reach.
                pillar = world.AddBody("box", 0.12, 0.12, 2.0, new WorldPose(), "ÇELİK");
                pillar.Name = "Sütun";
                pillar.IsStatic = true;   // placed once the arm's frame is known
            });
            var start = arm.Measured;
            var flange = arm.BasePose * ArmKinematics.Forward(arm.Model, start);
            var r = flange.P - arm.BasePose.P;
            var reach = new Vec3(r.X, r.Y, 0);
            var around = Quat.FromAxisAngle(Vec3.UnitZ, Math.PI / 2).Rotate(reach);
            var body = p.Scene.Bodies.Single(b => b.Tag is WorldObject wo && wo.Id == pillar!.Id);
            body.Position = new Vec3(arm.BasePose.P.X + around.X, arm.BasePose.P.Y + around.Y, 1.0);

            var end = (double[])start.Clone();
            end[0] += Math.PI;   // J1 half a turn: the arm swings through where the pillar stands
            var swing = new JointMotion(arm.Model, start, end, 0.5, ProfileShape.SCurve);
            var hit = p.CheckArmPath(arm, swing);
            _out.WriteLine(hit?.ToString() ?? "clear");
            Assert.NotNull(hit);
            Assert.Equal("Sütun", hit!.Other);
            Assert.InRange(hit.Time, 0.05, swing.Duration * 0.75);

            // Turning the other way round it misses the pillar.
            end[0] = start[0] - Math.PI / 6;
            var back = p.CheckArmPath(arm, new JointMotion(arm.Model, start, end, 0.5, ProfileShape.SCurve));
            _out.WriteLine(back?.ToString() ?? "clear");
            Assert.True(back == null || back.Other != "Sütun");
        }
    }
}
