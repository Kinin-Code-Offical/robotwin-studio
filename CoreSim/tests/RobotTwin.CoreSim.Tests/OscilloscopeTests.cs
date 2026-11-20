using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Instruments;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Instruments")]
    public class OscilloscopeTests
    {
        /// <summary>Samples a waveform at irregular instants, like the adaptive solver does.</summary>
        private static SampleBuffer Sampled(Func<double, double> f, double until, double minStep, double maxStep, int seed = 7)
        {
            var buf = new SampleBuffer(400_000);
            var rng = new Random(seed);
            for (double t = 0; t <= until; t += minStep + (maxStep - minStep) * rng.NextDouble()) buf.Add(t, f(t));
            return buf;
        }

        private static double Sine(double t) => Math.Sin(2 * Math.PI * 1000 * t);

        private static Oscilloscope Scope(ISampleSource src, double secPerDiv, double voltsPerDiv, bool noise = false)
        {
            var scope = new Oscilloscope { SecondsPerDiv = secPerDiv, NoiseEnabled = noise };
            scope.Channels[0].Source = src;
            scope.Channels[0].Scale = voltsPerDiv;
            for (int i = 1; i < 4; i++) scope.Channels[i].Enabled = false;
            return scope;
        }

        [Theory]
        [InlineData(1e-3, 1, 2e-3)]
        [InlineData(2e-3, 1, 5e-3)]
        [InlineData(5e-3, 1, 10e-3)]
        [InlineData(1.0, -1, 0.5)]
        [InlineData(0.5, -1, 0.2)]
        [InlineData(0.2, -1, 0.1)]
        public void KnobsStepThroughOneTwoFive(double from, int dir, double expected)
        {
            Assert.Equal(expected, Steps125.Next(from, dir), 12);
        }

        [Fact]
        public void SnapAndAtLeastStayOnTheSequence()
        {
            Assert.Equal(2e-3, Steps125.Snap(0.003), 12);
            Assert.Equal(5e-3, Steps125.AtLeast(0.003), 12);
            Assert.Equal(10, Steps125.Range(1e-3, 1).Count);
        }

        [Fact]
        public void TriggeredDisplayIsStableFromFrameToFrame()
        {
            var src = Sampled(Sine, 0.06, 4e-6, 14e-6);
            var scope = Scope(src, 200e-6, 0.5);
            var a = scope.Acquire(0.031)!;
            var b = scope.Acquire(0.0473)!;
            Assert.Equal(TriggerStatus.Triggered, a.Status);
            Assert.Equal(TriggerStatus.Triggered, b.Status);
            Assert.NotEqual(a.TriggerTime, b.TriggerTime);

            var ta = a.Traces[0].Values;
            var tb = b.Traces[0].Values;
            int center = ta.Length / 2;
            Assert.InRange(ta[center], -0.03, 0.03);
            Assert.True(ta[center + 20] > ta[center - 20], "rising slope at the trigger point");
            double maxDiff = ta.Zip(tb, (x, y) => Math.Abs(x - y)).Max();
            Assert.True(maxDiff < 0.03, $"frames differ by {maxDiff}");
        }

        [Fact]
        public void FallingSlopeAndLevelMoveTheTriggerPoint()
        {
            var src = Sampled(Sine, 0.03, 4e-6, 14e-6);
            var scope = Scope(src, 200e-6, 0.5);
            scope.TriggerSlope = TriggerSlope.Falling;
            scope.TriggerLevel = 0.5;
            var f = scope.Acquire(0.02)!;
            var v = f.Traces[0].Values;
            int c = v.Length / 2;
            Assert.InRange(v[c], 0.47, 0.53);
            Assert.True(v[c + 20] < v[c - 20]);
        }

        [Fact]
        public void AutomaticMeasurementsOfASine()
        {
            var src = Sampled(t => 2.0 + Sine(t), 0.03, 4e-6, 10e-6);
            var scope = Scope(src, 500e-6, 0.5);
            scope.Channels[0].Position = -4.0;
            scope.TriggerLevel = 2.0;
            var m = scope.Acquire(0.025)!.Traces[0].Measurements;
            Assert.InRange(m.PeakToPeak, 1.96, 2.05);
            Assert.InRange(m.Mean, 1.97, 2.03);
            Assert.InRange(m.Rms, Math.Sqrt(4 + 0.5) - 0.02, Math.Sqrt(4 + 0.5) + 0.02);
            Assert.InRange(m.Frequency, 990, 1010);
            Assert.InRange(m.Period, 0.99e-3, 1.01e-3);
        }

        [Fact]
        public void DutyCycleAndRiseTimeOfAPulse()
        {
            var w = new PulseWaveform(0, 5, 100e-6, 25e-6 - 1e-6, 0, 1e-6, 1e-6);
            var src = new SampleBuffer();
            for (double t = 0; t <= 2e-3; t += 0.1e-6) src.Add(t, w.Value(t));

            var scope = Scope(src, 50e-6, 1.0);
            scope.TriggerLevel = 2.5;
            var m = scope.Acquire(1.5e-3)!.Traces[0].Measurements;
            Assert.InRange(m.Duty, 0.24, 0.26);
            Assert.InRange(m.Frequency, 9.9e3, 10.1e3);

            scope.SecondsPerDiv = 0.5e-6;
            scope.ResetTriggerScan();
            var edge = scope.Acquire(1.6e-3)!.Traces[0].Measurements;
            Assert.InRange(edge.RiseTime, 0.75e-6, 0.85e-6);
        }

        [Fact]
        public void AcCouplingRemovesTheDcOffsetAndGndIsFlat()
        {
            var src = Sampled(t => 3.0 + Sine(t), 0.3, 8e-6, 20e-6);
            var scope = Scope(src, 500e-6, 0.5);
            scope.Channels[0].Coupling = ScopeCoupling.AC;
            var m = scope.Acquire(0.25)!.Traces[0].Measurements;
            Assert.InRange(m.Mean, -0.06, 0.06);
            Assert.InRange(m.PeakToPeak, 1.9, 2.1);

            scope.Channels[0].Coupling = ScopeCoupling.GND;
            var g = scope.Acquire(0.29)!.Traces[0];
            Assert.All(g.Values.Where(x => !double.IsNaN(x)), x => Assert.Equal(0.0, x, 9));
        }

        [Fact]
        public void TenTimesProbeShowsTenTimesTheNoiseAtTheSameDisplayedScale()
        {
            var src = Sampled(_ => 1.0, 0.02, 4e-6, 10e-6);
            double Std(double probe)
            {
                var scope = Scope(src, 100e-6, 0.05, noise: true);
                scope.Channels[0].ProbeAttenuation = probe;
                scope.Channels[0].Position = -1.0 / 0.05;
                var v = scope.Acquire(0.015)!.Traces[0].Values.Where(x => !double.IsNaN(x)).ToArray();
                double mean = v.Average();
                return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Length);
            }

            double x1 = Std(1), x10 = Std(10);
            Assert.True(x10 > 1.8 * x1, $"1×: {x1}, 10×: {x10}");
        }

        [Fact]
        public void SignalsBeyondTheScreenAreClipped()
        {
            var src = Sampled(t => 10 * Sine(t), 0.02, 4e-6, 10e-6);
            var scope = Scope(src, 200e-6, 0.5);
            var tr = scope.Acquire(0.015)!.Traces[0];
            Assert.True(tr.Clipped);
            Assert.True(tr.Values.Where(x => !double.IsNaN(x)).Max() <= 5.12 * 0.5 + 1e-9);
        }

        [Fact]
        public void BandwidthLimitSlowsAFastEdge()
        {
            var w = new PulseWaveform(0, 1, 1e-6, 0.5e-6, 0, 1e-9, 1e-9);
            var src = new SampleBuffer();
            for (double t = 0; t <= 20e-6; t += 0.25e-9) src.Add(t, w.Value(t));

            double Rise(bool limit)
            {
                var scope = Scope(src, 20e-9, 0.2);
                scope.TriggerLevel = 0.5;
                scope.Channels[0].BandwidthLimit = limit;
                return scope.Acquire(10e-6)!.Traces[0].Measurements.RiseTime;
            }

            double full = Rise(false), limited = Rise(true);
            Assert.InRange(full, 2e-9, 6e-9);
            Assert.InRange(limited, 14e-9, 22e-9);
        }

        [Fact]
        public void SampleRateIsLimitedByTheRecordLength()
        {
            var scope = new Oscilloscope { RecordLength = 10_000, SecondsPerDiv = 1e-3 };
            Assert.Equal(1e6, scope.SampleRate, 6);
            scope.SecondsPerDiv = 10e-9;
            Assert.Equal(1e9, scope.SampleRate, 6);
        }

        [Fact]
        public void NormalModeWaitsWhileAutoModeFreeRuns()
        {
            var src = Sampled(_ => 1.0, 0.05, 4e-6, 10e-6);
            var scope = Scope(src, 1e-3, 0.5);
            scope.TriggerLevel = 2.0;
            scope.TriggerMode = TriggerMode.Normal;
            Assert.Null(scope.Acquire(0.04));
            Assert.Equal(TriggerStatus.Waiting, scope.Status);

            scope.TriggerMode = TriggerMode.Auto;
            var f = scope.Acquire(0.045)!;
            Assert.Equal(TriggerStatus.Auto, f.Status);
            Assert.InRange(f.Traces[0].Measurements.Mean, 0.98, 1.02);
        }

        [Fact]
        public void SingleCapturesOnceThenStops()
        {
            var src = Sampled(Sine, 0.05, 4e-6, 10e-6);
            var scope = Scope(src, 100e-6, 0.5);
            scope.Single();
            var f = scope.Acquire(0.02)!;
            Assert.Equal(TriggerStatus.Triggered, f.Status);
            Assert.False(scope.Running);
            Assert.Same(f, scope.Acquire(0.04));
        }

        [Fact]
        public void HoldoffSpacesTheTriggers()
        {
            var src = Sampled(Sine, 0.2, 4e-6, 10e-6);
            var scope = Scope(src, 50e-6, 0.5);
            scope.Holdoff = 2.6e-3;
            var times = new System.Collections.Generic.List<double>();
            for (double now = 0.01; now < 0.19; now += 0.4e-3)
            {
                var f = scope.Acquire(now);
                if (f != null && f.Status == TriggerStatus.Triggered && (times.Count == 0 || f.TriggerTime != times[^1])) times.Add(f.TriggerTime);
            }

            Assert.True(times.Count > 10);
            for (int i = 1; i < times.Count; i++) Assert.True(times[i] - times[i - 1] >= 2.6e-3 + 0.5e-3 - 1e-9);
        }

        [Fact]
        public void MathChannelSubtractsTheInputs()
        {
            var a = Sampled(t => 1.0 + Sine(t), 0.03, 4e-6, 10e-6);
            var b = Sampled(_ => 1.0, 0.03, 4e-6, 10e-6, 3);
            var scope = Scope(a, 200e-6, 0.5);
            scope.Channels[1].Enabled = true;
            scope.Channels[1].Source = b;
            scope.Channels[1].Scale = 0.5;
            scope.MathFunction = ScopeMathFunction.Subtract;
            scope.MathScale = 0.5;
            var f = scope.Acquire(0.025)!;
            Assert.NotNull(f.Math);
            Assert.InRange(f.Math!.Measurements.Mean, -0.05, 0.05);
            Assert.InRange(f.Math.Measurements.PeakToPeak, 1.9, 2.1);
        }

        [Fact]
        public void AutosetFindsScaleTimebaseAndLevel()
        {
            var src = Sampled(t => 2.5 + 2.5 * Sine(t), 0.3, 4e-6, 10e-6);
            var scope = Scope(src, 1.0, 10);
            scope.Autoset(0.25);
            var ch = scope.Channels[0];
            Assert.InRange(ch.Scale, 0.5, 2.0);
            Assert.InRange(scope.SecondsPerDiv, 0.2e-3, 0.5e-3);
            Assert.InRange(scope.TriggerLevel, 2.3, 2.7);
            var f = scope.Acquire(0.26)!;
            Assert.Equal(TriggerStatus.Triggered, f.Status);
        }

        [Fact]
        public void ProbeLoadPullsDownAHighImpedanceNode()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), mid = c.Node("mid");
            c.Add(new VoltageSource("V1", vcc, 0, 10.0));
            c.Add(new Resistor("R1", vcc, mid, 10e6));
            c.Add(new Resistor("R2", mid, 0, 10e6));
            var probe = c.Add(new ScopeProbeLoad("~CH1"));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(1e-3);
            Assert.InRange(sim.NodeVoltage(mid), 4.99, 5.01);

            probe.SetProbe(1);
            probe.Node = mid;
            sim.Step(0.05);
            double r2 = 1.0 / (1.0 / 10e6 + 1.0 / 1e6);
            Assert.InRange(sim.NodeVoltage(mid), 10 * r2 / (10e6 + r2) - 0.01, 10 * r2 / (10e6 + r2) + 0.01);

            probe.SetProbe(10);
            sim.Step(0.05);
            Assert.InRange(sim.NodeVoltage(mid), 3.32, 3.35);
        }
            [Fact]
        public void PeriodicSignalStaysTriggeredBetweenTriggers()
        {
            var src = Sampled(Sine, 0.1, 4e-6, 10e-6);
            var scope = Scope(src, 100e-6, 0.5);
            for (double now = 0.01; now < 0.09; now += 0.13e-3)
            {
                scope.Acquire(now);
                Assert.Equal(TriggerStatus.Triggered, scope.Status);
            }
        }

        [Fact]
        public void AutoScaleCentresAProbedSignalOnScreen()
        {
            var dc = Sampled(_ => 1.97, 0.02, 4e-6, 10e-6);
            var scope = Scope(dc, 1e-3, 0.1);
            Assert.True(scope.AutoScaleChannel(scope.Channels[0], 0.02, true));
            var ch = scope.Channels[0];
            Assert.InRange(Math.Abs(1.97 / ch.Scale + ch.Position), 0.0, 0.01);
            Assert.InRange(Math.Abs(ch.Position), 0.0, 3.5);
            Assert.Equal(1.97, scope.TriggerLevel, 6);

            var sq = Sampled(t => t % 1e-3 < 0.5e-3 ? 5.0 : 0.0, 0.02, 4e-6, 10e-6);
            scope.Channels[0].Source = sq;
            scope.AutoScaleChannel(scope.Channels[0], 0.02, true);
            Assert.Equal(1.0, scope.Channels[0].Scale, 9);
            Assert.False(scope.Acquire(0.019)!.Traces[0].Clipped);
        }
    }
}
