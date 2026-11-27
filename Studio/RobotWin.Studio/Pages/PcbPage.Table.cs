using System.Linq;
using System.Windows.Controls;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [table] KiCad's table on the board, edited in the shared table editor (Theme/TableEditor.cs) with the board's layer
    /// beside the text size and the rule width; one step to undo.
    /// </summary>
    public partial class PcbPage
    {
        private static readonly (GraphicLayer Layer, string Name)[] TableLayers =
        {
            (GraphicLayer.SilkTop, "Üst serigrafi"), (GraphicLayer.SilkBottom, "Alt serigrafi"), (GraphicLayer.FabTop, "Üst montaj (fab)"),
            (GraphicLayer.FabBottom, "Alt montaj (fab)"), (GraphicLayer.Drawings, "Çizimler")
        };

        private void EditTable(BoardGraphic table)
        {
            if (Board == null) return;
            var edit = new TableEdit
            {
                Widths = table.ColumnWidths!.ToList(), Heights = table.RowHeights!.ToList(),
                Cells = Enumerable.Range(0, table.Rows * table.Columns).Select(i => table.Cell(i / table.Columns, i % table.Columns)).ToList(),
                TextSize = table.CellTextSize, Rule = table.Width
            };
            var layer = new ComboBox { Width = 180 };
            foreach (var (l, name) in TableLayers) layer.Items.Add(new ComboBoxItem { Content = name, Tag = l, IsSelected = l == table.Layer });
            if (!TableEditor.Show(Owner, edit, 1, layer, "Katman")) return;
            Edit($"Tablo güncellendi ({edit.Rows} × {edit.Columns})", () =>
            {
                table.ColumnWidths = edit.Widths.ToList();
                table.RowHeights = edit.Heights.ToList();
                table.Cells = edit.Cells.ToList();
                table.TextSize = edit.TextSize;
                table.Width = edit.Rule;
                if (layer.SelectedItem is ComboBoxItem { Tag: GraphicLayer gl }) table.Layer = gl;
                table.FitTable();
            });
        }
    }
}
