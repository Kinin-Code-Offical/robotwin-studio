using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[print] The FDM checks on parts whose answers are known by hand.</summary>
    public class PrintCheckTests
    {
        private readonly ITestOutputHelper _out;

        public PrintCheckTests(ITestOutputHelper output) => _out = output;

        private static readonly PrinterProfile Ender = PrinterProfile.Find("ender3");

        [Fact]
        public void ACalibrationCubeIsPrintableAndCostsAFewGrams()
        {
            var r = PrintCheck.Analyze(PrimitiveBuilder.Box(20, 20, 20), Ender, infill: 0.2);
            _out.WriteLine($"küp: kapalı {r.Closed}, sığar {r.FitsBed}, sarkma {r.OverhangArea:0.0} mm², tabla {r.BedContact:0} mm², en ince duvar {r.ThinnestWall:0.0} mm, " +
                           $"{r.FilamentG:0.0} g, {r.FilamentM:0.00} m, {r.Hours * 60:0} dk, {r.Layers} katman");
            Assert.True(r.Printable);
            Assert.Equal(0, r.OverhangArea, 6);
            Assert.Equal(400, r.BedContact, 3);
            Assert.Equal(20, r.ThinnestWall, 3);
            Assert.Equal(100, r.Layers);
            Assert.InRange(r.FilamentG, 2, 5);   // slicers give 3–4 g at 20 % infill
            Assert.InRange(r.Hours * 60, 8, 40);
            Assert.Null(r.BetterDown);
        }

        [Fact]
        public void AMushroomNeedsSupportUnlessItIsTurnedOver()
        {
            // A 10 × 10 column under a 40 × 40 cap: 1500 mm² of cap overhangs, none when the cap lies on the bed.
            var column = PrimitiveBuilder.Box(10, 10, 20);
            var cap = PrimitiveBuilder.Box(40, 40, 5).Translate(new Vec3(0, 0, 11.5));   // 1 mm into the column
            var mushroom = Csg.Union(column, cap);
            var b = mushroom.Bounds;
            mushroom.Translate(new Vec3(0, 0, -b.Min.Z));
            var r = PrintCheck.Analyze(mushroom, Ender);
            _out.WriteLine($"mantar: sarkma {r.OverhangArea:0.0} mm², destek {r.NeedsSupport}, daha iyi yön {r.BetterDown} ({r.BetterOverhang:0.0} mm²)");
            Assert.True(r.Closed);
            Assert.Equal(1500, r.OverhangArea, 1);
            Assert.True(r.NeedsSupport);
            Assert.NotNull(r.BetterDown);
            Assert.Equal(1, r.BetterDown!.Value.Z, 6);   // the cap's top on the bed
            Assert.Equal(0, r.BetterOverhang, 6);
        }

        [Fact]
        public void AWallThinnerThanTwoLinesIsFound()
        {
            var r = PrintCheck.Analyze(PrimitiveBuilder.Box(30, 30, 0.6), Ender);
            _out.WriteLine($"0,6 mm plaka: en ince {r.ThinnestWall:0.00} mm (en az {Ender.MinWall:0.00}), ince alan {r.ThinArea:0} mm²");
            Assert.Equal(0.6, r.ThinnestWall, 3);
            Assert.True(r.ThinArea > 1000);
            Assert.False(r.Printable);
        }

        [Fact]
        public void APartLargerThanTheBedDoesNotFitButTurnedItMight()
        {
            Assert.False(PrintCheck.Analyze(PrimitiveBuilder.Box(300, 20, 20), Ender).FitsBed);
            // 240 × 200 does not fit 220 × 220 either way; 210 × 240 fits a 250 × 210 bed turned.
            Assert.False(PrintCheck.Analyze(PrimitiveBuilder.Box(240, 200, 10), Ender).FitsBed);
            Assert.True(PrintCheck.Analyze(PrimitiveBuilder.Box(210, 240, 10), PrinterProfile.Find("mk4")).FitsBed);
        }

        [Fact]
        public void AnOpenMeshIsNotPrintable()
        {
            var box = PrimitiveBuilder.Box(20, 20, 20);
            box.Faces.RemoveAt(0);
            var r = PrintCheck.Analyze(box, Ender);
            Assert.False(r.Closed);
            Assert.True(r.OpenEdges > 0);
            Assert.False(r.Printable);
        }
    }
}
