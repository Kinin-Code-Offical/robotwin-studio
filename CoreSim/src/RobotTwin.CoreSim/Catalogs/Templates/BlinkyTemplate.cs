using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Catalogs.Templates
{
    public static class BlinkyTemplate
    {
        public static TemplateSpec GetSpec()
        {
            return new TemplateSpec
            {
                TemplateId = "mvp.blinky",
                DisplayName = "Blinky: Arduino + LED",
                Description = "Standard Hello World: Arduino Uno blinking an LED on Pin 13.",
                SystemType = "CircuitOnly",
                DefaultCircuit = new CircuitSpec
                {
                    Name = "Blinky Circuit",
                    Components = new List<ComponentInstance>
                    {
                        new ComponentInstance { X=0, Y=0, CatalogID="source_5v", InstanceID="pwr" },
                        new ComponentInstance { X=0, Y=2, CatalogID="gnd",       InstanceID="gnd" },
                        new ComponentInstance { X=4, Y=0, CatalogID="uno",       InstanceID="uno" },
                        new ComponentInstance { X=8, Y=0, CatalogID="resistor",  InstanceID="r1" },
                        new ComponentInstance { X=10, Y=0,CatalogID="led",       InstanceID="led1" }
                    },
                    Connections = new List<Connection>
                    {
                        new Connection { FromComponentID="pwr", FromPin="VCC", ToComponentID="uno", ToPin="5V" },
                        new Connection { FromComponentID="pwr", FromPin="GND", ToComponentID="gnd", ToPin="GND" },
                        new Connection { FromComponentID="uno", FromPin="GND", ToComponentID="gnd", ToPin="GND" },
                        new Connection { FromComponentID="uno", FromPin="D13", ToComponentID="r1",  ToPin="1" },
                        new Connection { FromComponentID="r1",  FromPin="2",   ToComponentID="led1",ToPin="A" },
                        new Connection { FromComponentID="led1",FromPin="K",   ToComponentID="gnd", ToPin="GND" }
                    }
                }
            };
        }
    }
}
