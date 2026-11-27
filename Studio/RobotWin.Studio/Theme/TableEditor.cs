using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Theme
{
    /// <summary>[table] A table's contents as the editor changes them: sizes in the owner's units, cells row by row.</summary>
    public sealed class TableEdit
    {
        public List<double> Widths { get; set; } = new List<double>();
        public List<double> Heights { get; set; } = new List<double>();
        public List<string> Cells { get; set; } = new List<string>();

        /// <summary>Text height (mm).</summary>
        public double TextSize { get; set; }

        /// <summary>Rule width (mm).</summary>
        public double Rule { get; set; }

        public int Rows => Heights.Count;
        public int Columns => Widths.Count;
    }

    /// <summary>
    /// [table] KiCad's table editor, shared by the sheet and the board: the cells typed in a grid (Enter starts a line), rows
    /// and columns added or taken away (the other cells keep their text), each column's width over it and each row's height
    /// beside it, in millimetres whatever the owner's units; the text size, the rule width, and whatever the owner adds
    /// (the board's layer).
    /// </summary>
    public static class TableEditor
    {
        private static double Read(TextBox box, double fallback) =>
            double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : fallback;

        private static string Mm(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

        /// <summary>Edits the table in place; true when Tamam was pressed. <paramref name="toMm"/>: millimetres per unit of its sizes.</summary>
        public static bool Show(Window? owner, TableEdit table, double toMm, FrameworkElement? extra = null, string? extraLabel = null)
        {
            var panel = new StackPanel();
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380 };
            var cellBoxes = new List<TextBox>();
            var widthBoxes = new List<TextBox>();
            var heightBoxes = new List<TextBox>();

            void Keep()
            {
                for (int i = 0; i < cellBoxes.Count && i < table.Cells.Count; i++) table.Cells[i] = cellBoxes[i].Text;
                for (int c = 0; c < widthBoxes.Count && c < table.Columns; c++) table.Widths[c] = Read(widthBoxes[c], table.Widths[c] * toMm) / toMm;
                for (int r = 0; r < heightBoxes.Count && r < table.Rows; r++) table.Heights[r] = Read(heightBoxes[r], table.Heights[r] * toMm) / toMm;
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
                for (int c = 0; c < table.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                for (int r = 0; r <= table.Rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                void Put(UIElement e, int row, int col)
                {
                    Grid.SetRow(e, row);
                    Grid.SetColumn(e, col);
                    grid.Children.Add(e);
                }

                Put(new TextBlock { Text = "mm", Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center }, 0, 0);
                for (int c = 0; c < table.Columns; c++)
                {
                    var wb = new TextBox { Text = Mm(table.Widths[c] * toMm), Margin = new Thickness(2), ToolTip = Lang.T("Sütun genişliği (mm)") };
                    widthBoxes.Add(wb);
                    Put(wb, 0, c + 1);
                }

                for (int r = 0; r < table.Rows; r++)
                {
                    var hb = new TextBox { Text = Mm(table.Heights[r] * toMm), Margin = new Thickness(2), ToolTip = Lang.T("Satır yüksekliği (mm)") };
                    heightBoxes.Add(hb);
                    Put(hb, r + 1, 0);
                    for (int c = 0; c < table.Columns; c++)
                    {
                        var cell = new TextBox { Text = TableGrid.Cell(table.Cells, table.Columns, r, c), Margin = new Thickness(2), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
                        cellBoxes.Add(cell);
                        Put(cell, r + 1, c + 1);
                    }
                }
            }

            void Resize(int rows, int columns)
            {
                var (w, h, cells) = TableGrid.Resize(table.Widths, table.Heights, table.Cells, rows, columns, table.Widths.LastOrDefault(), table.Heights.LastOrDefault());
                table.Widths = w;
                table.Heights = h;
                table.Cells = cells;
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

            sizes.Children.Add(Step("+ Satır", "Alta bir satır ekle", () => Resize(table.Rows + 1, table.Columns)));
            sizes.Children.Add(Step("− Satır", "Son satırı kaldır", () => Resize(table.Rows - 1, table.Columns)));
            sizes.Children.Add(Step("+ Sütun", "Sağa bir sütun ekle", () => Resize(table.Rows, table.Columns + 1)));
            sizes.Children.Add(Step("− Sütun", "Son sütunu kaldır", () => Resize(table.Rows, table.Columns - 1)));
            panel.Children.Add(sizes);
            panel.Children.Add(Caption(Lang.T("Hücreye yazın (Enter yeni satır). Üst satır sütun genişlikleri, sol sütun satır yükseklikleridir (mm)."), 8));
            panel.Children.Add(scroll);

            var textSize = new TextBox { Text = Mm(table.TextSize), Width = 70 };
            var rule = new TextBox { Text = Mm(table.Rule), Width = 70 };
            var options = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            void Labeled(string text, FrameworkElement input)
            {
                options.Children.Add(new TextBlock { Text = Lang.T(text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                input.Margin = new Thickness(0, 0, 16, 0);
                options.Children.Add(input);
            }

            Labeled("Yazı boyu (mm)", textSize);
            Labeled("Çizgi (mm)", rule);
            if (extra != null) Labeled(extraLabel ?? string.Empty, extra);
            panel.Children.Add(options);
            Fill();

            var dlg = MakeDialog(owner, Lang.T("Tablo"), 640, panel, out var ok);
            ok.Click += (_, _) => dlg.DialogResult = true;
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(dlg) > 0 ? VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return false;
            }

            if (dlg.ShowDialog() != true) return false;
            Keep();
            table.TextSize = Read(textSize, table.TextSize);
            table.Rule = Read(rule, table.Rule);
            return true;
        }
    }
}
