using System.Linq;
using System.Text.Json;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[group] KiCad's groups on a board: grouped items are found together, kept in the file and taken apart again.</summary>
    public class BoardGroupTests
    {
        [Fact]
        public void AGroupHoldsItsItemsAndIsSavedAndUngrouped()
        {
            var b = new BoardDocument();
            var hole = new MountingHole { X = 2, Y = 2 };
            var text = new BoardText { Text = "J1", X = 5, Y = 2 };
            var loose = new Via { X = 9, Y = 9 };
            b.MountingHoles.Add(hole);
            b.Texts.Add(text);
            b.Vias.Add(loose);
            string name = BoardGroups.Group(b, new object[] { hole, text });
            Assert.Equal("Grup 1", name);
            Assert.Equal(2, BoardGroups.Members(b, name).Count);
            Assert.Null(loose.Group);
            Assert.Equal("Grup 2", BoardGroups.Group(b, new object[] { loose }));

            string json = JsonSerializer.Serialize(text);
            Assert.Contains("\"Group\":\"Grup 1\"", json);
            Assert.DoesNotContain("Group", JsonSerializer.Serialize(new Via()));   // ungrouped items write nothing

            Assert.Equal(2, BoardGroups.Ungroup(b, new object[] { text }));   // the whole group, from one of its items
            Assert.Null(hole.Group);
            Assert.Equal(new[] { "Grup 2" }, BoardGroups.Names(b).ToArray());
        }
    }
}
