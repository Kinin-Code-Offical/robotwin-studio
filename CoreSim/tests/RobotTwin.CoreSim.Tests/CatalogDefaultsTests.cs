using Xunit;
using RobotTwin.CoreSim.Catalogs;
using RobotTwin.CoreSim.Specs;
using System.Linq;

namespace RobotTwin.CoreSim.Tests
{
    public class CatalogDefaultsTests
    {
        [Fact]
        public void ComponentCatalog_GetDefaults_ReturnsRequiredItems()
        {
            var defaults = ComponentCatalog.GetDefaults();
            
            Assert.NotEmpty(defaults);
            Assert.Contains(defaults, c => c.ID == "resistor");
            Assert.Contains(defaults, c => c.ID == "led");
            Assert.Contains(defaults, c => c.ID == "uno");
            Assert.Contains(defaults, c => c.ID == "source_5v");
            Assert.Contains(defaults, c => c.ID == "gnd");
        }

        [Fact]
        public void BoardCatalog_GetDefaults_ReturnsArduinoUno()
        {
            var defaults = BoardCatalog.GetDefaults();
            
            Assert.NotEmpty(defaults);
            
            var uno = defaults.FirstOrDefault(b => b.ID == "uno_board");
            Assert.NotNull(uno);
            Assert.Equal("Arduino Uno R3", uno.Name);
            
            Assert.Contains(uno.Pins, p => p.Name == "D13");
            Assert.Contains(uno.Pins, p => p.Name == "A0");
            Assert.Contains(uno.Pins, p => p.Name == "GND");
        }
    }
}
