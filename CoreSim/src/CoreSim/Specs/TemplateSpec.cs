using System.Collections.Generic;

namespace CoreSim.Specs
{
    /// <summary>
    /// Defines a project template used to initialize a new user session.
    /// Bundles all necessary specs (Circuit, Robot, Firmware, World).
    /// </summary>
    public class TemplateSpec
    {
        public string? ID { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; } = "1.0";

        // Paths or inline definitions for default specs
        // We use strings (JSON or IDs) to keep it loose for now, 
        // or we could embed the Spec objects directly if they are lightweight.
        // For portability, let's assume this Spec contains the actual initial configurations.
        
        public CircuitSpec DefaultCircuit { get; set; }
        public RobotSpec DefaultRobot { get; set; }
        public WorldSpec DefaultWorld { get; set; }
        
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
}
