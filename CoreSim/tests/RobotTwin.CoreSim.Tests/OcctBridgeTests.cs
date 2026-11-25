using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[occt] STEP in and out through the Open CASCADE bridge (third_party/occt/win-x64).</summary>
    public class OcctBridgeTests
    {
        private readonly ITestOutputHelper _out;

        public OcctBridgeTests(ITestOutputHelper output)
        {
            _out = output;
            // The bridge as the repository keeps it (the app has it in its occt folder).
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "third_party", "occt", "win-x64");
                if (!Directory.Exists(candidate)) continue;
                OcctBridge.AddSearchFolder(candidate);
                break;
            }
        }

        private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        [Fact]
        public void ReadsAStepPartAsAMeshInMetres()
        {
            Assert.True(OcctBridge.Available, OcctBridge.LoadError);
            _out.WriteLine("Open CASCADE " + OcctBridge.Version);
            var mesh = PartModelFiles.LoadMesh(Fixture("screw.step"));
            var size = mesh.Bounds.Size;
            _out.WriteLine($"{mesh.TriangleCount} üçgen, {mesh.Vertices.Count} köşe, boyut {size.X * 1000:0.##} × {size.Y * 1000:0.##} × {size.Z * 1000:0.##} mm, renk {mesh.TriangleColors != null}");
            Assert.True(mesh.TriangleCount > 500);
            // A screw of a few centimetres, read in millimetres and handed over in metres.
            double longest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            Assert.InRange(longest, 0.005, 0.5);
            Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Vertices.Count - 1));
        }

        [Fact]
        public void WritesAMeshAsAStepSolidThatReadsBack()
        {
            Assert.True(OcctBridge.Available, OcctBridge.LoadError);
            var box = MeshModel.CreateBox(new Vec3(0.04, 0.02, 0.01));   // 40 × 20 × 10 mm
            string path = Path.Combine(Path.GetTempPath(), "rw-occt-" + Guid.NewGuid().ToString("N") + ".step");
            try
            {
                OcctBridge.WriteStep(box, path);
                string text = File.ReadAllText(path);
                Assert.Contains("ISO-10303-21", text);
                Assert.Contains("MANIFOLD_SOLID_BREP", text);   // the closed box became a solid
                var back = OcctBridge.Read(path);
                var size = back.Bounds.Size;
                Assert.Equal(0.04, size.X, 6);
                Assert.Equal(0.02, size.Y, 6);
                Assert.Equal(0.01, size.Z, 6);
                Assert.Equal(12, back.TriangleCount);   // six flat faces, two triangles each
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void AMissingFileOrABadFileSaysWhy()
        {
            Assert.True(OcctBridge.Available, OcctBridge.LoadError);
            Assert.Throws<FileNotFoundException>(() => OcctBridge.Read(Path.Combine(Path.GetTempPath(), "yok-" + Guid.NewGuid() + ".step")));
            string bad = Path.Combine(Path.GetTempPath(), "rw-bad-" + Guid.NewGuid().ToString("N") + ".step");
            File.WriteAllText(bad, "bu bir STEP dosyası değil");
            try
            {
                var ex = Assert.Throws<InvalidDataException>(() => OcctBridge.Read(bad));
                Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            }
            finally
            {
                File.Delete(bad);
            }
        }
    }
}
