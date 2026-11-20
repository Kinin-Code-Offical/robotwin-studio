using System.Linq;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Design;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    public class AnalysisRunTests
    {
        private readonly ITestOutputHelper _out;

        public AnalysisRunTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void CarRunRecordsSignalsStressesTheChassisAndComputesDrag()
        {
            var world = WorldExamples.CarDetailed(PartLibrary.Default);
            var plate = world.Objects.First(o => o.Kind == WorldObjectKind.Body && o.SizeX > 0.1);
            var s = new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom,
                Duration = 0.5,
                OutputRate = 200,
                Accuracy = AccuracyPreset.Standard,
                Firmware = false,
                Stress = true,
                StressObjects = { plate.Id },
                StressInstants = 3,
                StressTargetCells = 300,
                Aero = true,
                AeroObject = plate.Id,
                AeroSpeed = 1.0,
                AeroResolution = 12,
                AeroPasses = 6
            };

            var clock = System.Diagnostics.Stopwatch.StartNew();
            string phase = string.Empty;
            var r = AnalysisRun.Run(world, s, progress: (name, _) =>
            {
                if (name != phase) _out.WriteLine($"{clock.Elapsed.TotalSeconds,6:0.0} s  {name}");
                phase = name;
            });
            _out.WriteLine($"{r.Channels.Count} samples, {r.Channels.Channels.Count} channels, {r.WallSeconds:0.0} s wall");
            Assert.Equal(s.SampleCount + 1, r.Channels.Count);
            Assert.True(r.Channels.Channels.Count > 20);

            var stress = Assert.Single(r.Stress);
            foreach (var note in stress.Notes) _out.WriteLine("  " + note);
            foreach (var f in stress.Frames) _out.WriteLine($"  t {f.Time:0.000}: {f.Iterations} CG iterations, {f.LoadCount} loads");
            var critical = stress.Critical;
            Assert.NotNull(critical);
            _out.WriteLine($"stress: {stress.Method}, max σ {critical!.MaxVonMises / 1e6:0.###} MPa, safety {critical.MinSafetyFactor:0.#}");
            Assert.True(critical.MaxVonMises > 0);
            Assert.True(critical.MinSafetyFactor > 1, "a car chassis carrying its own motors must not yield");

            Assert.NotNull(r.Aero);
            _out.WriteLine($"aero: Cd {r.Aero!.MeanCd:0.00}, drag {r.Aero.DragForce * 1000:0.###} mN, Re {r.Aero.ReynoldsPhysical:0}");
            Assert.True(r.Aero.MeanCd > 0.1 && r.Aero.MeanCd < 10);
        }

        [Fact]
        public void CarRunHeatsTheDissipatingPartsAboveAmbient()
        {
            var world = WorldExamples.CarDetailed(PartLibrary.Default);
            var s = new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom,
                Duration = 0.5,
                OutputRate = 200,
                Accuracy = AccuracyPreset.Fast,
                Firmware = false,
                Thermal = true,
                ThermalExtend = 60,
                ThermalFrames = 10
            };

            var r = AnalysisRun.Run(world, s);
            Assert.NotEmpty(r.Thermal);
            foreach (var t in r.Thermal)
            {
                _out.WriteLine($"{t.Name} ({t.Material}): {t.Nx}×{t.Ny}×{t.Nz}, peak {t.PeakTemperature:0.00} °C, power {t.History[^1].Power:0.###} W");
                foreach (var note in t.Notes) _out.WriteLine("  " + note);
            }

            var hot = r.Thermal.OrderByDescending(t => t.PeakTemperature).First();
            Assert.True(hot.PeakTemperature > world.Environment.AmbientC + 0.05, "dissipating parts warm up");
            Assert.True(hot.PeakTemperature < 200, "and stay physical");
            Assert.InRange(hot.Frames.Count, s.ThermalFrames - 1, s.ThermalFrames + 2);
        }
    }
}
