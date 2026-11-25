using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>[uncertainty] How much a result owes to the solver's step, measured instead of assumed.</summary>
    public sealed class StepCheck
    {
        /// <summary>Simulated time compared (s): the run's start, at most <see cref="AnalysisRun.StepCheckSeconds"/>.</summary>
        public double Seconds { get; set; }

        /// <summary>The physics substep of the run and of the check (s).</summary>
        public double Step { get; set; }

        public double HalfStep { get; set; }

        /// <summary>The largest difference of any object's position between the two (m), and whose it was.</summary>
        public double PositionM { get; set; }

        public string? PositionObject { get; set; }

        /// <summary>The recorded quantity that moved most, its difference relative to its own range over the window.</summary>
        public double WorstRelative { get; set; }

        public string? WorstChannel { get; set; }
    }

    public static partial class AnalysisRun
    {
        /// <summary>A centred moving average over <paramref name="width"/> samples.</summary>
        private static double[] Smooth(double[] v, int width)
        {
            var o = new double[v.Length];
            int half = width / 2;
            for (int i = 0; i < v.Length; i++)
            {
                int lo = Math.Max(0, i - half), hi = Math.Min(v.Length - 1, i + half);
                double sum = 0;
                for (int k = lo; k <= hi; k++) sum += v[k];
                o[i] = sum / (hi - lo + 1);
            }

            return o;
        }

        /// <summary>The start of a run that the time-step check repeats at half the step (s).</summary>
        public const double StepCheckSeconds = 2.0;

        /// <summary>
        /// [uncertainty] Repeats the start of the run with half the physics substep and compares what both recorded: every
        /// object's path and every channel. Semi-implicit Euler is first order, so the run's own step error is about the
        /// difference between the two (the half-step run carries about half of it): a measured bound on how far the
        /// result depends on the step, for this world, instead of a figure from a test somewhere else.
        /// </summary>
        private static void TimeStepCheck(WorldDocument world, AnalysisSettings s, IWorldGeometry? geometry, AnalysisResult result,
            CancellationToken cancel, Action<string, double>? progress)
        {
            var acc = s.AccuracyValues;
            double dt = 1.0 / s.OutputRate;
            int n = Math.Min(result.Channels.Count, (int)Math.Round(StepCheckSeconds / dt) + 1);
            if (n < 3) return;
            var options = new RunnerOptions { Seed = s.Seed, Firmware = s.Firmware, MaxStep = acc.AnalogMaxStep, RelTol = acc.RelTol, Liquid = s.LiquidQuality, Esp32Builder = Esp32Builder };
            var temp = new AnalysisResult { Settings = s };
            using (var runner = new WorldRunner(world, options, geometry))
            {
                var scene = runner.Physics?.Scene;
                if (scene == null) return;
                scene.MaxSubstep = acc.MaxSubstep / 2;
                scene.Iterations = Math.Max(scene.Iterations, acc.Iterations);
                scene.SleepEnabled = acc.Sleep;
                var probe = new WorldProbe(runner, s, temp, Array.Empty<string>());
                var poses = PoseRecorder(world, temp.Channels);
                var replay = s.Inputs is { Count: > 0 } journal ? new InputReplay(journal) : null;
                replay?.Start(runner);
                probe.Sample();
                RecordPoses(runner.Physics, temp.Channels, poses, world);
                for (int i = 1; i < n && !cancel.IsCancellationRequested; i++)
                {
                    if (replay != null) replay.Step(runner, dt);
                    else runner.Step(dt);
                    probe.Sample();
                    RecordPoses(runner.Physics, temp.Channels, poses, world);
                    if (runner.Halted) break;
                    if ((i & 31) == 0) progress?.Invoke("Zaman adımı denetimi (yarım adımla)", (double)i / n);
                }

                probe.Detach();
            }

            if (cancel.IsCancellationRequested) return;
            var check = new StepCheck { Seconds = (n - 1) * dt, Step = acc.MaxSubstep, HalfStep = acc.MaxSubstep / 2 };
            var main = result.Channels.Channels.ToDictionary(c => c.Name, StringComparer.Ordinal);

            // Paths: the largest distance between where an object was in the two runs.
            foreach (var o in world.Objects)
            {
                string key = "pose/" + o.Id + "/";
                if (!main.TryGetValue(key + "px", out var ax) || !main.TryGetValue(key + "py", out var ay) || !main.TryGetValue(key + "pz", out var az)) continue;
                var bx = temp.Channels.Find(key + "px");
                var by = temp.Channels.Find(key + "py");
                var bz = temp.Channels.Find(key + "pz");
                if (bx == null || by == null || bz == null) continue;
                int m = Math.Min(n, Math.Min(Math.Min(ax.Count, bx.Count), Math.Min(Math.Min(ay.Count, by.Count), Math.Min(az.Count, bz.Count))));
                for (int i = 0; i < m; i++)
                {
                    double dx = ax[i] - bx[i], dy = ay[i] - by[i], dz = az[i] - bz[i];
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (!double.IsFinite(d) || d <= check.PositionM) continue;
                    check.PositionM = d;
                    check.PositionObject = o.Name;
                }
            }

            // Everything else recorded, smoothed over 50 ms: the difference against the channel's own range in the window.
            // Two kinds of channel are left out, as they differ from run to run whatever the step and say nothing about it:
            // one that is mostly jitter (its smoothed range under a fifth of its raw one), and one that barely moves next to
            // the others of its unit (under 5 % of the largest range in it, such as a turning wheel's sideways spin beside
            // its spin about its axle).
            int w = Math.Max(1, (int)Math.Round(0.05 / dt));
            var pairs = new List<(Channel A, double[] Sa, double[] Sb, double Range)>();
            var widest = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var b in temp.Channels.Channels)
            {
                if (b.Name.StartsWith("pose/", StringComparison.Ordinal) || !main.TryGetValue(b.Name, out var a)) continue;
                int m = Math.Min(n, Math.Min(a.Count, b.Count));
                if (m < 3) continue;
                double[] ra = new double[m], rb = new double[m];
                bool finite = true;
                for (int i = 0; i < m && finite; i++)
                {
                    ra[i] = a[i];
                    rb[i] = b[i];
                    finite = double.IsFinite(ra[i]) && double.IsFinite(rb[i]);
                }

                if (!finite || ra.All(v => v == Math.Round(v))) continue;   // counts and states (contacts, a switch) jump, they do not drift
                double raw = ra.Max() - ra.Min();
                var sa = Smooth(ra, w);
                var sb = Smooth(rb, w);
                double range = sa.Max() - sa.Min();
                if (!(range > 1e-12) || range < 0.2 * raw) continue;
                pairs.Add((a, sa, sb, range));
                widest[a.Unit] = Math.Max(widest.TryGetValue(a.Unit, out var x) ? x : 0, range);
            }

            foreach (var (a, sa, sb, range) in pairs)
            {
                if (range < 0.05 * widest[a.Unit]) continue;
                double diff = 0;
                for (int i = 0; i < sa.Length; i++) diff = Math.Max(diff, Math.Abs(sa[i] - sb[i]));
                double rel = diff / range;
                if (rel <= check.WorstRelative) continue;
                check.WorstRelative = rel;
                check.WorstChannel = a.Label;
            }

            result.StepCheck = check;
        }
    }
}
