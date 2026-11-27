using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[table] KiCad's table on the board: ruled cells of text, on the legend, kept when rows and columns change, written to KiCad.</summary>
    public class BoardTableTests
    {
        [Fact]
        public void ATableIsRuledAndItsCellsAreWritten()
        {
            var t = BoardGraphic.Table(10, 20, 2, 3, 12, 3);
            Assert.True(t.IsComplete);
            Assert.Equal((10.0, 20.0, 46.0, 26.0), t.Bounds());
            t.SetCell(0, 0, "REV");
            t.SetCell(0, 1, "B");
            t.SetCell(1, 2, "2026");
            var strokes = t.Strokes();
            // the frame, two column rules and a row rule
            Assert.Contains(strokes, s => s.X1 == 22 && s.X2 == 22 && s.Y1 == 20 && s.Y2 == 26);
            Assert.Contains(strokes, s => s.X1 == 34 && s.X2 == 34);
            Assert.Contains(strokes, s => s.Y1 == 23 && s.Y2 == 23 && s.X1 == 10 && s.X2 == 46);
            // the text inside its cell
            Assert.Contains(strokes, s => s.X1 > 10 && s.X1 < 22 && s.Y1 > 20 && s.Y1 < 23 && !(s.X1 == s.X2 && (s.X1 == 10 || s.X1 == 22)));
            Assert.Contains(strokes, s => s.X1 > 34 && s.X1 < 46 && s.Y1 > 23 && s.Y1 < 26);
            Assert.Equal(0, t.DistanceTo(30, 22), 9);   // anywhere on it picks it
            Assert.StartsWith("tablo 2 × 3", t.Describe());

            // on the bottom silk it reads backwards, within the same frame
            t.Layer = GraphicLayer.SilkBottom;
            Assert.Contains(t.Strokes(), s => s.X1 > 34 && s.X1 < 46 && s.Y1 > 20 && s.Y1 < 23);
        }

        [Fact]
        public void RowsAndColumnsComeAndGoKeepingTheText()
        {
            var t = BoardGraphic.Table(0, 0, 2, 2);
            t.SetCell(0, 0, "A");
            t.SetCell(1, 1, "D");
            t.ColumnWidths![1] = 20;
            t.ResizeTable(3, 3);
            Assert.Equal(9, t.Cells!.Count);
            Assert.Equal("A", t.Cell(0, 0));
            Assert.Equal("D", t.Cell(1, 1));
            Assert.Equal(20, t.ColumnWidths[2]);   // a new column as wide as the last
            Assert.Equal(12 + 20 + 20, t.Points[1].X, 9);
            t.ResizeTable(1, 1);
            Assert.Equal(new[] { "A" }, t.Cells);
            t.Translate(5, 5);
            Assert.Equal((5.0, 5.0, 17.0, 8.0), t.Bounds());
        }

        [Fact]
        public void ATableGoesOnTheLegendAndToKiCad()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            var t = BoardGraphic.Table(30, 35, 2, 2, 15, 4);
            t.SetCell(0, 0, "MOTOR");
            t.SetCell(0, 1, "12 V");
            board.Graphics.Add(t);

            var silk = GerberExporter.SilkStrokes(board, CopperLayer.Top);
            Assert.True(silk.Count(s => s.Stroke.X1 >= 30 && s.Stroke.X2 <= 60 && s.Stroke.Y1 >= 35 && s.Stroke.Y1 <= 43) > 10);
            string pcb = KiCadExporter.ExportPcb(board, "table");
            Assert.Contains("(table", pcb);
            Assert.Contains("(column_count 2)", pcb);
            Assert.Contains("(table_cell \"MOTOR\"", pcb);
            Assert.Equal(4, pcb.Split(new[] { "(table_cell" }, StringSplitOptions.None).Length - 1);
            var copy = board.Clone().Graphics.Single();
            Assert.Equal("12 V", copy.Cell(0, 1));
            Assert.Equal(new[] { 15.0, 15.0 }, copy.ColumnWidths);
        }
    }
}
