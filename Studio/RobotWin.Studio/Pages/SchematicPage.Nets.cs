using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [net-navigator] KiCad's Net Navigator (studied in place) as a dock tab: every net of the sheet with its pin count,
    /// searchable by net or by pin ("R1", "R1.2", "GND"); a net picked is highlighted on the sheet (and cross-probed to the
    /// board) and the view goes to it; its pins are listed beside it, and a pin picked selects its part.
    /// </summary>
    public partial class SchematicPage
    {
        private RadioButton? _dockNets;
        private Grid? _netsPane;
        private TextBox? _netsSearch;
        private ListBox? _netsList, _netPins;
        private TextBlock? _netsSummary;
        private Netlist? _netlist;

        private void BuildNetsDock()
        {
            if (DockMc.Parent is not Panel tabs || McPane.Parent is not Grid host) return;
            _dockNets = new RadioButton
            {
                Style = (Style)FindResource("Chip"), Content = "Hatlar", GroupName = "schdock", Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Ağ gezgini: şemadaki bütün hatlar ve pinleri; seçilen hat şemada vurgulanır"
            };
            tabs.Children.Add(_dockNets);
            _netsPane = new Grid { Visibility = Visibility.Collapsed };
            _netsPane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
            _netsPane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

            var left = new DockPanel { Margin = new Thickness(12, 8, 8, 8) };
            _netsSearch = new TextBox { Margin = new Thickness(0, 0, 0, 6), Tag = "Hat ya da pin ara: GND, R1, R1.2" };   // Tag: the box's placeholder
            DockPanel.SetDock(_netsSearch, Dock.Top);
            left.Children.Add(_netsSearch);
            _netsSummary = new TextBlock { Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(_netsSummary, Dock.Top);
            left.Children.Add(_netsSummary);
            _netsList = new ListBox { BorderThickness = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(_netsList, ScrollBarVisibility.Disabled);
            left.Children.Add(_netsList);
            _netsPane.Children.Add(left);

            var right = new DockPanel { Margin = new Thickness(8, 8, 12, 8) };
            var pinsTitle = new TextBlock { Text = "Pinler", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 2, 0, 6) };
            DockPanel.SetDock(pinsTitle, Dock.Top);
            right.Children.Add(pinsTitle);
            _netPins = new ListBox { BorderThickness = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(_netPins, ScrollBarVisibility.Disabled);
            right.Children.Add(_netPins);
            Grid.SetColumn(right, 1);
            _netsPane.Children.Add(right);
            host.Children.Add(_netsPane);

            _netsSearch.TextChanged += (_, _) => FillNets();
            _netsList.SelectionChanged += (_, _) => OnNetPicked();
            _netPins.SelectionChanged += (_, _) => OnNetPinPicked();
            W.DocumentChanged += (_, _) =>
            {
                if (_netsPane.IsVisible) RefreshNets();
            };
        }

        /// <summary>The netlist again (the sheet changed or the tab opened) and the list with it, keeping the picked net.</summary>
        private void RefreshNets()
        {
            if (_netsList == null) return;
            try
            {
                _netlist = W.Schematic.BuildNetlist(W.Library);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                _netlist = null;
            }

            FillNets();
        }

        private void FillNets()
        {
            if (_netsList == null || _netsSummary == null) return;
            string? keep = (_netsList.SelectedItem as ListBoxItem)?.Tag is Net picked ? picked.Name : Canvas.HighlightedNet;
            _netsList.Items.Clear();
            var nets = _netlist?.Nets ?? new List<Net>();
            string q = (_netsSearch?.Text ?? string.Empty).Trim();
            bool Hit(Net n) =>
                q.Length == 0
                || n.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                || n.Connections.Any(c => c.ToString().StartsWith(q, StringComparison.CurrentCultureIgnoreCase)
                                          || string.Equals(c.RefDes, q, StringComparison.CurrentCultureIgnoreCase));
            var shown = nets.Where(Hit)
                .OrderBy(n => n.IsGround ? 0 : n.IsAutoNamed ? 2 : 1)
                .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _netsSummary.Text = Lang.T(q.Length == 0 ? $"{nets.Count} hat" : $"{shown.Count} / {nets.Count} hat");
            ListBoxItem? again = null;
            foreach (var n in shown)
            {
                var row = new DockPanel();
                var count = new TextBlock { Text = Lang.T($"{n.Connections.Count} pin"), Foreground = (Brush)FindResource("B.TextMuted"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
                row.Children.Add(new TextBlock
                {
                    Text = n.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)FindResource(n.IsAutoNamed ? "B.TextSecondary" : "B.Text"),
                    FontFamily = (FontFamily)FindResource("F.Mono")
                });
                var item = new ListBoxItem { Content = row, Tag = n, ToolTip = n.IsAutoNamed ? Lang.T("Adı bir pinden türetilmiş hat (etiket yok)") : null };
                _netsList.Items.Add(item);
                if (keep != null && string.Equals(n.Name, keep, StringComparison.Ordinal)) again = item;
            }

            if (again != null) _netsList.SelectedItem = again;
            else FillPins(null);
        }

        private void FillPins(Net? net)
        {
            if (_netPins == null) return;
            _netPins.Items.Clear();
            if (net == null) return;
            foreach (var c in net.Connections.OrderBy(c => c.RefDes, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Pin, StringComparer.OrdinalIgnoreCase))
            {
                var part = W.Schematic.FindPart(c.RefDes);
                string what = part != null ? W.Library.Find(part.PartId)?.Name ?? part.PartId : string.Empty;
                var row = new DockPanel();
                var kind = new TextBlock { Text = Lang.T(what), Foreground = (Brush)FindResource("B.TextMuted"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                DockPanel.SetDock(kind, Dock.Right);
                row.Children.Add(kind);
                row.Children.Add(new TextBlock { Text = c.ToString(), FontSize = 12, FontFamily = (FontFamily)FindResource("F.Mono"), Foreground = (Brush)FindResource("B.Text") });
                _netPins.Items.Add(new ListBoxItem { Content = row, Tag = c });
            }
        }

        private void OnNetPicked()
        {
            if (_netsList?.SelectedItem is not ListBoxItem { Tag: Net net })
            {
                FillPins(null);
                return;
            }

            FillPins(net);
            Canvas.HighlightedNet = net.Name;
            if (FirstPin(net) is { } at) Canvas.ZoomToPoint(at, 14);
            W.Status = Lang.T($"Hat: {net.Name} · {net.Connections.Count} pin");
        }

        private void OnNetPinPicked()
        {
            if (_netPins?.SelectedItem is not ListBoxItem { Tag: NetConnection c }) return;
            var part = W.Schematic.FindPart(c.RefDes);
            if (part == null) return;
            Canvas.SelectItems(new object[] { part });
            var pos = W.Schematic.PinPositions(W.Library).FirstOrDefault(p => ReferenceEquals(p.Part, part) && string.Equals(p.Pin.Name, c.Pin, StringComparison.OrdinalIgnoreCase));
            Canvas.ZoomToPoint(pos.Part != null ? pos.Position : part.Position, 18);
        }

        private GridPoint? FirstPin(Net net)
        {
            var first = net.Connections.FirstOrDefault();
            if (first == null) return null;
            foreach (var (part, pin, position) in W.Schematic.PinPositions(W.Library))
            {
                if (string.Equals(part.RefDes, first.RefDes, StringComparison.OrdinalIgnoreCase) && string.Equals(pin.Name, first.Pin, StringComparison.OrdinalIgnoreCase)) return position;
            }

            return null;
        }

        // ================================================================ [notes] texts on the sheet

        /// <summary>A new text at a grid point, or an existing one edited: its words (several lines), size, frame and weight.</summary>
        private void EditNote(object? target)
        {
            var note = target as SchematicNote;
            var at = target is GridPoint g ? g : note?.Position ?? new GridPoint(0, 0);
            var panel = new StackPanel { Width = 420 };
            panel.Children.Add(new TextBlock { Text = Lang.T(note == null ? "Metin ekle" : "Metni düzenle"), Style = (Style)FindResource("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 8) });
            var box = new TextBox { Text = note?.Text ?? string.Empty, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            panel.Children.Add(box);
            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var size = new ComboBox { Width = 90, Margin = new Thickness(0, 0, 12, 0), ToolTip = Lang.T("Yazı boyu") };
            foreach (var s in new[] { 0.7, 1.0, 1.5, 2.0, 3.0 }) size.Items.Add(new ComboBoxItem { Content = s.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "×", Tag = s });
            size.SelectedIndex = Math.Max(0, new[] { 0.7, 1.0, 1.5, 2.0, 3.0 }.ToList().FindIndex(s => Math.Abs(s - (note?.Size ?? 1.0)) < 1e-6));
            var boxed = new CheckBox { Content = Lang.T("Çerçeveli (metin kutusu)"), IsChecked = note?.Boxed ?? false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var bold = new CheckBox { Content = Lang.T("Kalın"), IsChecked = note?.Bold ?? false, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(size);
            row.Children.Add(boxed);
            row.Children.Add(bold);
            panel.Children.Add(row);
            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Metin"), 480, panel, out var ok);
            ok.Click += (_, _) => dlg.DialogResult = true;
            dlg.Loaded += (_, _) => box.Focus();
            if (dlg.ShowDialog() != true) return;
            string text = box.Text.TrimEnd();
            double sz = (size.SelectedItem as ComboBoxItem)?.Tag is double d ? d : 1.0;
            W.BeginChange();
            if (note == null)
            {
                if (text.Length == 0)
                {
                    W.CancelChange();
                    return;
                }

                W.Schematic.Notes.Add(new SchematicNote { Text = text, Position = at, Size = sz, Boxed = boxed.IsChecked == true, Bold = bold.IsChecked == true, Sheet = Canvas.CurrentSheet });   // [sheets]
                W.CommitChange("Metin eklendi");
            }
            else if (text.Length == 0)
            {
                W.Schematic.Notes.Remove(note);
                W.CommitChange("Metin silindi");
            }
            else
            {
                note.Text = text;
                note.Size = sz;
                note.Boxed = boxed.IsChecked == true;
                note.Bold = bold.IsChecked == true;
                W.CommitChange("Metin düzenlendi");
            }

            Canvas.InvalidateVisual();
        }
    }
}
