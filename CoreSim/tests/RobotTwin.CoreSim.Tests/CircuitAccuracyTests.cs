using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [analog] How close the circuit solver comes to closed-form answers at the step the app uses (0.2 ms at most,
    /// relative tolerance 10⁻³), how its error shrinks with the step, and how well an undamped tank keeps its energy:
    /// the figures behind the circuit chapter's validity range.
    /// </summary>
    [Trait("Category", "Analog")]
    public class CircuitAccuracyTests
    {
        private readonly ITestOutputHelper _out;

        public CircuitAccuracyTests(ITestOutputHelper output) => _out = output;

        private static AnalogOptions App => new() { MaxStep = 2e-4, RelTol = 1e-3 };

        private static (AnalogSimulator sim, Capacitor cap) Rc(AnalogOptions options)
        {
            // 1 kΩ, 1 µF, 5 V switched on at t = 0: v = 5·(1 − e^(−t/τ)), τ = 1 ms
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), top = c.Node("cap");
            c.Add(new VoltageSource("V1", vcc, AnalogCircuit.Ground, 5.0));
            c.Add(new Resistor("R1", vcc, top, 1000));
            var cap = c.Add(new Capacitor("C1", top, 0, 1e-6));
            var sim = new AnalogSimulator(c, options);
            sim.Initialize();
            return (sim, cap);
        }

        [Fact]
        public void AnRcChargeFollowsTheExponentialAtTheAppsStep()
        {
            var (sim, _) = Rc(App);
            double worst = 0;
            while (sim.Time < 5e-3 - 1e-12)
            {
                sim.Step(2e-4);
                worst = Math.Max(worst, Math.Abs(sim.NodeVoltage("cap") - 5.0 * (1 - Math.Exp(-sim.Time / 1e-3))));
            }

            _out.WriteLine($"RC at the app's step: largest error {worst * 1000:F3} mV of 5 V over five time constants ({worst / 5 * 100:F3} %)");
            Assert.True(worst < 1e-2, $"{worst * 1000:F2} mV");   // 0.2 %: the step control's tolerance is 10⁻³ of the signal
        }

        [Fact]
        public void AnRlCurrentRisesAlongTheExponentialAtTheAppsStep()
        {
            // 10 V, 10 Ω, 10 mH: i = 1 A·(1 − e^(−t/τ)), τ = 1 ms
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), mid = c.Node("mid");
            c.Add(new VoltageSource("V1", vcc, AnalogCircuit.Ground, 10.0));
            c.Add(new Resistor("R1", vcc, mid, 10));
            var l = c.Add(new Inductor("L1", mid, 0, 0.01) { SeriesResistance = 0.0 });
            var sim = new AnalogSimulator(c, App);
            sim.Initialize();
            double worst = 0;
            while (sim.Time < 5e-3 - 1e-12)
            {
                sim.Step(2e-4);
                worst = Math.Max(worst, Math.Abs(l.Current - (1 - Math.Exp(-sim.Time / 1e-3))));
            }

            _out.WriteLine($"RL at the app's step: largest error {worst * 1000:F3} mA of 1 A over five time constants");
            Assert.True(worst < 5e-3, $"{worst * 1000:F2} mA");
        }

        [Fact]
        public void TheErrorShrinksWithTheSquareOfTheStep()
        {
            // The trapezoidal rule is second order: a lossless LC tank's period comes out long by (ωh)²/12, so halving the
            // step quarters the error. (An RC charge from a switched-on source mixes in the few backward Euler steps the
            // solver takes after a discontinuity, which are first order.)
            double period = 2 * Math.PI * Math.Sqrt(1e-3 * 1e-6);
            int[] perPeriod = { 32, 64, 128, 256 };   // coarser than T/32 the step control takes smaller steps by itself
            var errors = perPeriod.Select(k => TankPeriod(period / k, 20) / period - 1).ToArray();
            var ratios = Enumerable.Range(1, errors.Length - 1).Select(i => errors[i - 1] / errors[i]).ToArray();
            _out.WriteLine("LC period error at " + string.Join(", ", perPeriod.Select((k, i) => $"T/{k}: {errors[i] * 100:F4} % (theory {Math.Pow(2 * Math.PI / k, 2) / 12 * 100:F4} %)")));
            _out.WriteLine("ratio per halving: " + string.Join(", ", ratios.Select(r => r.ToString("F2"))));
            Assert.All(ratios, r => Assert.InRange(r, 3.6, 4.4));
            for (int i = 0; i < perPeriod.Length; i++) Assert.InRange(errors[i] / (Math.Pow(2 * Math.PI / perPeriod[i], 2) / 12), 0.9, 1.1);
        }

        /// <summary>A 1 mH, 1 µF tank started at 1 V, stepped at <paramref name="dt"/>: its mean period over the cycles.</summary>
        private static double TankPeriod(double dt, int cycles)
        {
            var c = new AnalogCircuit();
            int top = c.Node("top");
            c.Add(new Capacitor("C1", top, 0, 1e-6) { InitialVoltage = 1.0 });
            c.Add(new Inductor("L1", top, 0, 1e-3) { SeriesResistance = 0.0 });
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = dt });
            sim.Initialize();
            double period = 2 * Math.PI * Math.Sqrt(1e-3 * 1e-6), last = 1, first = double.NaN, latest = double.NaN;
            int crossings = 0;
            while (sim.Time < (cycles + 0.5) * period)
            {
                double t0 = sim.Time;
                sim.Step(dt);
                double v = sim.NodeVoltage("top");
                if (last > 0 && v <= 0)
                {
                    double t = t0 + dt * last / (last - v);
                    if (crossings == 0) first = t;
                    latest = t;
                    crossings++;
                }

                last = v;
            }

            return (latest - first) / (crossings - 1);
        }

        [Fact]
        public void AnLcTankKeepsItsAmplitudeAndPeriodForAHundredCycles()
        {
            // 1 mH, 1 µF, 1 V: f = 5033 Hz. The trapezoidal rule keeps a lossless tank's energy; its period comes out a
            // little long, by (ωh)²/12 for the step h it takes.
            var c = new AnalogCircuit();
            int top = c.Node("top");
            c.Add(new Capacitor("C1", top, 0, 1e-6) { InitialVoltage = 1.0 });
            c.Add(new Inductor("L1", top, 0, 1e-3) { SeriesResistance = 0.0 });
            var sim = new AnalogSimulator(c, App);
            sim.Initialize();
            double period = 2 * Math.PI * Math.Sqrt(1e-3 * 1e-6), dt = period / 64;
            double last = 1, firstCrossing = double.NaN, lastCrossing = double.NaN, peak = 0;
            int crossings = 0;
            while (sim.Time < 100.5 * period)
            {
                double t0 = sim.Time;
                sim.Step(dt);
                double v = sim.NodeVoltage("top");
                if (last > 0 && v <= 0)
                {
                    double t = t0 + dt * last / (last - v);   // falling zero crossing, interpolated
                    if (crossings == 0) firstCrossing = t;
                    lastCrossing = t;
                    crossings++;
                }

                if (sim.Time > 99.5 * period) peak = Math.Max(peak, Math.Abs(v));
                last = v;
            }

            double measured = (lastCrossing - firstCrossing) / (crossings - 1);
            _out.WriteLine($"LC tank: period {measured * 1e6:F3} µs (theory {period * 1e6:F3}, {(measured / period - 1) * 100:F4} %), amplitude after 100 cycles {peak:F4} V of 1 V");
            Assert.InRange(measured / period - 1, -1e-3, 1e-3);
            Assert.InRange(peak, 0.99, 1.01);
        }
    }
}
