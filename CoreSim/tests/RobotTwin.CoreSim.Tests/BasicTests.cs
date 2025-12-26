using Xunit;
using RobotTwin.CoreSim;

using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Tests;

public class BasicTests
{
    [Fact]
    public void Instance_NotNull()
    {
        var spec = new CircuitSpec { Name = "Basic" };
        var instance = new CoreSimInstance();
        Assert.NotNull(instance);
        Assert.NotEmpty(instance.Version);
    }
}
