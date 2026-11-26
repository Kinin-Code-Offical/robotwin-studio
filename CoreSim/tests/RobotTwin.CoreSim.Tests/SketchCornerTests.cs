using System;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sketch-fillet] Sketch fillet and chamfer: the corner cut back, the profile follows, the dimensions still drive it.</summary>
    public class SketchCornerTests
    {
        /// <summary>A fully dimensioned 40 × 20 rectangle; its corners P1 (0,0) … P3 (40,20).</summary>
        private static ConstrainedSketch Plate()
        {
            var s = new ConstrainedSketch();
            var lines = s.AddRectangle(0, 0, 40, 20);
            s.Constrain(SketchConstraintKind.Fix, s.Points[0].Id);
            s.Constrain(SketchConstraintKind.Length, lines[0].Id, value: 40);
            s.Constrain(SketchConstraintKind.Length, lines[1].Id, value: 20);
            Assert.Equal(SketchState.FullyConstrained, s.Solve().State);
            return s;
        }

        private static double Area(ConstrainedSketch s)
        {
            var loop = s.Loops(256).OrderByDescending(l => l.Count).First();
            double a = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                var p = loop[i];
                var q = loop[(i + 1) % loop.Count];
                a += p.X * q.Y - q.X * p.Y;
            }

            return Math.Abs(a) / 2;
        }

        [Fact]
        public void AFilletRoundsTheCornerAndTheDimensionsStillDrive()
        {
            var s = Plate();
            string corner = s.Points[2].Id;   // (40, 20)
            Assert.Null(SketchCorners.Fillet(s, corner, 5));
            var solved = s.Solve();
            Assert.Equal(SketchState.FullyConstrained, solved.State);
            Assert.Equal(800 - (25 - Math.PI * 25 / 4), Area(s), 1);   // the square corner less its quarter circle

            // the plate's length still drives it, the fillet goes with the corner
            s.Constraints.First(c => c.Kind == SketchConstraintKind.Length && c.Value == 40).Value = 50;
            Assert.True(s.Solve().Solved);
            Assert.Equal(1000 - (25 - Math.PI * 25 / 4), Area(s), 1);
            Assert.Contains(s.Entities, e => e.Kind == SketchEntityKind.Arc);
        }

        [Fact]
        public void AChamferCutsAStraightEdge()
        {
            var s = Plate();
            Assert.Null(SketchCorners.Chamfer(s, s.Points[1].Id, 5));   // (40, 0)
            Assert.Equal(SketchState.FullyConstrained, s.Solve().State);
            Assert.Equal(800 - 12.5, Area(s), 3);
        }

        [Fact]
        public void ACornerThatCannotBeCutIsLeftAlone()
        {
            var s = Plate();
            int before = s.Entities.Count;
            Assert.NotNull(SketchCorners.Fillet(s, s.Points[2].Id, 30));   // the 20 mm side is too short
            Assert.NotNull(SketchCorners.Chamfer(s, "nope", 2));
            Assert.Equal(before, s.Entities.Count);
            Assert.Equal(SketchState.FullyConstrained, s.Solve().State);
        }
    }
}
