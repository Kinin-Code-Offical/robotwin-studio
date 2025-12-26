using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public enum SimulationMode
    {
        Fast, // Behavioral, C# calculated
        Accurate // Netlist, SPICE based
    }

    public class CircuitSpec
    {
        public string Id { get; set; } = string.Empty;
        public SimulationMode Mode { get; set; }
        public List<ComponentSpec> Components { get; set; } = new List<ComponentSpec>();
        public List<NetSpec> Nets { get; set; } = new List<NetSpec>();
    }

    public class ComponentSpec
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // e.g. "ArduinoUno", "LED", "Resistor"
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    public class NetSpec
    {
        public string Id { get; set; } = string.Empty;
        public List<string> Nodes { get; set; } = new List<string>(); // "ComponentId.PinName"
    }
}
