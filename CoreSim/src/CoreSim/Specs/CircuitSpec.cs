using System.Collections.Generic;

namespace CoreSim.Specs
{
    public class CircuitSpec
    {
        public string Name { get; set; }
        public List<ComponentInstance> Components { get; set; } = new List<ComponentInstance>();
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }

    public class ComponentInstance
    {
        public string InstanceID { get; set; }
        public string CatalogID { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public Dictionary<string, object> ParameterOverrides { get; set; } = new Dictionary<string, object>();
    }

    public class Connection
    {
        public string FromComponentID { get; set; }
        public string FromPin { get; set; }
        public string ToComponentID { get; set; }
        public string ToPin { get; set; }
    }
}
