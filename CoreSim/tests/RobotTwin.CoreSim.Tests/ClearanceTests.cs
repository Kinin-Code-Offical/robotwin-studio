using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[clearance] The convex distance (GJK) and SolidWorks' Clearance Verification on a world.</summary>
    public class ClearanceTests
    {
        private static List<Vec3> Cube(Vec3 c, double h, double yawDeg = 0)
        {
            double a = yawDeg * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
            var list = new List<Vec3>();
            foreach (double x in new[] { -h, h })
            foreach (double y in new[] { -h, h })
            foreach (double z in new[] { -h, h })
                list.Add(new Vec3(c.X + x * cs - y * sn, c.Y + x * sn + y * cs, c.Z + z));
            return list;
        }

        [Fact]
        public void FaceToFaceEdgeToEdgeAndCornerToFaceGaps()
        {
            var face = ConvexDistance.Between(Cube(new Vec3(0, 0, 0), 1), Cube(new Vec3(5, 0.3, 0.2), 1));
            Assert.False(face.Overlapping);
            Assert.Equal(3, face.Distance, 9);
            Assert.Equal(1, face.OnA.X, 9);
            Assert.Equal(4, face.OnB.X, 9);

            var edge = ConvexDistance.Between(Cube(new Vec3(0, 0, 0), 1), Cube(new Vec3(4, 4, 0), 1));
            Assert.Equal(Math.Sqrt(8), edge.Distance, 9);   // corner edges (1,1) and (3,3)

            // a cube turned 45° points a corner at the other's face: d − √2 − 1
            var corner = ConvexDistance.Between(Cube(new Vec3(0, 0, 0), 1), Cube(new Vec3(5, 0, 0), 1, 45));
            Assert.Equal(5 - Math.Sqrt(2) - 1, corner.Distance, 9);
            Assert.Equal(5 - Math.Sqrt(2), corner.OnB.X, 9);
        }

        [Fact]
        public void OverlappingAndTouchingShapesHaveNoGap()
        {
            Assert.True(ConvexDistance.Between(Cube(new Vec3(0, 0, 0), 1), Cube(new Vec3(1.5, 0.2, 0), 1, 30)).Overlapping);
            var touching = ConvexDistance.Between(Cube(new Vec3(0, 0, 0), 1), Cube(new Vec3(2, 0, 0), 1));
            Assert.Equal(0, touching.Distance, 9);
        }

        [Fact]
        public void RandomShapesAgreeWithASampledDistance()
        {
            var rng = new Random(7);
            for (int n = 0; n < 40; n++)
            {
                var a = Enumerable.Range(0, 12).Select(_ => new Vec3(rng.NextDouble(), rng.NextDouble(), rng.NextDouble())).ToList();
                var off = new Vec3(1.2 + rng.NextDouble(), rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                var b = Enumerable.Range(0, 12).Select(_ => new Vec3(rng.NextDouble(), rng.NextDouble(), rng.NextDouble()) + off).ToList();
                var gap = ConvexDistance.Between(a, b);
                // the two nearest points are on the shapes' hulls, so no pair of vertices is nearer than the gap
                double vertexMin = a.SelectMany(p => b.Select(q => (p - q).Length)).Min();
                Assert.True(gap.Distance <= vertexMin + 1e-9, $"{gap.Distance} > {vertexMin}");
                Assert.Equal(gap.Distance, (gap.OnA - gap.OnB).Length, 6);
                // and every vertex of B lies beyond the separating plane through OnA
                var nrm = (gap.OnB - gap.OnA).Normalized();
                Assert.All(b, q => Assert.True(nrm.Dot(q - gap.OnA) >= gap.Distance - 1e-7));
                Assert.All(a, p => Assert.True(nrm.Dot(p - gap.OnA) <= 1e-7));
            }
        }

        private static WorldObject Box(WorldDocument w, string name, double x, double size = 0.02) =>
            new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Body, Shape = "box", SizeX = size, SizeY = size, SizeZ = size, MassKg = 0.1, Pose = new WorldPose { X = x, Z = 0.01 } };

        [Fact]
        public void PairsNearerThanTheMinimumAreListedNearestFirst()
        {
            var w = new WorldDocument();
            var a = Box(w, "A", 0);
            var b = Box(w, "B", 0.023);   // 3 mm gap
            var c = Box(w, "C", -0.0215);  // 1.5 mm gap on the other side
            var d = Box(w, "D", 0.2);
            w.Objects.AddRange(new[] { a, b, c, d });
            var geo = new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default);
            var found = Clearance.Find(w, geo, new ClearanceOptions { MinimumMm = 5 });
            Assert.Equal(2, found.Count);
            Assert.Equal(1.5, found[0].GapMm, 6);
            Assert.Equal(3, found[1].GapMm, 6);
            Assert.Equal(0.010, found[1].PointA.X, 9);
            Assert.Equal(0.013, found[1].PointB.X, 9);
            Assert.Single(Clearance.Find(w, geo, new ClearanceOptions { MinimumMm = 2 }));

            b.Pose.X = 0.015;   // now B overlaps A
            var overlap = Clearance.Find(w, geo, new ClearanceOptions { MinimumMm = 2 });
            Assert.Contains(overlap, p => p.Overlapping && p.GapMm == 0);
            Assert.DoesNotContain(Clearance.Find(w, geo, new ClearanceOptions { MinimumMm = 2, IncludeOverlapping = false }), p => p.Overlapping);
        }
    }
}
