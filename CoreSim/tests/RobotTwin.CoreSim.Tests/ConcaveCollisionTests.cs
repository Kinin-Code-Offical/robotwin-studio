using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Modeling;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Hollow parts collide as their real shape (union of convex pieces), not as a filled hull.</summary>
    public sealed class ConcaveCollisionTests
    {
        /// <summary>60 × 40 × 30 mm tray with 2.5 mm walls, opening up, bottom at z = 0 (metres).</summary>
        private static MeshModel Tray()
        {
            var m = PrimitiveBuilder.RoundedBox(60, 40, 30, 0, 1, false).Translate(new Vec3(0, 0, 15));
            int top = Enumerable.Range(0, m.Faces.Count).First(f => m.FaceNormal(f).Z > 0.9);
            MeshEdit.DeleteFaces(m, new[] { top });
            MeshCut.Solidify(m, 2.5);
            return m.ToMeshModel();
        }

        /// <summary>Upright tube Ø40 / Ø30 mm, 50 mm tall, bottom at z = 0 (metres).</summary>
        private static MeshModel Tube() => PrimitiveBuilder.Tube(20, 15, 50, 32).Translate(new Vec3(0, 0, 25)).ToMeshModel();

        private static double PieceHulls(MeshModel mesh, out int count)
        {
            var pieces = ConvexDecomposition.Decompose(mesh);
            count = pieces.Count;
            return pieces.Sum(p => ConvexDecomposition.HullVolume(p));
        }

        [Fact]
        public void ATrayIsSplitIntoItsBottomAndWalls()
        {
            var tray = Tray();
            double solid = Math.Abs(MassProperties.FromMesh(tray, 1000).Mass) / 1000;
            double hulls = PieceHulls(tray, out int count);
            Assert.InRange(count, 5, 12);
            Assert.True(hulls < solid * 1.05, $"pieces' hulls {hulls * 1e9:0} mm³ for a {solid * 1e9:0} mm³ tray");
            // the single hull would be the full 60 × 40 × 30 block
            Assert.True(ConvexDecomposition.HullVolume(tray.Vertices) > solid * 3);
        }

        [Fact]
        public void ATubeIsSplitIntoWedgesThatLeaveTheBoreOpen()
        {
            var tube = Tube();
            double solid = Math.PI * (0.02 * 0.02 - 0.015 * 0.015) * 0.05;
            double hulls = PieceHulls(tube, out int count);
            Assert.InRange(count, 4, ConvexDecomposition.DefaultMaxPieces);
            Assert.True(hulls < solid * 1.15, $"{count} pieces, hulls {hulls * 1e9:0} mm³ for {solid * 1e9:0} mm³");
        }

        [Fact]
        public void ConvexAndOpenMeshesStayOnePiece()
        {
            var box = PrimitiveBuilder.RoundedBox(20, 20, 20, 0, 1, false).ToMeshModel();
            Assert.Single(ConvexDecomposition.Decompose(box));
            var open = PrimitiveBuilder.RoundedBox(20, 20, 20, 0, 1, false);
            MeshEdit.DeleteFaces(open, new[] { 0 });
            Assert.Single(ConvexDecomposition.Decompose(open.ToMeshModel()));
        }

        [Fact]
        public void ACubeDroppedIntoATrayRestsOnItsInnerFloor()
        {
            var scene = new PhysicsScene();
            var tray = SceneBody.FromMesh("tray", Tray(), null, 0.05, "pla", default, 32, concave: true);
            Assert.NotNull(tray.Pieces);
            scene.AddBody(tray);
            var cube = SceneBody.Box("cube", new Vec3(0.01, 0.01, 0.01), 0.005, new Vec3(0.004, -0.003, 0.06), "pla");
            scene.AddBody(cube);
            for (int i = 0; i < 2500; i++) scene.Step(1e-3);
            // inner floor 2.5 mm, half cube 5 mm; the filled hull would hold it on the rim at 30 + 5 mm
            Assert.InRange(cube.Position.Z, 0.0065, 0.0085);
            Assert.InRange(tray.Position.Z - tray.ModelOffset.Z, -0.0005, 0.0005);
        }

        [Fact]
        public void ABallDroppedIntoATubeFallsDownTheBore()
        {
            var scene = new PhysicsScene();
            var tube = SceneBody.FromMesh("tube", Tube(), null, 0.05, "pla", default, 32, concave: true);
            tube.IsStatic = true;
            scene.AddBody(tube);
            var ball = SceneBody.Sphere("ball", 0.006, 0.002, new Vec3(0.001, 0.0005, 0.08), "steel");
            scene.AddBody(ball);
            for (int i = 0; i < 2500; i++) scene.Step(1e-3);
            Assert.InRange(ball.Position.Z, 0.0055, 0.0075);                 // on the ground inside the bore
            Assert.True(Math.Sqrt(ball.Position.X * ball.Position.X + ball.Position.Y * ball.Position.Y) < 0.015 - 0.006 + 0.001);
        }

        [Fact]
        public void InTheWorldATrayModelHoldsABoxAndIsNotAnOverlap()
        {
            var world = new WorldDocument();
            var tray = world.AddModel(Tray(), "Tepsi", new WorldPose());
            var cube = world.AddBody("box", 0.01, 0.01, 0.01, new WorldPose { Z = 0.06 });
            var geometry = new ApproximateGeometry(world, PartLibrary.Default);
            var p = WorldPhysics.Build(world, geometry.GeometryOf, null);
            for (int i = 0; i < 2500; i++) p.Advance(1e-3, null);
            var cubePose = p.PoseOf(cube.Id)!.Value;
            var trayPose = p.PoseOf(tray.Id)!.Value;
            Assert.InRange(cubePose.Position.Z - trayPose.Position.Z, -0.001, 0.012);   // down inside, not on the 30 mm rim
            Assert.Empty(p.FindInterpenetrations());                                      // resting inside is not "inside each other"
        }

        [Fact]
        public void ARayIntoATrayHitsItsFloor()
        {
            var scene = new PhysicsScene { GroundEnabled = false };
            var tray = SceneBody.FromMesh("tray", Tray(), null, 0.05, "pla", default, 32, concave: true);
            tray.IsStatic = true;
            scene.AddBody(tray);
            Assert.True(scene.Raycast(new Vec3(0, 0, 0.1), new Vec3(0, 0, -1), 1, out var hit));
            Assert.InRange(hit.Point.Z, 0.0024, 0.0026);
        }
    }
}
