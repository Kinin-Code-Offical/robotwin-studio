using System.Collections.Generic;

namespace CoreSim.Catalogs
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
        public string ID { get; set; }
        public string Name { get; set; }
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
    }
}
