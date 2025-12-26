using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public class CircuitSpec
    {
        public required string Name { get; set; }
        public List<ComponentInstance> Components { get; set; } = new List<ComponentInstance>();
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }

    public class ComponentInstance
    {
        public required string InstanceID { get; set; }
        public required string CatalogID { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public Dictionary<string, object> ParameterOverrides { get; set; } = new Dictionary<string, object>();
    }

    public class Connection
    {
        public required string FromComponentID { get; set; }
        public required string FromPin { get; set; }
        public required string ToComponentID { get; set; }
        public required string ToPin { get; set; }
    }
}
