using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[esc] A brushless ESC reads servo pulses, arms on low throttle, ramps, and stops on signal loss.</summary>
    public class EscBrushlessTests
    {
        private readonly ITestOutputHelper _out;

        public EscBrushlessTests(ITestOutputHelper output) => _out = output;

        private static Waveform Pulse(double widthUs) => new PulseWaveform(0, 3.3, 0.02, widthUs * 1e-6, 0.001);

        [Fact]
        public void EscArmsOnLowThrottleThenSpinsTheMotorAndStopsWhenTheSignalIsLost()
        {
            var c = new AnalogCircuit();
            int bp = c.Node("BAT+"), sig = c.Node("SIG"), mp = c.Node("M+");
            c.Add(new Battery("BAT", bp, 0, BatteryChemistry.LithiumIon, 3, 2.2));
            var esc = c.Add(new AveragedPwmDriver("ESC", bp, mp, 0) { Resistance = 0.012, CurrentRating = 20, PulseNode = sig });
            var motor = c.Add(new DcMotor("M", mp, 0));
            motor.SetFromDatasheet(11.1, 25530, 0.7, 123, 1, 20e-6);
            var rc = c.Add(new VoltageSource("RC", sig, 0, Pulse(1500)));   // receiver powers up at half stick
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 2e-5 });
            sim.Initialize(InitialState.OperatingPoint);
            void Run(double s) { for (double t = 0; t < s; t += 2e-5) sim.Step(2e-5); }

            Run(0.5);
            _out.WriteLine($"half stick at power-up: armed {esc.Armed}, {motor.OutputRpm:F0} rpm");
            Assert.False(esc.Armed, "an ESC does not start the motor at half throttle on power-up");
            Assert.True(Math.Abs(motor.OutputRpm) < 10);

            rc.Waveform = Pulse(1000);
            Run(0.5);
            Assert.True(esc.Armed, "low-throttle pulses arm it");
            rc.Waveform = Pulse(1500);
            Run(0.6);
            double rpm = motor.OutputRpm, amps = esc.OutputCurrent;
            _out.WriteLine($"1500 µs: duty {esc.Duty:F2}, {rpm:F0} rpm, motor {amps:F2} A");
            Assert.InRange(esc.Duty, 0.45, 0.55);
            Assert.InRange(rpm, 0.5 * 12.6 * 2300 * 0.9, 0.5 * 12.6 * 2300 * 1.02);   // half of Kv × a charged pack (12.6 V), no prop

            rc.Waveform = new DcWaveform(0);   // receiver unplugged
            Run(0.6);
            _out.WriteLine($"signal lost: duty {esc.Duty:F2}, {motor.OutputRpm:F0} rpm");
            Assert.Equal(0, esc.Duty, 3);
            Assert.True(motor.OutputRpm < rpm * 0.5, "failsafe: the motor spins down");
        }
    }
}
