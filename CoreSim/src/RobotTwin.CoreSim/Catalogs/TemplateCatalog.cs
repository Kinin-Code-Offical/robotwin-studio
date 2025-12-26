using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;
using BlinkyTpl = RobotTwin.CoreSim.Catalogs.Templates.BlinkyTemplate;

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
                BlinkyTpl.GetSpec(),
                // Blank Template
                new TemplateSpec
                {
                    TemplateId = "mvp.blank",
                    DisplayName = "Blank Template",
                    Description = "Start from an empty circuit-only project.",
                    SystemType = "CircuitOnly",
                    DefaultCircuit = new CircuitSpec { Name = "New Circuit" }
                },
                new TemplateSpec 
                { 
                    TemplateId = "mvp.linefollower", 
                    DisplayName = "Line Follower Kit", 
                    Description = "Basic 2-servo robot with line sensors.",
                    SystemType = "Robot",
                    DefaultCircuit = new CircuitSpec { Name = "Line Follower Circuit" },
                    DefaultRobot = new RobotSpec { Name = "Line Follower Robot" }
                },
                new TemplateSpec 
                { 
                    TemplateId = "mvp.arm", 
                    DisplayName = "Robotic Arm Kit", 
                    Description = "3-DOF robotic arm.",
                    SystemType = "Robot",
                    DefaultCircuit = new CircuitSpec { Name = "Arm Circuit" },
                    DefaultRobot = new RobotSpec { Name = "Arm Robot" }
                }
            };
        }
    }
}
