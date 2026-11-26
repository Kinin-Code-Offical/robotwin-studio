using RobotWin.Studio.Help;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Tests;

/// <summary>[components] A component card's ? opens a section that exists, in both guides.</summary>
public class CardHelpTests
{
    private static MarkdownDocument Guide(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RobotWinStudio.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return MarkdownParser.Parse(File.ReadAllText(Path.Combine(dir!, relative)));
    }

    [Fact]
    public void EveryCardHeadingIsInBothGuides()
    {
        var tr = MarkdownDocument.Walk(Guide("docs/kullanim/07-dunya.md").Blocks).OfType<MdHeading>().Select(h => h.Anchor).ToHashSet();
        var en = MarkdownDocument.Walk(Guide("docs/en/guide/07-world.md").Blocks).OfType<MdHeading>().Select(h => h.Anchor).ToHashSet();
        foreach (var (key, trHeading, enHeading) in CardHelp.Topics)
        {
            Assert.True(tr.Contains(MarkdownParser.Slug(trHeading)), key + " → " + trHeading);
            Assert.True(en.Contains(MarkdownParser.Slug(enHeading)), key + " → " + enHeading);
        }
    }

    [Fact]
    public void ACardFindsItsSectionOrSearchesForItsName()
    {
        Assert.Equal("07-dunya.md#Paraşüt", CardHelp.Section("Kurtarma paraşütü", false, s => s));
        Assert.Equal("07-world.md#PID tuner: a motor's speed loop", CardHelp.Section("PID ayarlayıcı", true, s => s));
        Assert.Equal("search:Light curtain", CardHelp.Section("Güvenlik ışık perdesi", true, _ => "Light curtain"));
    }
}
