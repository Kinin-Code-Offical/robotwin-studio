using RobotTwin.CoreSim.Models.Physics;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Physics")]
    public class DeterministicNoiseTests
    {
        [Fact]
        public void SampleSignedIsDeterministic()
        {
            double a = DeterministicNoise.SampleSigned("R1:V", 10);
            double b = DeterministicNoise.SampleSigned("R1:V", 10);

            Assert.Equal(a, b, 12);
        }

        [Fact]
        public void SampleSignedChangesWithStep()
        {
            double a = DeterministicNoise.SampleSigned("R1:V", 10);
            double b = DeterministicNoise.SampleSigned("R1:V", 11);

            Assert.NotEqual(a, b);
        }
    }
}
