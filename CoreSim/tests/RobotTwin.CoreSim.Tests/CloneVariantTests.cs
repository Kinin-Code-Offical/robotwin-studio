using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public class CloneVariantTests
    {
        private readonly ITestOutputHelper _out;

        public CloneVariantTests(ITestOutputHelper output) => _out = output;

        private static double Num(WorldCircuit c, string fullRef, string key) =>
            double.Parse(c.PartParameters[fullRef][key], CultureInfo.InvariantCulture);

        [Fact]
        public void CloneServosDifferFromTheDatasheetEachInItsOwnWayAndRepeatably()
        {
            var w = new WorldDocument();
            var a = w.AddComponent(PartLibrary.Default, "servo", new WorldPose());
            var b = w.AddComponent(PartLibrary.Default, "servo", new WorldPose { X = 0.1 });
            var orig = w.AddComponent(PartLibrary.Default, "servo", new WorldPose { X = 0.2 });
            a.Parameters["variant"] = "klon";
            b.Parameters["variant"] = "klon";
            var c1 = WorldCompiler.Compile(w, PartLibrary.Default);
            var c2 = WorldCompiler.Compile(w, PartLibrary.Default);
            string ra = c1.PartParameters.Keys.First(k => k.StartsWith(WorldCircuit.Prefix(a.Id)));
            string rb = c1.PartParameters.Keys.First(k => k.StartsWith(WorldCircuit.Prefix(b.Id)));
            string ro = c1.PartParameters.Keys.First(k => k.StartsWith(WorldCircuit.Prefix(orig.Id)));
            double ia = Num(c1, ra, "istall"), ib = Num(c1, rb, "istall"), io = Num(c1, ro, "istall");
            _out.WriteLine($"stall current: clone A {ia:0.000} A, clone B {ib:0.000} A, datasheet {io:0.000} A");
            Assert.Equal(0.65, io, 6);                          // the original part is the datasheet part
            Assert.InRange(ia, 0.65 * 0.65, 0.65 * 1.35);      // clones stay within ±35 %
            Assert.NotEqual(ia, ib);                            // …and each clone is its own part
            Assert.Equal(ia, Num(c2, ra, "istall"), 12);        // the same physical part every run
            Assert.True(c1.Draws.ContainsKey(ra));
            Assert.False(c1.Draws.ContainsKey(ro));
        }

        [Fact]
        public void CurrentSpreadIsWiderForClones()
        {
            var def = PartLibrary.Default.Find("servo")!;
            var orig = ToleranceModel.CurrentSpread(def, new System.Collections.Generic.Dictionary<string, string> { ["model"] = "SG90" });
            var clone = ToleranceModel.CurrentSpread(def, new System.Collections.Generic.Dictionary<string, string> { ["model"] = "SG90", ["variant"] = "klon" });
            Assert.True(clone.Fraction > orig.Fraction);
            Assert.Contains("klon", clone.Reason);
        }

        [Fact]
        public void RenderUncertaintyRunsGiveABandAroundTheMotorCurrent()
        {
            var world = WorldExamples.CarDetailed(PartLibrary.Default);
            foreach (var o in world.Objects.Where(o => o.PartId == "dc-motor" || (o.PresetId ?? string.Empty).Contains("motor"))) o.Parameters["variant"] = "klon";
            var s = new AnalysisSettings { Preset = AnalysisPreset.Custom, Duration = 0.3, OutputRate = 100, Accuracy = AccuracyPreset.Fast, Firmware = false, UncertaintyRuns = 3 };
            var r = AnalysisRun.Run(world, s);
            Assert.Equal(3, r.BandRuns);
            var current = r.Channels.Channels.Where(c => c.Name.StartsWith("dev/", System.StringComparison.Ordinal) && c.Unit == "A" && r.Bands.ContainsKey(c.Name))
                .Select(c => (c, band: r.Bands[c.Name]))
                .OrderByDescending(x => x.band.Hi.Zip(x.band.Lo, (h, l) => h - l).Max())
                .First();
            double width = current.band.Hi.Zip(current.band.Lo, (h, l) => h - l).Max();
            _out.WriteLine($"{current.c.Label}: widest band {width * 1000:0.#} mA");
            Assert.True(width > 0.001, "different draws give different currents");
            for (int i = 0; i < current.c.Count; i++) Assert.InRange(current.c[i], current.band.Lo[i] - 1e-12, current.band.Hi[i] + 1e-12);
        }
    }
}
