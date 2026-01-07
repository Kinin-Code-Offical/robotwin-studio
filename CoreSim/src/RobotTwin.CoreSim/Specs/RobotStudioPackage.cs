using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public class RobotStudioPackage
    {
        public RobotSpec Robot { get; set; } = new RobotSpec { Name = "Robot" };
        public CircuitSpec Circuit { get; set; } = new CircuitSpec();
        public AssemblySpec Assembly { get; set; } = new AssemblySpec();
        public EnvironmentSpec Environment { get; set; } = new EnvironmentSpec();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public ProjectManifest? Project { get; set; }
    }
}
