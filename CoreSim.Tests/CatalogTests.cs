using Xunit;
using CoreSim.Catalogs;
using CoreSim.Specs;
using System.Text.Json;

namespace CoreSim.Tests
{
    public class CatalogTests
    {
        [Fact]
        public void CanSerializeAndDeserializeComponentCatalog()
        {
            var catalog = new ComponentCatalog();
            catalog.Components.Add(new ComponentDefinition 
            { 
                ID = "resistor-1k", 
                Name = "1k Resistor", 
                Type = ComponentType.Passive,
                Pins = new System.Collections.Generic.List<string> { "1", "2" }
            });

            var json = JsonSerializer.Serialize(catalog);
            var deserialized = JsonSerializer.Deserialize<ComponentCatalog>(json);

            Assert.NotNull(deserialized);
            Assert.Single(deserialized.Components);
            Assert.Equal("resistor-1k", deserialized.Components[0].ID);
        }

        [Fact]
        public void CanSerializeAndDeserializeRobotSpec()
        {
            var robot = new RobotSpec { Name = "TestBot" };
            robot.Parts.Add(new PartInstance { InstanceID = "p1", CatalogID = "chassis" });
            
            var json = JsonSerializer.Serialize(robot);
            var deserialized = JsonSerializer.Deserialize<RobotSpec>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("TestBot", deserialized.Name);
            Assert.Single(deserialized.Parts);
        }
    }
}
