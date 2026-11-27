using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[table] KiCad's table on the sheet: cells kept when it grows, upright when turned, copied, saved, to KiCad and back.</summary>
    public class SchematicTableTests
    {
        [Fact]
        public void ASheetTableKeepsItsCellsAndStaysUpright()
        {
            var t = SchematicShape.Table(new GridPoint(10, 10), 2, 2, 6, 1.5);
            Assert.Equal(new GridPoint(22, 13), t.Points[1]);
            t.SetCell(0, 0, "Kart");
            t.SetCell(1, 1, "Rev B");
            t.ResizeTable(3, 3);
            Assert.Equal("Kart", t.Cell(0, 0));
            Assert.Equal("Rev B", t.Cell(1, 1));
            Assert.Equal(9, t.Cells!.Count);
            Assert.Equal(new GridPoint(28, 14.5), t.Points[1]);

            SchematicEditing.Rotate(new object[] { t }, new GridPoint(0, 0));
            Assert.Equal(18, t.Points[1].X - t.Points[0].X, 9);   // still 18 wide and 4.5 high
            Assert.Equal(4.5, t.Points[1].Y - t.Points[0].Y, 9);
            SchematicEditing.Mirror(new object[] { t }, new GridPoint(0, 0), true);
            Assert.Equal(18, t.Points[1].X - t.Points[0].X, 9);
        }

        [Fact]
        public void ASheetTableIsCopiedSavedAndGoesToKiCadAndBack()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var b = SchematicEditing.Bounds(SchematicEditing.AllItems(sch), PartLibrary.Default)!.Value;
            var t = SchematicShape.Table(new GridPoint(b.MaxX + 4, b.MinY), 2, 2, 8, 2);
            t.SetCell(0, 0, "MOTOR");
            t.SetCell(0, 1, "12 V");
            t.SetCell(1, 0, "AKIM");
            t.SetCell(1, 1, "2 A");
            sch.Shapes.Add(t);
            Assert.Contains(t, SchematicEditing.ItemsInRect(sch, PartLibrary.Default, t.Bounds(), false));

            var pasted = SchematicFragment.FromItems(new object[] { t }, PartLibrary.Default, "t").PasteInto(sch, PartLibrary.Default, new GridPoint(b.MaxX + 30, b.MaxY));
            var copy = Assert.IsType<SchematicShape>(Assert.Single(pasted.Items));
            Assert.Equal("2 A", copy.Cell(1, 1));
            copy.SetCell(1, 1, "3 A");
            Assert.Equal("2 A", t.Cell(1, 1));   // its own cells, not the original's

            var saved = Schematic.FromJson(sch.ToJson()).Shapes.First(s => s.Kind == SheetShapeKind.Table);
            Assert.Equal(new[] { "MOTOR", "12 V", "AKIM", "2 A" }, saved.Cells);

            string text = KiCadExporter.ExportSchematic(sch, PartLibrary.Default, "table");
            Assert.Contains("(table\n", text.Replace("\r", ""));
            Assert.Contains("(column_count 2)", text);
            Assert.Contains("(table_cell \"MOTOR\"", text);
            Assert.Contains("(column_widths 20.32 20.32)", text);   // 8 grid steps of 2.54 mm
            var back = KiCadSchematicImport.Import(text, PartLibrary.Default).Schematic;
            var again = back.Shapes.First(s => s.Kind == SheetShapeKind.Table && s.Cell(0, 0) == "MOTOR");
            Assert.Equal(2, again.Columns);
            Assert.Equal(2, again.Rows);
            Assert.Equal("2 A", again.Cell(1, 1));
            Assert.True(again.Points[1].X > again.Points[0].X && again.Points[1].Y > again.Points[0].Y);
        }
    }
}
