using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[section-props] Section properties against the textbook values of rectangles and holes.</summary>
    public class SectionPropertiesTests
    {
        private static IReadOnlyList<Vec2> Rect(double x0, double y0, double x1, double y1) =>
            new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) };

        [Fact]
        public void ARectangleHasItsTextbookMoments()
        {
            var r = SectionProperties.Of(new[] { Rect(0, 0, 40, 20) });
            Assert.Equal(800, r.Area, 9);
            Assert.Equal(120, r.Perimeter, 9);
            Assert.Equal((20.0, 10.0), (r.CentroidU, r.CentroidV));
            Assert.Equal(40 * Math.Pow(20, 3) / 12, r.Iu, 6);   // b·h³/12 about the horizontal axis
            Assert.Equal(20 * Math.Pow(40, 3) / 12, r.Iv, 6);
            Assert.Equal(0, r.Iuv, 6);
            Assert.Equal(r.Iv, r.I1, 6);
            Assert.Equal(r.Iu / 10, r.Wu, 6);   // I / (h/2)
        }

        [Fact]
        public void PiecesInEitherTurnAddUpAndTheirSeamIsNoEdge()
        {
            // the same rectangle as two triangles, one of them clockwise
            var a = new[] { new Vec2(0, 0), new Vec2(40, 0), new Vec2(40, 20) };
            var b = new[] { new Vec2(0, 0), new Vec2(0, 20), new Vec2(40, 20) };   // clockwise
            var r = SectionProperties.Of(new IReadOnlyList<Vec2>[] { a, b });
            Assert.Equal(800, r.Area, 9);
            Assert.Equal(120, r.Perimeter, 9);
            Assert.Equal(40 * Math.Pow(20, 3) / 12, r.Iu, 6);
        }

        [Fact]
        public void AHoleIsTakenOffAndATurnedSectionHasItsPrincipalAxes()
        {
            var r = SectionProperties.OfProfile(Rect(0, 0, 40, 20), new[] { Rect(15, 5, 25, 15) });
            Assert.Equal(700, r.Area, 9);
            Assert.Equal(40 * Math.Pow(20, 3) / 12 - Math.Pow(10, 4) / 12, r.Iu, 6);
            Assert.Equal(160, r.Perimeter, 9);

            // an L: unequal legs give a product of inertia and turned principal axes
            var l = SectionProperties.Of(new[] { (IReadOnlyList<Vec2>)new[] { new Vec2(0, 0), new Vec2(30, 0), new Vec2(30, 5), new Vec2(5, 5), new Vec2(5, 20), new Vec2(0, 20) } });
            Assert.NotEqual(0, l.Iuv, 3);
            Assert.True(l.I1 > Math.Max(l.Iu, l.Iv) && l.I2 < Math.Min(l.Iu, l.Iv));
            Assert.Equal(l.Iu + l.Iv, l.I1 + l.I2, 6);   // the trace does not turn
        }

        [Fact]
        public void ABoxCutAcrossXIsMeasuredInYAndZ()
        {
            var doc = new ModelDocument();
            var box = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            box.Primitive!.Set("x", 40);
            box.Primitive.Set("y", 20);
            box.Primitive.Set("z", 10);
            var r = SectionProperties.AtPlane(doc, new SectionPlane { Axis = SectionAxis.X, Offset = 0 })!;
            Assert.Equal(("Y", "Z"), (r.U, r.V));
            Assert.Equal(200, r.Area, 6);
            Assert.Equal(20 * Math.Pow(10, 3) / 12, r.Iu, 3);
            Assert.Equal(10 * Math.Pow(20, 3) / 12, r.Iv, 3);
            Assert.Null(SectionProperties.AtPlane(doc, new SectionPlane { Axis = SectionAxis.X, Offset = 500 }));   // past the part
        }
    }
}
