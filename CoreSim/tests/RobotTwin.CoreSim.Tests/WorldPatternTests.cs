using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[pattern] Component patterns: a row by a step, a ring round an axis, copies fixed where the original is.</summary>
    public class WorldPatternTests
    {
        private static (WorldDocument W, WorldObject Plate, WorldObject Screw) PlateWithScrew()
        {
            var w = new WorldDocument();
            var plate = new WorldObject { Id = w.NextId("O"), Name = "Plaka", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.3, SizeY = 0.3, SizeZ = 0.005 };
            w.Objects.Add(plate);   // before the next id is drawn
            var screw = new WorldObject { Id = w.NextId("O"), Name = "Vida", Kind = WorldObjectKind.Body, Shape = "cylinder", SizeX = 0.003, SizeY = 0.003, SizeZ = 0.01, Pose = new WorldPose { X = 0.1, Z = 0.005 } };
            w.Objects.Add(screw);
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = screw.Id, ObjectB = plate.Id, AnchorX = 0.1, AnchorZ = 0.005, Fastening = "screw" });
            return (w, plate, screw);
        }

        [Fact]
        public void ARowStepsAndStaysScrewedOn()
        {
            var (w, plate, screw) = PlateWithScrew();
            var made = WorldPatterns.Linear(w, new[] { screw.Id }, 4, new Vec3(0, 0.05, 0));
            Assert.Equal(3, made.Count);
            Assert.Equal(new[] { 0.05, 0.10, 0.15 }, made.Select(m => Math.Round(m.Pose.Y, 9)).ToArray());
            Assert.All(made, m => Assert.Equal(0.1, m.Pose.X, 9));
            // each copy fixed to the plate as the original, with the same fastening
            Assert.All(made, m => Assert.Contains(w.Joints, j => j.ObjectA == m.Id && j.ObjectB == plate.Id && j.Kind == WorldJointKind.Fixed && j.Fastening == "screw"));
            Assert.Equal(4, w.MountedOn(plate.Id).Count);
        }

        [Fact]
        public void ARingSharesAFullTurnOrSpreadsOverAnArc()
        {
            var (w, _, screw) = PlateWithScrew();
            var ring = WorldPatterns.Circular(w, new[] { screw.Id }, 4, 360, Vec3.UnitZ, Vec3.Zero);
            Assert.Equal(3, ring.Count);
            var at = ring.Select(m => (Math.Round(m.Pose.X, 6), Math.Round(m.Pose.Y, 6))).ToList();
            Assert.Equal(new[] { (0.0, 0.1), (-0.1, 0.0), (0.0, -0.1) }, at);
            Assert.Equal(new[] { 90.0, 180.0, 270.0 }, ring.Select(m => Math.Round(m.Pose.Yaw, 6)).ToArray());

            var (w2, _, screw2) = PlateWithScrew();
            var arc = WorldPatterns.Circular(w2, new[] { screw2.Id }, 3, 90, Vec3.UnitZ, Vec3.Zero, keepMount: false);
            Assert.Equal(new[] { 45.0, 90.0 }, arc.Select(m => Math.Round(m.Pose.Yaw, 6)).ToArray());   // the last at the end of the arc
            Assert.DoesNotContain(w2.Joints, j => arc.Any(m => m.Id == j.ObjectA));   // free copies
        }

        [Fact]
        public void APartWithRidersIsCopiedWholeAndTurnedOnce()
        {
            var (w, plate, screw) = PlateWithScrew();
            // the plate with its screw, in a ring of three round a point beside it: the screws turn with their plates
            var made = WorldPatterns.Circular(w, new[] { plate.Id, screw.Id }, 3, 360, Vec3.UnitZ, new Vec3(-0.5, 0, 0));
            Assert.Equal(4, made.Count);   // two plates, two screws
            var plates = made.Where(m => m.Name.StartsWith("Plaka", StringComparison.Ordinal)).ToList();
            foreach (var p in plates)
            {
                var rider = w.MountedOn(p.Id).Single();
                double d = Math.Sqrt(Math.Pow(rider.Pose.X - p.Pose.X, 2) + Math.Pow(rider.Pose.Y - p.Pose.Y, 2));
                Assert.Equal(0.1, d, 6);   // still 100 mm from its plate's middle: turned once, with it
            }
        }
    }
}
