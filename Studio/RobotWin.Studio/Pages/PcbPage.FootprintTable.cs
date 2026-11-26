using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [fp-assign] KiCad's Assign Footprints window as a table of every part on the board: designator, value, part,
    /// footprint and whether every pin of the part finds its pad there; a row's button picks another footprint
    /// (the same picker as a single part's), and the table follows.
    /// </summary>
    public partial class PcbPage
    {
        public void ShowFootprintTable()
        {
            var board = Board;
            if (board.Components.Count == 0)
            {
                W.Status = Lang.T("Kartta parça yok");
                return;
            }

            var panel = new StackPanel();
            void Fill()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock { Text = Lang.T("Ayak izi atama"), Style = S("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 4) });
                panel.Children.Add(Caption(Lang.T("Her parçanın ayak izi ve parçanın her pininin orada pedi olup olmadığı. Değiştir başka bir ayak izi seçtirir."), 0));
                var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                foreach (var w in new[] { 70.0, 120, 150, 0, 92 })
                    grid.ColumnDefinitions.Add(w > 0 ? new ColumnDefinition { Width = new GridLength(w) } : new ColumnDefinition());
                int row = 0;
                void Cell(int col, UIElement e)
                {
                    Grid.SetRow(e, row);
                    Grid.SetColumn(e, col);
                    grid.Children.Add(e);
                }

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                foreach (var (col, head) in new[] { (0, "Parça"), (1, "Değer"), (2, "Tür"), (3, "Ayak izi") })
                    Cell(col, new TextBlock { Text = Lang.T(head), Style = S("Text.Overline"), Margin = new Thickness(0, 0, 8, 6) });
                foreach (var comp in board.Components.OrderBy(c => c.RefDes, RobotTwin.CoreSim.Design.Annotator.NaturalComparer))
                {
                    row++;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var part = W.Schematic.FindPart(comp.RefDes);
                    var def = part != null ? W.Library.Find(part.PartId) : null;
                    var fp = FootprintLibrary.Get(comp.FootprintId);
                    var missing = fp != null && def != null && part != null
                        ? def.Pins.Select(p => def.ResolvePad(p, W.Schematic.ResolveParameters(W.Library, part))).Where(p => fp.FindPad(p) == null).Distinct().ToList()
                        : new System.Collections.Generic.List<string>();
                    Cell(0, new TextBlock { Text = comp.RefDes, Style = S("Text.Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) });
                    Cell(1, new TextBlock { Text = comp.Value, Style = S("Text.Body"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) });
                    Cell(2, new TextBlock { Text = def?.Name ?? "—", Style = S("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) });
                    var state = new TextBlock
                    {
                        Text = fp == null ? comp.FootprintId + " · " + Lang.T("kütüphanede yok") : missing.Count > 0 ? comp.FootprintId + " · " + Lang.T("pedi olmayan pin") + ": " + string.Join(", ", missing) : comp.FootprintId,
                        Style = S("Text.Mono"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 2, 8, 2)
                    };
                    if (fp == null || missing.Count > 0) state.Foreground = (Brush)FindResource("B.Danger");
                    Cell(3, state);
                    var change = Button(Lang.T("Değiştir…"), (_, _) =>
                    {
                        PickFootprint(comp);
                        Fill();
                    });
                    change.Margin = new Thickness(0, 2, 0, 2);
                    Cell(4, change);
                }

                panel.Children.Add(grid);
            }

            Fill();
            var body = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 190) };
            var dlg = MakeDialog(Window.GetWindow(this), Lang.T("Ayak izi atama"), 760, body, out var close, Lang.T("Kapat"), withCancel: false);
            close.Click += (_, _) => dlg.DialogResult = true;
            close.IsCancel = true;
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(dlg) > 0 ? VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            dlg.ShowDialog();
        }
    }
}
