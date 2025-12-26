using System.Collections.Generic;

namespace RobotTwin.CoreSim.Catalogs
{
    public enum PartType
    {
        Chassis,
        Actuator,
        Sensor,
        Wheel,
        Decorator
    }

    public class PartDefinition
    {
        public required string ID { get; set; }
        public required string Name { get; set; }
        public PartType Type { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>(); // Mass, Torque, Dimensions
        public List<string> MountingPoints { get; set; } = new List<string>();
    }

    public class PartCatalog
    {
        public List<PartDefinition> Parts { get; set; } = new List<PartDefinition>();
        
        public PartDefinition? Find(string id)
        {
            return Parts.Find(p => p.ID == id);
        }
    }
}
