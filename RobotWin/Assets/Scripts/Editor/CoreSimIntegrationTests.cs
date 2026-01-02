using NUnit.Framework;
using RobotTwin.CoreSim.Specs;

public class CoreSimIntegrationTests
{
    [Test]
    public void CoreSim_Dll_IsAccessible()
    {
        var spec = new CircuitSpec { Id = "Test", Mode = SimulationMode.Fast };
        Assert.IsNotNull(spec);
    }
}
