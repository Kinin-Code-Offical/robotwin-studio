using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [table] The cells of a table (KiCad's table on a sheet or a board) kept row by row beside its columns' widths and
    /// rows' heights: reading and writing a cell, and growing or shrinking the table while every other cell keeps its text.
    /// </summary>
    public static class TableGrid
    {
        public static string Cell(IReadOnlyList<string>? cells, int columns, int row, int column)
        {
            int i = row * columns + column;
            return cells != null && column >= 0 && column < columns && i >= 0 && i < cells.Count ? cells[i] : string.Empty;
        }

        public static void SetCell(List<string> cells, int columns, int row, int column, string text)
        {
            int i = row * columns + column;
            while (cells.Count <= i) cells.Add(string.Empty);
            cells[i] = text;
        }

        /// <summary>
        /// The table at a new size: new columns as wide as the last one, new rows as high as the last, the cells that remain
        /// keeping their text.
        /// </summary>
        public static (List<double> Widths, List<double> Heights, List<string> Cells) Resize(IReadOnlyList<double> widths, IReadOnlyList<double> heights,
            IReadOnlyList<string>? cells, int rows, int columns, double defaultWidth, double defaultHeight)
        {
            rows = Math.Max(1, rows);
            columns = Math.Max(1, columns);
            double lastW = widths.Count > 0 ? widths[widths.Count - 1] : defaultWidth, lastH = heights.Count > 0 ? heights[heights.Count - 1] : defaultHeight;
            var w = Enumerable.Range(0, columns).Select(c => c < widths.Count ? widths[c] : lastW).ToList();
            var h = Enumerable.Range(0, rows).Select(r => r < heights.Count ? heights[r] : lastH).ToList();
            var text = new List<string>(rows * columns);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                text.Add(r < heights.Count ? Cell(cells, widths.Count, r, c) : string.Empty);
            return (w, h, text);
        }
    }
}
