using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[robot-control] Joint torques from the links' dynamics, torque-limited motions, payload capacity, cycle time.</summary>
    public class ArmDynamicsTests
    {
        private readonly ITestOutputHelper _out;

        public ArmDynamicsTests(ITestOutputHelper output) => _out = output;

        /// <summary>A 0.5 m, 2 kg bar on a horizontal axis (about Y) at the origin, lying along +X at q = 0.</summary>
        private static ArmModel Bar(double maxTorque = 10)
        {
            var m = new ArmModel { Id = "bar" };
            m.Joints.Add(new ArmJointSpec { Name = "J1", Axis = Vec3.UnitY, Point = Vec3.Zero, Min = -Math.PI, Max = Math.PI, MaxSpeed = 3, MaxAccel = 20, MaxTorque = maxTorque });
            m.Links.Add(new ArmLinkSpec { Name = "bar", Center = new Vec3(0.25, 0, 0), Size = new Vec3(0.5, 0.04, 0.04), Mass = 2 });
            m.Flange = new Transform3(new Vec3(0.5, 0, 0), Quat.Identity);
            return m;
        }

        [Fact]
        public void TheWeightOfAHorizontalBarIsMgTimesHalfItsLength()
        {
            var m = Bar();
            var tau = ArmDynamics.Torques(m, new[] { 0.0 }, new[] { 0.0 }, new[] { 0.0 });
            // The weight (−Z) at +X turns the bar about +Y (down, toward −Z) with 4.905 N·m: the drive holds it with −4.905.
            Assert.Equal(-2 * 9.81 * 0.25, tau[0], 3);
            // Hanging straight down (q = 90° about +Y turns +X into −Z): no torque.
            var down = ArmDynamics.Torques(m, new[] { Math.PI / 2 }, new[] { 0.0 }, new[] { 0.0 });
            Assert.InRange(Math.Abs(down[0]), 0, 1e-3);
            // A 1 kg payload at the bar's end adds 1·g·0.5.
            var loaded = ArmDynamics.Torques(m, new[] { 0.0 }, new[] { 0.0 }, new[] { 0.0 }, 1.0);
            Assert.Equal(-(2 * 9.81 * 0.25 + 9.81 * 0.5), loaded[0], 3);
        }

        [Fact]
        public void AcceleratingTheBarTakesItsInertiaAboutTheAxis()
        {
            var m = Bar();
            double alpha = 10;
            var still = ArmDynamics.Torques(m, new[] { 0.0 }, new[] { 0.0 }, new[] { 0.0 });
            var accel = ArmDynamics.Torques(m, new[] { 0.0 }, new[] { 0.0 }, new[] { alpha });
            // I about the axis = m(sx² + sz²)/12 + m·(L/2)², for the box turning about Y.
            double inertia = 2 * (0.5 * 0.5 + 0.04 * 0.04) / 12 + 2 * 0.25 * 0.25;
            _out.WriteLine($"τ(α) − τ(0) = {accel[0] - still[0]:F4} N·m, I·α = {inertia * alpha:F4} N·m");
            Assert.Equal(inertia * alpha, accel[0] - still[0], 2);
        }

        [Fact]
        public void TheIndustrialArmHoldsItsRatedPayloadAtHome()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            double capacity = ArmDynamics.PayloadCapacity(m, new[] { (System.Collections.Generic.IReadOnlyList<double>)m.Home });
            _out.WriteLine($"{m.Name}: rated {m.RatedPayload} kg, holds {capacity:F1} kg at home within its peak torques");
            Assert.True(capacity >= m.RatedPayload, $"capacity {capacity:F2} kg below the rated {m.RatedPayload} kg");
        }

        [Fact]
        public void AMoveTooHardForADriveIsSlowedUntilItFits()
        {
            // A weak drive: the weight takes 4.9 of its 6 N·m, so a fast swing needs more than it has.
            var m = Bar(maxTorque: 6);
            var move = new JointMotion(m, new[] { -0.3 }, new[] { 0.3 }, 1.0, ProfileShape.SCurve);
            var before = ArmDynamics.Check(m, move);
            double scale = ArmDynamics.TimeScaleForTorque(m, move);
            var after = ArmDynamics.Check(m, move.Stretched(scale));
            _out.WriteLine($"swing {move.Duration:F3} s: {before.Ratio * 100:F0} % of peak torque; slowed ×{scale:F2} to {move.Duration * scale:F3} s: {after.Ratio * 100:F0} %");
            Assert.True(before.Ratio > 1);
            Assert.True(scale > 1 && !double.IsInfinity(scale));
            Assert.InRange(after.Ratio, 0, 0.95 + 1e-3);
            // A drive too weak even for the weight cannot be helped by slowing.
            Assert.True(double.IsPositiveInfinity(ArmDynamics.TimeScaleForTorque(Bar(maxTorque: 3), move)));
        }

        [Fact]
        public void TheSCurvesJerkFallsWithTheCubeOfTheStretch()
        {
            var p = MotionProfile.Plan(1.0, 2.0, 10.0, ProfileShape.SCurve);
            var q = p.Stretched(2);
            Assert.Equal(p.PeakJerk / 8, q.PeakJerk, 6);
            Assert.True(double.IsPositiveInfinity(MotionProfile.Plan(1.0, 2.0, 10.0, ProfileShape.Trapezoid).PeakJerk));
            // A jerk limit on the axis stretches the move until the jerk fits.
            var m = Bar(maxTorque: 1000);
            m.Joints[0].MaxJerk = 50;
            var move = new JointMotion(m, new[] { 0.0 }, new[] { 1.0 }, 1.0, ProfileShape.SCurve);
            Assert.InRange(1.0 * move.Profile.PeakJerk, 0, 50 * 1.001);
        }
    }
}
