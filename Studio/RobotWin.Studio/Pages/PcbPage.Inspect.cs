using System;
using System.Globalization;
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
    /// [board-stats] KiCad's Inspect menu, the parts worth having (studied in place): the board's statistics as a table with
    /// its drill list, copied as text for a fab quote.
    /// </summary>
    public partial class PcbPage
    {
        /// <summary>[palette] Shows the board statistics.</summary>
        public void ShowStatistics() => OnStatistics(this, new RoutedEventArgs());

        /// <summary>[palette] The next DRC marker (F8), from the palette.</summary>
        public void NextMarker() => StepMarker(1);

        /// <summary>[palette] Saves the DRC report.</summary>
        public void SaveDrcReport() => OnDrcReport(this, new RoutedEventArgs());

        private void OnStatistics(object sender, RoutedEventArgs e)
        {
            var st = BoardStatistics.Of(Board);
            var c = CultureInfo.InvariantCulture;
            string F(double v, string f = "0.##") => v.ToString(f, c);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "Kart istatistikleri", Style = S("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(Caption("Kartın neyden yapıldığı: üretici teklifi ve hızlı bir denetim için.", 0));

            var columns = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel();
            var right = new StackPanel();
            Grid.SetColumn(right, 2);
            columns.Children.Add(left);
            columns.Children.Add(right);
            panel.Children.Add(columns);

            void Line(StackPanel host, string label, string value)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = label, Style = S("Text.Body") });
                var v = new TextBlock { Text = value, Style = S("Text.Mono"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(v, 1);
                row.Children.Add(v);
                host.Children.Add(row);
            }

            left.Children.Add(Overline("Kart", 4));
            Line(left, "Boyut", $"{F(st.Width)} × {F(st.Height)} mm");
            Line(left, "Alan", $"{F(st.Area / 100, "0.###")} cm²");
            Line(left, "Bakır katman", st.CopperLayers.ToString(c));
            Line(left, "Kalınlık", $"{F(st.Thickness)} mm");
            left.Children.Add(Overline("Parçalar"));
            Line(left, "Üst yüz", st.PartsTop.ToString(c));
            Line(left, "Alt yüz", st.PartsBottom.ToString(c));
            Line(left, "Delikli", st.PartsThroughHole.ToString(c));
            Line(left, "SMD", st.PartsSmd.ToString(c));
            left.Children.Add(Overline("Pedler"));
            Line(left, "Delikli", st.PadsThroughHole.ToString(c));
            Line(left, "SMD", st.PadsSmd.ToString(c));
            Line(left, "Kaplamasız", st.PadsNonPlated.ToString(c));

            right.Children.Add(Overline("Bakır", 4));
            Line(right, "Hatlar", st.Nets.ToString(c));
            Line(right, "Yol parçaları", st.Tracks.ToString(c));
            Line(right, "Yol uzunluğu", $"{F(st.TrackLength, "0.#")} mm");
            foreach (var kv in st.LengthByLayer.OrderBy(kv => kv.Key)) Line(right, "  " + PcbLayers.DisplayName(kv.Key), $"{F(kv.Value, "0.#")} mm");
            Line(right, "Vialar (delikten deliğe)", st.ViasThrough.ToString(c));
            if (st.ViasBlindBuried > 0) Line(right, "Vialar (kör/gömülü)", st.ViasBlindBuried.ToString(c));
            Line(right, "Dolgu alanları", st.Zones.ToString(c));
            right.Children.Add(Overline($"Delikler · {st.Holes}"));
            foreach (var d in st.Drills) Line(right, $"Ø{F(d.Diameter, "0.###")} mm · {(d.Plated ? "kaplamalı" : "kaplamasız")} · {d.Kind}", "× " + d.Count.ToString(c));
            if (st.Drills.Count == 0) right.Children.Add(Caption("Delik yok", 0));

            var copy = Button("Panoya kopyala", (_, _) =>
            {
                Clipboard.SetText(st.ToText(Lang.T));
                W.Status = "Kart istatistikleri panoya kopyalandı";
            });
            copy.Margin = new Thickness(0, 14, 0, 0);
            copy.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(copy);

            var body = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 190) };
            var dlg = MakeDialog(Window.GetWindow(this), "Kart istatistikleri", 640, body, out var ok, "Kapat", withCancel: false);
            ok.Click += (_, _) => dlg.DialogResult = true;
            ok.IsCancel = true;   // Esc closes it too
            if (Unattended.Active)
            {
                // --snapshot: shown without waiting, rendered instead of the main window.
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(dlg) > 0 ? VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            dlg.ShowDialog();
        }

        /// <summary>
        /// [select-filter] KiCad's Selection Filter at the foot of the layers: which kinds a click, a box or Ctrl+A picks.
        /// "Hepsi" turns every kind on or off; the filter lasts for the session.
        /// </summary>
        private void AddSelectionFilter()
        {
            var filter = Canvas.SelectionFilter;
            LayersPanel.Children.Add(Overline("Seçim süzgeci"));
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            var all = new CheckBox { Content = "Hepsi", IsChecked = filter.All, Margin = new Thickness(0, 2, 8, 4) };
            all.Click += (_, _) =>
            {
                filter.SetAll(all.IsChecked == true);
                UpdateLayersPanel();
            };
            grid.Children.Add(all);
            foreach (var (label, get, set) in filter.Kinds())
            {
                var cb = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 2, 8, 4) };
                cb.Click += (_, _) =>
                {
                    set(cb.IsChecked == true);
                    UpdateLayersPanel();   // "Hepsi" and the note follow
                };
                grid.Children.Add(cb);
            }

            LayersPanel.Children.Add(grid);
            if (!filter.All) LayersPanel.Children.Add(Caption("Kapalı türler çizilir ve denetlenir, yalnızca seçilemez.", 2));
        }
    }
}
