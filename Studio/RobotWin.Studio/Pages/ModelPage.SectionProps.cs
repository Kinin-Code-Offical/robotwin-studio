using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [section-props] SolidWorks' Section Properties at the section view's plane: area, perimeter, centroid, the second
    /// moments about centroidal axes, their product, the principal moments and axis, polar moment, radii of gyration and
    /// section moduli, for the selected objects (all visible ones when none is selected). CoreSim Modeling/SectionProperties.cs.
    /// </summary>
    public partial class ModelPage
    {
        private void OnSectionProps(object sender, RoutedEventArgs e)
        {
            if (_vp.Section == null) return;
            var chosen = _ed.Doc.Objects.Where(o => _ed.Selected.Contains(o.Id)).ToList();
            var r = SectionProperties.AtPlane(_ed.Doc, _vp.Section, chosen.Count > 0 ? chosen : null);
            if (r == null)
            {
                _ed.Status(Lang.T("Düzlem bir parçayı kesmiyor"));
                return;
            }

            string N(double v, string f = "0.###")
            {
                string t = v.ToString(f, CultureInfo.InvariantCulture);
                return t.TrimStart('-').Trim('0', '.').Length == 0 ? "0" : t;   // no "−0"
            }
            var sb = new StringBuilder();
            void Row(string label, string value) => sb.Append(Lang.T(label).PadRight(30)).Append(value).Append('\n');
            Row("Düzlem", $"{_vp.Section.Axis} = {N(_vp.Section.Offset)} mm ({r.U}–{r.V})");
            Row("Nesneler", chosen.Count > 0 ? string.Join(", ", chosen.Select(o => o.Name)) : Lang.T("görünen hepsi"));
            Row("Alan", $"{N(r.Area)} mm²");
            Row("Çevre", $"{N(r.Perimeter)} mm");
            Row("Ağırlık merkezi", $"{r.U} {N(r.CentroidU)} · {r.V} {N(r.CentroidV)} mm");
            Row($"I{r.U} (ağırlık merkezinden)", $"{N(r.Iu, "0.##")} mm⁴");
            Row($"I{r.V} (ağırlık merkezinden)", $"{N(r.Iv, "0.##")} mm⁴");
            Row($"I{r.U}{r.V}", $"{N(r.Iuv, "0.##")} mm⁴");
            Row("Asal momentler", $"{N(r.I1, "0.##")} · {N(r.I2, "0.##")} mm⁴");
            Row("Asal eksen açısı", $"{N(r.PrincipalAngle, "0.##")}° ({r.U} ekseninden)");
            Row("Kutupsal moment J", $"{N(r.J, "0.##")} mm⁴");
            Row("Atalet yarıçapları", $"{r.U} {N(r.Ru)} · {r.V} {N(r.Rv)} mm");
            Row("Kesit modülleri", $"W{r.U} {N(r.Wu, "0.##")} · W{r.V} {N(r.Wv, "0.##")} mm³");
            string text = sb.ToString();

            var panel = new StackPanel { Width = 560 };
            panel.Children.Add(new TextBlock { Text = Lang.T("Kesit özellikleri"), Style = (Style)FindResource("Text.Title"), FontSize = Shell.TypeScale.Heading, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = Lang.T("Kesit görünümünün düzleminde, kesilen yüzlerin alan integralleri; momentler kesitin ağırlık merkezinden geçen eksenlere göre."), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(new TextBox
            {
                Text = text, IsReadOnly = true, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 360, Padding = new Thickness(8)
            });
            var copy = new Button { Content = Lang.T("Panoya kopyala"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)FindResource("Btn") };
            copy.Click += (_, _) =>
            {
                Clipboard.SetText(text);
                _ed.Status(Lang.T("Kesit özellikleri panoya kopyalandı"));
            };
            panel.Children.Add(copy);
            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Kesit özellikleri"), 620, panel, out var ok, "Kapat", withCancel: false);
            ok.Click += (_, _) => dlg.DialogResult = true;
            ok.IsCancel = true;
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
