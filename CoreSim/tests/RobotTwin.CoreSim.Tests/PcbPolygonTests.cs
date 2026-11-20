using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Design")]
    public class PcbPolygonTests
    {
        private static List<PcbPoint> Sq(double x, double y, double s) => Shapes.Rect(x, y, x + s, y + s);

        private static double Area(IEnumerable<PolyRegion> r) => r.Sum(x => x.Area);

        [Fact]
        public void UnionOfOverlappingSquaresHasTheRightArea()
        {
            var r = PolyBool.Union(new[] { Sq(0, 0, 10), Sq(5, 5, 10) });
            var region = Assert.Single(r);
            Assert.Equal(175, region.Area, 6);
            Assert.Empty(region.Holes);
            Assert.True(region.Contains(1, 1));
            Assert.True(region.Contains(14, 14));
            Assert.False(region.Contains(14, 1));
        }

        [Fact]
        public void DifferenceMakesAHoleAndTouchingRegionsStaySeparate()
        {
            var r = PolyBool.Difference(PolyBool.Union(new[] { Sq(0, 0, 10) }), new[] { Sq(3, 3, 4) });
            var region = Assert.Single(r);
            Assert.Single(region.Holes);
            Assert.Equal(84, region.Area, 6);
            Assert.False(region.Contains(5, 5));
            Assert.True(region.Contains(1, 5));

            // Edge-touching squares merge, corner-touching ones are two islands.
            Assert.Single(PolyBool.Union(new[] { Sq(0, 0, 1), Sq(1, 0, 1) }));
            Assert.Equal(2, PolyBool.Union(new[] { Sq(0, 0, 1), Sq(1, 1, 1) }).Count);
            Assert.Equal(2, Area(PolyBool.Union(new[] { Sq(0, 0, 1), Sq(1, 1, 1) })), 9);
        }

        [Fact]
        public void IntersectionAndCrossingEdges()
        {
            var a = PolyBool.Union(new[] { Sq(0, 0, 10) });
            var r = PolyBool.Intersection(a, new[] { Shapes.Circle(10, 10, 4) });
            Assert.Equal(Math.PI * 16 / 4, Area(r), 1);

            // A bow-tie: two triangles meeting at a crossing point.
            var bow = new List<PcbPoint> { new PcbPoint(0, 0), new PcbPoint(4, 4), new PcbPoint(4, 0), new PcbPoint(0, 4) };
            var u = PolyBool.Union(new[] { bow }, PolyFillRule.NonZero);
            Assert.Equal(2, u.Count);
            Assert.Equal(8, Area(u), 6);
        }

        [Fact]
        public void ManyOverlappingCirclesUnionToAnAnnulus()
        {
            var circles = Enumerable.Range(0, 36)
                .Select(i => Shapes.Circle(20 + 10 * Math.Cos(i * Math.PI / 18), 20 + 10 * Math.Sin(i * Math.PI / 18), 2.5))
                .ToList();
            var r = PolyBool.Union(circles);
            var ring = Assert.Single(r);
            Assert.Single(ring.Holes);
            Assert.True(ring.Contains(30, 20));
            Assert.False(ring.Contains(20, 20));
            // Roughly an annulus between radii 7.5 and 12.5.
            Assert.InRange(ring.Area, Math.PI * (12.5 * 12.5 - 7.5 * 7.5) * 0.93, Math.PI * (12.5 * 12.5 - 7.5 * 7.5) * 1.02);
        }

        [Fact]
        public void OffsettingGrowsWithRoundCornersAndShrinksWithSharpOnes()
        {
            var sq = PolyBool.Union(new[] { Sq(0, 0, 10) });
            var grown = Assert.Single(PolyOffset.Offset(sq, 1.0));
            Assert.Equal(100 + 40 + Math.PI, grown.Area, 1);
            var b = grown.Bounds();
            Assert.Equal(-1, b.MinX, 2);
            Assert.Equal(11, b.MaxY, 2);

            var shrunk = Assert.Single(PolyOffset.Offset(sq, -1.0));
            Assert.Equal(64, shrunk.Area, 6);

            // Shrinking a thin bar by more than half its width removes it.
            Assert.Empty(PolyOffset.Offset(PolyBool.Union(new[] { Shapes.Rect(0, 0, 10, 0.4) }), -0.25));

            // A hole shrinks when the region grows.
            var withHole = PolyBool.Difference(sq, new[] { Sq(3, 3, 4) });
            var g = Assert.Single(PolyOffset.Offset(withHole, 0.5));
            var hole = Assert.Single(g.Holes);
            Assert.Equal(9, -Polygon.Area(hole), 6);

            // Opening (shrink then grow) removes a neck narrower than the width.
            var neck = PolyBool.Union(new[] { Sq(0, 0, 4), Sq(8, 0, 4), Shapes.Rect(4, 1.9, 8, 2.1) });
            Assert.Single(neck);
            var opened = PolyOffset.Offset(PolyOffset.Offset(neck, -0.25), 0.25);
            Assert.Equal(2, opened.Count);
        }

        [Fact]
        public void FractureLinksHolesIntoOneContourWithTheSameArea()
        {
            var plate = PolyBool.Difference(PolyBool.Union(new[] { Sq(0, 0, 20) }), new[] { Sq(2, 2, 3), Sq(10, 4, 3), Shapes.Circle(6, 14, 2) });
            var region = Assert.Single(plate);
            Assert.Equal(3, region.Holes.Count);
            var single = Polygon.Fracture(region);
            Assert.Equal(region.Area, Polygon.Area(single), 6);
            Assert.True(Polygon.Winding(single, 1, 1) != 0);
            Assert.True(Polygon.Winding(single, 3.5, 3.5) == 0);
            Assert.True(Polygon.Winding(single, 6, 14) == 0);
        }

        [Fact]
        public void StadiumAndPadShapesContainTheTrueGeometry()
        {
            var s = Shapes.Stadium(0, 0, 10, 0, 1);
            foreach (double a in Enumerable.Range(0, 64).Select(i => i * Math.PI / 32))
            {
                Assert.True(Polygon.Winding(s, 10 + 0.999 * Math.Cos(a), 0.999 * Math.Sin(a)) != 0);
            }

            var pad = Shapes.Pad(PadShape.Rect, 0, 0, 2, 1, 0.25, 0.2);
            Assert.True(Polygon.Area(pad) > 0);
            Assert.True(Polygon.Winding(pad, 1.19, 0) != 0);
            Assert.True(Polygon.Winding(pad, 1.21, 0) == 0);
        }
    }
}
