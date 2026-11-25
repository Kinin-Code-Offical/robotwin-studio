using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    /// <summary>[uncertainty] The time-step check repeats the start of a run at half the step and says how much changed.</summary>
    public class StepCheckTests
    {
        private readonly ITestOutputHelper _out;

        public StepCheckTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TheCarsPathBarelyDependsOnTheStep()
        {
            var world = WorldExamples.CarDetailed(PartLibrary.Default);
            var s = new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom,
                Duration = 1.0,
                OutputRate = 100,
                Accuracy = AccuracyPreset.Fast,
                Firmware = false,
                TimeStepCheck = true
            };
            var r = AnalysisRun.Run(world, s);
            var c = r.StepCheck;
            Assert.NotNull(c);
            _out.WriteLine($"{c!.Seconds} s at {c.Step * 1000} / {c.HalfStep * 1000} ms: position {c.PositionM * 1000:F3} mm ({c.PositionObject}), worst {c.WorstRelative * 100:F2} % ({c.WorstChannel})");
            Assert.InRange(c.Seconds, 0.99, 1.01);
            Assert.Equal(c.Step / 2, c.HalfStep, 12);
            Assert.True(c.PositionM < 0.01, $"{c.PositionM * 1000:F1} mm");   // a car rolling for a second: millimetres at most
            Assert.NotNull(c.PositionObject);

            // It travels with the saved result.
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rw-stepcheck-" + System.Guid.NewGuid().ToString("N") + ".rwanalysis");
            try
            {
                r.Save(path);
                var back = AnalysisResult.Load(path).StepCheck;
                Assert.NotNull(back);
                Assert.Equal(c.PositionM, back!.PositionM, 12);
                Assert.Equal(c.WorstChannel, back.WorstChannel);
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }
    }
}
