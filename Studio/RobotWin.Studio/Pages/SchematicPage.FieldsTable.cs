using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [fields] KiCad's Symbol Fields Table: every part of the sheet in one table (reference, kind, value, footprint,
    /// description), the same ones grouped in a row as KiCad groups them (or one row per part). A value typed in a row
    /// changes every part of that row at once, one step to undo; a row's references select those parts on the sheet.
    /// </summary>
    public partial class SchematicPage
    {
        private bool _fieldsGrouped = true;

        public void ShowFieldsTable()
        {
            if (W.Schematic.Parts.Count == 0)
            {
                ShowToast(Lang.T("Şemada parça yok."));
                return;
            }

            var panel = new StackPanel();
            void Fill()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock { Text = Lang.T("Alan tablosu"), Style = S("Text.Title"), FontSize = Shell.TypeScale.Heading, Margin = new Thickness(0, 0, 0, 4) });
                panel.Children.Add(RobotWin.Studio.Pcb.PcbForms.Caption(Lang.T("Şemadaki bütün parçalar. Bir satırın değeri değişince o satırdaki bütün parçalar değişir (tek adımda geri alınır); referansa tıklamak parçaları şemada seçer."), 0));
                var group = new CheckBox { Content = Lang.T("Aynı tür ve değerdekileri grupla"), IsChecked = _fieldsGrouped, Margin = new Thickness(0, 8, 0, 0) };
                group.Click += (_, _) =>
                {
                    _fieldsGrouped = group.IsChecked == true;
                    Fill();
                };
                panel.Children.Add(group);
                var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                foreach (var w in new[] { 150.0, 140, 120, 150, 0 })
                    grid.ColumnDefinitions.Add(w > 0 ? new ColumnDefinition { Width = new GridLength(w) } : new ColumnDefinition());
                int row = 0;
                void Cell(int col, UIElement e)
                {
                    Grid.SetRow(e, row);
                    Grid.SetColumn(e, col);
                    grid.Children.Add(e);
                }

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                foreach (var (col, head) in new[] { (0, "Referans"), (1, "Tür"), (2, "Değer"), (3, "Kılıf"), (4, "Açıklama") })
                    Cell(col, new TextBlock { Text = Lang.T(head), Style = S("Text.Overline"), Margin = new Thickness(0, 0, 8, 6) });
                var lines = BomBuilder.Build(W.Schematic, W.Library);
                var rows = _fieldsGrouped
                    ? lines.Select(l => (Refs: l.References.ToList(), Line: l)).ToList()
                    : lines.SelectMany(l => l.References.Select(r => (Refs: new List<string> { r }, Line: l)))
                        .OrderBy(x => x.Refs[0], Annotator.NaturalComparer).ToList();
                foreach (var (refs, line) in rows)
                {
                    row++;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var parts = refs.Select(r => W.Schematic.FindPart(r)).Where(p => p != null).Select(p => p!).ToList();
                    var def = W.Library.Find(line.PartId);
                    var link = new TextBlock
                    {
                        Text = string.Join(", ", refs), Style = S("Text.Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2), Cursor = Cursors.Hand,
                        ToolTip = Lang.T("Şemada seç")
                    };
                    link.MouseLeftButtonUp += (_, _) => Canvas.SelectItems(parts.Cast<object>());
                    Cell(0, link);
                    Cell(1, new TextBlock { Text = line.Name, Style = S("Text.Body"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) });
                    if (def != null && !string.IsNullOrEmpty(def.ValueKey))
                    {
                        var param = def.Parameters.FirstOrDefault(x => x.Key == def.ValueKey);
                        void Apply(string v)
                        {
                            v = v.Trim();
                            if (v.Length == 0 || v == line.Value) return;
                            // a value with a unit must read as one ("4k7", "100nF", "10kΩ"); anything else is refused
                            if (param != null && param.Unit is "Ω" or "F" or "H" or "V" or "A" && !SiValue.TryParse(v, out _))
                            {
                                ShowToast(string.Format(Lang.T("'{0}' bir değer olarak okunamadı (ör. 4k7, 100nF)."), v));
                                Fill();
                                return;
                            }

                            W.BeginChange();
                            foreach (var p in parts) p.Parameters[def.ValueKey!] = v;
                            W.CommitChange(parts.Count == 1 ? $"{parts[0].RefDes} değeri: {v}" : $"{parts.Count} parçanın değeri: {v}");
                            Fill();
                        }

                        if (param?.Choices is { Length: > 0 } choices)
                        {
                            // [fields] a value from a list: chosen, not typed
                            var combo = new ComboBox { Margin = new Thickness(0, 2, 8, 2), ToolTip = Lang.T("Satırdaki bütün parçalar için seçin") };
                            string current = W.Schematic.ResolveParameters(W.Library, parts[0]).TryGetValue(def.ValueKey!, out var cur) ? cur : string.Empty;
                            foreach (var c in choices) combo.Items.Add(c);
                            combo.SelectedItem = choices.FirstOrDefault(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase));
                            combo.SelectionChanged += (_, _) =>
                            {
                                if (combo.SelectedItem is string pick && !string.Equals(pick, current, StringComparison.OrdinalIgnoreCase)) Apply(pick);
                            };
                            Cell(2, combo);
                        }
                        else
                        {
                            var box = new TextBox { Text = line.Value, Margin = new Thickness(0, 2, 8, 2), ToolTip = Lang.T("Değeri yazın; Enter ya da başka yere geçince satırdaki bütün parçalara uygulanır") };
                            box.KeyDown += (_, e) =>
                            {
                                if (e.Key == Key.Enter) Apply(box.Text);
                            };
                            box.LostKeyboardFocus += (_, _) => Apply(box.Text);
                            Cell(2, box);
                        }
                    }
                    else Cell(2, new TextBlock { Text = line.Value, Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) });

                    Cell(3, new TextBlock { Text = line.Footprint, Style = S("Text.Mono"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) });
                    Cell(4, new TextBlock { Text = line.Description, Style = S("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) });
                }

                panel.Children.Add(grid);
            }

            Fill();
            var body = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 190) };
            var dlg = MakeDialog(Window.GetWindow(this), Lang.T("Alan tablosu"), 860, body, out var close, Lang.T("Kapat"), withCancel: false);
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
