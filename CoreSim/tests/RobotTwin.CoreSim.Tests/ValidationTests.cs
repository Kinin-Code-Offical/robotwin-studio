using Xunit;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Validation;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Tests
{
    public class ValidationTests
    {
        [Fact]
        public void Validate_EmptySpec_ReturnsWarning()
        {
            var spec = new CircuitSpec { Name = "Empty" };
            var result = CircuitValidator.Validate(spec);
            
            Assert.True(result.IsValid); // Technically valid but warns
            Assert.Contains(result.Warnings, w => w.Contains("no components"));
        }

        [Fact]
        public void Validate_DuplicateIDs_ReturnsError()
        {
            var spec = new CircuitSpec
            {
                Name = "Dupes",
                Components = new List<ComponentInstance>
                {
                    new ComponentInstance { InstanceID = "c1", CatalogID = "resistor" },
                    new ComponentInstance { InstanceID = "c1", CatalogID = "led" }
                }
            };
            var result = CircuitValidator.Validate(spec);
            
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Duplicate"));
        }

        [Fact]
        public void Validate_MissingConnectionTarget_ReturnsError()
        {
             var spec = new CircuitSpec
            {
                Name = "BadConn",
                Components = new List<ComponentInstance>
                {
                    new ComponentInstance { InstanceID = "c1", CatalogID = "resistor" }
                },
                Connections = new List<Connection>
                {
                    new Connection { FromComponentID = "c1", ToComponentID = "GHOST" }
                }
            };
            var result = CircuitValidator.Validate(spec);
            
            Assert.Contains(result.Errors, e => e.Contains("GHOST"));
        }
    }
}
