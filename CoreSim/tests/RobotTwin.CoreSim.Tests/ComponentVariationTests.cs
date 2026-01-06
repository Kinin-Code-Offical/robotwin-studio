using RobotTwin.CoreSim.Models.Physics;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Physics")]
    public class ComponentVariationTests
    {
        [Fact]
        public void ToleranceHandlesPercentInputs()
        {
            double nominal = 100.0;
            double tolPercent = 5.0;
            double sample = 1.0;

            double adjusted = ComponentVariation.ApplyTolerance(nominal, tolPercent, sample);

            Assert.Equal(105.0, adjusted, 6);
        }

        [Fact]
        public void AgingScalesByYears()
        {
            double nominal = 50.0;
            double ratePerYear = 0.02;
            double years = 3.0;

            double adjusted = ComponentVariation.ApplyAging(nominal, ratePerYear, years);

            Assert.Equal(53.0, adjusted, 6);
        }

        [Fact]
        public void NoiseUsesSignedSample()
        {
            double value = 10.0;
            double noiseRms = 0.5;
            double sample = -1.0;

            double adjusted = ComponentVariation.ApplyNoise(value, noiseRms, sample);

            Assert.Equal(9.5, adjusted, 6);
        }
    }
}
