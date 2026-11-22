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
    /// <summary>[surface] A surface finish changes how a part radiates heat, through the shared heat network.</summary>
    public class SurfaceFinishTests
    {
        private readonly ITestOutputHelper _out;

        public SurfaceFinishTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AnAnodisedBlockCoolsFasterThanAPolishedOne()
        {
            var w = WorldExamples.Surfaces(PartLibrary.Default);
            var polished = w.Objects.First(o => o.Name.StartsWith("Parlatılmış"));
            var anodised = w.Objects.First(o => o.Name.StartsWith("Siyah eloksal"));
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, PartLibrary.Default).GeometryOf, null);
            double t0 = p.TemperatureOf(polished.Id);
            for (int i = 0; i < 600 * 1000; i += 10) p.Advance(1e-2, (AnalogSimulator?)null);   // 10 minutes
            double tp = p.TemperatureOf(polished.Id), ta = p.TemperatureOf(anodised.Id), amb = w.Environment.AmbientC;
            _out.WriteLine($"start {t0:F1} °C → after 10 min polished {tp:F1} °C, anodised {ta:F1} °C (ambient {amb:F1})");
            Assert.InRange(t0, 149, 151);
            Assert.True(ta < tp - 5, "radiation from the black anodising carries clearly more heat away");
            // Same convection, different radiation: the anodised block's excess temperature drops faster.
            Assert.True((ta - amb) / (tp - amb) < 0.9);
        }

        [Fact]
        public void TheFinishSetsTheEmissivityTheMaterialKeepsItsOwnOtherwise()
        {
            var o = new WorldObject();
            Assert.Equal(0.1, SurfaceFinish.EmissivityOf(o, 0.1));
            o.Parameters["surface"] = "eloksal";
            Assert.Equal(0.85, SurfaceFinish.EmissivityOf(o, 0.1));
            o.Parameters["surface"] = "baski";
            Assert.Equal(0.92, SurfaceFinish.EmissivityOf(o, 0.92));   // print layers keep the material's
        }
    }
}
