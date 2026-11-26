using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sketch-mirror] Mirror entities: the copy lies mirrored, shares points on the axis and follows its original.</summary>
    public class SketchMirrorTests
    {
        [Fact]
        public void AHalfProfileBecomesWholeAndTheCopyFollows()
        {
            var s = new ConstrainedSketch();
            // a vertical construction axis at x = 0, and half a roof: from the axis down and out to (20, 0)
            var axisTop = s.AddPoint(0, 30);
            var axisBottom = s.AddPoint(0, -5);
            var axis = s.AddLine(axisBottom, axisTop, construction: true);
            s.Constrain(SketchConstraintKind.Fix, axisTop.Id);
            s.Constrain(SketchConstraintKind.Fix, axisBottom.Id);
            var apex = s.AddPoint(0, 20);
            var eave = s.AddPoint(20, 0);
            var roof = s.AddLine(apex, eave);
            var hole = s.AddCircle(s.AddPoint(8, 5), 2);
            s.Constrain(SketchConstraintKind.PointOnLine, apex.Id, axis.Id);

            var made = new List<string>();
            Assert.Null(SketchMirror.Mirror(s, new[] { roof.Id, hole.Id, axis.Id }, axis.Id, made));
            Assert.Equal(2, made.Count);
            var copy = s.Entity(made[0])!;
            Assert.Equal(apex.Id, copy.Start);   // the apex on the axis is shared, not copied
            var image = s.Point(copy.End)!;
            Assert.Equal((-20.0, 0.0), (Math.Round(image.X, 6), Math.Round(image.Y, 6)));
            var circle = s.Entity(made[1])!;
            Assert.Equal(-8, s.Point(circle.Center)!.X, 6);

            // the original moves: the copy follows, and the copied hole keeps the original's size
            s.Constrain(SketchConstraintKind.Fix, eave.Id);
            s.Constraints.Last().Value = 25;   // the eave to (25, 0)
            s.Constrain(SketchConstraintKind.Diameter, hole.Id, value: 6);
            Assert.True(s.Solve().Solved);
            Assert.Equal(-25, s.Point(copy.End)!.X, 6);
            Assert.Equal(3, circle.Radius, 6);
        }

        [Fact]
        public void TheAxisMustBeALine()
        {
            var s = new ConstrainedSketch();
            var c = s.AddCircle(s.AddPoint(0, 0), 3);
            var l = s.AddLine(s.AddPoint(5, 0), s.AddPoint(5, 5));
            Assert.NotNull(SketchMirror.Mirror(s, new[] { l.Id }, c.Id));
            Assert.Equal(2, s.Entities.Count);
        }
    }
}
