using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Analog")]
    public class AnalogSimulatorTests
    {
        private static (AnalogCircuit c, int vcc) WithSupply(double volts)
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc");
            c.Add(new VoltageSource("V1", vcc, AnalogCircuit.Ground, volts));
            return (c, vcc);
        }

        [Fact]
        public void VoltageDividerMatchesOhmsLaw()
        {
            var (c, vcc) = WithSupply(12.0);
            int mid = c.Node("mid");
            c.Add(new Resistor("R1", vcc, mid, 1000));
            c.Add(new Resistor("R2", mid, 0, 2000));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);

            Assert.Equal(8.0, sim.NodeVoltage("mid"), 6);
            Assert.Equal(0.004, sim.Signal("V1", "I"), 9);
            Assert.Equal(0.048, sim.Signal("V1", "P"), 9);
        }

        [Fact]
        public void RcChargingFollowsExponential()
        {
            var (c, vcc) = WithSupply(5.0);
            int cap = c.Node("cap");
            c.Add(new Resistor("R1", vcc, cap, 1000));
            c.Add(new Capacitor("C1", cap, 0, 1e-6));
            var sim = new AnalogSimulator(c);
            sim.Initialize();

            const double tau = 1e-3;
            for (int i = 1; i <= 50; i++)
            {
                sim.Step(1e-4);
                double expected = 5.0 * (1.0 - Math.Exp(-sim.Time / tau));
                Assert.InRange(sim.NodeVoltage("cap"), expected - 0.01, expected + 0.01);
            }
        }

        [Fact]
        public void RlCurrentRiseFollowsExponential()
        {
            var (c, vcc) = WithSupply(10.0);
            int mid = c.Node("mid");
            c.Add(new Resistor("R1", vcc, mid, 10));
            var l = c.Add(new Inductor("L1", mid, 0, 0.01) { SeriesResistance = 0.0 });
            var sim = new AnalogSimulator(c);
            sim.Initialize();

            const double tau = 0.01 / 10.0;
            sim.Step(tau);
            Assert.InRange(l.Current, 0.632 - 0.005, 0.632 + 0.005);
            sim.Step(4 * tau);
            Assert.InRange(l.Current, 0.993 - 0.005, 0.993 + 0.005);
        }

        [Fact]
        public void LcTankOscillatesAtResonance()
        {
            var c = new AnalogCircuit();
            int top = c.Node("top");
            c.Add(new Capacitor("C1", top, 0, 1e-6) { InitialVoltage = 1.0 });
            c.Add(new Inductor("L1", top, 0, 1e-3) { SeriesResistance = 0.0 });
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 2e-6 });
            sim.Initialize();

            // f = 1/(2*pi*sqrt(LC)) = 5033 Hz; a full period later the capacitor is back at +1 V.
            double period = 2 * Math.PI * Math.Sqrt(1e-3 * 1e-6);
            sim.Step(period / 2);
            Assert.InRange(sim.NodeVoltage("top"), -1.02, -0.97);
            sim.Step(period / 2);
            Assert.InRange(sim.NodeVoltage("top"), 0.97, 1.02);
        }

        [Fact]
        public void LedWithResistorDrawsExpectedCurrent()
        {
            var (c, vcc) = WithSupply(5.0);
            int a = c.Node("a");
            c.Add(new Resistor("R1", vcc, a, 150));
            var led = c.Add(new Led("D1", a, 0, forwardVoltage: 2.0, ratedCurrent: 0.02));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);

            // (5 - ~2.0) / 150 = 20 mA
            Assert.InRange(led.Current, 0.0185, 0.0210);
            Assert.InRange(led.Voltage, 1.9, 2.15);
            Assert.InRange(led.Brightness, 0.9, 1.05);
        }

        [Fact]
        public void ReversedLedStaysDark()
        {
            var (c, vcc) = WithSupply(5.0);
            int a = c.Node("a");
            c.Add(new Resistor("R1", vcc, a, 220));
            var led = c.Add(new Led("D1", 0, a, 2.0));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.True(Math.Abs(led.Current) < 1e-6);
            Assert.Equal(0.0, led.Brightness);
        }

        [Fact]
        public void LedWithoutResistorBurnsOut()
        {
            var (c, vcc) = WithSupply(5.0);
            var led = c.Add(new Led("D1", vcc, 0, 2.0));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            for (int i = 0; i < 100 && !led.IsFailed; i++) sim.Step(1e-3);
            Assert.True(led.IsFailed);
            Assert.Contains(sim.Events, e => e.DeviceId == "D1" && e.Kind == AnalogEventKind.Failure);
        }

        [Fact]
        public void ZenerClampsVoltage()
        {
            var (c, vcc) = WithSupply(12.0);
            int k = c.Node("k");
            c.Add(new Resistor("R1", vcc, k, 470));
            c.Add(new Diode("Z1", 0, k) { BreakdownVoltage = 5.1, BreakdownCurrent = 0.005 });
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.InRange(sim.NodeVoltage("k"), 5.1, 5.35);
        }

        [Fact]
        public void NpnSaturatesAsLowSideSwitch()
        {
            var (c, vcc) = WithSupply(5.0);
            int collector = c.Node("c"), b = c.Node("b"), drive = c.Node("drive");
            var drv = c.Add(new VoltageSource("VIN", drive, 0, 0.0));
            c.Add(new Resistor("RB", drive, b, 1000));
            c.Add(new Resistor("RC", vcc, collector, 100));
            var q = c.Add(new Bjt("Q1", collector, b, 0));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.InRange(sim.NodeVoltage("c"), 4.99, 5.0);

            drv.Waveform = 5.0;
            sim.Step(1e-4);
            Assert.InRange(sim.NodeVoltage("c"), 0.0, 0.25);
            Assert.InRange(q.CollectorCurrent, 0.047, 0.051);
            Assert.InRange(q.Vbe, 0.6, 0.85);
        }

        [Fact]
        public void NpnActiveRegionHasCurrentGain()
        {
            var (c, vcc) = WithSupply(10.0);
            int collector = c.Node("c"), b = c.Node("b");
            c.Add(new CurrentSource("IB", b, 0, 10e-6));
            c.Add(new Resistor("RC", vcc, collector, 1000));
            var q = c.Add(new Bjt("Q1", collector, b, 0) { ForwardBeta = 150 });
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.InRange(q.CollectorCurrent / q.BaseCurrent, 140, 160);
        }

        [Fact]
        public void NMosfetSwitchesMotorLoad()
        {
            var (c, vcc) = WithSupply(12.0);
            int d = c.Node("d"), g = c.Node("g");
            var gate = c.Add(new VoltageSource("VG", g, 0, 0.0));
            c.Add(new Resistor("RL", vcc, d, 12));
            var m = c.Add(new Mosfet("M1", d, g, 0) { Threshold = 2.0 });
            m.SetOnResistance(0.05, 10.0);
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.True(Math.Abs(m.DrainCurrent) < 1e-6);

            gate.Waveform = 10.0;
            sim.Step(1e-4);
            Assert.InRange(m.DrainCurrent, 0.99, 1.0);
            Assert.InRange(m.Vds, 0.0, 0.06);
        }

        [Fact]
        public void OverloadedResistorBurnsOpen()
        {
            var (c, vcc) = WithSupply(12.0);
            var r = c.Add(new Resistor("R1", vcc, 0, 47) { PowerRating = 0.25 });
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            // 3 W into a quarter-watt part.
            for (int i = 0; i < 400 && !r.IsFailed; i++) sim.Step(0.05);
            Assert.True(r.IsFailed);
            sim.Step(0.01);
            Assert.True(Math.Abs(r.Current) < 1e-9);
        }

        [Fact]
        public void PulseSourceDrivesRcWithEdgesResolved()
        {
            var c = new AnalogCircuit();
            int src = c.Node("src"), cap = c.Node("cap");
            c.Add(new VoltageSource("V1", src, 0, PulseWaveform.FromDuty(0, 5, 1000, 0.5)));
            c.Add(new Resistor("R1", src, cap, 1000));
            c.Add(new Capacitor("C1", cap, 0, 100e-9));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            sim.Step(0.5e-3);
            // tau = 0.1 ms: after 0.5 ms of high input the cap is essentially charged.
            Assert.InRange(sim.NodeVoltage("cap"), 4.95, 5.0);
            sim.Step(0.5e-3);
            Assert.InRange(sim.NodeVoltage("cap"), 0.0, 0.05);
        }

        [Fact]
        public void BatterySagsAndDrains()
        {
            var c = new AnalogCircuit();
            int p = c.Node("p");
            var bat = c.Add(new Battery("B1", p, 0, BatteryChemistry.LithiumIon, 1, capacityAh: 0.001));
            c.Add(new Resistor("RL", p, 0, 4));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            sim.Step(0.01);
            Assert.InRange(bat.Voltage, 3.6, 4.2);
            Assert.True(bat.Voltage < bat.OpenCircuitVoltage);
            sim.Step(1.0);
            Assert.True(bat.StateOfCharge < 1.0);
        }

        [Fact]
        public void SameInputsGiveBitIdenticalResults()
        {
            double Run()
            {
                var (c, vcc) = WithSupply(5.0);
                int a = c.Node("a"), b = c.Node("b");
                c.Add(new Resistor("R1", vcc, a, 330));
                c.Add(new Led("D1", a, b, 2.1));
                c.Add(new Capacitor("C1", b, 0, 10e-6));
                c.Add(new Resistor("R2", b, 0, 1000));
                var sim = new AnalogSimulator(c);
                sim.Initialize();
                for (int i = 0; i < 200; i++) sim.Step(1e-4);
                return sim.NodeVoltage("b");
            }

            Assert.Equal(BitConverter.DoubleToInt64Bits(Run()), BitConverter.DoubleToInt64Bits(Run()));
        }

        [Fact]
        public void VoltageSourceLoopReportsSingularMatrix()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a");
            c.Add(new VoltageSource("V1", a, 0, 5.0));
            c.Add(new VoltageSource("V2", a, 0, 3.0));
            var sim = new AnalogSimulator(c);
            var ex = Assert.Throws<AnalogSimulationException>(() => sim.Initialize(InitialState.OperatingPoint));
            Assert.Contains("V2", ex.Message);
        }
    }
}
