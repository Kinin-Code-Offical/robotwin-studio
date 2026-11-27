using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[history] Editable feature history: booleans and holes stay features, their tools keep their parameters.</summary>
    public class ModelFeatureTests
    {
        private static (ModelDocument Doc, ModelObject Block, ModelObject Pin) BlockAndPin()
        {
            var doc = new ModelDocument();
            var block = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            block.Primitive!.Set("x", 40);
            block.Primitive.Set("y", 40);
            block.Primitive.Set("z", 20);
            block.Transform.Position = new Vec3(0, 0, 10);
            var pin = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder);
            pin.Primitive!.Set("d", 10);
            pin.Primitive.Set("h", 40);
            pin.Primitive.Set("seg", 128);
            pin.Transform.Position = new Vec3(0, 0, 10);
            return (doc, block, pin);
        }

        /// <summary>A 128-sided cylinder's volume (the tool is that polygon, not a true circle).</summary>
        private static double Hole(double d, double h) => 64 * (d / 2) * (d / 2) * Math.Sin(2 * Math.PI / 128) * h;

        private static void Near(double expected, double actual, double tolerance = 0.5) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

        [Fact]
        public void ACutStaysAFeatureWhoseToolKeepsItsSize()
        {
            var (doc, block, pin) = BlockAndPin();
            var made = ModelFeatures.AddBoolean(doc, block, new[] { pin }, BooleanOp.Difference);
            Assert.Single(made);
            Assert.Single(doc.Objects);   // the tool left the model, into the feature
            Assert.NotNull(block.Primitive);   // the block is still a parametric box
            Near(40 * 40 * 20 - Hole(10, 20), block.LocalMesh().Volume());

            made[0].Primitive!.Set("d", 16);   // the hole made wider
            Near(40 * 40 * 20 - Hole(16, 20), block.LocalMesh().Volume());
            block.Primitive!.Set("z", 30);   // the block made taller: the cut still goes through
            Near(40 * 40 * 30 - Hole(16, 30), block.LocalMesh().Volume());
        }

        [Fact]
        public void SuppressedReorderedAndMovedFeaturesRebuildThePart()
        {
            var (doc, block, pin) = BlockAndPin();
            var boss = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder);
            boss.Primitive!.Set("d", 20);
            boss.Primitive.Set("h", 20);
            boss.Primitive.Set("seg", 128);
            boss.Transform.Position = new Vec3(0, 0, 30);   // stands on the block's top
            ModelFeatures.AddBoolean(doc, block, new[] { pin }, BooleanOp.Difference);
            ModelFeatures.AddBoolean(doc, block, new[] { boss }, BooleanOp.Union);
            double cutThenBoss = block.LocalMesh().Volume();   // the boss covers the hole's top
            Assert.Equal(40 * 40 * 20 - Hole(10, 20) + Hole(20, 20), cutThenBoss, 0);
            Assert.True(ModelFeatures.Move(block, 1, -1));   // boss first, then the pin cuts through both
            Near(40 * 40 * 20 - Hole(10, 20) + Hole(20, 20) - Hole(10, 10), block.LocalMesh().Volume());

            block.Features![0].Suppressed = true;   // the boss left out
            Near(40 * 40 * 20 - Hole(10, 20), block.LocalMesh().Volume());

            var p = block.Features[1].Placement;   // the pin moved 8 mm along X: still a whole hole
            block.Features[1].Placement = Affine.Translate(new Vec3(8, 0, 0)) * p;
            Near(40 * 40 * 20 - Hole(10, 20), block.LocalMesh().Volume());
            Assert.Contains(block.LocalMesh().Vertices, v => Math.Abs(v.X - 13) < 0.01);
        }

        [Fact]
        public void AHoleIsAFeatureOfItsSize()
        {
            var doc = new ModelDocument();
            var plate = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            plate.Primitive!.Set("x", 30);
            plate.Primitive.Set("y", 30);
            plate.Primitive.Set("z", 6);
            plate.Transform.Position = new Vec3(0, 0, 3);
            double full = plate.LocalMesh().Volume();
            var f = ModelFeatures.AddHole(doc, plate, new Vec3(0, 0, 6), Vec3.UnitZ, new DrillSpec { Size = "M3" });
            double m3 = full - plate.LocalMesh().Volume();
            Assert.InRange(m3, Hole(3.4, 6) * 0.95, Hole(3.4, 6) * 1.01);
            f.Drill!.Size = "M5";
            double m5 = full - plate.LocalMesh().Volume();
            Assert.InRange(m5, Hole(5.5, 6) * 0.95, Hole(5.5, 6) * 1.01);
        }

        [Fact]
        public void TheHistoryIsSavedCopiedBakedAndAToolGivenBack()
        {
            var (doc, block, pin) = BlockAndPin();
            ModelFeatures.AddBoolean(doc, block, new[] { pin }, BooleanOp.Difference);
            double v = block.LocalMesh().Volume();
            var back = ModelDocument.FromJson(doc.ToJson());
            Assert.Equal(v, back.Objects.Single().LocalMesh().Volume(), 6);
            Assert.Equal(PrimitiveKind.Cylinder, back.Objects.Single().Features!.Single().Primitive!.Kind);

            var copy = block.Clone();
            copy.Features![0].Primitive!.Set("d", 2);
            Assert.Equal(10, block.Features![0].Primitive!.Get("d"));   // a deep copy

            var tool = ModelFeatures.Extract(doc, block, 0)!;
            Assert.Null(block.Features);
            Assert.Equal(40 * 40 * 20, block.LocalMesh().Volume(), 3);
            Assert.Equal(10, doc.WorldMatrix(tool).Apply(Vec3.Zero).Z, 9);   // back where it was

            ModelFeatures.AddBoolean(doc, block, new[] { tool }, BooleanOp.Difference);
            block.SetMesh(block.LocalMesh().Clone());   // baked: one mesh, no history
            Assert.Null(block.Features);
            Assert.Equal(v, block.LocalMesh().Volume(), 6);
        }
    }
}
