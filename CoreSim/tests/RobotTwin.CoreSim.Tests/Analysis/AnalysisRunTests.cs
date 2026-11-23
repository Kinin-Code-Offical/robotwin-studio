using System.Linq;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Mechanics;
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
    
        [Fact]
        public void RenderRecordsPosesAndComputesFlowAroundTheWholeCar()
        {
            var world = WorldExamples.CarDetailed(PartLibrary.Default);
            var s = new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom,
                Duration = 0.4,
                OutputRate = 100,
                Accuracy = AccuracyPreset.Fast,
                Firmware = false,
                Aero = true,
                Aero3D = true,
                AeroTunnel = true,          // an explicit wind-tunnel test: 3 m/s of air blowing towards −X
                AeroSpeed = 3,
                AeroDirectionDeg = 180,
                AeroResolution = 12,
                AeroPasses = 10
            };

            var r = AnalysisRun.Run(world, s);
            var plate = world.Objects.First(o => o.Kind == WorldObjectKind.Body && o.SizeX > 0.1);
            var first = AnalysisRun.RecordedPose(r.Channels, plate.Id, 0);
            var last = AnalysisRun.RecordedPose(r.Channels, plate.Id, r.Channels.Count - 1);
            Assert.NotNull(first);
            Assert.NotNull(last);
            Assert.InRange(first!.Value.Position.Z, plate.Pose.Z - 0.01, plate.Pose.Z + 0.01);
            Assert.NotEmpty(r.AeroFrames);
            foreach (var f in r.AeroFrames)
            {
                _out.WriteLine($"t {f.Time:0.00} s: {f.Nx}×{f.Ny}×{f.Nz}, wind {f.Speed:0.0} m/s, F = ({f.Force.X * 1000:0.##}, {f.Force.Y * 1000:0.##}, {f.Force.Z * 1000:0.##}) mN, Cd {f.MeanCd:0.00}");
                foreach (var n in f.Notes) _out.WriteLine("  " + n);
            }

            var a = r.AeroFrames[0];
            Assert.True(a.Force.Dot(a.AxisX) > 0, "drag acts along the relative wind");
            Assert.InRange(a.Force.Length, 1e-4, 0.5);   // a toy car in a 3 m/s breeze: millinewtons
        }

        [Fact]
        public void StillAirAroundAPartThatDoesNotMoveHasNoFlow()
        {
            // [air] A block resting on the floor in a world without wind: the render must not invent air (it used to blow a
            // 2 m/s "test speed" at anything that did not move).
            var world = new WorldDocument();
            world.AddBody("box", 0.04, 0.03, 0.02, new WorldPose { Z = 0.0 }, "PLA");
            var s = new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom, Duration = 0.3, OutputRate = 100, Accuracy = AccuracyPreset.Fast, Firmware = false,
                Aero = true, Aero3D = true, AeroResolution = 12
            };

            var r = AnalysisRun.Run(world, s);
            foreach (var l in r.Log) _out.WriteLine(l);
            foreach (var f in r.AeroFrames.Take(1)) foreach (var n in f.Notes) _out.WriteLine("  " + n);
            Assert.Empty(r.AeroFrames);
            Assert.Contains(r.Log, l => l.StartsWith("Hava akışı"));
        }

        [Fact]
        public void AFallingBlockPushesTheAirAndTheAirBrakesIt()
        {
            // [air] A 40 × 30 × 20 mm block falls 0.25 m in still air: the air it pushes moves, the force on it points up,
            // and its size is that of the drag of a box at the speed it has (½ρv²·Cd·A, Cd ≈ 1.05 face-on).
            var world = new WorldDocument();
            var block = world.AddBody("box", 0.04, 0.03, 0.02, new WorldPose { Z = 0.25 }, "PLA");
            var s = new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom, Duration = 0.2, OutputRate = 200, Accuracy = AccuracyPreset.Fast, Firmware = false,
                Aero = true, Aero3D = true, AeroResolution = 12
            };

            var r = AnalysisRun.Run(world, s);
            foreach (var l in r.Log) _out.WriteLine(l);
            Assert.NotEmpty(r.AeroFrames);
            foreach (var f in r.AeroFrames.Where((_, i) => i % 2 == 0))
                _out.WriteLine($"t {f.Time:0.000} s: smoke {f.Tracers!.Length / 6}, F = ({f.Force.X * 1000:0.00}, {f.Force.Y * 1000:0.00}, {f.Force.Z * 1000:0.00}) mN");
            foreach (var n in r.AeroFrames[0].Notes) _out.WriteLine("  " + n);

            // Just before the end: the block falls at about g·t.
            var last = r.AeroFrames[r.AeroFrames.Count - 1];
            double v = 9.81 * last.Time;
            double drag = 0.5 * 1.2 * v * v * 1.05 * 0.04 * 0.03;
            _out.WriteLine($"expected drag at {v:0.00} m/s: {drag * 1000:0.00} mN");
            Assert.True(last.Force.Z > 0, "the air pushes up against the fall");
            Assert.InRange(last.Force.Z, 0.6 * drag, 1.4 * drag);   // coarse (3.3 mm) transient flow: measured −12 %
            Assert.True(last.Tracers!.Length / 6 > 20, "the pushed air carries smoke");
            Assert.All(r.AeroFrames, f => Assert.True(f.Tracers != null));
        }

        [Fact]
        public void TheRenderKeepsTheCableRopesAndTheAirInItsFile()
        {
            // [render] The replay draws cables where they were simulated, and a saved render keeps its air frames.
            var world = new WorldDocument();
            var block = world.AddBody("box", 0.04, 0.03, 0.02, new WorldPose { Z = 0.1 }, "PLA");
            var r = new AnalysisResult();
            r.Cables.Add(new CableTrack { CableId = "C1" });
            r.Cables[0].Add(0, new[] { new Vec3(0, 0, 0), new Vec3(0.05, 0, -0.01), new Vec3(0.1, 0, 0) }, true, true);
            r.Cables[0].Add(0.1, new[] { new Vec3(0, 0, 0), new Vec3(0.05, 0, -0.03), new Vec3(0.1, 0, 0) }, true, false);
            r.AeroFrames.Add(new CfdResult { Name = "Hava", Is3D = true, Time = 0.05, Nx = 4, Ny = 4, Nz = 4, Dx = 0.01, Speed = 1, Force = new Vec3(0, 0, 0.002), Tracers = new float[] { 0, 0, 0.1f, 0, 0, 0.5f } });
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rw-cable-air-" + System.Guid.NewGuid().ToString("N") + AnalysisResult.Extension);
            try
            {
                r.Save(path);
                var back = AnalysisResult.Load(path);
                var mid = back.Cables.Single().At(0.05)!.Value;
                Assert.Equal(-0.02, mid.Points[1].Z, 4);        // blended halfway between the two frames
                Assert.True(back.Cables[0].At(0.1)!.Value.PlugA && !back.Cables[0].At(0.1)!.Value.PlugB);
                var a = back.AeroFrames.Single();
                Assert.Equal(0.05, a.Time, 6);
                Assert.Equal(0.002, a.Force.Z, 6);
                Assert.Equal(6, a.Tracers!.Length);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void StressResultsCarryAMeshCheckAndExportAsABalancedInpDeck()
        {
            // A 40 × 30 × 20 mm PLA block dropped from 5 cm: impact contact loads.
            var world = new WorldDocument();
            var plate = world.AddBody("box", 0.04, 0.03, 0.02, new WorldPose { Z = 0.05 }, "PLA");
            var s = new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom, Duration = 0.3, OutputRate = 200, Accuracy = AccuracyPreset.Fast, Firmware = false,
                Stress = true, StressObjects = { plate.Id }, StressInstants = 4, StressTargetCells = 150, StressMeshCheck = true
            };
            var r = AnalysisRun.Run(world, s);
            var st = Assert.Single(r.Stress);
            foreach (var n in st.Notes) _out.WriteLine(n);
            Assert.True(double.IsFinite(st.MeshChange), "the critical instant is re-solved on a finer mesh");
            Assert.True(RobotTwin.CoreSim.Analysis.Export.InpExport.CanExport(st));
            string inp = RobotTwin.CoreSim.Analysis.Export.InpExport.Write(st);
            Assert.Contains("*ELEMENT, TYPE=C3D10", inp);
            Assert.Contains("*CLOAD", inp);
            Assert.Contains("*BOUNDARY", inp);
            // Inertia relief: the exported loads (contacts + gravity + d'Alembert forces) are in balance,
            // so the 3-2-1 supports carry (almost) nothing — the same problem RobotWin solved.
            var f = st.ExportForces!;
            double sx = 0, sy = 0, sz = 0, mag = 0;
            for (int i = 0; i < f.Length; i += 3)
            {
                sx += f[i];
                sy += f[i + 1];
                sz += f[i + 2];
                mag += System.Math.Abs(f[i]) + System.Math.Abs(f[i + 1]) + System.Math.Abs(f[i + 2]);
            }

            double residual = System.Math.Sqrt(sx * sx + sy * sy + sz * sz);
            _out.WriteLine($"export: {inp.Length / 1024} kB, net force {residual:E2} N of {mag:E2} N applied");
            Assert.True(residual < 1e-6 * mag + 1e-9);
        }
}
}
