using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    /// <summary>
    /// Represents the root of a saved project (.rtwin file).
    /// Aggregates all necessary specs.
    /// </summary>
    public class ProjectManifest
    {
        public required string ProjectName { get; set; }
        public required string Version { get; set; } = "1.0.0";
        public required string Description { get; set; }

        // The core triad of a simulation
        public required CircuitSpec Circuit { get; set; }
        public required RobotSpec Robot { get; set; }
        public required WorldSpec World { get; set; }

        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}
