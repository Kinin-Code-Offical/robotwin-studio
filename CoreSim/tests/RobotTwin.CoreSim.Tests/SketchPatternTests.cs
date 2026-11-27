using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sketch-pattern] Linear and circular sketch patterns: copies in place, held by dimensions, following the original.</summary>
    public class SketchPatternTests
    {
        [Fact]
        public void ARowOfHolesFollowsTheFirst()
        {
            var s = new ConstrainedSketch();
            var centre = s.AddPoint(0, 0);
            var hole = s.AddCircle(centre, 3);
            var made = new List<string>();
            Assert.Null(SketchPattern.Linear(s, new[] { hole.Id }, 4, 10, 0, made));
            Assert.Equal(3, made.Count);
            Assert.Equal(new[] { 10.0, 20.0, 30.0 }, made.Select(id => Math.Round(s.Point(s.Entity(id)!.Center)!.X, 6)));

            // the first hole grows and moves: the others keep their size and their 10 mm pitch from it
            var origin = s.AddPoint(-5, 0);
            s.Constrain(SketchConstraintKind.Fix, origin.Id);
            s.Constrain(SketchConstraintKind.HorizontalDistance, origin.Id, centre.Id, 10);
            s.Constrain(SketchConstraintKind.VerticalDistance, origin.Id, centre.Id, 2);
            s.Constrain(SketchConstraintKind.Diameter, hole.Id, value: 8);
            Assert.True(s.Solve().Solved);
            for (int i = 0; i < 3; i++)
            {
                var c = s.Entity(made[i])!;
                Assert.Equal(5 + 10 * (i + 1), s.Point(c.Center)!.X, 6);
                Assert.Equal(2, s.Point(c.Center)!.Y, 6);
                Assert.Equal(4, c.Radius, 6);
            }
        }

        [Fact]
        public void ACopiedOutlineKeepsItsCornersJoined()
        {
            var s = new ConstrainedSketch();
            var a = s.AddPoint(0, 0);
            var b = s.AddPoint(0, 5);
            var c = s.AddPoint(5, 5);
            var up = s.AddLine(a, b);
            var across = s.AddLine(b, c);
            var made = new List<string>();
            Assert.Null(SketchPattern.Linear(s, new[] { up.Id, across.Id }, 3, 0, -8, made));
            Assert.Equal(4, made.Count);
            Assert.Equal(3 + 2 * 3, s.Points.Count);   // each copy has its own three corners, shared by its two lines
            for (int i = 0; i < 2; i++)
            {
                Assert.Equal(s.Entity(made[2 * i])!.End, s.Entity(made[2 * i + 1])!.Start);
                Assert.Equal(-8 * (i + 1), s.Point(s.Entity(made[2 * i])!.Start)!.Y, 6);
            }
        }

        [Fact]
        public void BoltHolesRoundACentreFollowTheFirst()
        {
            var s = new ConstrainedSketch();
            var centre = s.AddPoint(0, 0);
            s.Constrain(SketchConstraintKind.Fix, centre.Id);
            var first = s.AddPoint(10, 0);
            var hole = s.AddCircle(first, 2);
            var made = new List<string>();
            Assert.Null(SketchPattern.Circular(s, new[] { hole.Id }, centre.Id, 6, 360, made));
            Assert.Equal(5, made.Count);
            for (int i = 0; i < 5; i++)
            {
                var p = s.Point(s.Entity(made[i])!.Center)!;
                double a = 60 * (i + 1) * Math.PI / 180;
                Assert.Equal(10 * Math.Cos(a), p.X, 6);
                Assert.Equal(10 * Math.Sin(a), p.Y, 6);
            }

            // the first hole moves out to a 15 mm bolt circle: every copy follows round the centre
            s.Constrain(SketchConstraintKind.HorizontalDistance, centre.Id, first.Id, 15);
            s.Constrain(SketchConstraintKind.VerticalDistance, centre.Id, first.Id, 0);
            Assert.True(s.Solve().Solved);
            for (int i = 0; i < 5; i++)
            {
                var p = s.Point(s.Entity(made[i])!.Center)!;
                double a = 60 * (i + 1) * Math.PI / 180;
                Assert.Equal(15 * Math.Cos(a), p.X, 5);
                Assert.Equal(15 * Math.Sin(a), p.Y, 5);
                Assert.Equal(2, s.Entity(made[i])!.Radius, 6);
            }
        }

        [Fact]
        public void LessThanAFullTurnSpreadsFromFirstToLast()
        {
            var s = new ConstrainedSketch();
            var centre = s.AddPoint(0, 0);
            var line = s.AddLine(s.AddPoint(5, 0), s.AddPoint(10, 0));
            var made = new List<string>();
            Assert.Null(SketchPattern.Circular(s, new[] { line.Id }, centre.Id, 3, 90, made));
            var last = s.Entity(made[1])!;
            Assert.Equal((0.0, 10.0), (Math.Round(s.Point(last.End)!.X, 6), Math.Round(s.Point(last.End)!.Y, 6)));
            var mid = s.Point(s.Entity(made[0])!.Start)!;
            Assert.Equal(5 / Math.Sqrt(2), mid.X, 6);
            Assert.Equal(5 / Math.Sqrt(2), mid.Y, 6);
        }

        [Fact]
        public void BadRequestsChangeNothing()
        {
            var s = new ConstrainedSketch();
            var hole = s.AddCircle(s.AddPoint(3, 0), 1);
            Assert.NotNull(SketchPattern.Linear(s, new[] { hole.Id }, 1, 5, 0));
            Assert.NotNull(SketchPattern.Linear(s, Array.Empty<string>(), 3, 5, 0));
            Assert.NotNull(SketchPattern.Circular(s, new[] { hole.Id }, "yok", 4, 360));
            Assert.Single(s.Entities);
            Assert.Single(s.Points);
        }
    }
}
