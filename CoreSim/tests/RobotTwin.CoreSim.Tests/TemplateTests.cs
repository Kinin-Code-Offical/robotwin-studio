using Xunit;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Catalogs;
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
                DefaultCircuitId = "circuit-lf-1",
                DefaultRobotId = "robot-lf-1"
            };

            var json = JsonSerializer.Serialize(template);
            var deserialized = JsonSerializer.Deserialize<TemplateSpec>(json);

            Assert.NotNull(deserialized);
            // Verify aliases work
            Assert.Equal("mvp.linefollower.2servo", deserialized.ID);
            Assert.Equal("mvp.linefollower.2servo", deserialized.TemplateId);
            
            Assert.Equal("Line Follower Kit", deserialized.Name);
            Assert.Equal("Line Follower Kit", deserialized.DisplayName);
            
            Assert.Equal("circuit-lf-1", deserialized.DefaultCircuitId);
            Assert.Equal("robot-lf-1", deserialized.DefaultRobotId);
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
