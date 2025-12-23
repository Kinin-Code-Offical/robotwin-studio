using System.Collections.Generic;
using RobotTwin.CoreSim.Specs; // Assuming BoardSpec is/will be here

namespace RobotTwin.CoreSim.Catalogs
{
    public class BoardCatalog
    {
        public List<BoardSpec> Boards { get; set; } = new List<BoardSpec>();

        public BoardSpec? Find(string id)
        {
            return Boards.Find(b => b.ID == id);
        }

        public static List<BoardSpec> GetDefaults()
        {
            var uno = new BoardSpec
            {
                ID = "uno",
                Name = "Arduino Uno R3",
                MCU = "ATmega328P"
            };

            // Digital D0-D13
            for (int i = 0; i <= 13; i++)
            {
                uno.Pins.Add(new PinDef 
                { 
                    Name = $"D{i}", 
                    HardwareIndex = i, 
                    SupportedTypes = new List<PinType> { PinType.Digital, PinType.PWM } 
                });
            }

            // Analog A0-A5
            for (int i = 0; i <= 5; i++)
            {
                uno.Pins.Add(new PinDef 
                { 
                    Name = $"A{i}", 
                    HardwareIndex = 14 + i, 
                    SupportedTypes = new List<PinType> { PinType.Analog, PinType.Digital } 
                });
            }
            
            // Power
            uno.Pins.Add(new PinDef { Name = "5V", SupportedTypes = new List<PinType> { PinType.Power } });
            uno.Pins.Add(new PinDef { Name = "3.3V", SupportedTypes = new List<PinType> { PinType.Power } });
            uno.Pins.Add(new PinDef { Name = "GND", SupportedTypes = new List<PinType> { PinType.GND } });
            uno.Pins.Add(new PinDef { Name = "VIN", SupportedTypes = new List<PinType> { PinType.Power } });

            return new List<BoardSpec> { uno };
        }
    }
}
