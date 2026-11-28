using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [islands] Bodies that touch or are joined only through what does not move (the floor) are solved as islands of
    /// their own, side by side on the cores: the same motion, bit for bit, as all of them solved together.
    /// </summary>
    public class PhysicsIslandTests
    {
        /// <summary>Six towers of boxes, three driven gear pairs, three pendulums and four rolling wheels, apart on one floor.</summary>
        private static PhysicsScene Yard(out List<SceneBody> watched)
        {
            var scene = new PhysicsScene { GroundMaterial = "wood" };
            watched = new List<SceneBody>();
            for (int t = 0; t < 6; t++)
                for (int k = 0; k < 4; k++)
                {
                    var b = SceneBody.Box($"t{t}b{k}", new Vec3(0.05, 0.05, 0.05), 0.1, new Vec3(0.3 * t + 0.004 * k, 0.003 * k, 0.026 + 0.051 * k), "wood");
                    scene.AddBody(b);
                    watched.Add(b);
                }

            for (int g = 0; g < 3; g++)
            {
                double x = 0.3 * g, y = 0.8;
                var a = SceneBody.Cylinder($"g{g}a", 0.03, 0.01, 0.1, new Vec3(x, y, 1));
                var b = SceneBody.Cylinder($"g{g}b", 0.06, 0.01, 0.4, new Vec3(x + 0.1, y, 1));
                scene.AddBody(a);
                scene.AddBody(b);
                var ja = scene.AddJoint(JointKind.Hinge, a, null, new Vec3(x, y, 1), Vec3.UnitZ);
                var jb = scene.AddJoint(JointKind.Hinge, b, null, new Vec3(x + 0.1, y, 1), Vec3.UnitZ);
                ja.Drive = JointDrive.Velocity;
                ja.TargetSpeed = 5 + g;
                ja.MaxTorque = 2;
                jb.MotorTorque = 0.05;
                scene.AddCoupling(ja, jb, -0.5);
                watched.Add(a);
                watched.Add(b);

                var bob = SceneBody.Box($"p{g}", new Vec3(0.02, 0.02, 0.2), 0.3, new Vec3(x, -0.8, 0.9));
                scene.AddBody(bob);
                scene.AddJoint(JointKind.Hinge, bob, null, new Vec3(x, -0.8, 1.0), Vec3.UnitY);
                bob.AngularVelocity = new Vec3(0, 2 + g, 0);
                watched.Add(bob);
            }

            for (int w = 0; w < 4; w++)
            {
                // wheels rolling on the floor: a tread's contacts name their patch by index
                var wheel = SceneBody.Cylinder($"w{w}", 0.04, 0.02, 0.2, new Vec3(-0.5, 0.25 * w, 0.0401), "rubber");
                wheel.Orientation = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
                wheel.AngularVelocity = new Vec3(0, 10 + w, 0);
                scene.AddBody(wheel);
                watched.Add(wheel);
            }

            return scene;
        }

        private static List<(Vec3 X, Quat Q, Vec3 V)> Run(bool sideBySide, out int islands, out bool used)
        {
            var scene = Yard(out var watched);
            scene.ForceIslandsSideBySide = sideBySide;
            scene.ForceNarrowSideBySide = sideBySide;
            islands = 0;
            used = false;
            for (int i = 0; i < 400; i++)
            {
                scene.Step(2e-3);
                islands = Math.Max(islands, scene.IslandCount);
                used |= scene.IslandsSideBySide && scene.NarrowSideBySide;
            }

            return watched.Select(b => (b.Position, b.Orientation, b.Velocity)).ToList();
        }

        [Fact]
        public void IslandsAndContactTestsSideBySideMoveBitForBitAsAllTogether()
        {
            var apart = Run(true, out int islands, out bool used);
            var together = Run(false, out _, out bool usedTogether);
            Assert.True(islands >= 12, $"{islands} islands");
            Assert.True(used || WorkTeam.Shared.Workers == 0);
            Assert.False(usedTogether);
            for (int i = 0; i < apart.Count; i++)
            {
                Assert.Equal(together[i].X, apart[i].X);
                Assert.Equal(together[i].Q, apart[i].Q);
                Assert.Equal(together[i].V, apart[i].V);
            }
        }

        [Fact]
        public void BoxesThatTouchAreOneIsland()
        {
            var scene = new PhysicsScene { GroundMaterial = "wood" };
            // three rows of five boxes side by side, touching; the rows apart: three islands (enough contacts to look)
            for (int row = 0; row < 3; row++)
                for (int k = 0; k < 5; k++)
                    scene.AddBody(SceneBody.Box($"r{row}b{k}", new Vec3(0.05, 0.05, 0.05), 0.1, new Vec3(0.0502 * k, 0.5 * row, 0.025), "wood"));
            if (WorkTeam.Shared.Workers == 0) return;   // islands are only looked for when there are cores to share them
            for (int i = 0; i < 10; i++) scene.Step(2e-3);
            Assert.Equal(3, scene.IslandCount);
        }
    }
}
