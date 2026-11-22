using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[fasten] Screws, glues and ties hold, conduct heat and soften as the real ones do.</summary>
    public class FasteningTests
    {
        private readonly ITestOutputHelper _out;

        public FasteningTests(ITestOutputHelper output) => _out = output;

        private static (WorldDocument World, WorldObject Weight) Hanging(string fastening, double startC)
        {
            var w = new WorldDocument();
            var beam = w.AddBody("box", 0.3, 0.1, 0.02, new WorldPose { Z = 0.4 }, "AHŞAP");
            beam.IsStatic = true;
            beam.Parameters["temperature"] = startC.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var weight = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { Z = 0.35 }, "ÇELİK");
            weight.MassKg = 1.0;
            weight.Parameters["temperature"] = startC.ToString(System.Globalization.CultureInfo.InvariantCulture);
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = weight.Id, ObjectB = beam.Id, Fastening = fastening });
            return (w, weight);
        }

        private static WorldPhysics Build(WorldDocument w) => WorldPhysics.Build(w, new ApproximateGeometry(w, PartLibrary.Default).GeometryOf, null);

        [Fact]
        public void HotGlueHoldsAKilogramColdAndLetsGoWhenThePartsAreHot()
        {
            var (cold, cw) = Hanging("sicak-tutkal", 25);
            var p = Build(cold);
            for (int i = 0; i < 2000; i++) p.Advance(1e-3, (AnalogSimulator?)null);
            double zCold = p.PoseOf(cw.Id)!.Value.Position.Z;

            var (hot, hw) = Hanging("sicak-tutkal", 95);
            var q = Build(hot);
            for (int i = 0; i < 2000; i++) q.Advance(1e-3, (AnalogSimulator?)null);
            double zHot = q.PoseOf(hw.Id)!.Value.Position.Z;
            var events = q.DrainEvents().Select(e => e.Message).ToList();
            _out.WriteLine($"cold: z {zCold:F3} m; hot: z {zHot:F3} m; {string.Join(" | ", events)}");
            Assert.InRange(zCold, 0.345, 0.355);                       // still glued under the beam
            Assert.True(zHot < 0.1, "at 95 °C the hot glue has flowed and the weight falls");
            Assert.Contains(events, e => e.Contains("tuttuğunu bıraktı"));
        }

        [Fact]
        public void EpoxyPassesHeatBetweenPartsFarBetterThanAZipTie()
        {
            double Gap(string fastening)
            {
                var w = new WorldDocument { Environment = { Gravity = 0 } };
                var a = w.AddBody("box", 0.04, 0.04, 0.01, new WorldPose(), "ALÜMİNYUM");
                a.IsStatic = true;
                a.Parameters["temperature"] = "120";
                var b = w.AddBody("box", 0.04, 0.04, 0.01, new WorldPose { Z = 0.01 }, "ALÜMİNYUM");
                b.IsStatic = true;
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = b.Id, ObjectB = a.Id, Fastening = fastening });
                var p = Build(w);
                for (int i = 0; i < 60000; i += 10) p.Advance(1e-2, (AnalogSimulator?)null);   // one minute
                return p.TemperatureOf(a.Id) - p.TemperatureOf(b.Id);
            }

            double epoxy = Gap("epoksi"), tie = Gap("kelepce");
            _out.WriteLine($"after 60 s: epoxy ΔT {epoxy:F1} K, zip tie ΔT {tie:F1} K");
            Assert.True(epoxy < 0.5 * tie, "the glue layer couples the blocks thermally");
        }

        [Fact]
        public void ScrewsAndGluesHaveTheirRealStrengths()
        {
            Assert.Equal(2000, Fastening.For("vida-somun").BreakForce(0.001));
            Assert.Equal(500, Fastening.For("vida-plastik").BreakForce(0.001));
            Assert.Equal(15e6 * 0.5 * 0.0025, Fastening.For("epoksi").BreakForce(0.0025), 6);   // 25 mm × 100 mm face, half wetted
            Assert.Equal(0, Fastening.For(string.Empty).BreakForce(1));
            Assert.Equal(0.5, Fastening.For("sicak-tutkal").StrengthFactor(70), 6);
        }
    }
}
