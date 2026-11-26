using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[shapes] KiCad's schematic graphics: lines, rectangles, circles and arcs on the sheet.</summary>
    public class SchematicShapeTests
    {
        private static SchematicShape Rect(double x0, double y0, double x1, double y1) =>
            new SchematicShape { Kind = SheetShapeKind.Rectangle, Points = { new GridPoint(x0, y0), new GridPoint(x1, y1) } };

        [Fact]
        public void AnArcGoesThroughItsMiddlePointTheShortOrTheLongWay()
        {
            // a quarter from (1,0) through (√½,√½) to (0,1) about the origin
            var arc = new SchematicShape { Kind = SheetShapeKind.Arc, Points = { new GridPoint(1, 0), new GridPoint(Math.Sqrt(0.5), Math.Sqrt(0.5)), new GridPoint(0, 1) } };
            var (centre, radius) = arc.ArcCircle()!.Value;
            Assert.Equal(0, centre.X, 9);
            Assert.Equal(0, centre.Y, 9);
            Assert.Equal(1, radius, 9);
            var b = arc.Bounds();
            Assert.Equal(0, b.MinX, 6);
            Assert.Equal(1, b.MaxX, 6);
            Assert.True(b.MinY > -1e-6);   // stays in the quarter it was drawn through

            // the same ends through (−1,0): three quarters round, the long way
            var big = new SchematicShape { Kind = SheetShapeKind.Arc, Points = { new GridPoint(1, 0), new GridPoint(-1, 0), new GridPoint(0, 1) } };
            var bb = big.Bounds();
            Assert.Equal(-1, bb.MinX, 6);
            Assert.Equal(-1, bb.MinY, 2);
        }

        [Fact]
        public void ShapesAreSelectedMovedTurnedAndDeletedWithTheSheet()
        {
            var doc = new Schematic();
            var box = Rect(0, 0, 4, 2);
            var circle = new SchematicShape { Kind = SheetShapeKind.Circle, Points = { new GridPoint(10, 10), new GridPoint(12, 10) } };
            doc.Shapes.Add(box);
            doc.Shapes.Add(circle);
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;

            Assert.Contains(box, SchematicEditing.ItemsInRect(doc, lib, new GridRect(-1, -1, 5, 3), crossing: false));
            Assert.DoesNotContain(circle, SchematicEditing.ItemsInRect(doc, lib, new GridRect(-1, -1, 5, 3), crossing: false));
            Assert.Contains(circle, SchematicEditing.ItemsInRect(doc, lib, new GridRect(11.5, 9, 13, 11), crossing: true));   // its edge crosses

            SchematicEditing.Translate(new object[] { box }, new GridPoint(1, 1));
            Assert.Equal(new GridPoint(1, 1), box.Points[0]);
            SchematicEditing.Rotate(new object[] { circle }, new GridPoint(10, 10));
            Assert.Equal(2, circle.Radius, 9);   // a turn keeps it a circle of its size

            Assert.Equal(1, SchematicEditing.Delete(doc, new object[] { box }));
            Assert.Single(doc.Shapes);
        }

        [Fact]
        public void ShapesGoToKiCadAndComeBack()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var b = SchematicEditing.Bounds(SchematicEditing.AllItems(sch), PartLibrary.Default)!.Value;
            sch.Shapes.Add(new SchematicShape { Kind = SheetShapeKind.Rectangle, Points = { new GridPoint(b.MinX - 2, b.MinY - 2), new GridPoint(b.MaxX + 2, b.MaxY + 2) }, Dashed = true, Width = 0.25 });
            sch.Shapes.Add(new SchematicShape { Kind = SheetShapeKind.Circle, Points = { new GridPoint(b.MaxX + 6, b.MinY), new GridPoint(b.MaxX + 8, b.MinY) }, Filled = true });
            sch.Shapes.Add(new SchematicShape { Kind = SheetShapeKind.Arc, Points = { new GridPoint(b.MaxX + 4, b.MaxY), new GridPoint(b.MaxX + 6, b.MaxY - 2), new GridPoint(b.MaxX + 8, b.MaxY) } });
            sch.Shapes.Add(new SchematicShape { Kind = SheetShapeKind.Line, Points = { new GridPoint(b.MinX, b.MaxY + 4), new GridPoint(b.MaxX, b.MaxY + 4) } });

            string text = KiCadExporter.ExportSchematic(sch, PartLibrary.Default, "shapes");
            Assert.Contains("(rectangle", text);
            Assert.Contains("(circle", text);
            Assert.Contains("(arc", text);
            Assert.Contains("(polyline", text);
            Assert.Contains("(type dash)", text);

            var back = KiCadSchematicImport.Import(text, PartLibrary.Default).Schematic;
            Assert.Equal(new[] { SheetShapeKind.Line, SheetShapeKind.Rectangle, SheetShapeKind.Circle, SheetShapeKind.Arc }, back.Shapes.Select(x => x.Kind).ToArray());
            var frame = back.Shapes.Single(x => x.Kind == SheetShapeKind.Rectangle);
            Assert.True(frame.Dashed);
            Assert.Equal(0.25, frame.Width, 6);
            Assert.True(back.Shapes.Single(x => x.Kind == SheetShapeKind.Circle).Filled);
            // the exporter lays the parts out again (stubs and labels), so only the shapes' own form is compared
            Assert.True(back.Shapes.Single(x => x.Kind == SheetShapeKind.Circle).Radius > 0);
            Assert.NotNull(back.Shapes.Single(x => x.Kind == SheetShapeKind.Arc).ArcCircle());
        }

        [Fact]
        public void ShapesAreCopiedAndPastedAsTheirOwn()
        {
            var doc = new Schematic();
            var line = new SchematicShape { Kind = SheetShapeKind.Line, Points = { new GridPoint(0, 0), new GridPoint(5, 0), new GridPoint(5, 3) }, Dashed = true, Width = 0.3 };
            doc.Shapes.Add(line);
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
            var copy = SchematicFragment.FromItems(new object[] { line }, lib);
            Assert.Equal(1, copy.Count);
            copy.PasteInto(doc, lib, new GridPoint(20, 20));
            Assert.Equal(2, doc.Shapes.Count);
            var pasted = doc.Shapes[1];
            Assert.NotSame(line, pasted);
            Assert.True(pasted.Dashed);
            Assert.Equal(0.3, pasted.Width);
            Assert.Equal(3, pasted.Points.Count);
            Assert.Equal(new GridPoint(0, 0), line.Points[0]);   // the original stays
        }
    }
}
