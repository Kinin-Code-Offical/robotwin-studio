using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Catalogs
{
    public class TemplateCatalog
    {
        public List<TemplateSpec> Templates { get; set; } = new List<TemplateSpec>();

        public TemplateSpec? Find(string id)
        {
            return Templates.Find(t => t.TemplateId == id);
        }

        public void Register(TemplateSpec template)
        {
            if (Find(template.TemplateId) == null)
            {
                Templates.Add(template);
            }
        }

        public static List<TemplateSpec> GetDefaults()
        {
            return new List<TemplateSpec>
            {
                // Verification Template (Feature #31)
                new TemplateSpec 
                { 
                    ID = "mvp.exampletemplate-01", 
                    Name = "ExampleTemplate-01: Blinky", 
                    Description = "Hello World: Arduino Uno + Resistor + LED on D13.",
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
                },
                // Blank Template
                new TemplateSpec
                {
                    ID = "mvp.blank",
                    Name = "Blank Template",
                    Description = "Start from an empty circuit-only project.",
                    SystemType = "CircuitOnly",
                    DefaultCircuit = new CircuitSpec { Name = "New Circuit" }
                },
                new TemplateSpec 
                { 
                    ID = "mvp.linefollower", 
                    Name = "Line Follower Kit", 
                    Description = "Basic 2-servo robot with line sensors.",
                    DefaultCircuit = new CircuitSpec { Name = "Line Follower Circuit" },
                    DefaultRobot = new RobotSpec { Name = "Line Follower Robot" }
                },
                new TemplateSpec 
                { 
                    ID = "mvp.arm", 
                    Name = "Robotic Arm Kit", 
                    Description = "3-DOF robotic arm.",
                     DefaultCircuit = new CircuitSpec { Name = "Arm Circuit" },
                    DefaultRobot = new RobotSpec { Name = "Arm Robot" }
                }
            };
        }
    }
}
