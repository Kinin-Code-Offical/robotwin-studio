using Xunit;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Validation;
using RobotTwin.CoreSim.Catalogs;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Tests
{
    public class ValidationRulesTests_NetIntegrity
    {
        [Fact(Skip = "Short circuit logic removed from MVP-2 Validator")]
        public void Validate_ShortCircuit_ReturnsError()
        {
            // Arrange
            var spec = new CircuitSpec
            {
                Name = "ShortCircuit",
                Components = new List<ComponentInstance>
                {
                    new ComponentInstance { InstanceID = "pwr", CatalogID = "source_5v", ParameterOverrides = new Dictionary<string, object>() },
                    new ComponentInstance { InstanceID = "gnd", CatalogID = "gnd", ParameterOverrides = new Dictionary<string, object>() }
                },
                Connections = new List<Connection>
                {
                    // Direct short from Power to GND
                    new Connection { FromComponentID = "pwr", FromPin = "VCC", ToComponentID = "gnd", ToPin = "GND" }
                }
            };

            // Act
            var result = CircuitValidator.ValidateCircuit(spec);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Short Circuit detected"));
        }

        [Fact(Skip = "Power Budget logic removed")]
        public void Validate_PowerBudgetBreach_ReturnsWarning()
        {
            // Arrange: Active components but NO power source
            var spec = new CircuitSpec
            {
                Name = "NoPowerActive",
                Components = new List<ComponentInstance>
                {
                    new ComponentInstance { InstanceID = "led1", CatalogID = "led", ParameterOverrides = new Dictionary<string, object>() },
                    new ComponentInstance { InstanceID = "gnd", CatalogID = "gnd", ParameterOverrides = new Dictionary<string, object>() }
                },
                Connections = new List<Connection>
                {
                    new Connection { FromComponentID = "led1", FromPin = "K", ToComponentID = "gnd", ToPin = "GND" }
                }
            };

            // Act
            var result = CircuitValidator.ValidateCircuit(spec);

            // Assert
            Assert.True(result.IsValid); // Still valid to have unpowered circuit, but warning
            Assert.Contains(result.Warnings, w => w.Contains("Power Budget Breach") || w.Contains("missing Power Source"));
        }
    }
}
