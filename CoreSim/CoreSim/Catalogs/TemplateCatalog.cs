using System.Collections.Generic;
using CoreSim.Specs;

namespace CoreSim.Catalogs
{
    public class TemplateDefinition
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public CircuitSpec DefaultCircuit { get; set; }
        public RobotSpec DefaultRobot { get; set; }
        public WorldSpec DefaultWorld { get; set; }
        // Potentially Firmware reference
    }

    public class TemplateCatalog
    {
        public List<TemplateDefinition> Templates { get; set; } = new List<TemplateDefinition>();
        
        public TemplateDefinition Find(string id)
        {
            return Templates.Find(t => t.ID == id);
        }

        public static List<TemplateDefinition> GetDefaults()
        {
            var defaults = new List<TemplateDefinition>();

            // 1. Circuit Sandbox
            defaults.Add(new TemplateDefinition
            {
                ID = "tpl-circuit-sandbox",
                Name = "Circuit Sandbox",
                Description = "Empty board for testing circuits.",
                DefaultCircuit = new CircuitSpec { Name = "Empty Circuit" },
                DefaultRobot = null, // No robot
                DefaultWorld = null
            });

            // 2. Line Follower MVP
            defaults.Add(new TemplateDefinition
            {
                ID = "tpl-line-follower",
                Name = "Line Follower (MVP)",
                Description = "Pre-built 2-servo robot with line sensors.",
                DefaultCircuit = new CircuitSpec { Name = "Line Follower Circuit" },
                DefaultRobot = new RobotSpec { Name = "LineBot v1" },
                DefaultWorld = new WorldSpec { Name = "Oval Track" }
            });

            return defaults;
        }
    }
}
