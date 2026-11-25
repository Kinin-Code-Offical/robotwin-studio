using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[ac] Small-signal frequency response against the textbook: an RC corner, an RLC resonance, a transistor stage's gain.</summary>
    public class AcAnalysisTests
    {
        private readonly ITestOutputHelper _out;

        public AcAnalysisTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AnRcLowPassFallsThreeDecibelsAtItsCorner()
        {
            var c = new AnalogCircuit();
            int input = c.Node("in"), output = c.Node("out");
            c.Add(new VoltageSource("VIN", input, AnalogCircuit.Ground, 0.0));
            c.Add(new Resistor("R1", input, output, 1000));
            c.Add(new Capacitor("C1", output, AnalogCircuit.Ground, 1e-6));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            double fc = 1 / (2 * Math.PI * 1000 * 1e-6);   // 159.15 Hz
            var r = sim.AcResponse("VIN", output, 0, new[] { 1.0, fc, 10 * fc, 100 * fc });
            _out.WriteLine(string.Join("; ", r.Select(p => $"{p.Frequency:0.#} Hz: {p.Decibels:0.00} dB, {p.PhaseDegrees:0.0}°")));
            Assert.InRange(r[0].Decibels, -0.01, 0.001);
            Assert.InRange(r[1].Decibels, -3.02, -3.00);
            Assert.InRange(r[1].PhaseDegrees, -45.1, -44.9);
            Assert.InRange(r[2].Decibels, -20.1, -20.0);   // −20 dB a decade above
            Assert.InRange(r[3].Decibels, -40.1, -39.9);
            double corner = AnalogSimulator.Corner(sim.AcResponse("VIN", output, 0, AnalogSimulator.Decades(1, 1e5, 40).ToList()));
            Assert.InRange(corner, fc * 0.995, fc * 1.005);
        }

        [Fact]
        public void ASeriesRlcPassesItsResonanceWhole()
        {
            var c = new AnalogCircuit();
            int input = c.Node("in"), a = c.Node("a"), output = c.Node("out");
            c.Add(new VoltageSource("VIN", input, AnalogCircuit.Ground, 0.0));
            c.Add(new Inductor("L1", input, a, 0.01) { SeriesResistance = 0.0 });
            c.Add(new Capacitor("C1", a, output, 1e-6));
            c.Add(new Resistor("R1", output, AnalogCircuit.Ground, 100));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            double f0 = 1 / (2 * Math.PI * Math.Sqrt(0.01 * 1e-6));   // 1591.5 Hz
            var r = sim.AcResponse("VIN", output, 0, new[] { f0 / 10, f0, f0 * 10 });
            _out.WriteLine(string.Join("; ", r.Select(p => $"{p.Frequency:0.#} Hz: {p.Decibels:0.00} dB, {p.PhaseDegrees:0.0}°")));
            Assert.InRange(r[1].Decibels, -0.01, 0.01);   // L and C cancel: the resistor sees the whole input
            Assert.InRange(Math.Abs(r[1].PhaseDegrees), 0, 0.5);
            Assert.True(r[0].Decibels < -15 && r[2].Decibels < -15);
        }

        /// <summary>An op-amp's gain-bandwidth product sets a closed-loop stage's bandwidth: gain 10 on a 1 MHz part ends near 100 kHz.</summary>
        [Fact]
        public void ANonInvertingAmplifiersBandwidthIsItsGainBandwidthOverItsGain()
        {
            var c = new AnalogCircuit();
            int vp = c.Node("vp"), vn = c.Node("vn"), input = c.Node("in"), fb = c.Node("fb"), output = c.Node("out");
            c.Add(new VoltageSource("VP", vp, AnalogCircuit.Ground, 12.0));
            c.Add(new VoltageSource("VN", vn, AnalogCircuit.Ground, -12.0));
            c.Add(new VoltageSource("VIN", input, AnalogCircuit.Ground, 0.0));
            c.Add(new OpAmp("U1", input, fb, output, vp, vn) { GainBandwidth = 1e6 });
            c.Add(new Resistor("RF", output, fb, 9000));
            c.Add(new Resistor("RG", fb, AnalogCircuit.Ground, 1000));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            var r = sim.AcResponse("VIN", output, 0, AnalogSimulator.Decades(10, 1e7, 40)).ToList();
            double corner = AnalogSimulator.Corner(r);
            _out.WriteLine($"10 Hz: {r[0].Decibels:0.00} dB, köşe {corner / 1000:0.0} kHz");
            Assert.InRange(r[0].Decibels, 19.9, 20.0);   // ×10
            Assert.InRange(corner, 85e3, 115e3);   // GBW / 10
        }

        [Fact]
        public void ACommonEmitterStageGainsWhatItsOperatingPointGives()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), b = c.Node("b"), col = c.Node("c"), e = c.Node("e");
            c.Add(new VoltageSource("VCC", vcc, AnalogCircuit.Ground, 12.0));
            c.Add(new VoltageSource("VIN", b, AnalogCircuit.Ground, 2.0));   // bias and signal on the base
            c.Add(new Resistor("RC", vcc, col, 4700));
            c.Add(new Resistor("RE", e, AnalogCircuit.Ground, 1000));
            var q = c.Add(new Bjt("Q1", col, b, e) { ForwardBeta = 150 });
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            double gm = q.CollectorCurrent / AnalogMath.ThermalVoltage(25.0);
            double expected = -gm * 4700 / (1 + gm * 1000 * 151.0 / 150.0);   // emitter degeneration
            var r = sim.AcResponse("VIN", col, 0, new[] { 1000.0 });
            _out.WriteLine($"Ic {q.CollectorCurrent * 1000:0.000} mA, kazanç {r[0].Gain.Real:0.000} (beklenen {expected:0.000}), faz {r[0].PhaseDegrees:0.0}°");
            Assert.InRange(r[0].Gain.Real, expected * 1.02, expected * 0.98);
            Assert.InRange(Math.Abs(r[0].Gain.Imaginary), 0, 1e-6);
            Assert.InRange(Math.Abs(Math.Abs(r[0].PhaseDegrees) - 180), 0, 0.01);   // inverting
        }
    }
}
