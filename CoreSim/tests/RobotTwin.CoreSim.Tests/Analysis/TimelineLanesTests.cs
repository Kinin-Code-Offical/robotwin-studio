using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    /// <summary>[unified-timeline] A render's record on one time axis: the board's pins and interrupts beside its serial output and the run's quantities.</summary>
    public class TimelineLanesTests
    {
        private readonly ITestOutputHelper _out;

        public TimelineLanesTests(ITestOutputHelper output) => _out = output;

        private static string StudioFirmware(string file)
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "Studio", "RobotWin.Studio", "Firmware", file);
                if (File.Exists(p)) return p;
            }

            throw new FileNotFoundException(file);
        }

        [Fact]
        public void ABlinkingBoardsPinsInterruptsAndPrintsShareOneTimeAxis()
        {
            var w = new WorldDocument { Title = "Blink" };
            var uno = w.AddComponent(PartLibrary.Default, "arduino-uno", new WorldPose { Z = 0.01 });
            uno.Name = "Uno";
            uno.Parameters["firmware"] = StudioFirmware("blink-serial.hex");
            var s = new AnalysisSettings { Preset = AnalysisPreset.Custom, Duration = 2.3, OutputRate = 100, Accuracy = AccuracyPreset.Fast, Firmware = true, Stress = false, Thermal = false, Aero = false };
            var r = AnalysisRun.Run(w, s);

            // D13: HIGH, a second later LOW, a second later HIGH again (delay(1000) twice)
            var d13 = Assert.Single(r.Logic, l => l.Pin == "D13");
            _out.WriteLine($"D13 from {d13.Start:0.000} s, {(d13.Initial ? "HIGH" : "LOW")}, edges {string.Join(", ", d13.Edges.Select(e => e.ToString("0.000")))}");
            Assert.InRange(d13.Edges.Count, 3, 4);
            double high = d13.Edges.First(e => d13.LevelAt(e + 1e-6));
            double low = d13.Edges.First(e => e > high && !d13.LevelAt(e + 1e-6));
            Assert.InRange(low - high, 0.99, 1.01);
            Assert.True(d13.LevelAt(high + 0.5));
            Assert.False(d13.LevelAt(low + 0.5));

            // millis(): TIMER0's overflow every 1.024 ms, each a few microseconds long
            var ovf = r.Interrupts.Where(i => i.Vector == "TIMER0_OVF").ToList();
            _out.WriteLine($"{ovf.Count} TIMER0_OVF, {r.Interrupts.Count} interrupts in all; longest {ovf.Max(i => i.End - i.Start) * 1e6:0.0} µs");
            Assert.InRange(ovf.Count, 2000, 2300);
            Assert.All(ovf, i => Assert.InRange(i.End - i.Start, 1e-6, 50e-6));
            double period = (ovf[^1].Start - ovf[0].Start) / (ovf.Count - 1);
            Assert.InRange(period, 1.020e-3, 1.028e-3);

            // the lanes: the print, the interrupts and the pin in the board's group, on the same clock as the print
            var lanes = TimelineLanes.Build(r);
            foreach (var g in lanes.GroupBy(l => l.Group)) _out.WriteLine($"{g.Key}: {string.Join(", ", g.Select(l => l.Title + (l.Default ? "" : "*")))}");
            var serial = Assert.Single(lanes, l => l.Title == "Seri port");
            Assert.StartsWith("Yazılım · Uno", serial.Group, StringComparison.Ordinal);   // the board's prints sit with its pins
            Assert.Contains(serial.Marks, m => m.Text.Contains("Hello"));
            Assert.Contains(lanes, l => l.Group.StartsWith("Yazılım · Uno", StringComparison.Ordinal) && l.Kind == TimelineLaneKind.Spans && l.Title == "TIMER0_OVF");
            var pin = Assert.Single(lanes, l => l.Group.StartsWith("Yazılım · Uno", StringComparison.Ordinal) && l.Title == "D13");
            Assert.Equal("HIGH", pin.ReadAt(high + 0.5, 0.001, r.Channels));
            Assert.Equal("LOW", pin.ReadAt(low + 0.5, 0.001, r.Channels));
            Assert.All(lanes.Where(l => l.Default).GroupBy(l => l.Group).Where(g => g.First().Kind == TimelineLaneKind.Analog), g => Assert.InRange(g.Count(), 1, TimelineLanes.MaxDefaultPerGroup));
            Assert.Contains(lanes, l => l.Default && l.Kind == TimelineLaneKind.Analog && l.Title.Contains("D13"));   // the pin's voltage moved
            Assert.DoesNotContain(lanes, l => l.Default && l.Kind == TimelineLaneKind.Analog && l.Title.Contains("A3"));   // an unused pin did not
            // a 0/1 channel reads as a pin does: the circuit's view of D13 (its "D" state)
            var state = lanes.FirstOrDefault(l => l.Kind == TimelineLaneKind.Analog && l.Binary && l.Title.Contains("D13"));
            Assert.NotNull(state);
            Assert.Equal("HIGH", state!.ReadAt(high + 0.5, 0.001, r.Channels));
            Assert.Equal("LOW", state.ReadAt(low + 0.5, 0.001, r.Channels));
            double firstPrint = serial.Marks.Min(m => m.Time);
            Assert.InRange(firstPrint - high, -0.05, 0.05);   // println and digitalWrite(HIGH) are a few lines apart

            // it all goes into the .rwanalysis and comes back
            string path = Path.Combine(Path.GetTempPath(), "rw-timeline-" + Guid.NewGuid().ToString("N") + AnalysisResult.Extension);
            try
            {
                r.Save(path);
                var back = AnalysisResult.Load(path);
                var d13b = Assert.Single(back.Logic, l => l.Pin == "D13");
                Assert.Equal(d13.Edges, d13b.Edges);
                Assert.Equal(d13.Initial, d13b.Initial);
                Assert.Equal(r.Interrupts.Count, back.Interrupts.Count);
                Assert.Equal("TIMER0_OVF", back.Interrupts.First(i => i.Vector == "TIMER0_OVF").Vector);
                Assert.Equal(r.BoardNames, back.BoardNames);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
