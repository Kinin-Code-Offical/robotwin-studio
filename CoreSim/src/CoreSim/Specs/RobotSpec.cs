using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public class RobotSpec
    {
        public string Name { get; set; }
        public List<PartInstance> Parts { get; set; } = new List<PartInstance>();
        public List<Joint> Joints { get; set; } = new List<Joint>();
    }

    public class PartInstance
    {
        public string InstanceID { get; set; }
        public string CatalogID { get; set; }
        public Dictionary<string, object> Config { get; set; } = new Dictionary<string, object>();
        // Base transform relative to parent or origin could be handled here or in Joints
    }

    public class Joint
    {
        public string? ID { get; set; }
        public string? ParentPartID { get; set; }
        public string? ParentMountPoint { get; set; }
        public string? ChildPartID { get; set; }
        public string? ChildMountPoint { get; set; }
        // Potentially offset/rotation tweaks
    }
}
