using Xunit;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Validation;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Tests
{
    public class ValidationRulesTests
    {
        [Fact]
        public void Validate_ActiveCircuitMissingGND_ReturnsWarning()
        {
            var spec = new CircuitSpec
            {
                Name = "NoGND",
                Components = new List<ComponentInstance>
                {
                    new ComponentInstance { InstanceID = "led1", CatalogID = "led" },
                    new ComponentInstance { InstanceID = "pwr", CatalogID = "source_5v" }
                }
            };

            var result = CircuitValidator.Validate(spec);
            Assert.True(result.IsValid); // Warnings don't fail valid
            Assert.Contains(result.Warnings, w => w.Contains("missing GND"));
        }

        [Fact]
        public void Validate_ActiveCircuitMissingPower_ReturnsWarning()
        {
             var spec = new CircuitSpec
            {
                Name = "NoPower",
                Components = new List<ComponentInstance>
                {
                     new ComponentInstance { InstanceID = "c1", CatalogID = "resistor", ParameterOverrides = new Dictionary<string, object>() },
                     new ComponentInstance { InstanceID = "c2", CatalogID = "resistor", ParameterOverrides = new Dictionary<string, object>() }
                }
            };

            var result = CircuitValidator.Validate(spec);
            Assert.Contains(result.Warnings, w => w.Contains("missing Power"));
        }

        [Fact]
        public void Validate_InvalidPin_ReturnsError()
        {
            var spec = new CircuitSpec
            {
                 Name = "Dangling",
                 Components = new List<ComponentInstance>
                 {
                     new ComponentInstance { InstanceID = "c1", CatalogID = "resistor", ParameterOverrides = new Dictionary<string, object>() }
                 },
                 Connections = new List<Connection>
                 {
                     new Connection { FromComponentID = "c1", FromPin = "99", ToComponentID = "c2", ToPin = "1" }
                 }
            };

            var result = CircuitValidator.Validate(spec);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Does not exist") || e.Contains("does not exist"));
        }

        [Fact]
        public void Validate_ValidSimpleCircuit_ReturnsClean()
        {
            var spec = new CircuitSpec
            {
                Name = "Good",
                Components = new List<ComponentInstance>
                {
                    new ComponentInstance { InstanceID = "pwr", CatalogID = "source_5v" },
                    new ComponentInstance { InstanceID = "gnd", CatalogID = "gnd" },
                    new ComponentInstance { InstanceID = "led1", CatalogID = "led" }
                },
                Connections = new List<Connection>
                {
                    new Connection { FromComponentID = "pwr", FromPin = "VCC", ToComponentID = "led1", ToPin = "A" },
                    new Connection { FromComponentID = "led1", FromPin = "K", ToComponentID = "gnd", ToPin = "GND" }
                }
            };

            var result = CircuitValidator.Validate(spec);
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }
    }
}
