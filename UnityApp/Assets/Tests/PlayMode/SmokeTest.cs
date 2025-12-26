using NUnit.Framework;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.Tests.PlayMode
{
    public class SmokeTest
    {
        [Test]
        public void CircuitSpec_Initializes_Collections()
        {
            var spec = new CircuitSpec { Id = "smoke", Mode = SimulationMode.Fast };
            Assert.IsNotNull(spec.Components);
            Assert.IsNotNull(spec.Nets);
        }

        [Test]
        public void TemplateSpec_CanBeConstructed()
        {
            var template = new TemplateSpec
            {
                TemplateId = "smoke-template",
                DisplayName = "Smoke Template",
                Description = "Smoke test template",
                SystemType = "CircuitOnly",
                DefaultCircuit = new CircuitSpec { Id = "smoke", Mode = SimulationMode.Fast }
            };

            Assert.IsNotNull(template.DefaultCircuit);
        }
    }
}
