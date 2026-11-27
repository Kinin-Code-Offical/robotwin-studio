using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[spin] Blender's Spin: a profile swept round an axis in steps, closing on itself after a whole turn.</summary>
    public class SpinTests
    {
        /// <summary>A rectangle profile r1…r2 by h turned θ in k straight steps: the volume of those k wedges.</summary>
        private static double Swept(double r1, double r2, double h, double degrees, int k) =>
            h * k / 2.0 * Math.Sin(degrees * Math.PI / 180 / k) * (r2 * r2 - r1 * r1);

        private static (PolyMesh Mesh, int Face) Profile(double r1, double r2, double h)
        {
            var m = new PolyMesh();
            int a = m.AddVertex(new Vec3(r1, 0, 0)), b = m.AddVertex(new Vec3(r2, 0, 0)), c = m.AddVertex(new Vec3(r2, 0, h)), d = m.AddVertex(new Vec3(r1, 0, h));
            return (m, m.AddFace(a, b, c, d));
        }

        [Fact]
        public void AWholeTurnOfAProfileIsAClosedRing()
        {
            var (m, f) = Profile(5, 10, 10);
            MeshSpin.Spin(m, new[] { f }, Array.Empty<(int, int)>(), Vec3.Zero, Vec3.UnitZ, 360, 64);
            Assert.True(m.IsClosed);
            Assert.Equal(Swept(5, 10, 10, 360, 64), Math.Abs(m.Volume()), 6);
            Assert.True(m.Volume() > 0);   // facing out
            Assert.Equal(4 * 64, m.Vertices.Count);   // the last step closes on the first ring
        }

        [Fact]
        public void PartOfATurnEndsInCapsAndTheProfileOnTheAxisStaysOnePoint()
        {
            var (m, f) = Profile(5, 10, 10);
            var r = MeshSpin.Spin(m, new[] { f }, Array.Empty<(int, int)>(), Vec3.Zero, Vec3.UnitZ, 90, 8);
            Assert.True(m.IsClosed);
            Assert.Equal(Swept(5, 10, 10, 90, 8), m.Volume(), 6);
            Assert.Equal(4, r.Vertices.Count);   // the end ring is selected

            var (lathe, g) = Profile(0, 10, 10);   // touches the axis: a solid cylinder
            MeshSpin.Spin(lathe, new[] { g }, Array.Empty<(int, int)>(), Vec3.Zero, Vec3.UnitZ, 360, 32);
            Assert.True(lathe.IsClosed);
            Assert.Equal(Swept(0, 10, 10, 360, 32), lathe.Volume(), 6);
            Assert.Equal(2 + 2 * 32, lathe.Vertices.Count);   // the two points on the axis are not copied
        }

        [Fact]
        public void EdgesAloneSweepAnOpenSurface()
        {
            var m = new PolyMesh();
            int a = m.AddVertex(new Vec3(5, 0, 0)), b = m.AddVertex(new Vec3(5, 0, 10));
            var r = MeshSpin.Spin(m, Array.Empty<int>(), new[] { (a, b) }, Vec3.Zero, Vec3.UnitZ, 360, 16);
            Assert.Equal(16, m.Faces.Count);
            Assert.Equal(2 * 16, m.Vertices.Count);
            Assert.All(m.Faces, face => Assert.Equal(4, face.Length));
            Assert.Equal(2, r.Vertices.Count);
        }

        [Fact]
        public void AFaceOfASolidSweepsOutOfIt()
        {
            var cube = PrimitiveBuilder.Box(10, 10, 10);   // centred: its top at z = 5
            int top = Enumerable.Range(0, cube.Faces.Count).First(i => cube.FaceNormal(i).Z > 0.9);
            // the top turned a quarter round its back edge (y = 5, z = 5, along X): a quarter cylinder grows over the back
            MeshSpin.Spin(cube, new[] { top }, Array.Empty<(int, int)>(), new Vec3(0, 5, 5), Vec3.UnitX, -90, 12);
            Assert.True(cube.IsClosed);
            Assert.Equal(1000 + 10 * 12 / 2.0 * Math.Sin(Math.PI / 2 / 12) * 100, cube.Volume(), 6);
        }
    }
}
