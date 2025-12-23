namespace RobotTwin.CoreSim.Specs
{
    /// <summary>
    /// Defines a Project Template, which is a pre-configured system archetype.
    /// Used to initialize a new Session.
    /// </summary>
    public class TemplateSpec
    {
        public string TemplateId { get; set; } = string.Empty;
        
        // Backward compatibility for UnityApp / Tests
        public string ID { get => TemplateId; set => TemplateId = value; }

        public string DisplayName { get; set; } = string.Empty;
        
        // Backward compatibility for UnityApp / Tests
        public string Name { get => DisplayName; set => DisplayName = value; }

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The type of system: "CircuitOnly", "Robot", "Mechatronic", etc.
        /// </summary>
        public string SystemType { get; set; } = "CircuitOnly";

        /// <summary>
        /// Default Circuit Specification (JSON or ID).
        /// </summary>
        public string? DefaultCircuitId { get; set; }

        /// <summary>
        /// Default Robot Specification (JSON or ID).
        /// </summary>
        public string? DefaultRobotId { get; set; }

        /// <summary>
        /// Default World environment ID.
        /// </summary>
        public string? DefaultWorldId { get; set; }

        // Backward compatibility / Embedded specs
        public CircuitSpec? DefaultCircuit { get; set; }
        public RobotSpec? DefaultRobot { get; set; }
        public WorldSpec? DefaultWorld { get; set; }
    }
}
