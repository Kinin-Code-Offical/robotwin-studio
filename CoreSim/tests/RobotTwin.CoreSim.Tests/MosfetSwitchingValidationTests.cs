using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [validation] A MOSFET switched on into a clamped constant current: the gate-charge test circuit of the datasheets.
    /// With the transistor's capacitances constant the turn-on has closed-form phases: the gate charges through R_g with
    /// τ = R_g (C_gs + C_gd) to the threshold (delay) and on to the plateau voltage V_p = V_th + √(2 I / K) while the
    /// current moves from the clamp diode to the transistor; then on the Miller plateau the whole gate current
    /// (V_drive − V_p) / R_g discharges C_gd while the drain falls from the clamp to V_p − V_th; the gate charge to the
    /// drive voltage is C_gs V_drive + C_gd (V_drive + V_clamp − V_ds,on) whatever the path. IRFZ44N-like values.
    /// </summary>
    [Trait("Category", "Analog")]
    public class MosfetSwitchingValidationTests
    {
        private readonly ITestOutputHelper _out;

        public MosfetSwitchingValidationTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TheTurnOnFollowsTheGateChargeModel()
        {
            // the gate pulse comes once the switch-on of the supply (its dv/dt through C_gd) has died away: 20 τ
            const double Vdd = 24, Vdrive = 10, Rg = 10, I0 = 10, Cgs = 1.3e-9, Cgd = 0.2e-9, Vth = 3, delay = 300e-9;
            var c = new AnalogCircuit();
            int vdd = c.Node("vdd"), drv = c.Node("drv"), gate = c.Node("gate"), sw = c.Node("sw");
            c.Add(new VoltageSource("VDD", vdd, 0, Vdd));
            c.Add(new VoltageSource("VG", drv, 0, new PulseWaveform(0, Vdrive, 1e-3, 5e-4, delay, 1e-12, 1e-12)));
            c.Add(new Resistor("RG", drv, gate, Rg));
            c.Add(new CurrentSource("I0", sw, 0, I0));   // pushes 10 A into the switch node
            c.Add(new Diode("D1", sw, vdd));               // the clamp: the current goes to the supply while the switch is off
            var q = c.Add(new Mosfet("Q1", sw, gate, 0) { Threshold = Vth, ChannelModulation = 0, GateSourceCapacitance = Cgs, GateDrainCapacitance = Cgd });
            q.SetOnResistance(0.0175, 10);
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 5e-11 });
            sim.Initialize();
            while (sim.Time < delay - 1e-12) sim.Step(Math.Min(5e-11, delay - sim.Time));
            double vClamp = sim.NodeVoltage("sw");
            Assert.InRange(sim.NodeVoltage("gate"), -0.01, 0.01);   // settled: the gate back at 0 V

            double K = q.Transconductance, tau = Rg * (Cgs + Cgd), vp = Vth + Math.Sqrt(2 * I0 / K), vov = vp - Vth;
            double tTh = tau * Math.Log(Vdrive / (Vdrive - Vth)), tP = tau * Math.Log(Vdrive / (Vdrive - vp));
            double tPlateau = Cgd * (vClamp - vov) * Rg / (Vdrive - vp);
            double vdsOn = I0 / (K * (Vdrive - Vth));

            double prevT = sim.Time, prevVg = sim.NodeVoltage("gate"), prevVd = vClamp, qg = 0;
            double atTh = double.NaN, fallStart = double.NaN, fallEnd = double.NaN;
            double Cross(double t0, double a, double t1, double b, double level) => t0 + (level - a) / (b - a) * (t1 - t0);
            while (sim.Time < delay + 300e-9)
            {
                sim.Step(5e-11);
                double t = sim.Time, vg = sim.NodeVoltage("gate"), vd = sim.NodeVoltage("sw");
                qg += 0.5 * ((Vdrive - prevVg) + (Vdrive - vg)) / Rg * (t - prevT);
                if (double.IsNaN(atTh) && vg >= Vth) atTh = Cross(prevT, prevVg, t, vg, Vth);
                if (double.IsNaN(fallStart) && vd <= vClamp - 0.2) fallStart = Cross(prevT, prevVd, t, vd, vClamp - 0.2);
                if (double.IsNaN(fallEnd) && vd <= vov) fallEnd = Cross(prevT, prevVd, t, vd, vov);
                (prevT, prevVg, prevVd) = (t, vg, vd);
            }

            double qgTheory = Cgs * Vdrive + Cgd * (Vdrive + vClamp - sim.NodeVoltage("sw"));
            double plateau = fallEnd - fallStart;
            _out.WriteLine($"clamp {vClamp:0.000} V, K {K:0.00} A/V², V_p {vp:0.000} V, τ {tau * 1e9:0.00} ns");
            _out.WriteLine($"to V_th: {(atTh - delay) * 1e9:0.000} ns (theory {tTh * 1e9:0.000} ns, {((atTh - delay) / tTh - 1) * 100:0.0} %)");
            _out.WriteLine($"drain starts to fall at {(fallStart - delay) * 1e9:0.000} ns (V_p reached in theory at {tP * 1e9:0.000} ns); Miller plateau {plateau * 1e9:0.000} ns (theory {tPlateau * 1e9:0.000} ns, {(plateau / tPlateau - 1) * 100:0.0} %)");
            _out.WriteLine($"gate charge {qg * 1e9:0.000} nC (C·V {qgTheory * 1e9:0.000} nC, {(qg / qgTheory - 1) * 100:0.00} %); V_ds,on {sim.NodeVoltage("sw") * 1e3:0.0} mV (≈ I/(K·V_ov) {vdsOn * 1e3:0.0} mV)");
            Assert.InRange((atTh - delay) / tTh, 0.97, 1.03);
            Assert.InRange((fallStart - delay) / tP, 0.95, 1.08);
            Assert.InRange(plateau / tPlateau, 0.93, 1.07);
            Assert.InRange(qg / qgTheory, 0.98, 1.02);
            Assert.InRange(sim.NodeVoltage("sw") / vdsOn, 0.9, 1.15);
        }
    }
}
