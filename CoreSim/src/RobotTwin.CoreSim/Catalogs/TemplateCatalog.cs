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
