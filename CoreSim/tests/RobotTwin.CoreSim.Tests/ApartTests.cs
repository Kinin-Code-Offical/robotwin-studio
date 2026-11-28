using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [perf] The contact tests' early way out: two solids are called apart only when they are farther than the margin,
    /// checked against the exact distance between their hulls, and it is found for most pairs that are clearly apart.
    /// </summary>
    public class ApartTests
    {
        /// <summary>The shape's solid as world points: corners of a box or a hull, a cylinder's rims circumscribed (it contains the cylinder).</summary>
        private static List<Vec3> World(CollisionShape s, Vec3 x, Quat q, double radius, double halfHeight)
        {
            IEnumerable<Vec3> local;
            if (s.Kind == ShapeKind.Cylinder)
            {
                const int sides = 96;
                double r = radius / Math.Cos(Math.PI / sides);
                local = Enumerable.Range(0, sides).SelectMany(i =>
                {
                    double a = 2 * Math.PI * i / sides;
                    return new[] { new Vec3(r * Math.Cos(a), r * Math.Sin(a), halfHeight), new Vec3(r * Math.Cos(a), r * Math.Sin(a), -halfHeight) };
                });
            }
            else local = s.Vertices;

            return local.Select(p => x + q.Rotate(p)).ToList();
        }

        [Fact]
        public void TwoSolidsAreCalledApartOnlyWhenTheyAreFartherThanTheMargin()
        {
            var rng = new Random(11);
            int apart = 0, clearlyApart = 0, found = 0;
            for (int trial = 0; trial < 4000; trial++)
            {
                (CollisionShape S, double R, double H) Make()
                {
                    switch (rng.Next(3))
                    {
                        case 0:
                            return (CollisionShape.Box(new Vec3(0.02 + rng.NextDouble() * 0.2, 0.02 + rng.NextDouble() * 0.2, 0.02 + rng.NextDouble() * 0.2)), 0, 0);
                        case 1:
                        {
                            double r = 0.01 + rng.NextDouble() * 0.1, height = 0.01 + rng.NextDouble() * 0.2;
                            return (CollisionShape.Cylinder(r, height), r, height / 2);
                        }

                        default:
                            var pts = Enumerable.Range(0, 40).Select(_ => new Vec3(rng.NextDouble() - 0.5, (rng.NextDouble() - 0.5) * 0.3, (rng.NextDouble() - 0.5) * 0.1) * 0.4).ToList();
                            return (CollisionShape.ConvexHull(pts, 64), 0, 0);
                    }
                }

                Quat Turn() => Quat.FromAxisAngle(new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5).Normalized(), rng.NextDouble() * Math.PI * 2);
                var (sa, ra, ha) = Make();
                var (sb, rb, hb) = Make();
                var qa = Turn();
                var qb = Turn();
                var xa = Vec3.Zero;
                var xb = new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.6;
                double margin = rng.NextDouble() * 0.01;
                bool said = PhysicsScene.Apart(sa, xa, qa, sb, xb, qb, margin);
                var gap = ConvexDistance.Between(World(sa, xa, qa, ra, ha), World(sb, xb, qb, rb, hb));
                // a circumscribed rim lies up to 0.05 % of the radius outside the cylinder: within that, the hulls are nearer
                double slack = 0.0006 * (ra + rb) + 1e-9;
                if (said)
                {
                    apart++;
                    Assert.True(gap.Distance > margin - slack, $"trial {trial}: called apart at a gap of {gap.Distance * 1000:F3} mm, margin {margin * 1000:F3} mm");
                }

                if (gap.Distance > margin + 0.01)
                {
                    clearlyApart++;
                    if (said) found++;
                }
            }

            Assert.True(apart > 500, $"{apart} pairs called apart");
            Assert.True(found > 0.95 * clearlyApart, $"{found} of {clearlyApart} clearly separate pairs found apart");
        }

        /// <summary>
        /// A hull block dropped onto a short post: proved apart while it is high, it is tested again as it falls (the
        /// remembered gap shrinks by how far it moved), lands on the post's top and stays there.
        /// </summary>
        [Fact]
        public void ABlockFallingOntoAPostIsCaughtThoughItWasApartAtFirst()
        {
            var scene = new PhysicsScene { GroundMaterial = "wood" };
            var log = SceneBody.Cylinder("log", 0.05, 0.1, 2.0, new Vec3(0, 0, 0.05), "wood");   // standing: a flat top at 100 mm
            log.IsStatic = true;
            scene.AddBody(log);
            var corners = new List<Vec3>();
            foreach (double x in new[] { -0.03, 0.03 })
                foreach (double y in new[] { -0.03, 0.03 })
                    foreach (double z in new[] { -0.02, 0.02 })
                        corners.Add(new Vec3(x, y, z));
            var block = new SceneBody("block") { Shape = CollisionShape.ConvexHull(corners), Material = "wood", Position = new Vec3(0, 0, 0.6) };
            block.SetMassProperties(MassProperties.Box(0.2, new Vec3(0.06, 0.06, 0.04)));
            scene.AddBody(block);
            for (int k = 0; k < 150; k++) scene.Step(0.01);
            Assert.True(block.Position.Z > 0.1 + 0.015, $"the block sank to z = {block.Position.Z * 1000:F1} mm (post top at 100 mm)");
            Assert.True(block.Position.Z < 0.14, $"the block floats at z = {block.Position.Z * 1000:F1} mm");
        }

        [Fact]
        public void TouchingOrOverlappingSolidsAreNeverCalledApart()
        {
            var box = CollisionShape.Box(new Vec3(0.1, 0.1, 0.1));
            var wheel = CollisionShape.Cylinder(0.05, 0.02);
            Assert.False(PhysicsScene.Apart(box, Vec3.Zero, Quat.Identity, wheel, new Vec3(0, 0, 0.05), Quat.Identity, 0.001));   // overlapping
            Assert.False(PhysicsScene.Apart(box, Vec3.Zero, Quat.Identity, wheel, new Vec3(0, 0, 0.0605), Quat.Identity, 0.001));   // 0.5 mm apart, inside the margin
            Assert.True(PhysicsScene.Apart(box, Vec3.Zero, Quat.Identity, wheel, new Vec3(0, 0, 0.065), Quat.Identity, 0.001));   // 5 mm apart
        }
    }
}
