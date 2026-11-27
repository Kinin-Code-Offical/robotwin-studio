using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [validation] A DC motor switched onto a supply: the inrush current. The motor is two coupled first-order systems,
    /// the winding L di/dt = V − R i − k ω and the rotor J dω/dt = k i − b ω, so its start is known exactly; the reference
    /// here integrates those two equations with a step a thousand times finer than the circuit's. The current jumps
    /// toward V / R in a few electrical time constants (L / R), peaks while the rotor is still slow, and falls to the
    /// no-load current as the back-EMF builds over the mechanical time constant (J R / k²).
    /// </summary>
    [Trait("Category", "Analog")]
    public class MotorStartValidationTests
    {
        private readonly ITestOutputHelper _out;

        public MotorStartValidationTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AMotorsInrushCurrentFollowsTheTwoEquationModel()
        {
            const double V = 6.0;
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc");
            c.Add(new VoltageSource("V1", vcc, AnalogCircuit.Ground, V));
            var m = c.Add(new DcMotor("M1", vcc, AnalogCircuit.Ground) { Resistance = 2.0, Inductance = 1e-3, MotorConstant = 0.01, Inertia = 1e-6, ViscousFriction = 1e-6 });
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 2e-4, RelTol = 1e-3 });
            sim.Initialize();

            // the reference: the same two equations at the motor's own winding resistance and constant, RK4 at 0.2 µs
            double R = m.HotResistance, k = m.HotConstant, L = m.Inductance, J = m.Inertia, b = m.ViscousFriction;
            double i = 0, w = 0, t = 0;
            const double h = 2e-7;
            (double Di, double Dw) F(double ci, double cw) => ((V - R * ci - k * cw) / L, (k * ci - b * cw) / J);
            void Advance(double until)
            {
                while (t < until - 1e-12)
                {
                    double s = Math.Min(h, until - t);
                    var k1 = F(i, w);
                    var k2 = F(i + s / 2 * k1.Di, w + s / 2 * k1.Dw);
                    var k3 = F(i + s / 2 * k2.Di, w + s / 2 * k2.Dw);
                    var k4 = F(i + s * k3.Di, w + s * k3.Dw);
                    i += s / 6 * (k1.Di + 2 * k2.Di + 2 * k3.Di + k4.Di);
                    w += s / 6 * (k1.Dw + 2 * k2.Dw + 2 * k3.Dw + k4.Dw);
                    t += s;
                }
            }

            double peakSim = 0, peakRef = 0, peakSimAt = 0, peakRefAt = 0, worst = 0;
            while (sim.Time < 0.1 - 1e-12)
            {
                sim.Step(1e-4);
                Advance(sim.Time);
                worst = Math.Max(worst, Math.Abs(m.Current - i));
                if (m.Current > peakSim) (peakSim, peakSimAt) = (m.Current, sim.Time);
                if (i > peakRef) (peakRef, peakRefAt) = (i, t);
            }

            double noLoad = V * b / (R * b + k * k), speed = k * V / (R * b + k * k);
            _out.WriteLine($"inrush peak {peakSim:0.0000} A at {peakSimAt * 1e3:0.00} ms (reference {peakRef:0.0000} A at {peakRefAt * 1e3:0.00} ms, {(peakSim / peakRef - 1) * 100:0.00} %); V/R {V / R:0.000} A");
            _out.WriteLine($"largest difference along the start {worst * 1e3:0.00} mA; after 0.1 s {m.Current * 1e3:0.00} mA (no-load {noLoad * 1e3:0.00} mA), {m.AngularVelocity:0.0} rad/s (steady {speed:0.0} rad/s)");
            Assert.InRange(peakSim / peakRef, 0.98, 1.02);
            Assert.InRange(peakSimAt - peakRefAt, -2e-4, 2e-4);
            Assert.True(worst < 0.02 * peakRef, $"{worst * 1e3:0.0} mA");
            Assert.InRange(m.AngularVelocity / speed, 0.97, 1.0);   // five mechanical time constants: within 1 % of steady
            Assert.InRange(peakRef, 0.85 * V / R, V / R);
        }
    }
}
