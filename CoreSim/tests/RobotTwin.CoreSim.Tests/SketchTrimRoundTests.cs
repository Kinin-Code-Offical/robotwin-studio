using System;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sketch-trim] Power trim on circles and arcs: a circle becomes the arc left, an arc is shortened or split.</summary>
    public class SketchTrimRoundTests
    {
        [Fact]
        public void ACircleCutTwiceBecomesTheArcThatIsLeft()
        {
            var s = new ConstrainedSketch();
            var o = s.AddPoint(0, 0);
            var circle = s.AddCircle(o, 10);
            s.Constrain(SketchConstraintKind.Diameter, circle.Id, value: 20);
            s.AddLine(s.AddPoint(-20, 0), s.AddPoint(20, 0));
            Assert.Null(SketchTrim.Trim(s, circle.Id, 0, 10));   // the top half goes
            Assert.Equal(SketchEntityKind.Arc, circle.Kind);
            var start = s.Point(circle.Start)!;
            var end = s.Point(circle.End)!;
            Assert.Equal(-10, start.X, 6);
            Assert.Equal(10, end.X, 6);   // from the left crossing round the bottom to the right one
            Assert.Contains(s.Constraints, k => k.Kind == SketchConstraintKind.Diameter && k.A == circle.Id);   // its size stays with it
        }

        [Fact]
        public void AnArcIsSplitOrShortenedBetweenItsCrossings()
        {
            var s = new ConstrainedSketch();
            var o = s.AddPoint(0, 0);
            var arc = s.AddArc(o, s.AddPoint(10, 0), s.AddPoint(-10, 0));   // the upper half
            s.AddLine(s.AddPoint(0, -20), s.AddPoint(0, 20));   // crosses it at 90°
            s.AddLine(s.AddPoint(5, -20), s.AddPoint(5, 20));   // and at 60°
            Assert.Null(SketchTrim.Trim(s, arc.Id, 10 * Math.Cos(75 * Math.PI / 180), 10 * Math.Sin(75 * Math.PI / 180)));
            var arcs = s.Entities.Where(e => e.Kind == SketchEntityKind.Arc).ToList();
            Assert.Equal(2, arcs.Count);
            Assert.Equal(5, s.Point(arc.End)!.X, 6);   // kept 0° … 60°
            Assert.Equal(0, s.Point(arcs[1].Start)!.X, 6);   // and 90° … 180°
            Assert.Contains(s.Constraints, k => k.Kind == SketchConstraintKind.Equal);

            var t = new ConstrainedSketch();
            var c = t.AddPoint(0, 0);
            var half = t.AddArc(c, t.AddPoint(10, 0), t.AddPoint(-10, 0));
            t.AddLine(t.AddPoint(0, -20), t.AddPoint(0, 20));
            Assert.Null(SketchTrim.Trim(t, half.Id, -7, 7));   // the end's piece goes: 0° … 90° stays
            Assert.Equal(0, t.Point(half.End)!.X, 6);
            Assert.Equal(10, t.Point(half.End)!.Y, 6);
        }

        [Fact]
        public void ACircleCutOnceIsRefusedAndOneUncutGoesWhole()
        {
            var s = new ConstrainedSketch();
            var circle = s.AddCircle(s.AddPoint(0, 0), 10);
            s.AddLine(s.AddPoint(5, 0), s.AddPoint(20, 0));   // meets it once
            Assert.NotNull(SketchTrim.Trim(s, circle.Id, 0, 10));
            Assert.Equal(SketchEntityKind.Circle, circle.Kind);

            var t = new ConstrainedSketch();
            var lone = t.AddCircle(t.AddPoint(0, 0), 4);
            Assert.Null(SketchTrim.Trim(t, lone.Id, 0, 4));
            Assert.Empty(t.Entities);
            Assert.Empty(t.Points);
        }
    }
}
