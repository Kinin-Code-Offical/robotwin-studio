using System;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[mate-limit] Limit distance and limit angle: free inside the range, held at its nearer end outside it.</summary>
    public class MateLimitTests
    {
        private static MateFeature Face(WorldObject o, double x, double y, double z, double nx, double ny, double nz) =>
            new MateFeature { Object = o.Id, Kind = MateFeatureKind.Plane, X = x, Y = y, Z = z, DX = nx, DY = ny, DZ = nz };

        /// <summary>A fixed table and a cube parallel to it at a height (its bottom face this far above the table's top).</summary>
        private static (WorldDocument W, WorldObject Table, WorldObject Cube) Stack(double gap)
        {
            var w = new WorldDocument();
            var table = w.AddBody("box", 0.4, 0.3, 0.02, new WorldPose { Z = 0.01 });
            table.IsStatic = true;
            var cube = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { Z = 0.02 + gap + 0.025 });
            WorldMates.Add(w, MateKind.Parallel, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, 1));
            return (w, table, cube);
        }

        [Fact]
        public void InsideItsRangeALimitHoldsNothing()
        {
            var (w, table, cube) = Stack(0.03);
            var before = cube.Pose.Z;
            var (_, r) = WorldMates.Add(w, MateKind.LimitDistance, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1), 0.01, 0.05);
            Assert.Equal(before, cube.Pose.Z, 9);   // 30 mm is within 10 … 50: not moved
            Assert.Equal(MateState.UnderConstrained, r.State);
            Assert.Equal(4, r.Freedom);   // parallel leaves 4; the limit takes none
            Assert.Empty(r.Culprits);
        }

        [Fact]
        public void OutsideItIsBroughtToTheNearerEndAndHeldThere()
        {
            var (w, table, cube) = Stack(0.08);
            var (_, r) = WorldMates.Add(w, MateKind.LimitDistance, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1), 0.01, 0.05);
            Assert.Equal(0.02 + 0.05 + 0.025, cube.Pose.Z, 6);   // down to the 50 mm end
            Assert.Equal(3, r.Freedom);   // at the end it holds like a distance
            Assert.Equal(MateState.UnderConstrained, r.State);
        }

        [Fact]
        public void AnAngleLimitKeepsAFlapWithinItsSwing()
        {
            var w = new WorldDocument();
            var bed = w.AddBody("box", 0.2, 0.2, 0.01, new WorldPose());
            bed.IsStatic = true;
            var flap = w.AddBody("box", 0.1, 0.01, 0.1, new WorldPose { Z = 0.2, Roll = 70 });   // its face 70° from the bed's
            var (mate, _) = WorldMates.Add(w, MateKind.LimitAngle, Face(bed, 0, 0, 0.005, 0, 0, 1), Face(flap, 0, 0, 0.05, 0, 0, 1), 0, 45);
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, flap.Pose.Orientation.Rotate(new RobotTwin.CoreSim.Mechanics.Vec3(0, 0, 1)).Z))) * 180 / Math.PI;
            Assert.Equal(45, angle, 3);   // swung back to its 45° end
            var copy = mate.Clone();
            Assert.Equal(45, copy.Value2);
        }
    }
}
