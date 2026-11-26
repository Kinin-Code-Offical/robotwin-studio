using System;
using RobotTwin.CoreSim.Robotics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[deflection] The tool centre point sagging as the drives wind up under the weight they hold.</summary>
    public class ArmDeflectionTests
    {
        private readonly ITestOutputHelper _out;

        public ArmDeflectionTests(ITestOutputHelper output) => _out = output;

        private static double[] Stretched(ArmModel m) => new[] { 0.0, Math.PI / 180 * 80, Math.PI / 180 * -80, 0.0, 0.0, 0.0 };

        [Fact]
        public void AnIndustrialArmSagsTenthsOfAMillimetreUnderItsRatedLoad()
        {
            var m = ArmCatalog.Get("RW6-850");
            var q = Stretched(m);
            var (total, payload) = ArmDynamics.Deflection(m, q, m.RatedPayload);
            var (_, twice) = ArmDynamics.Deflection(m, q, 2 * m.RatedPayload);
            _out.WriteLine($"RW6-850 reaching out, 5 kg: sag {total.Length * 1000:0.000} mm in all, {payload.Length * 1000:0.000} mm from the load ({payload})");
            Assert.True(payload.Z < 0, "the load pulls the tool down");
            Assert.InRange(payload.Length * 1000, 0.1, 2.0);
            Assert.Equal(2 * payload.Length, twice.Length, 9);   // linear in the load
            Assert.True(total.Length > payload.Length);           // the arm's own weight sags it too
        }

        [Fact]
        public void AServoArmSagsMillimetres()
        {
            var m = ArmCatalog.Get("RWD-6");
            var q = Stretched(m);
            var (_, payload) = ArmDynamics.Deflection(m, q, m.RatedPayload);
            _out.WriteLine($"RWD-6 reaching out, {m.RatedPayload * 1000:0} g: {payload.Length * 1000:0.0} mm from the load");
            Assert.InRange(payload.Length * 1000, 1.0, 30.0);
        }
    }
}
