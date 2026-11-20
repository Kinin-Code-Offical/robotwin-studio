using System;
using System.IO;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    public class PartModel3DTests
    {
        private static PartModel3D BoxModel() => new PartModel3D
        {
            Mesh = MeshModel.CreateBox(new Vec3(0.010, 0.005, 0.003)).ToEmbedded(),
            Name = "box.stl",
            OffsetZ = 1.5,
            Color = "#336699"
        };

        [Fact]
        public void CustomPartKeepsItsModelThroughSaveAndLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rw-model3d-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var part = new CustomPart { Id = "user-box", Name = "Kutu", BasePartId = "resistor", Model3D = BoxModel() };
                string path = Path.Combine(dir, "user-box.rwpart");
                part.Save(path);
                var loaded = CustomPart.Load(path);
                Assert.NotNull(loaded.Model3D);
                Assert.Equal("box.stl", loaded.Model3D!.Name);
                Assert.Equal(1.5, loaded.Model3D.OffsetZ, 9);
                Assert.Equal(12, MeshModel.FromEmbedded(loaded.Model3D.Mesh).TriangleCount);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void RegisteringPartPublishesModelThroughParameters()
        {
            var lib = PartLibrary.Default;
            string id = "user-model-" + Guid.NewGuid().ToString("N");
            var part = new CustomPart { Id = id, Name = "Model test", BasePartId = "resistor", Model3D = BoxModel() };
            Assert.True(part.Register(lib));
            var preset = lib.FindPreset(id)!;
            Assert.Equal(id, preset.Parameters[PartModels3D.ParameterKey]);
            var resolved = lib.ResolveParameters(lib.Get("resistor"), preset, null);
            Assert.NotNull(PartModels3D.For(resolved));

            Assert.True(PartModels3D.Attach(lib, id, null));
            Assert.Null(PartModels3D.Find(id));
            Assert.False(preset.Parameters.ContainsKey(PartModels3D.ParameterKey));

            Assert.True(PartModels3D.Attach(lib, id, BoxModel()));
            Assert.NotNull(PartModels3D.For(lib.ResolveParameters(lib.Get("resistor"), preset, null)));
        }

        [Fact]
        public void PartsWithoutModelHaveNoModelParameter()
        {
            var lib = PartLibrary.Default;
            string id = "user-plain-" + Guid.NewGuid().ToString("N");
            Assert.True(new CustomPart { Id = id, Name = "Plain", BasePartId = "resistor" }.Register(lib));
            Assert.False(lib.FindPreset(id)!.Parameters.ContainsKey(PartModels3D.ParameterKey));
        }
    }
}
