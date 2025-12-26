using Xunit;
using RobotTwin.CoreSim.Contracts;
using RobotTwin.CoreSim.Serialization;

namespace RobotTwin.CoreSim.Tests
{
    public class SerializationTests
    {
        [Fact]
        public void Serialize_IOContract_RoundTrip()
        {
            var contract = new IOContract
            {
                Name = "Test Circuit", // Assuming IOContract now has a Name property
                Components = new List<ComponentInstance> // Assuming IOContract now has a Components property
                {
                    new ComponentInstance { InstanceID = "r1", CatalogID = "resistor", ParameterOverrides = new Dictionary<string, object>() }
                },
                SignalId = "TEST_SIG",
                Type = SignalType.PwmOutput,
                Unit = "Hz",
                NominalValue = 50.0
            };

            string json = SimulationSerializer.Serialize(contract);
            Assert.Contains("PwmOutput", json); // Enum as string

            var deserialized = SimulationSerializer.Deserialize<IOContract>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(contract.SignalId, deserialized.SignalId);
            Assert.Equal(contract.Type, deserialized.Type);
            Assert.Equal(contract.Unit, deserialized.Unit);
            Assert.Equal(contract.NominalValue, deserialized.NominalValue);
        }
    }
}
