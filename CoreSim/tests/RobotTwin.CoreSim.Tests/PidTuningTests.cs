using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[pid] Step-test identification, the classic tuning rules and the closed-loop simulation.</summary>
    public class PidTuningTests
    {
        private readonly ITestOutputHelper _out;

        public PidTuningTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static List<(double T, double Y)> StepOf(Fopdt m, double du, double duration, double dt, double noise = 0, int seed = 1)
        {
            var rng = new Random(seed);
            var list = new List<(double, double)>();
            for (double t = 0; t <= duration + 1e-12; t += dt) list.Add((t, m.Step(t, du) + noise * (rng.NextDouble() * 2 - 1)));
            return list;
        }

        [Theory]
        [InlineData(2.5, 0.12, 0.01)]
        [InlineData(40, 0.05, 0.0)]
        [InlineData(0.8, 3.0, 1.2)]
        public void SmithsMethodRecoversAFirstOrderModel(double k, double tau, double theta)
        {
            var m = new Fopdt(k, tau, theta);
            var id = PidTuning.Identify(StepOf(m, 6, 8 * tau + 2 * theta, tau / 200), 6)!.Value;
            _out.WriteLine($"true {m}, identified {id}");
            Assert.InRange(id.Gain, k * 0.995, k * 1.005);
            Assert.InRange(id.TimeConstant, tau * 0.98, tau * 1.02);
            Assert.InRange(id.DeadTime, theta - 0.02 * tau, theta + 0.02 * tau);
        }

        [Fact]
        public void NoisyMeasurementsStillGiveTheModel()
        {
            var m = new Fopdt(25, 0.08, 0.004);
            var id = PidTuning.Identify(StepOf(m, 5, 0.8, 0.001, noise: 1.0), 5)!.Value;
            _out.WriteLine($"true {m}, identified {id} (±1 unit noise on 125)");
            Assert.InRange(id.Gain, 24.5, 25.5);
            Assert.InRange(id.TimeConstant, 0.07, 0.09);
        }

        [Fact]
        public void AnIntegratingProcessIsNotForcedIntoTheModel()
        {
            // A position under a speed command keeps rising: no first-order model fits.
            var ramp = Enumerable.Range(0, 200).Select(i => (i * 0.01, i * 0.01 * 3.0)).ToList();
            Assert.Null(PidTuning.Identify(ramp, 1));
        }

        [Fact]
        public void SimcIsTheTextbookFormula()
        {
            var m = new Fopdt(2, 10, 1);
            var simc = PidTuning.Rules(m).First(r => r.Name.StartsWith("SIMC"));
            // τc = θ = 1: Kc = 10 / (2·2) = 2.5, Ti = min(10, 8) = 8.
            Assert.Equal(2.5, simc.Gains.Kc, 9);
            Assert.Equal(8, simc.Gains.Ti, 9);
            var zn = PidTuning.Rules(m).First(r => r.Name.StartsWith("Ziegler"));
            Assert.Equal(6, zn.Gains.Kc, 9);   // 1.2·10 / (2·1)
            Assert.Equal(2, zn.Gains.Ti, 9);
            Assert.Equal(0.5, zn.Gains.Td, 9);
        }

        [Fact]
        public void ASimcLoopIsWellDampedAndZieglerNicholsOvershoots()
        {
            var m = new Fopdt(2, 10, 1);
            var rules = PidTuning.Rules(m);
            var simc = PidTuning.Simulate(m, rules.First(r => r.Name.StartsWith("SIMC")).Gains, 1, 60, 0.05, -100, 100);
            var zn = PidTuning.Simulate(m, rules.First(r => r.Name.StartsWith("Ziegler")).Gains, 1, 60, 0.05, -100, 100);
            _out.WriteLine($"SIMC: rise {simc.RiseTime:F2} s, overshoot {simc.Overshoot:F1} %, settle {simc.SettlingTime:F2} s, IAE {simc.Iae:F2}");
            _out.WriteLine($"Z–N : rise {zn.RiseTime:F2} s, overshoot {zn.Overshoot:F1} %, settle {zn.SettlingTime:F2} s, IAE {zn.Iae:F2}");
            // Skogestad: the SIMC loop with τc = θ overshoots about 10 % at most for a setpoint step.
            Assert.InRange(simc.Overshoot, 0, 15);
            Assert.True(zn.Overshoot > simc.Overshoot + 10);
            Assert.InRange(Math.Abs(simc.SteadyError), 0, 0.5);
            Assert.False(double.IsNaN(simc.SettlingTime));
        }

        [Fact]
        public void ALimitedOutputDoesNotWindUp()
        {
            // A fast-asking PI against a ±1 limit: with the integral held at the limit it settles with little overshoot.
            var m = new Fopdt(1, 1, 0.05);
            var g = new PidGains(20, 0.5, 0);
            var r = PidTuning.Simulate(m, g, 0.9, 10, 0.01, -1, 1);
            _out.WriteLine($"rise {r.RiseTime:F2} s, overshoot {r.Overshoot:F1} %, saturated {r.Saturated * 100:F0} % of the time");
            Assert.True(r.Saturated > 0.05);
            Assert.InRange(r.Overshoot, 0, 5);
            Assert.InRange(Math.Abs(r.SteadyError), 0, 0.5);
        }

        [Fact]
        public void TheCodeCarriesTheGains()
        {
            var g = new PidGains(2.5, 8, 0.2);
            string code = PidTuning.ArduinoCode(g, 0.01, 0, 255, "SIMC");
            Assert.Contains("KP = 2.5f", code);
            Assert.Contains("KI = 0.3125f", code);
            Assert.Contains("KD = 0.5f", code);
            Assert.Contains("U_MAX = 255f", code);
        }

        /// <summary>
        /// The car's left wheel on the bench: a PWM step identifies its speed loop (motor, gearbox, wheel and the car it
        /// pushes), SIMC gives a PI, and the PI run in the physics answers like the model said.
        /// </summary>
        [Fact]
        public void APidTunedOnTheModelBehavesSoInThePhysics()
        {
            var lib = PartLibrary.Default;
            var w = WorldExamples.CarDetailed(lib);
            var geometry = new ApproximateGeometry(w, lib);
            var motor = w.Objects.First(o => o.PartId == "motor-tt" || (o.PresetId ?? string.Empty).Contains("tt"));
            var bench = new MotorBench(w, geometry, motor.Id, 6.0);
            var step = bench.StepTest(200);
            var model = PidTuning.Identify(step, 200)!.Value;
            _out.WriteLine($"step to PWM 200: {step[^1].Y:F1} rpm after {step[^1].T:F2} s; model {model} (rpm per PWM)");
            Assert.InRange(model.Gain, 0.3, 2.0);
            const double period = 0.01;
            // The sample-and-hold adds half a period of dead time.
            var plant = new Fopdt(model.Gain, model.TimeConstant, model.DeadTime + period / 2);
            var gains = PidTuning.Rules(plant, Math.Max(plant.DeadTime, 0.3 * plant.TimeConstant)).First(r => r.Name.StartsWith("SIMC")).Gains;
            double setpoint = 0.6 * step[^1].Y;
            var predicted = PidTuning.Simulate(plant, gains, setpoint, 1.5, period, 0, 255);
            var real = bench.ClosedLoop(gains, setpoint, 1.5, period, 0, 255);
            _out.WriteLine($"PI {gains}: model rise {predicted.RiseTime:F3} s, overshoot {predicted.Overshoot:F1} %; physics rise {real.RiseTime:F3} s, overshoot {real.Overshoot:F1} %, error {real.SteadyError:F2} %");
            Assert.InRange(Math.Abs(real.SteadyError), 0, 2);
            Assert.InRange(real.Overshoot, 0, predicted.Overshoot + 10);
            // Rise times are read at the loop's samples: within two periods of the model's, or 60 %.
            Assert.InRange(real.RiseTime, 0.6 * predicted.RiseTime - 2 * period, 1.6 * predicted.RiseTime + 2 * period);
            // The gear motor's own rotor is part of the loop: tens of milliseconds, not the 3 ms of a torque–speed line alone.
            Assert.InRange(model.TimeConstant, 0.01, 0.2);
        }
    }
}
