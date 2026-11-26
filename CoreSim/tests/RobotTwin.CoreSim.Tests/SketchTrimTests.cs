using System;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sketch-trim] Power trim and extend on sketch lines.</summary>
    public class SketchTrimTests
    {
        private static (ConstrainedSketch S, SketchEntity Line) Across(params double[] cutsAtX)
        {
            var s = new ConstrainedSketch();
            var line = s.AddLine(s.AddPoint(0, 0), s.AddPoint(30, 0));
            foreach (double x in cutsAtX) s.AddLine(s.AddPoint(x, -5), s.AddPoint(x, 5));
            return (s, line);
        }

        private static (double, double) Ends(ConstrainedSketch s, SketchEntity l) => (Math.Round(s.Point(l.Start)!.X, 6), Math.Round(s.Point(l.End)!.X, 6));

        [Fact]
        public void TheClickedPieceGoesAndTheCutEndStaysOnTheCutter()
        {
            var (s, line) = Across(10);
            Assert.Null(SketchTrim.Trim(s, line.Id, 20, 0));
            Assert.Equal((0.0, 10.0), Ends(s, line));
            Assert.Contains(s.Constraints, k => k.Kind == SketchConstraintKind.PointOnLine && k.A == line.End);
            Assert.Equal(2, s.Entities.Count);
            Assert.Equal(4, s.Points.Count);   // the old far end went, the cut point came

            var (m, mid) = Across(10, 20);
            Assert.Null(SketchTrim.Trim(m, mid.Id, 15, 0));   // the middle goes: two pieces left
            var pieces = m.Entities.Where(e => Math.Abs(m.Point(e.Start)!.Y) < 1e-9 && Math.Abs(m.Point(e.End)!.Y) < 1e-9).Select(e => Ends(m, e)).OrderBy(x => x.Item1).ToList();
            Assert.Equal(new[] { (0.0, 10.0), (20.0, 30.0) }, pieces);
        }

        [Fact]
        public void ALineNothingCrossesGoesWhole()
        {
            var (s, line) = Across();
            Assert.Null(SketchTrim.Trim(s, line.Id, 15, 0));
            Assert.Empty(s.Entities);
            Assert.Empty(s.Points);
        }

        [Fact]
        public void TheInsideOfACircleIsCutOut()
        {
            var s = new ConstrainedSketch();
            var line = s.AddLine(s.AddPoint(-20, 0), s.AddPoint(20, 0));
            var ring = s.AddCircle(s.AddPoint(0, 0), 5);
            Assert.Null(SketchTrim.Trim(s, line.Id, 1, 0));
            var ends = s.Entities.Where(e => e.Kind == SketchEntityKind.Line).Select(e => Ends(s, e)).OrderBy(x => x.Item1).ToList();
            Assert.Equal(new[] { (-20.0, -5.0), (5.0, 20.0) }, ends);
            Assert.Equal(2, s.Constraints.Count(k => k.Kind == SketchConstraintKind.PointOnCircle && k.B == ring.Id));
        }

        [Fact]
        public void AFreeEndRunsOnToTheNextEntity()
        {
            var s = new ConstrainedSketch();
            var line = s.AddLine(s.AddPoint(0, 0), s.AddPoint(5, 0));
            s.AddLine(s.AddPoint(10, -5), s.AddPoint(10, 5));
            Assert.Null(SketchTrim.Extend(s, line.Id, 4.5, 0));
            Assert.Equal((0.0, 10.0), Ends(s, line));
            Assert.NotNull(SketchTrim.Extend(s, line.Id, 0.5, 0));   // nothing behind its start
        }
    }
}
