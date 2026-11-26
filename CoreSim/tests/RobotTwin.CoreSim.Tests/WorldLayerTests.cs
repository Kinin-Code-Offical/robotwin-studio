using System.Linq;
using System.Text.Json;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[layers] Unity's Tags and Layers: the collision matrix lets one layer's parts pass through another's.</summary>
    public class WorldLayerTests
    {
        /// <summary>A plate fixed 0.3 m up and a box dropped onto it from 0.6 m; where the box is after a second.</summary>
        private static double DropOntoShelf(bool passThrough)
        {
            var w = new WorldDocument();
            var shelf = new WorldObject { Id = w.NextId("O"), Name = "Raf", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.3, SizeY = 0.3, SizeZ = 0.02, IsStatic = true, Pose = new WorldPose { Z = 0.3 } };
            w.Objects.Add(shelf);   // before the next id is drawn
            var box = new WorldObject { Id = w.NextId("O"), Name = "Kutu", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.05, SizeY = 0.05, SizeZ = 0.05, MassKg = 0.2, Pose = new WorldPose { Z = 0.6 } };
            w.Objects.Add(box);
            int ghosts = WorldLayers.Add(w, "Hayalet");
            WorldLayers.SetLayer(box, ghosts);
            if (passThrough) WorldLayers.SetCollide(w, 0, ghosts, false);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            for (int i = 0; i < 1000; i++) p.Advance(1e-3, null);
            return p.PoseOf(box.Id)!.Value.Position.Z;
        }

        [Fact]
        public void APartPassesThroughALayerItDoesNotMeet()
        {
            Assert.InRange(DropOntoShelf(false), 0.31, 0.33);   // rests on the shelf
            Assert.InRange(DropOntoShelf(true), -0.001, 0.01);   // through the shelf onto the floor (the floor always meets)
        }

        [Fact]
        public void TheMatrixIsSymmetricAndOnlyStoresWhatIsOff()
        {
            var w = new WorldDocument();
            int a = WorldLayers.Add(w, "Kablolar"), b = WorldLayers.Add(w, "Sensörler");
            Assert.Equal((1, 2), (a, b));
            Assert.Equal("Kablolar", WorldLayers.NameOf(w, 1));
            Assert.Equal(WorldLayers.DefaultLayer, WorldLayers.NameOf(w, 0));
            Assert.Null(w.LayerIgnores);

            WorldLayers.SetCollide(w, b, a, false);
            Assert.False(WorldLayers.Collide(w, 1, 2));
            Assert.False(WorldLayers.Collide(w, 2, 1));
            var masks = WorldLayers.Masks(w);
            Assert.Equal(0u, masks[1] & (1u << 2));
            Assert.Equal(0u, masks[2] & (1u << 1));
            Assert.Equal(uint.MaxValue, masks[0]);

            var back = JsonSerializer.Deserialize<WorldDocument>(JsonSerializer.Serialize(w))!;
            Assert.False(WorldLayers.Collide(back, 1, 2));
            Assert.Equal("Sensörler", WorldLayers.NameOf(back, 2));

            WorldLayers.SetCollide(w, 1, 2, true);
            Assert.Null(w.LayerIgnores);   // back to nothing stored
        }

        [Fact]
        public void TagsAreListedOnceAndAnEmptyTagIsNone()
        {
            var w = new WorldDocument();
            foreach (var (name, tag) in new[] { ("A", "engel"), ("B", "Engel"), ("C", "hedef"), ("D", " ") })
            {
                var o = new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Body, Shape = "box" };
                WorldLayers.SetTag(o, tag);
                w.Objects.Add(o);
            }

            Assert.Equal(new[] { "engel", "hedef" }, WorldLayers.Tags(w));
            Assert.Null(WorldLayers.TagOf(w.Objects[3]));
            Assert.False(w.Objects[3].Parameters.ContainsKey(WorldLayers.TagKey));
        }
    }
}
