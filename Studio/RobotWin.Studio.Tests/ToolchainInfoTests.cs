using RobotWin.Studio.Firmware;
using Xunit;

/// <summary>[toolchain] The "built with" line a project keeps, read from arduino-cli's own JSON.</summary>
public class ToolchainInfoTests
{
    [Fact]
    public void TheCompilerAndItsCoreAreReadFromArduinoCli()
    {
        const string version = "{\"Application\": \"arduino-cli\", \"VersionString\": \"1.6.0\", \"Commit\": \"50a0e80da\"}";
        const string cores = "{\"platforms\": [{\"id\": \"esp32:esp32\", \"installed_version\": \"3.3.13\"}, {\"id\": \"arduino:avr\", \"installed_version\": \"1.8.8\", \"latest_version\": \"1.8.8\"}]}";
        Assert.Equal("1.6.0", ToolchainInfo.CliVersion(version));
        Assert.Equal("1.8.8", ToolchainInfo.CoreVersion(cores, "arduino:avr"));
        Assert.Equal("arduino-cli 1.6.0 · arduino:avr 1.8.8", ToolchainInfo.Describe("1.6.0", "arduino:avr", "1.8.8"));
    }

    [Fact]
    public void OlderAndBrokenOutputsDoNotThrow()
    {
        Assert.Equal("1.8.6", ToolchainInfo.CoreVersion("[{\"id\": \"arduino:avr\", \"installed\": \"1.8.6\"}]", "arduino:avr"));
        Assert.Null(ToolchainInfo.CoreVersion("{\"platforms\": []}", "arduino:avr"));
        Assert.Null(ToolchainInfo.CliVersion("not json"));
        Assert.Equal("arduino-cli ? · arduino:avr ?", ToolchainInfo.Describe(null, "arduino:avr", null));
    }
}
