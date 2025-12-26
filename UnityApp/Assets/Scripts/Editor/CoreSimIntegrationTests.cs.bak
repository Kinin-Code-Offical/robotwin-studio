using NUnit.Framework;
using UnityEngine;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Specs;

public class CoreSimIntegrationTests
{
    // Minimal EditMode Smoke Test for Unity-CoreSim integration
    [Test]
    public void CoreSim_Dll_IsAccessible_And_RunEngine_Initializes()
    {
        var spec = new CircuitSpec { Name = "Test" };
        var engine = new RunEngine(spec);
        
        Assert.NotNull(engine.Session);
        Assert.AreEqual(0, engine.Session.TickIndex);
        
        engine.Step();
        Assert.AreEqual(1, engine.Session.TickIndex);
    }
}
