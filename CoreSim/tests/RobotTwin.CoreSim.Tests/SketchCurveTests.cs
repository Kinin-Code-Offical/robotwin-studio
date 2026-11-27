using System;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[curves] Ellipses and splines in the dimensioned sketch: solved, made into profiles, mirrored, removed.</summary>
    public class SketchCurveTests
    {
        private static double Area(ConstrainedSketch s) => Math.Abs(Triangulator.SignedArea(s.Loops()[0]));

        [Fact]
        public void AnEllipseIsAProfileOfItsOwnTurnedWithItsMajorAxis()
        {
            var s = new ConstrainedSketch();
            var c = s.AddPoint(0, 0);
            var e = s.AddEllipse(c, s.AddPoint(10, 0), 5);
            Assert.True(s.Solve().Solved);
            Assert.InRange(Area(s), Math.PI * 50 * 0.995, Math.PI * 50);   // π·a·b, a little less as a polygon
            Assert.NotNull(s.ToProfile());

            // its half minor axis is a dimension like a radius; the major axis follows its end point
            s.Constrain(SketchConstraintKind.Radius, e.Id, null, 3);
            Assert.True(s.Solve().Solved);
            Assert.Equal(3, e.Radius, 6);
            var turned = new ConstrainedSketch();
            turned.AddEllipse(turned.AddPoint(0, 0), turned.AddPoint(6, 8), 3);
            Assert.InRange(Area(turned), Math.PI * 30 * 0.995, Math.PI * 30);
            var far = turned.Curve(turned.Entities[0]).OrderByDescending(p => p.Length).First();
            Assert.InRange(far.Length, 9.99, 10.0001);
            Assert.InRange(Math.Abs(far.Y / far.X), 8.0 / 6 - 0.02, 8.0 / 6 + 0.02);   // along (6, 8)
        }

        [Fact]
        public void AnOpenSplineClosesWithALineAndPassesThroughItsPoints()
        {
            var s = new ConstrainedSketch();
            var a = s.AddPoint(0, 0);
            var top = s.AddPoint(10, 5);
            var b = s.AddPoint(20, 0);
            var spline = s.AddSpline(new[] { a, top, b });
            s.AddLine(b, a);
            Assert.True(s.Solve().Solved);
            var loop = s.Loops()[0];
            Assert.InRange(Area(s), 50, 90);   // fuller than the triangle through the same points
            Assert.Contains(loop, p => Math.Abs(p.X - 10) < 1e-9 && Math.Abs(p.Y - 5) < 1e-9);

            // moving a point moves the curve; removing it removes the spline
            top.Y = 8;
            Assert.Contains(s.Loops()[0], p => Math.Abs(p.Y - 8) < 1e-9);
            var copy = s.Clone();
            Assert.NotSame(spline.Through, copy.Entity(spline.Id)!.Through);
            s.Remove(top.Id);
            Assert.DoesNotContain(s.Entities, x => x.Kind == SketchEntityKind.Spline);
        }

        [Fact]
        public void AClosedSplineIsALoopAndCurvesAreMirrored()
        {
            var s = new ConstrainedSketch();
            var p = new[] { s.AddPoint(0, 0), s.AddPoint(10, 0), s.AddPoint(10, 10), s.AddPoint(0, 10) };
            s.AddSpline(new[] { p[0], p[1], p[2], p[3], p[0] });
            Assert.True(s.Solve().Solved);
            Assert.InRange(Area(s), 100, 140);   // the square's corners rounded outward

            var m = new ConstrainedSketch();
            var axis = m.AddLine(m.AddPoint(30, -10), m.AddPoint(30, 30), true);
            var oval = m.AddEllipse(m.AddPoint(20, 0), m.AddPoint(26, 0), 2);
            var wave = m.AddSpline(new[] { m.AddPoint(10, 10), m.AddPoint(15, 14), m.AddPoint(20, 10) });
            Assert.Null(SketchMirror.Mirror(m, new[] { oval.Id, wave.Id }, axis.Id));
            Assert.Equal(2, m.Entities.Count(x => x.Kind == SketchEntityKind.Ellipse));
            var mirrored = m.Entities.Last(x => x.Kind == SketchEntityKind.Spline);
            Assert.Equal(3, mirrored.Through!.Count);
            Assert.Contains(m.Curve(mirrored), q => Math.Abs(q.X - 45) < 1e-6 && Math.Abs(q.Y - 14) < 1e-6);   // 15 mirrored across x = 30
        }
    }
}
