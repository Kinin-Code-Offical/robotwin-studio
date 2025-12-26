using Xunit;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Catalogs;
using System.Text.Json;
using System.Collections.Generic;
using RobotTwin.CoreSim.Validation;

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

        [Fact]
        public void CanRegisterTemplateInCatalog()
        {
            var catalog = new TemplateCatalog();
            var template = new TemplateSpec { TemplateId = "t1", DisplayName = "Test Template", Description = "Desc", SystemType = "CircuitOnly" };

            catalog.Register(template);

            var found = catalog.Find("t1");
            Assert.NotNull(found);
            Assert.Equal("Test Template", found.Name);
        }

        [Fact]
        public void ExampleTemplate01_Blinky_ShouldBeValidAndRunnable()
        {
            var defaults = TemplateCatalog.GetDefaults();
            var blinky = defaults.Find(t => t.ID == "mvp.blinky");
            
            Assert.NotNull(blinky);
            Assert.Equal("Blinky: Arduino + LED", blinky.Name);
            Assert.NotNull(blinky.DefaultCircuit);
            
            // Validate the circuit composition
            var result = CircuitValidator.Validate(blinky.DefaultCircuit);
            
            // Should be valid with 0 errors
            Assert.True(result.IsValid, $"Blinky template invalid: {string.Join(", ", result.Errors)}");
            Assert.Empty(result.Errors);
            
            // Check essential components exist
            Assert.Contains(blinky.DefaultCircuit.Components, c => c.InstanceID == "uno");
            Assert.Contains(blinky.DefaultCircuit.Components, c => c.InstanceID == "led1");
            
            // Check wiring
            Assert.Contains(blinky.DefaultCircuit.Connections, c => c.FromComponentID == "uno" && c.FromPin == "D13");
        }
    }
}
