using System.Linq;
using System.Windows;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [table] KiCad's table on the sheet, edited in the shared table editor (Theme/TableEditor.cs): its sizes in the sheet's
    /// grid steps shown in millimetres; one step to undo.
    /// </summary>
    public partial class SchematicPage
    {
        private void EditSheetTable(SchematicShape table)
        {
            if (table.ColumnWidths == null || table.RowHeights == null) return;
            var edit = new TableEdit
            {
                Widths = table.ColumnWidths.ToList(), Heights = table.RowHeights.ToList(),
                Cells = Enumerable.Range(0, table.Rows * table.Columns).Select(i => table.Cell(i / table.Columns, i % table.Columns)).ToList(),
                TextSize = table.CellTextSize, Rule = table.Width
            };
            if (!TableEditor.Show(Window.GetWindow(this), edit, 2.54)) return;
            W.BeginChange();
            table.ColumnWidths = edit.Widths.ToList();
            table.RowHeights = edit.Heights.ToList();
            table.Cells = edit.Cells.ToList();
            table.TextSize = edit.TextSize;
            table.Width = edit.Rule;
            table.FitTable();
            W.CommitChange($"Tablo güncellendi ({edit.Rows} × {edit.Columns})");
            Canvas.InvalidateVisual();
        }
    }
}
