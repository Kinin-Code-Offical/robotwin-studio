using Xunit;
using RobotTwin.CoreSim;

namespace RobotTwin.CoreSim.Tests;

public class BasicTests
{
    [Fact]
    public void Instance_NotNull()
    {
        var spec = new CircuitSpec { Name = "Test" };
        var instance = new CoreSimInstance();
        Assert.NotNull(instance);
        Assert.NotEmpty(instance.Version);
    }
}
