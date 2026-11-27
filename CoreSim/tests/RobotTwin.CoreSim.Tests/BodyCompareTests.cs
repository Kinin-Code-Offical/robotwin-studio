using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[compare] Compare bodies: shared volume and what only one of two bodies has.</summary>
    public class BodyCompareTests
    {
        [Fact]
        public void TheSameBodyIsTheSameAndAShiftedOneShowsBothDifferences()
        {
            var a = PrimitiveBuilder.Box(20, 20, 20);
            var same = ModelCompare.Compare(a, PrimitiveBuilder.Box(20, 20, 20));
            Assert.True(same.Same());
            Assert.Equal(8000, same.Common, 3);

            var moved = ModelCompare.Compare(a, PrimitiveBuilder.Box(20, 20, 20).Translate(new Vec3(5, 0, 0)));
            Assert.Equal(6000, moved.Common, 3);
            Assert.Equal(2000, moved.OnlyA, 3);
            Assert.Equal(2000, moved.OnlyB, 3);
            Assert.Equal(0.4, moved.Difference, 6);
            Assert.False(moved.Same());
        }

        [Fact]
        public void ARevisedPartShowsWhatWasTakenAway()
        {
            var doc = new ModelDocument();
            var old = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            old.Primitive!.Set("x", 30);
            var revised = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            revised.Primitive!.Set("x", 30);
            var pin = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder);
            pin.Primitive!.Set("d", 6);
            pin.Primitive.Set("h", 40);
            pin.Transform.Position = revised.Transform.Position;
            ModelFeatures.AddBoolean(doc, revised, new[] { pin }, BooleanOp.Difference);
            var c = ModelCompare.Compare(doc, old, revised);
            Assert.Equal(c.VolumeA - c.VolumeB, c.OnlyA, 3);   // the hole
            Assert.Equal(0, c.OnlyB, 3);
            Assert.True(c.AreaB > c.AreaA - 1e-6);   // the hole's wall adds surface
        }
    }
}
