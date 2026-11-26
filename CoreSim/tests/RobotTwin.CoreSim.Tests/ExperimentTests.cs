using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[experiment] Designed experiments run headless: factors, seeded repeats, metrics, statistics, report.</summary>
    public class ExperimentTests
    {
        private readonly ITestOutputHelper _out;

        public ExperimentTests(ITestOutputHelper output) => _out = output;

        /// <summary>A 40 mm steel ball let go 1 m above the floor.</summary>
        private static WorldDocument Drop()
        {
            var w = new WorldDocument { Title = "Düşen top" };
            var ball = w.AddBody("sphere", 0.04, 0.04, 0.04, new WorldPose { Z = 1.0 }, "ÇELİK");
            ball.Name = "Top";
            return w;
        }

        [Fact]
        public void AFallMeasuredUnderTwoGravitiesGivesTheirSpeeds()
        {
            var spec = new ExperimentSpec
            {
                Name = "Serbest düşme", Duration = 0.3, SampleInterval = 0.005, Repeats = 2, Seed = 7, Firmware = false,
                Factors = { new ExperimentFactor { Target = "environment.Gravity", Values = { "9.81", "1.62" } } },
                Metrics =
                {
                    new ExperimentMetric { Name = "en yüksek hız", Source = "Top.speed", Reduce = "max" },
                    new ExperimentMetric { Name = "düşüş", Source = "Top.z", Reduce = "range" }
                }
            };
            int reported = 0;
            var result = ExperimentRunner.Run(spec, Drop(), new SyncProgress(_ => reported++));
            _out.WriteLine(result.Markdown("Düşen top"));
            _out.WriteLine(result.SummaryCsv());
            Assert.Equal(4, result.Runs.Count);
            Assert.Equal(4, reported);
            Assert.All(result.Runs, r => Assert.Equal("", r.Error));
            Assert.Equal(new[] { 7, 8, 7, 8 }, result.Runs.Select(r => r.Seed));
            // v = g·t and h = g·t²/2 at 0.3 s (air drag on a steel ball is under 1 % here).
            var earth = result.StatOf(0, 0);
            var moon = result.StatOf(1, 0);
            Assert.InRange(earth.Mean, 9.81 * 0.3 * 0.97, 9.81 * 0.3 * 1.01);
            Assert.InRange(moon.Mean, 1.62 * 0.3 * 0.97, 1.62 * 0.3 * 1.01);
            Assert.InRange(result.StatOf(0, 1).Mean, 0.5 * 9.81 * 0.09 * 0.95, 0.5 * 9.81 * 0.09 * 1.02);
            Assert.Equal(2, earth.N);
            Assert.Contains("en yüksek hız mean", result.SummaryCsv().Split('\n')[0]);
            Assert.Contains("| 9.81 |", result.Markdown("Düşen top"));
            // The spec saved beside the results reads back the same.
            var back = ExperimentSpec.FromJson(spec.ToJson());
            Assert.Equal("environment.Gravity", back.Factors.Single().Target);
            Assert.Equal(2, back.Metrics.Count);
        }

        [Fact]
        public void FactorsReachPosesParametersAndTheEnvironment()
        {
            var w = Drop();
            ExperimentRunner.Apply(w, "Top.z", "0.5");
            ExperimentRunner.Apply(w, "Top.mass", "0,25");
            ExperimentRunner.Apply(w, "Top.fault_value", "40");
            ExperimentRunner.Apply(w, "environment.AmbientC", "-10");
            var ball = w.Objects.Single(o => o.Name == "Top");
            Assert.Equal(0.5, ball.Pose.Z);
            Assert.Equal(0.25, ball.MassKg);
            Assert.Equal("40", ball.Parameters["fault_value"]);
            Assert.Equal(-10, w.Environment.AmbientC);
            Assert.Throws<ArgumentException>(() => ExperimentRunner.Apply(w, "Yok.x", "1"));
            var range = new ExperimentFactor { Target = "Top.z", From = 0.2, To = 1.0, Steps = 5 };
            Assert.Equal(new[] { "0.2", "0.4", "0.6", "0.8", "1" }, range.Levels().Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Fact]
        public void TheConfidenceIntervalUsesStudentsT()
        {
            Assert.Equal(12.706, ExperimentResult.StudentT95(1), 3);
            Assert.Equal(2.262, ExperimentResult.StudentT95(9), 3);
            Assert.Equal(1.96, ExperimentResult.StudentT95(1000), 2);
        }

        /// <summary>Progress reported on the calling thread (Progress&lt;T&gt; posts to a context the test does not pump).</summary>
        private sealed class SyncProgress : IProgress<(int Done, int Total)>
        {
            private readonly Action<(int, int)> _report;
            public SyncProgress(Action<(int, int)> report) => _report = report;
            public void Report((int Done, int Total) value) => _report(value);
        }
    }
}
