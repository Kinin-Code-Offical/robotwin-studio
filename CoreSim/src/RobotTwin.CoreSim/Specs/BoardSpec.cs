using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public class BoardSpec
    {
        public required string ID { get; set; }
        public required string Name { get; set; }
        public required string MCU { get; set; }
        public List<PinDef> Pins { get; set; } = new List<PinDef>();
    }

    public enum PinType
    {
        Digital,
        Analog,
        PWM,
        Power,
        GND
    }

    public class PinDef
    {
        public string? Name { get; set; } // "D13", "A0", "5V"
        public int HardwareIndex { get; set; } 
        public List<PinType> SupportedTypes { get; set; } = new List<PinType>();
    }
}
