using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[proportional] Blender's proportional editing: shares fade from the selection to the radius.</summary>
    public class ProportionalTests
    {
        [Fact]
        public void PointsNearTheSelectionTakeMoreOfTheMove()
        {
            var pts = new List<Vec3>();
            for (int i = 0; i <= 10; i++) pts.Add(new Vec3(i, 0, 0));   // a row of points 1 mm apart
            var w = MeshProportional.Weights(pts, new[] { 0 }, 5);
            Assert.False(w.ContainsKey(0));   // the selection moves whole, not by a share
            Assert.Equal(4, w.Count);   // 1 … 4 mm away; 5 mm is the edge
            Assert.Equal(MeshProportional.Falloff(0.8, ProportionalFalloff.Smooth), w[1], 9);
            Assert.True(w[1] > w[2] && w[2] > w[3] && w[3] > w[4]);
            Assert.Equal(0.5, MeshProportional.Falloff(0.5, ProportionalFalloff.Smooth), 9);
            Assert.Equal(1, MeshProportional.Falloff(1, ProportionalFalloff.Sphere), 9);
            Assert.Equal(0.25, MeshProportional.Falloff(0.5, ProportionalFalloff.Sharp), 9);
        }

        [Fact]
        public void TheNearestSelectedPointCountsAndAShareBlends()
        {
            var pts = new List<Vec3> { new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(8, 0, 0) };
            var w = MeshProportional.Weights(pts, new[] { 0, 1 }, 4, ProportionalFalloff.Linear);
            Assert.Equal(0.5, w[2], 9);   // 2 mm from the nearer one
            var p = MeshProportional.Blend(new Vec3(8, 0, 0), new Vec3(8, 0, 10), w[2]);
            Assert.Equal(5, p.Z, 9);
            Assert.Empty(MeshProportional.Weights(pts, new int[0], 4));
        }
    }
}
