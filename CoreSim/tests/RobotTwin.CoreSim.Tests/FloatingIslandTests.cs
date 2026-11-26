using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[islands] Parts of a circuit with no path to ground get one reference, at the node named like a ground.</summary>
    public class FloatingIslandTests
    {
        /// <summary>A 6 V battery driving a gearmotor through two cables, nothing tied to ground; a grounded LED circuit beside it.</summary>
        private static (AnalogCircuit C, DcMotor M) Circuit()
        {
            var c = new AnalogCircuit();
            int plus = c.Node("BAT.+"), minus = c.Node("BAT.-"), m1 = c.Node("MOTOR.M1"), m2 = c.Node("MOTOR.M2");
            c.Add(new VoltageSource("V1", plus, minus, 6.0));
            c.Add(new Resistor("C1", plus, m1, 0.08));   // 24 AWG, 1 m
            c.Add(new Resistor("C2", m2, minus, 0.08));
            var m = c.Add(new DcMotor("M1", m1, m2));
            m.SetFromDatasheet(ratedVoltage: 12, noLoadRpm: 100, noLoadCurrent: 0.08, stallCurrent: 1.6, gearRatio: 78, inductance: 2e-3);
            int vcc = c.Node("UNO.5V");
            c.Add(new VoltageSource("V2", vcc, 0, 5.0));
            c.Add(new Resistor("R1", vcc, c.Node("LED.A"), 220));
            c.Add(new Resistor("R2", c.Node("LED.A"), 0, 330));
            return (c, m);
        }

        [Fact]
        public void AFloatingBatteryCircuitIsReferencedAtItsMinusAndKeepsItsSteps()
        {
            var (c, m) = Circuit();
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 2e-4 });
            sim.Initialize(InitialState.OperatingPoint);
            Assert.Equal(0.0, sim.NodeVoltage("BAT.-"), 9);
            Assert.Equal(6.0, sim.NodeVoltage("BAT.+"), 9);
            Assert.Equal(3.0, sim.NodeVoltage("LED.A"), 9);   // the grounded part as it was

            // A wheel that binds and frees every 10 ms, as a physics step hands the motor its load: the steps stay steps.
            double smallest = double.PositiveInfinity;
            for (int k = 0; k < 100; k++)
            {
                m.LoadTorque = k % 2 == 0 ? 0.9 : 0.01;
                var stats = sim.Step(0.01);
                smallest = Math.Min(smallest, stats.SmallestStep);
            }

            Assert.True(smallest > 1e-8, $"smallest step {smallest:G3} s");
            Assert.Equal(0.0, sim.NodeVoltage("BAT.-"), 9);
            Assert.Equal(6.0, sim.NodeVoltage("BAT.+") - sim.NodeVoltage("BAT.-"), 9);
        }

        [Fact]
        public void TheReferenceCarriesNoCurrent()
        {
            // The motor's current is what the battery and cables alone give it: the same as with its minus grounded.
            var (floating, mf) = Circuit();
            var grounded = new AnalogCircuit();
            int plus = grounded.Node("BAT.+"), m1 = grounded.Node("MOTOR.M1"), m2 = grounded.Node("MOTOR.M2");
            grounded.Add(new VoltageSource("V1", plus, 0, 6.0));
            grounded.Add(new Resistor("C1", plus, m1, 0.08));
            grounded.Add(new Resistor("C2", m2, 0, 0.08));
            var mg = grounded.Add(new DcMotor("M1", m1, m2));
            mg.SetFromDatasheet(ratedVoltage: 12, noLoadRpm: 100, noLoadCurrent: 0.08, stallCurrent: 1.6, gearRatio: 78, inductance: 2e-3);
            var a = new AnalogSimulator(floating);
            var b = new AnalogSimulator(grounded);
            a.Initialize();
            b.Initialize();
            mf.LoadTorque = mg.LoadTorque = 0.3;
            for (int k = 0; k < 50; k++)
            {
                a.Step(0.01);
                b.Step(0.01);
            }

            Assert.Equal(mg.Current, mf.Current, 6);
            Assert.Equal(mg.OutputRpm, mf.OutputRpm, 3);
        }
    }
}
