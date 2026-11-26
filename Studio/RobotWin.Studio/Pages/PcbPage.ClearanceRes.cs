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
    /// [clearance-res] KiCad's Inspect → Clearance Resolution for two selected items (tracks, vias, parts, pours): the
    /// nets and their classes, the clearance that holds between them and where it comes from, and the copper gap on
    /// every layer they share, each marked as within the rule or not.
    /// </summary>
    public partial class PcbPage
    {
        public void ShowClearanceResolution()
        {
            var items = Canvas.SelectionItems.Where(o => o is Track or Via or BoardComponent or Zone).ToList();
            if (items.Count != 2)
            {
                W.Status = Lang.T("Aralık çözümlemesi için iki öğe seçin (Ctrl+tık): hat, via, parça ya da dolgu");
                return;
            }

            var r = ClearanceResolution.Between(Board, items[0], items[1]);
            if (r == null)
            {
                W.Status = Lang.T("Seçilen öğelerden birinde bakır yok");
                return;
            }

            var c = CultureInfo.InvariantCulture;
            string Mm(double v) => v.ToString("0.###", c) + " mm";
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = Lang.T("Aralık çözümlemesi"), Style = S("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(Caption(Lang.T("Seçili iki öğe arasında hangi kuralın geçerli olduğu ve bakırlarının gerçekte ne kadar uzak olduğu."), 0));

            void Line(string label, string value, Brush? color = null)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = Lang.T(label), Style = S("Text.Body"), Foreground = (Brush)FindResource("B.TextSecondary") });
                var v = new TextBlock { Text = value, Style = S("Text.Body"), TextWrapping = TextWrapping.Wrap };
                if (color != null) v.Foreground = color;
                Grid.SetColumn(v, 1);
                row.Children.Add(v);
                panel.Children.Add(row);
            }

            panel.Children.Add(Overline(Lang.T("Öğeler"), 10));
            Line("Birinci", $"{Name(items[0])} · {r.NetA} · {r.ClassA} ({Mm(r.ClearanceA)})");
            Line("İkinci", $"{Name(items[1])} · {r.NetB} · {r.ClassB} ({Mm(r.ClearanceB)})");
            panel.Children.Add(Overline(Lang.T("Kural"), 10));
            Line("Gereken aralık", r.SameNet ? Lang.T("yok (aynı ağ)") : Mm(r.Required));
            Line("Nereden", Lang.T(r.Source));
            panel.Children.Add(Overline(Lang.T("Katmanlarda"), 10));
            if (r.Layers.Count == 0) Line("Ortak katman", Lang.T("yok: aynı bakır katmanında değiller, aralık sorun olmaz"));
            foreach (var (layer, actual) in r.Layers)
            {
                bool ok = r.SameNet || actual >= r.Required - 1e-9;
                string verdict = r.SameNet ? Lang.T("aynı ağ") : ok ? Lang.T("uygun") : Lang.T("KURAL DIŞI");
                Line(PcbLayers.DisplayName(layer), $"{Mm(actual)} · {verdict}", ok ? null : (Brush)FindResource("B.Danger"));
            }

            var dlg = MakeDialog(Window.GetWindow(this), Lang.T("Aralık çözümlemesi"), 560, panel, out var close, Lang.T("Kapat"), withCancel: false);
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

        private static string Name(object o) => o switch
        {
            Track t => "Hat " + t.Width.ToString("0.##", CultureInfo.InvariantCulture) + " mm",
            Via v => "Via Ø" + v.Diameter.ToString("0.##", CultureInfo.InvariantCulture) + " mm",
            BoardComponent c => c.RefDes,
            Zone z => "Dolgu " + (string.IsNullOrEmpty(z.Name) ? z.Net : z.Name),
            _ => "?"
        };
    }
}
