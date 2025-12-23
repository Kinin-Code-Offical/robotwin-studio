using Xunit;
using CoreSim.Specs;
using CoreSim.Catalogs;
using System.Text.Json;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Tests
{
    public class TemplateTests
    {
        [Fact]
        public void CanSerializeAndDeserializeTemplateSpec()
        {
            var template = new TemplateSpec
            {
                ID = "mvp.linefollower.2servo",
                Name = "Line Follower Kit",
                DefaultCircuit = new CircuitSpec
                {
                    Name = "LF Circuit",
                    Components = new List<ComponentInstance>
                    {
                        new ComponentInstance { InstanceID = "u1", CatalogID = "arduino-uno" }
                    }
                },
                DefaultRobot = new RobotSpec
                {
                    Name = "LF Robot",
                    Parts = new List<PartInstance>
                    {
                        new PartInstance { InstanceID = "chassis", CatalogID = "2wd-chassis" }
                    }
                }
            };

            var json = JsonSerializer.Serialize(template);
            var deserialized = JsonSerializer.Deserialize<TemplateSpec>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("mvp.linefollower.2servo", deserialized.ID);
            Assert.NotNull(deserialized.DefaultCircuit);
            Assert.Equal("LF Circuit", deserialized.DefaultCircuit.Name);
            Assert.NotNull(deserialized.DefaultRobot);
            Assert.Equal("LF Robot", deserialized.DefaultRobot.Name);
        }

        [Fact]
        public void CanRegisterTemplateInCatalog()
        {
            var catalog = new TemplateCatalog();
            var template = new TemplateSpec { ID = "t1", Name = "Test Template" };

            catalog.Register(template);

            var found = catalog.Find("t1");
            Assert.NotNull(found);
            Assert.Equal("Test Template", found.Name);
        }
    }
}
