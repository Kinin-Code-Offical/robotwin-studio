using System;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [sketch] A dimensioned sketch is solved as a whole: roughly drawn geometry moves onto its dimensions, the freedom
    /// left is counted, repeated and contradicting constraints are named, and the closed loops become a profile.
    /// </summary>
    public class SketchConstraintTests
    {
        private readonly ITestOutputHelper _out;

        public SketchConstraintTests(ITestOutputHelper output) => _out = output;

        private static ConstrainedSketch Rectangle(out System.Collections.Generic.List<SketchEntity> lines)
        {
            var s = new ConstrainedSketch();
            lines = s.AddRectangle(0.4, -0.3, 37.5, 21.2);   // drawn by hand, roughly 40 × 20
            return s;
        }

        [Fact]
        public void ARoughRectangleTakesItsDimensionsAndIsFullyConstrained()
        {
            var s = Rectangle(out var lines);
            var corner = s.Point(lines[0].Start)!;
            s.Constrain(SketchConstraintKind.Fix, corner.Id, null);
            s.Constraints.Last().Value = 0;
            s.Constraints.Last().Value2 = 0;
            s.Constrain(SketchConstraintKind.Length, lines[0].Id, value: 40);
            s.Constrain(SketchConstraintKind.Length, lines[1].Id, value: 20);
            var r = s.Solve();
            _out.WriteLine($"{r.State}, serbestlik {r.Freedom}, hata {r.Residual:G2} mm, {r.Iterations} yineleme; köşeler " +
                           string.Join(" ", s.Points.Select(p => $"({p.X:0.###}, {p.Y:0.###})")));
            Assert.Equal(SketchState.FullyConstrained, r.State);
            Assert.Equal(0, r.Freedom);
            var far = s.Point(lines[1].End)!;
            Assert.Equal(40, far.X, 6);
            Assert.Equal(20, far.Y, 6);
        }

        [Fact]
        public void WithoutDimensionsItKeepsFourFreedomsAndStaysWhereDrawn()
        {
            var s = Rectangle(out _);
            var before = s.Points.Select(p => (p.X, p.Y)).ToList();
            var r = s.Solve();
            Assert.Equal(SketchState.UnderConstrained, r.State);
            Assert.Equal(4, r.Freedom);   // where it is (2), its width and its height
            // Already level and upright: nothing needed to move.
            for (int i = 0; i < before.Count; i++)
            {
                Assert.InRange(Math.Abs(s.Points[i].X - before[i].X), 0, 0.5);
                Assert.InRange(Math.Abs(s.Points[i].Y - before[i].Y), 0, 0.5);
            }
        }

        [Fact]
        public void AConstraintThatRepeatsOthersIsNamed()
        {
            var s = Rectangle(out var lines);
            var extra = s.Constrain(SketchConstraintKind.Parallel, lines[0].Id, lines[2].Id);   // both are already level
            var r = s.Solve();
            _out.WriteLine($"{r.State}: {string.Join(", ", r.Culprits)}");
            Assert.Equal(SketchState.Redundant, r.State);
            Assert.Contains(extra.Id, r.Culprits);
        }

        [Fact]
        public void ContradictingDimensionsAreReportedAndTheDrawingIsLeftAlone()
        {
            var s = Rectangle(out var lines);
            var top = s.Constrain(SketchConstraintKind.Length, lines[0].Id, value: 40);
            var bottom = s.Constrain(SketchConstraintKind.Length, lines[2].Id, value: 30);   // a rectangle's opposite sides
            var before = s.Points.Select(p => (p.X, p.Y)).ToList();
            var r = s.Solve();
            _out.WriteLine($"{r.State}: {string.Join(", ", r.Culprits)}");
            Assert.Equal(SketchState.Conflicting, r.State);
            Assert.Contains(top.Id, r.Culprits);
            Assert.Contains(bottom.Id, r.Culprits);
            Assert.Equal(before[1].X, s.Points[1].X, 9);
        }

        [Fact]
        public void APlateWithAHoleBecomesAProfile()
        {
            var s = Rectangle(out var lines);
            var origin = s.Point(lines[0].Start)!;
            var fix = s.Constrain(SketchConstraintKind.Fix, origin.Id);
            fix.Value = fix.Value2 = 0;
            s.Constrain(SketchConstraintKind.Length, lines[0].Id, value: 40);
            s.Constrain(SketchConstraintKind.Length, lines[1].Id, value: 20);
            var c = s.AddPoint(19, 11);
            var hole = s.AddCircle(c, 4);
            s.Constrain(SketchConstraintKind.HorizontalDistance, origin.Id, c.Id, 20);
            s.Constrain(SketchConstraintKind.VerticalDistance, origin.Id, c.Id, 10);
            s.Constrain(SketchConstraintKind.Diameter, hole.Id, value: 6.4);
            var r = s.Solve();
            Assert.Equal(SketchState.FullyConstrained, r.State);
            var profile = s.ToProfile(128)!;
            double outer = Triangulator.SignedArea(profile.Outer.ToPolygon());
            double holeArea = Triangulator.SignedArea(profile.Holes.Single().ToPolygon(128));
            _out.WriteLine($"dış {outer:0.00} mm², delik {holeArea:0.00} mm²");
            Assert.Equal(800, outer, 6);
            Assert.InRange(holeArea, Math.PI * 3.2 * 3.2 * 0.99, Math.PI * 3.2 * 3.2);
        }

        [Fact]
        public void ALineTangentToACircleSitsAtItsRadius()
        {
            var s = new ConstrainedSketch();
            var c = s.AddPoint(0, 0);
            var circle = s.AddCircle(c, 9);
            s.Constrain(SketchConstraintKind.Fix, c.Id);
            s.Constrain(SketchConstraintKind.Radius, circle.Id, value: 10);
            var a = s.AddPoint(-20, 12);
            var b = s.AddPoint(20, 13);
            var line = s.AddLine(a, b);
            s.Constrain(SketchConstraintKind.Horizontal, line.Id);
            // The circle is to the right of the line's direction (below it): Value −1.
            s.Constrain(SketchConstraintKind.Tangent, line.Id, circle.Id, -1);
            var r = s.Solve();
            _out.WriteLine($"{r.State}, çizgi y = {a.Y:0.000} / {b.Y:0.000}");
            Assert.True(r.Solved);
            Assert.Equal(10, a.Y, 6);
            Assert.Equal(10, b.Y, 6);
        }

        [Fact]
        public void AnAngleBetweenTwoLinesIsHeld()
        {
            var s = new ConstrainedSketch();
            var o = s.AddPoint(0, 0);
            var p = s.AddPoint(30, 0);
            var q = s.AddPoint(20, 15);
            var l1 = s.AddLine(o, p);
            var l2 = s.AddLine(o, q);
            s.Constrain(SketchConstraintKind.Fix, o.Id);
            s.Constrain(SketchConstraintKind.Horizontal, l1.Id);
            s.Constrain(SketchConstraintKind.Angle, l1.Id, l2.Id, 30);
            s.Constrain(SketchConstraintKind.Length, l2.Id, value: 25);
            var r = s.Solve();
            double angle = Math.Atan2(q.Y, q.X) * 180 / Math.PI;
            _out.WriteLine($"{r.State}, açı {angle:0.0000}°, boy {Math.Sqrt(q.X * q.X + q.Y * q.Y):0.0000}");
            Assert.True(r.Solved);
            Assert.Equal(30, angle, 5);
            Assert.Equal(1, r.Freedom);   // the first line's length is free
        }

        [Fact]
        public void AShapeSketchBecomesAFullyDimensionedOneAndBack()
        {
            var shapes = new Sketch2D { Outer = SketchShape.Rect(40, 24) };
            shapes.Holes.Add(SketchShape.Circle(5, 10, 0));
            var s = ConstrainedSketch.From(shapes);
            var r = s.Solve();
            Assert.Equal(SketchState.FullyConstrained, r.State);
            // A driving dimension changed: the plate grows from its fixed corner, the hole keeps its place from it.
            s.Dimensions.First(d => d.Kind == SketchConstraintKind.Length).Value = 60;
            Assert.Equal(SketchState.FullyConstrained, s.Solve().State);
            var profile = s.ToProfile(64)!;
            Assert.Equal(60 * 24, Triangulator.SignedArea(profile.Outer.ToPolygon()), 6);
            var hole = profile.Holes.Single().ToPolygon(64);
            Assert.Equal(10, hole.Average(p => p.X), 6);
            Assert.Equal(0, hole.Average(p => p.Y), 6);
        }

        [Fact]
        public void ABracketWithATangentRoundEndIsFullyConstrained()
        {
            var s = ConstrainedSketch.Bracket();
            var r = s.Solve();
            var end = s.Entities.Single(e => e.Kind == SketchEntityKind.Arc);
            var c = s.Point(end.Center)!;
            _out.WriteLine($"{r.State}, {r.Iterations} yineleme; yay merkezi ({c.X:0.######}, {c.Y:0.######}) R {end.Radius:0.######}");
            Assert.Equal(SketchState.FullyConstrained, r.State);
            Assert.Equal(50, c.X, 6);
            Assert.Equal(20, c.Y, 6);
            Assert.Equal(20, end.Radius, 6);
            var profile = s.ToProfile(256)!;
            double area = Triangulator.SignedArea(profile.Outer.ToPolygon(256));
            Assert.InRange(area, 2000 + Math.PI * 200 * 0.999, 2000 + Math.PI * 200);
            Assert.Equal(2, profile.Holes.Count);
        }

        [Fact]
        public void ItTravelsInTheModelFile()
        {
            var shapes = new Sketch2D { Outer = SketchShape.Rect(40, 24) };
            shapes.Constrained = ConstrainedSketch.From(shapes);
            var options = RobotTwin.CoreSim.Formats.RobotWinFormats.Json;
            string json = System.Text.Json.JsonSerializer.Serialize(shapes, options);
            var back = System.Text.Json.JsonSerializer.Deserialize<Sketch2D>(json, options)!;
            Assert.Equal(shapes.Constrained.Constraints.Count, back.Constrained!.Constraints.Count);
            Assert.DoesNotContain("\"dimensions\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(SketchState.FullyConstrained, back.Constrained.Solve().State);
            Assert.True(back.ApplyConstrained());
            Assert.Equal(960, Triangulator.SignedArea(back.Outer.ToPolygon()), 6);
        }

        [Fact]
        public void ASlotOfTwoArcsAndTwoLinesIsOneLoop()
        {
            // A 30 × 10 slot: two half circles joined by two lines, the ends shared.
            var s = new ConstrainedSketch();
            var c1 = s.AddPoint(0, 0);
            var c2 = s.AddPoint(20, 0);
            var a1 = s.AddPoint(0, -5);
            var a2 = s.AddPoint(20, -5);
            var b2 = s.AddPoint(20, 5);
            var b1 = s.AddPoint(0, 5);
            s.AddLine(a1, a2);
            s.AddArc(c2, a2, b2);
            s.AddLine(b2, b1);
            s.AddArc(c1, b1, a1);
            var loops = s.Loops(64);
            Assert.Single(loops);
            double area = Triangulator.SignedArea(loops[0]);
            _out.WriteLine($"oluk alanı {area:0.00} mm² (kuram {20 * 10 + Math.PI * 25:0.00})");
            Assert.InRange(area, (200 + Math.PI * 25) * 0.995, 200 + Math.PI * 25);
        }
    }
}
