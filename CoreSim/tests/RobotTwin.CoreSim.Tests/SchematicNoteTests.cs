using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[notes] Texts on the sheet: saved, picked by a box, moved, copied and pasted, deleted; no effect on the nets.</summary>
    public class SchematicNoteTests
    {
        [Fact]
        public void NotesAreSavedMovedCopiedAndDeletedWithoutTouchingTheNets()
        {
            var s = new Schematic();
            s.Parts.Add(new SchematicPart { RefDes = "R1", PartId = "resistor", Position = new GridPoint(10, 10) });
            var note = new SchematicNote { Text = "Güç girişi\nen çok 12 V", Position = new GridPoint(30, 4), Size = 1.5, Boxed = true };
            s.Notes.Add(note);
            int nets = s.BuildNetlist(PartLibrary.Default).Nets.Count;

            var back = Schematic.FromJson(s.ToJson());
            var n = Assert.Single(back.Notes);
            Assert.Equal("Güç girişi\nen çok 12 V", n.Text);
            Assert.True(n.Boxed);
            Assert.Equal(1.5, n.Size);

            var b = note.Bounds();
            Assert.True(b.Width > 5 && b.Height > 3);   // two lines at 1.5×, framed
            Assert.Contains(note, SchematicEditing.ItemsInRect(s, PartLibrary.Default, new GridRect(25, 0, 60, 20), false));
            Assert.Contains(note, SchematicEditing.AllItems(s));

            SchematicEditing.Translate(new object[] { note }, new GridPoint(2, 3));
            Assert.Equal(new GridPoint(32, 7), note.Position);

            var fragment = SchematicFragment.FromItems(new object[] { note }, PartLibrary.Default, "test");
            var pasted = fragment.PasteInto(s, PartLibrary.Default, new GridPoint(50, 50));
            Assert.Equal(2, s.Notes.Count);
            Assert.Contains(pasted.Items, i => i is SchematicNote);

            Assert.Equal(1, SchematicEditing.Delete(s, new object[] { note }));
            Assert.Single(s.Notes);
            Assert.Equal(nets, s.BuildNetlist(PartLibrary.Default).Nets.Count);
        }
    }
}
