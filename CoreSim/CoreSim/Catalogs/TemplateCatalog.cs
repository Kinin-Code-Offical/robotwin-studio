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
    }
}
