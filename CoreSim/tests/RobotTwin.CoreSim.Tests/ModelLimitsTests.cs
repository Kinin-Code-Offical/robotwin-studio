using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[limits] The notes on what models leave out name real parts, and the key models have them.</summary>
    public class ModelLimitsTests
    {
        [Fact]
        public void EveryNoteIsForAPartInTheLibraryAndTheKeyModelsHaveOne()
        {
            var lib = PartLibrary.Default;
            foreach (var id in new[] { "resistor", "capacitor", "inductor", "diode", "led", "npn", "nmos", "opamp", "regulator", "buck-module", "boost-module",
                                       "battery", "dc-motor", "servo", "stepper", "a4988", "tmc2209", "button", "relay", "arduino-uno", "esp32-devkitc", "fuse" })
            {
                Assert.NotNull(lib.Find(id));
                Assert.NotEmpty(ModelLimits.Of(id));
            }

            Assert.Empty(ModelLimits.Of("no-such-part"));
            Assert.Empty(ModelLimits.Of(null));
            // Every part the notes name is in the library (a typo would leave a note nobody sees).
            foreach (var part in lib.Parts)
                foreach (string note in ModelLimits.Of(part.Id))
                    Assert.False(string.IsNullOrWhiteSpace(note));
            Assert.True(lib.Parts.Count(p => ModelLimits.Of(p.Id).Count > 0) >= 30);
        }
    }
}
