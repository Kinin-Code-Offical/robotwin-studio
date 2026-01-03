using RobotTwin.CoreSim.Models.Physics;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Physics")]
    public class ThermalModelTests
    {
        [Fact]
        public void IncreasesTemperatureWithCurrent()
        {
            var model = new ThermalModel
            {
                Resistance = 10.0,
                ThermalMass = 1.0,
                DissipationConstant = 0.0
            };

            var start = model.CurrentTempC;
            model.Update(2.0, 1.0);

            Assert.True(model.CurrentTempC > start);
        }

        [Fact]
        public void CoolsTowardAmbientWhenNoCurrent()
        {
            var model = new ThermalModel
            {
                Resistance = 10.0,
                ThermalMass = 1.0,
                DissipationConstant = 1.0
            };

            model.Update(2.0, 1.0);
            var hot = model.CurrentTempC;

            model.Update(0.0, 1.0);

            Assert.True(model.CurrentTempC < hot);
        }

        [Fact]
        public void FlagsOverheatingAboveThreshold()
        {
            var model = new ThermalModel
            {
                Resistance = 10.0,
                ThermalMass = 1.0,
                DissipationConstant = 0.0
            };

            model.Update(5.0, 1.0);

            Assert.True(model.IsOverheating(100.0));
        }
    }
}
