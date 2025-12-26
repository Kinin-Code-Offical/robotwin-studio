using Xunit;
using RobotTwin.CoreSim.Specs;

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
                TemplateId = "mvp.linefollower.2servo",
                DisplayName = "Line Follower Kit",
                SystemType = "Robot",
                Description = "Test Description",
                DefaultCircuitId = "circuit-lf-1",
                DefaultRobotId = "robot-lf-1"
            };

            var json = JsonSerializer.Serialize(template);
            var deserialized = JsonSerializer.Deserialize<TemplateSpec>(json);

            Assert.NotNull(deserialized);
            // Verify aliases work
            Assert.Equal("mvp.linefollower.2servo", deserialized.ID);
            Assert.Equal("mvp.linefollower.2servo", deserialized.TemplateId);
            
            Assert.Equal("Line Follower Kit", deserialized.DisplayName);
            Assert.Equal("Line Follower Kit", deserialized.DisplayName);
            
            Assert.Equal("circuit-lf-1", deserialized.DefaultCircuitId);
            Assert.Equal("robot-lf-1", deserialized.DefaultRobotId);
        }




    }
}
