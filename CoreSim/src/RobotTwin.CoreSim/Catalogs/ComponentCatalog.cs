using System.Collections.Generic;

namespace RobotTwin.CoreSim.Catalogs
{
    public enum ComponentType
    {
        Passive,
        Active,
        IC,
        Source,
        Electromechanical,
        Sensor
    }

    public class ComponentDefinition
    {
        public required string ID { get; set; }
        public required string Name { get; set; }
        public ComponentType Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public List<string> Pins { get; set; } = new List<string>();
    }

    public class ComponentCatalog
    {
        public List<ComponentDefinition> Components { get; set; } = new List<ComponentDefinition>();

        public ComponentDefinition? Find(string id)
        {
            return Components.Find(c => c.ID == id);
        }

        public static List<ComponentDefinition> GetDefaults()
        {
            return new List<ComponentDefinition>
            {
                new ComponentDefinition 
                { 
                    ID = "resistor", 
                    Name = "Resistor", 
                    Type = ComponentType.Passive,
                    Pins = new List<string> { "1", "2" },
                    Parameters = new Dictionary<string, object> { { "resistance", 1000 } }
                },
                new ComponentDefinition 
                { 
                    ID = "led", 
                    Name = "LED (Red)", 
                    Type = ComponentType.Active,
                    Pins = new List<string> { "A", "K" },
                    Parameters = new Dictionary<string, object> { { "color", "red" }, { "vf", 2.0 } }
                },
                new ComponentDefinition 
                { 
                    ID = "uno", 
                    Name = "Arduino Uno", 
                    Type = ComponentType.IC,
                    Pins = new List<string> { "5V", "GND", "VIN", "D13", "D12", "D11", "D10", "D9", "D8", "D7", "D6", "D5", "D4", "D3", "D2", "D1", "D0", "A0", "A1", "A2", "A3", "A4", "A5" }
                },
                new ComponentDefinition
                {
                    ID = "source_5v",
                    Name = "5V Source",
                    Type = ComponentType.Source,
                    Pins = new List<string> { "VCC", "GND" },
                    Parameters = new Dictionary<string, object> { { "voltage", 5.0 } }
                },
                new ComponentDefinition
                {
                    ID = "gnd",
                    Name = "GND Node",
                    Type = ComponentType.Passive,
                    Pins = new List<string> { "GND" }
                }
            };
        }
    }
}
