using System.Collections.Generic;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[junction] KiCad's Add Junction: wires that only cross do not join; a junction at the crossing joins them.</summary>
    public class SchematicJunctionTests
    {
        private static readonly GridPoint Cross = new GridPoint(10, 10);

        /// <summary>A horizontal wire (label A) and a vertical one (label B) crossing at (10, 10), and a third ending on the first (a T).</summary>
        private static Schematic Crossing()
        {
            var s = new Schematic();
            s.Wires.Add(new SchematicWire { Points = new List<GridPoint> { new GridPoint(0, 10), new GridPoint(20, 10) } });
            s.Wires.Add(new SchematicWire { Points = new List<GridPoint> { new GridPoint(10, 0), new GridPoint(10, 20) } });
            s.Labels.Add(new NetLabel { Name = "A", Position = new GridPoint(0, 10) });
            s.Labels.Add(new NetLabel { Name = "B", Position = new GridPoint(10, 0) });
            return s;
        }

        private static (string? H, string? V, bool Dot) Nets(Schematic s)
        {
            var n = s.BuildNetlist(PartLibrary.Default);
            return (n.WireNets[0], n.WireNets[1], n.Junctions.Contains(Cross));
        }

        [Fact]
        public void CrossingWiresJoinOnlyAtAJunction()
        {
            var s = Crossing();
            var before = Nets(s);
            Assert.NotEqual(before.H, before.V);
            Assert.False(before.Dot);

            Assert.True(SchematicEditing.PlaceJunction(s, Cross));
            var after = Nets(s);
            Assert.Equal(after.H, after.V);
            Assert.True(after.Dot);   // drawn as a dot
            Assert.True(SchematicEditing.HasRemovableJunction(s, Cross));

            Assert.Equal(2, SchematicEditing.RemoveJunction(s, Cross));
            var again = Nets(s);
            Assert.NotEqual(again.H, again.V);
            Assert.Equal(2, s.Wires[0].Points.Count);   // the wires are as they were
            Assert.Equal(2, s.Wires[1].Points.Count);
        }

        [Fact]
        public void ATeeStaysJoinedAndOneWireIsNoJunction()
        {
            var s = Crossing();
            s.Wires.Add(new SchematicWire { Points = new List<GridPoint> { new GridPoint(5, 10), new GridPoint(5, 30) } });   // ends on the horizontal wire
            var tee = new GridPoint(5, 10);
            Assert.Equal(s.BuildNetlist(PartLibrary.Default).WireNets[0], s.BuildNetlist(PartLibrary.Default).WireNets[2]);
            Assert.False(SchematicEditing.HasRemovableJunction(s, tee));   // nothing to take away: the T joins by itself

            var lone = new GridPoint(15, 10);
            Assert.False(SchematicEditing.PlaceJunction(s, lone));   // only one wire passes there
            Assert.Equal(2, s.Wires[0].Points.Count);
        }
    }
}
