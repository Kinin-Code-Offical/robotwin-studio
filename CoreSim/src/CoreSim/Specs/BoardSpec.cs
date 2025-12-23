using System.Collections.Generic;

namespace CoreSim.Specs
{
    public class BoardSpec
    {
        public string? ID { get; set; }
        public string? Name { get; set; } // "Arduino Uno R3"
        public string? MCU { get; set; } // "ATmega328P"
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
