using System.Linq;
using System.Text.Json.Nodes;
using RobotWin.Studio.Shell;
using Xunit;

namespace RobotWin.Studio.Tests
{
    /// <summary>[workspaces] A project's own settings (its layout among them) and the built-in workspaces' tool strips.</summary>
    public class ProjectSettingsTests
    {
        [Fact]
        public void SettingAnEntryKeepsTheOthers()
        {
            string? s = ProjectSettings.With(null, "workspace", new JsonObject { ["Page"] = "pcb" });
            s = ProjectSettings.With(s, "other", JsonValue.Create(3));
            Assert.Equal("pcb", (string?)ProjectSettings.Get(s, "workspace")?["Page"]);
            Assert.Equal(3, (int?)ProjectSettings.Get(s, "other"));

            s = ProjectSettings.With(s, "workspace", null);
            Assert.Null(ProjectSettings.Get(s, "workspace"));
            Assert.Equal(3, (int?)ProjectSettings.Get(s, "other"));
            Assert.Null(ProjectSettings.With(s, "other", null));   // nothing left: no settings.json at all
        }

        [Fact]
        public void UnreadableSettingsCountAsEmpty()
        {
            Assert.Null(ProjectSettings.Get("{ not json", "workspace"));
            Assert.Null(ProjectSettings.Get("[1, 2]", "workspace"));
            Assert.Equal("x", (string?)ProjectSettings.Get(ProjectSettings.With("{ not json", "k", JsonValue.Create("x")), "k"));
        }

        [Fact]
        public void StripsReadTheirStatesAndSkipUnknownOnes()
        {
            var strips = WorkspaceStrips.Parse(" tools=open, draw=closed,grid=folded,bad,x=maybe,=open").ToList();
            Assert.Equal(new[] { ("tools", true, false), ("draw", false, false), ("grid", true, true) }, strips);
            Assert.Empty(WorkspaceStrips.Parse(null));
        }
    }
}
