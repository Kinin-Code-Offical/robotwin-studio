using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [validation] A linear regulator's answer to its load, against its datasheet (TI LM340/LM7805, SNOSBT0L): the
    /// output resistance (8 mΩ at 1 kHz), the load regulation (typ. 10 mV, max. 25 mV from 5 mA to 1.5 A) and the
    /// output impedance curve (Figure 7, no output capacitor: flat at 8 mΩ, rising like an inductor above ~2 kHz to
    /// ~0.3 Ω at 100 kHz and levelling near 1 Ω toward 1 MHz). The model is that curve: the set point behind 8 mΩ in
    /// series with 0.5 µH ∥ 1.2 Ω. A load step on a 0.1 µF output then follows those equations, which the reference
    /// integrates with a fine step.
    /// </summary>
    [Trait("Category", "Analog")]
    public class RegulatorLoadValidationTests
    {
        private const double Rout = 0.008, Lout = 0.5e-6, Rol = 1.2;

        private readonly ITestOutputHelper _out;

        public RegulatorLoadValidationTests(ITestOutputHelper output) => _out = output;

        private static (AnalogSimulator Sim, LinearRegulator Reg) Build(Waveform load, double cout = 0, double maxStep = 1e-6)
        {
            var c = new AnalogCircuit();
            int vin = c.Node("vin"), vout = c.Node("vout");
            c.Add(new VoltageSource("VIN", vin, 0, 10.0));
            var reg = c.Add(new LinearRegulator("U1", vin, 0, vout, 5.0) { OutputResistance = Rout, OutputInductance = Lout, OpenLoopResistance = Rol, ThermalResistance = 0 });
            c.Add(new CurrentSource("ILOAD", 0, vout, load));   // draws the load from the output
            if (cout > 0) c.Add(new Capacitor("COUT", vout, 0, cout) { InitialVoltage = 5.0 });
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = maxStep, RelTol = 1e-4 });
            sim.Initialize(InitialState.OperatingPoint);
            return (sim, reg);
        }

        [Fact]
        public void LoadRegulationIsWithinTheDatasheet()
        {
            double V(double amps)
            {
                var (sim, _) = Build(amps);
                while (sim.Time < 1e-3) sim.Step(1e-4);
                return sim.NodeVoltage("vout");
            }

            double drop = V(0.005) - V(1.5);
            _out.WriteLine($"load regulation 5 mA → 1.5 A: {drop * 1e3:0.00} mV (datasheet typ. 10 mV, max. 25 mV)");
            Assert.InRange(drop, 0.005, 0.025);
        }

        [Fact]
        public void TheOutputImpedanceFollowsTheDatasheetCurve()
        {
            // Figure 7, C_OUT = 0, I_OUT = 500 mA, read off the log–log graph (±40 %)
            var points = new (double F, double Z)[] { (100, 0.008), (1e3, 0.009), (1e4, 0.028), (1e5, 0.27) };
            foreach (var (f, zSheet) in points)
            {
                const double ia = 0.05;
                var (sim, _) = Build(new SineWaveform(0.5, ia, f), 0, 1 / (f * 200));
                double period = 1 / f, lo = double.MaxValue, hi = double.MinValue;
                while (sim.Time < 6 * period)
                {
                    sim.Step(period / 200);
                    if (sim.Time < 3 * period) continue;
                    double v = sim.NodeVoltage("vout");
                    lo = Math.Min(lo, v);
                    hi = Math.Max(hi, v);
                }

                // R + (jωL ∥ R_ol)
                double x = 2 * Math.PI * f * Lout, den = Rol * Rol + x * x;
                double re = Rout + Rol * x * x / den, im = Rol * Rol * x / den;
                double z = (hi - lo) / (2 * ia), model = Math.Sqrt(re * re + im * im);
                _out.WriteLine($"{f,8:0} Hz: |Z| {z * 1e3,8:0.00} mΩ (R + jωL ∥ R_ol {model * 1e3:0.00} mΩ, datasheet ≈ {zSheet * 1e3:0} mΩ, {(z / zSheet - 1) * 100:+0;-0} %)");
                Assert.InRange(z / model, 0.97, 1.03);
                Assert.InRange(z / zSheet, 0.6, 1.4);
            }
        }

        [Fact]
        public void ALoadStepOnASmallCapacitorFollowsTheImpedanceModel()
        {
            // 0.1 → 0.6 A in 1 µs at t = 20 µs, a 0.1 µF ceramic on the output (the datasheet's suggestion)
            const double C = 1e-7, i0 = 0.1, i1 = 0.6, t0 = 20e-6, rise = 1e-6;
            var (sim, _) = Build(new PulseWaveform(i0, i1, 1.0, 0.5, t0, rise, rise), C, 5e-9);
            double Load(double t) => t <= t0 ? i0 : t >= t0 + rise ? i1 : i0 + (i1 - i0) * (t - t0) / rise;

            // reference: the regulator's current i = i_L + v_x / R_ol, v_x = 5 − R i − v across L ∥ R_ol, L di_L/dt = v_x,
            // C dv/dt = i − I_load(t), from the steady state at 0.1 A
            double iL = i0, v = 5.0 - Rout * i0, t = 0;
            void Advance(double until)
            {
                const double h = 1e-10;
                while (t < until - 1e-15)
                {
                    double s = Math.Min(h, until - t);
                    (double Di, double Dv) F(double tt, double cl, double cv)
                    {
                        double ci = (cl * Rol + 5.0 - cv) / (Rol + Rout);
                        return ((5.0 - Rout * ci - cv) / Lout, (ci - Load(tt)) / C);
                    }

                    var k1 = F(t, iL, v);
                    var k2 = F(t + s / 2, iL + s / 2 * k1.Di, v + s / 2 * k1.Dv);
                    var k3 = F(t + s / 2, iL + s / 2 * k2.Di, v + s / 2 * k2.Dv);
                    var k4 = F(t + s, iL + s * k3.Di, v + s * k3.Dv);
                    iL += s / 6 * (k1.Di + 2 * k2.Di + 2 * k3.Di + k4.Di);
                    v += s / 6 * (k1.Dv + 2 * k2.Dv + 2 * k3.Dv + k4.Dv);
                    t += s;
                }
            }

            // settle the start (the output stage picks up 0.1 A), then follow the step
            while (sim.Time < t0 - 1e-6) sim.Step(5e-9);
            Advance(sim.Time);
            double before = sim.NodeVoltage("vout"), lowSim = double.MaxValue, lowRef = double.MaxValue, worst = 0;
            while (sim.Time < t0 + 30e-6)
            {
                sim.Step(5e-9);
                Advance(sim.Time);
                double vs = sim.NodeVoltage("vout");
                lowSim = Math.Min(lowSim, vs);
                lowRef = Math.Min(lowRef, v);
                worst = Math.Max(worst, Math.Abs(vs - v));
            }

            double dipSim = before - lowSim, dipRef = (5.0 - Rout * i0) - lowRef;
            _out.WriteLine($"0.1 → 0.6 A in 1 µs on 0.1 µF: dip {dipSim * 1e3:0.0} mV (the model's equations {dipRef * 1e3:0.0} mV, {(dipSim / dipRef - 1) * 100:0.0} %); largest difference {worst * 1e3:0.00} mV; settles at {sim.NodeVoltage("vout"):0.0000} V (5 − R·I {5 - Rout * i1:0.0000} V)");
            Assert.InRange(dipSim / dipRef, 0.95, 1.05);
            Assert.True(worst < 0.05 * dipRef, $"{worst * 1e3:0.0} mV");
        }
    }
}
