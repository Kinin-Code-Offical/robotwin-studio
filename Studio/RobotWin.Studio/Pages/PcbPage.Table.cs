using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [table] KiCad's table editor on the board: the cells typed in a grid, rows and columns added or taken away (the
    /// other cells keep their text), each column's width and each row's height, the text size, the rule width and the layer.
    /// One step to undo.
    /// </summary>
    public partial class PcbPage
    {
        private static readonly (GraphicLayer Layer, string Name)[] TableLayers =
        {
            (GraphicLayer.SilkTop, "Üst serigrafi"), (GraphicLayer.SilkBottom, "Alt serigrafi"), (GraphicLayer.FabTop, "Üst montaj (fab)"),
            (GraphicLayer.FabBottom, "Alt montaj (fab)"), (GraphicLayer.Drawings, "Çizimler")
        };

        private static double ReadMm(TextBox box, double fallback) =>
            double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : fallback;

        private static string Mm(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

        private void EditTable(BoardGraphic table)
        {
            var board = Board;
            if (board == null) return;
            // a working copy: nothing changes on the board until Tamam
            var work = BoardGraphic.Table(0, 0, table.Rows, table.Columns);
            work.ColumnWidths = table.ColumnWidths!.ToList();
            work.RowHeights = table.RowHeights!.ToList();
            work.Cells = Enumerable.Range(0, table.Rows * table.Columns).Select(i => table.Cell(i / table.Columns, i % table.Columns)).ToList();
            work.TextSize = table.CellTextSize;
            work.Width = table.Width;
            work.Layer = table.Layer;

            var panel = new StackPanel();
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380 };
            var cellBoxes = new List<TextBox>();
            var widthBoxes = new List<TextBox>();
            var heightBoxes = new List<TextBox>();

            void Keep()
            {
                // the boxes' text into the working copy before the grid is laid out again
                for (int i = 0; i < cellBoxes.Count && i < work.Cells!.Count; i++) work.Cells[i] = cellBoxes[i].Text;
                for (int c = 0; c < widthBoxes.Count && c < work.Columns; c++) work.ColumnWidths![c] = ReadMm(widthBoxes[c], work.ColumnWidths[c]);
                for (int r = 0; r < heightBoxes.Count && r < work.Rows; r++) work.RowHeights![r] = ReadMm(heightBoxes[r], work.RowHeights[r]);
            }

            void Fill()
            {
                grid.Children.Clear();
                grid.RowDefinitions.Clear();
                grid.ColumnDefinitions.Clear();
                cellBoxes.Clear();
                widthBoxes.Clear();
                heightBoxes.Clear();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                for (int c = 0; c < work.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                for (int r = 0; r <= work.Rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                void Put(UIElement e, int row, int col)
                {
                    Grid.SetRow(e, row);
                    Grid.SetColumn(e, col);
                    grid.Children.Add(e);
                }

                Put(new TextBlock { Text = Lang.T("mm"), Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center }, 0, 0);
                for (int c = 0; c < work.Columns; c++)
                {
                    var wb = new TextBox { Text = Mm(work.ColumnWidths![c]), Margin = new Thickness(2), ToolTip = Lang.T("Sütun genişliği (mm)") };
                    widthBoxes.Add(wb);
                    Put(wb, 0, c + 1);
                }

                for (int r = 0; r < work.Rows; r++)
                {
                    var hb = new TextBox { Text = Mm(work.RowHeights![r]), Margin = new Thickness(2), ToolTip = Lang.T("Satır yüksekliği (mm)") };
                    heightBoxes.Add(hb);
                    Put(hb, r + 1, 0);
                    for (int c = 0; c < work.Columns; c++)
                    {
                        var cell = new TextBox { Text = work.Cell(r, c), Margin = new Thickness(2), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
                        cellBoxes.Add(cell);
                        Put(cell, r + 1, c + 1);
                    }
                }
            }

            var sizes = new StackPanel { Orientation = Orientation.Horizontal };
            Button Step(string text, string tip, Action act)
            {
                var b = new Button { Content = text, Style = S("Btn"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0), ToolTip = Lang.T(tip) };
                b.Click += (_, _) =>
                {
                    Keep();
                    act();
                    Fill();
                };
                return b;
            }

            sizes.Children.Add(Step("+ Satır", "Alta bir satır ekle", () => work.ResizeTable(work.Rows + 1, work.Columns)));
            sizes.Children.Add(Step("− Satır", "Son satırı kaldır", () => work.ResizeTable(work.Rows - 1, work.Columns)));
            sizes.Children.Add(Step("+ Sütun", "Sağa bir sütun ekle", () => work.ResizeTable(work.Rows, work.Columns + 1)));
            sizes.Children.Add(Step("− Sütun", "Son sütunu kaldır", () => work.ResizeTable(work.Rows, work.Columns - 1)));
            panel.Children.Add(sizes);
            panel.Children.Add(Caption(Lang.T("Hücreye yazın (Enter yeni satır). Üst satır sütun genişlikleri, sol sütun satır yükseklikleridir (mm)."), 8));
            panel.Children.Add(scroll);

            var textSize = new TextBox { Text = Mm(work.CellTextSize), Width = 70 };
            var rule = new TextBox { Text = Mm(work.Width), Width = 70 };
            var layer = new ComboBox { Width = 180 };
            foreach (var (l, name) in TableLayers) layer.Items.Add(new ComboBoxItem { Content = name, Tag = l, IsSelected = l == work.Layer });
            var options = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            void Labeled(string text, FrameworkElement input)
            {
                options.Children.Add(new TextBlock { Text = Lang.T(text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                input.Margin = new Thickness(0, 0, 16, 0);
                options.Children.Add(input);
            }

            Labeled("Yazı boyu (mm)", textSize);
            Labeled("Çizgi (mm)", rule);
            Labeled("Katman", layer);
            panel.Children.Add(options);
            Fill();

            var dlg = MakeDialog(Owner, Lang.T("Tablo"), 640, panel, out var ok);
            ok.Click += (_, _) => dlg.DialogResult = true;
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = System.Windows.Media.VisualTreeHelper.GetChildrenCount(dlg) > 0 ? System.Windows.Media.VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            if (dlg.ShowDialog() != true) return;
            Keep();
            Edit($"Tablo güncellendi ({work.Rows} × {work.Columns})", () =>
            {
                table.ColumnWidths = work.ColumnWidths!.ToList();
                table.RowHeights = work.RowHeights!.ToList();
                table.Cells = work.Cells!.ToList();
                table.TextSize = ReadMm(textSize, table.CellTextSize);
                table.Width = ReadMm(rule, table.Width);
                if (layer.SelectedItem is ComboBoxItem { Tag: GraphicLayer gl }) table.Layer = gl;
                table.FitTable();
            });
        }
    }
}
