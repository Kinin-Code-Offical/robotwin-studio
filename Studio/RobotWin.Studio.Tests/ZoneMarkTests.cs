using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Tests;

/// <summary>[trust] Windows' internet mark on a file (its Zone.Identifier stream): read as browsers write it, and removed as Unblock does.</summary>
public class ZoneMarkTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "rw-zone-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".rwworld");

    public ZoneMarkTests() => File.WriteAllText(_file, "world");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Fact]
    public void AFileFromTheInternetIsMarkedUntilItIsUnblocked()
    {
        Assert.Equal(0, ZoneMark.ZoneOf(_file));
        Assert.False(ZoneMark.FromElsewhere(_file));   // made on this PC

        // as a browser writes it beside a download
        File.WriteAllText(_file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/robot.rwworld\r\n");
        Assert.Equal(3, ZoneMark.ZoneOf(_file));
        Assert.True(ZoneMark.FromElsewhere(_file));
        Assert.Equal("world", File.ReadAllText(_file));   // the file itself is untouched

        ZoneMark.Unblock(_file);
        Assert.Equal(0, ZoneMark.ZoneOf(_file));
        Assert.False(ZoneMark.FromElsewhere(_file));
        Assert.Equal("world", File.ReadAllText(_file));
    }

    [Fact]
    public void AnIntranetFileIsNotTreatedAsFromElsewhere()
    {
        File.WriteAllText(_file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=1\r\n");
        Assert.Equal(1, ZoneMark.ZoneOf(_file));
        Assert.False(ZoneMark.FromElsewhere(_file));
        Assert.False(ZoneMark.FromElsewhere(null));
    }
}
