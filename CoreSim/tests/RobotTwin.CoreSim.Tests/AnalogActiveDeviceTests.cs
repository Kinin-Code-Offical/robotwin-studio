using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Analog")]
    public class AnalogActiveDeviceTests
    {
        [Fact]
        public void NonInvertingAmplifierHasGainOnePlusRfOverRg()
        {
            var c = new AnalogCircuit();
            int vp = c.Node("vp"), vn = c.Node("vn"), inp = c.Node("in"), outp = c.Node("out"), fb = c.Node("fb");
            c.Add(new VoltageSource("VP", vp, 0, 12.0));
            c.Add(new VoltageSource("VN", 0, vn, 12.0));
            c.Add(new VoltageSource("VIN", inp, 0, 0.5));
            c.Add(new OpAmp("U1", inp, fb, outp, vp, vn));
            c.Add(new Resistor("RF", outp, fb, 9000));
            c.Add(new Resistor("RG", fb, 0, 1000));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.InRange(sim.NodeVoltage("out"), 4.99, 5.01);
        }

        [Fact]
        public void OpAmpComparatorSaturatesAtRail()
        {
            var c = new AnalogCircuit();
            int vp = c.Node("vp"), a = c.Node("a"), b = c.Node("b"), o = c.Node("o");
            c.Add(new VoltageSource("VCC", vp, 0, 5.0));
            c.Add(new VoltageSource("VA", a, 0, 2.6));
            c.Add(new VoltageSource("VB", b, 0, 2.5));
            c.Add(new OpAmp("U1", a, b, o, vp, 0));
            c.Add(new Resistor("RL", o, 0, 10000));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.InRange(sim.NodeVoltage("o"), 3.3, 3.6);
        }

        [Fact]
        public void Regulator7805RegulatesAndDropsOut()
        {
            var c = new AnalogCircuit();
            int vin = c.Node("vin"), vout = c.Node("vout");
            var src = c.Add(new VoltageSource("V1", vin, 0, 9.0));
            var reg = c.Add(new LinearRegulator("U1", vin, 0, vout, 5.0));
            c.Add(new Resistor("RL", vout, 0, 50));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.InRange(sim.NodeVoltage("vout"), 4.95, 5.01);
            Assert.InRange(reg.Current, 0.098, 0.101);

            src.Waveform = 6.0;
            sim.Step(1e-3);
            Assert.InRange(sim.NodeVoltage("vout"), 3.85, 4.05);
        }

        [Fact]
        public void RegulatorShortCircuitHitsCurrentLimitAndThermalShutdown()
        {
            var c = new AnalogCircuit();
            int vin = c.Node("vin"), vout = c.Node("vout");
            c.Add(new VoltageSource("V1", vin, 0, 12.0));
            var reg = c.Add(new LinearRegulator("U1", vin, 0, vout, 5.0));
            c.Add(new Resistor("RS", vout, 0, 0.1));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            sim.Step(1e-3);
            Assert.InRange(reg.Current, 1.3, 1.5);
            for (int i = 0; i < 200 && !reg.InThermalShutdown; i++) sim.Step(0.05);
            Assert.True(reg.InThermalShutdown);
        }

        [Fact]
        public void DcMotorReachesNoLoadSpeedAndStallsUnderLoad()
        {
            var c = new AnalogCircuit();
            int p = c.Node("p");
            c.Add(new VoltageSource("V1", p, 0, 6.0));
            var m = c.Add(new DcMotor("M1", p, 0));
            m.SetFromDatasheet(ratedVoltage: 6.0, noLoadRpm: 280, noLoadCurrent: 0.1, stallCurrent: 1.5, gearRatio: 30);
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            for (int i = 0; i < 200; i++) sim.Step(0.005);

            Assert.InRange(m.OutputRpm, 250, 300);
            Assert.InRange(m.Current, 0.05, 0.2);

            m.LoadTorque = m.MotorConstant * 1.6 * m.GearRatio;
            for (int i = 0; i < 200; i++) sim.Step(0.005);
            Assert.True(Math.Abs(m.OutputRpm) < 20);
            Assert.InRange(m.Current, 1.3, 1.6);
        }

        [Fact]
        public void RelaySwitchesLoadAfterOperateTime()
        {
            var c = new AnalogCircuit();
            int coil = c.Node("coil"), vcc = c.Node("vcc"), lamp = c.Node("lamp");
            var drive = c.Add(new VoltageSource("VC", coil, 0, 0.0));
            c.Add(new VoltageSource("VCC", vcc, 0, 12.0));
            var k = c.Add(new Relay("K1", coil, 0, vcc, lamp, c.Node("nc")));
            c.Add(new Resistor("RL", lamp, 0, 100));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.False(k.Energized);

            drive.Waveform = 5.0;
            sim.Step(0.05);
            Assert.True(k.Energized);
            Assert.InRange(sim.NodeVoltage("lamp"), 11.9, 12.0);

            drive.Waveform = 0.0;
            sim.Step(0.05);
            Assert.False(k.Energized);
            Assert.InRange(sim.NodeVoltage("lamp"), 0.0, 0.01);
        }

        [Fact]
        public void DigitalPinDrivesLedAndReadsButtonWithPullup()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("5v"), d13 = c.Node("d13"), a = c.Node("a"), d2 = c.Node("d2");
            c.Add(new VoltageSource("USB", vcc, 0, 5.0));
            var outPin = c.Add(new DigitalPin("D13", d13, vcc, 0) { Mode = PinMode.OutputHigh });
            c.Add(new Resistor("R1", d13, a, 220));
            var led = c.Add(new Led("LED1", a, 0, 2.0));
            var inPin = c.Add(new DigitalPin("D2", d2, vcc, 0) { Mode = PinMode.InputPullup });
            var button = c.Add(new Switch("SW1", d2, 0));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);

            Assert.InRange(led.Current, 0.011, 0.014);
            Assert.True(inPin.DigitalValue);
            button.Closed = true;
            sim.Step(1e-4);
            Assert.False(inPin.DigitalValue);
            outPin.Mode = PinMode.OutputLow;
            sim.Step(1e-4);
            Assert.True(led.Current < 1e-6);
        }

        [Fact]
        public void ServoFollowsPulseWidth()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), sig = c.Node("sig");
            c.Add(new VoltageSource("V1", vcc, 0, 5.0));
            // 1.5 ms pulse at 50 Hz -> about 92 degrees with the 544-2400 us Arduino mapping; use 2.0 ms -> 140 deg.
            c.Add(new VoltageSource("PWM", sig, 0, new PulseWaveform(0, 5, 0.02, 2.0e-3, 0, 1e-7, 1e-7)));
            var servo = c.Add(new Servo("S1", vcc, 0, sig));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            for (int i = 0; i < 30; i++) sim.Step(0.01);
            double expected = (2.0e-3 + 1e-7 - 544e-6) / (2400e-6 - 544e-6) * 180.0;
            Assert.InRange(servo.AngleDeg, expected - 1.0, expected + 1.0);
        }

        [Fact]
        public void NandGateTruthTable()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), a = c.Node("a"), b = c.Node("b"), y = c.Node("y");
            c.Add(new VoltageSource("V1", vcc, 0, 5.0));
            var va = c.Add(new VoltageSource("VA", a, 0, 0.0));
            var vb = c.Add(new VoltageSource("VB", b, 0, 0.0));
            var gate = c.Add(new LogicGate("U1", LogicFunction.Nand, y, vcc, 0, a, b));
            c.Add(new Resistor("RL", y, 0, 10000));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            Assert.InRange(sim.NodeVoltage("y"), 4.9, 5.0);

            va.Waveform = 5.0;
            vb.Waveform = 5.0;
            sim.NotifyDiscontinuity();
            sim.Step(1e-6);
            Assert.False(gate.OutputState);
            Assert.InRange(sim.NodeVoltage("y"), 0.0, 0.1);
        }
    }
}
