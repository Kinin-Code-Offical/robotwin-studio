using System;
using System.Linq;
using RobotTwin.CoreSim.Instruments;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[scope] The math channel: sums, the power of a voltage and a current, charge and energy, slew rates.</summary>
    public class ScopeMathTests
    {
        private const double Dt = 1e-4;

        private static ScopeTrace Trace(string unit, Func<double, double> f, int n = 1001) =>
            new ScopeTrace("x", unit, 1, 0, Enumerable.Range(0, n).Select(i => f(i * Dt)).ToArray());

        [Fact]
        public void VoltsTimesAmpsArePowerAndItsMeanIsTheAveragePower()
        {
            // 5 V across a load drawing a 50 % PWM of 2 A: the mean of V × I is 5 W.
            var v = Trace("V", _ => 5.0);
            var i = Trace("A", t => (int)(t / 1e-3) % 2 == 0 ? 2.0 : 0.0);
            var p = ScopeMath.Compute(ScopeMathFunction.Multiply, v, i, Dt, 0, 1)!;
            Assert.Equal("W", p.Unit);
            Assert.Equal("CH1×CH2", p.Name);
            Assert.InRange(p.Values.Average(), 4.9, 5.1);
        }

        [Fact]
        public void TheIntegralOfACurrentIsItsChargeAndOfAPowerItsEnergy()
        {
            var i = Trace("A", _ => 0.5);   // 0.5 A for 0.1 s: 50 mC
            var q = ScopeMath.Compute(ScopeMathFunction.Integrate, i, null, Dt, 1, 0)!;
            Assert.Equal("C", q.Unit);
            Assert.Equal("∫CH2", q.Name);
            Assert.Equal(0.05, q.Values[^1], 9);
            Assert.Equal("J", ScopeMath.Integral("W"));
            Assert.Equal("V·s", ScopeMath.Integral("V"));
        }

        [Fact]
        public void TheDerivativeOfARampIsItsSlewRate()
        {
            var v = Trace("V", t => 2000 * t);   // 2 V/ms
            var d = ScopeMath.Compute(ScopeMathFunction.Differentiate, v, null, Dt, 0, 1)!;
            Assert.Equal("V/s", d.Unit);
            Assert.All(d.Values, x => Assert.Equal(2000, x, 6));
        }

        [Fact]
        public void VoltsAndAmpsDoNotAddButAVoltageDifferenceDoes()
        {
            var v1 = Trace("V", _ => 5.0);
            var v2 = Trace("V", _ => 3.3);
            var i = Trace("A", _ => 1.0);
            Assert.Null(ScopeMath.Compute(ScopeMathFunction.Add, v1, i, Dt, 0, 1));
            var diff = ScopeMath.Compute(ScopeMathFunction.Subtract, v1, v2, Dt, 0, 2)!;
            Assert.Equal("CH1−CH3", diff.Name);
            Assert.Equal(1.7, diff.Values[0], 9);
            Assert.Equal("V²", ScopeMath.Product("V", "V"));
        }

        [Fact]
        public void TheAutoScaleIsA125StepThatFitsTheTrace()
        {
            Assert.Equal(2.0, ScopeMath.NiceScale(new[] { 0.0, 5.0, -4.0 }), 9);     // 5/3 = 1.67 → 2
            Assert.Equal(0.05, ScopeMath.NiceScale(new[] { 0.12 }), 9);              // 0.04 → 0.05
            Assert.Equal(1000.0, ScopeMath.NiceScale(new[] { 2500.0 }), 9);          // 833 → 1000
            Assert.Equal(1.0, ScopeMath.NiceScale(new double[0]), 9);
        }
    }
}
