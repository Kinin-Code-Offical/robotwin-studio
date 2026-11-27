using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Modeling;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [holes] SolidWorks' Hole Wizard in the part editor: the kind of hole (clearance, counterbore, countersink, tap
    /// drill, heat-set insert), the screw size and fit, the depth; then each click on a surface drills one there along the
    /// surface's normal, until a right click. [shell] The Shell button starts the wall-thickness tool on the selected faces.
    /// </summary>
    public partial class ModelPage
    {
        private static readonly DrillSpec LastHole = new DrillSpec();

        private static readonly (HoleType Type, string Name)[] HoleTypes =
        {
            (HoleType.Clearance, "Geçiş deliği"), (HoleType.Counterbore, "Silindir başlı vida (yuvalı)"), (HoleType.Countersink, "Havşa başlı vida (90°)"),
            (HoleType.Tapped, "Kılavuz deliği (diş açılacak)"), (HoleType.Insert, "Isıyla gömme somun (3B baskı)")
        };

        private void OpenHoleWizard()
        {
            if (_ed.Mode == EditorMode.Edit)
            {
                _ed.Status("Delik sihirbazı nesne modunda çalışır (Tab)");
                return;
            }

            var spec = new DrillSpec { Type = LastHole.Type, Size = LastHole.Size, Fit = LastHole.Fit, Depth = LastHole.Depth };
            var panel = new StackPanel { Width = 380 };
            panel.Children.Add(new TextBlock { Text = Lang.T("Delik sihirbazı"), Style = (Style)FindResource("Text.Title"), FontSize = Shell.TypeScale.Heading, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = Lang.T("Vida ölçüsüne göre delik; ölçüler ISO 273, DIN 974-1, ISO 15065 ve kaba diş kılavuz çaplarından."), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

            ComboBox Combo<T>((T Value, string Name)[] items, T selected)
            {
                var c = new ComboBox { Width = 230, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var it in items) c.Items.Add(new ComboBoxItem { Content = Lang.T(it.Name), Tag = it.Value });
                c.SelectedIndex = Math.Max(0, Array.FindIndex(items, it => Equals(it.Value, selected)));
                return c;
            }

            UIElement Row(string label, UIElement input)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var t = new TextBlock { Text = Lang.T(label), Width = 110, VerticalAlignment = VerticalAlignment.Center };
                t.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                row.Children.Add(t);
                row.Children.Add(input);
                return row;
            }

            var type = Combo(HoleTypes, spec.Type);
            var size = Combo(HoleWizard.Sizes.Select(s => (s.Name, s.Name)).ToArray(), spec.Size);
            var fit = Combo(new[] { (HoleFit.Close, "Sıkı"), (HoleFit.Normal, "Normal"), (HoleFit.Loose, "Gevşek") }, spec.Fit);
            var depth = new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Text = spec.Depth > 0 ? spec.Depth.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) : "0", ToolTip = Lang.T("0: boydan boya (gömme somunda somunun boyu)") };
            panel.Children.Add(Row("Tür", type));
            panel.Children.Add(Row("Ölçü", size));
            panel.Children.Add(Row("Geçme", fit));
            panel.Children.Add(Row("Derinlik (mm)", depth));
            var summary = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(summary);

            void Read()
            {
                spec.Type = type.SelectedItem is ComboBoxItem { Tag: HoleType t } ? t : HoleType.Clearance;
                spec.Size = size.SelectedItem is ComboBoxItem { Tag: string s } ? s : "M3";
                spec.Fit = fit.SelectedItem is ComboBoxItem { Tag: HoleFit f } ? f : HoleFit.Normal;
                spec.Depth = double.TryParse(depth.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0 ? d : 0;
                fit.IsEnabled = spec.Type is HoleType.Clearance or HoleType.Counterbore or HoleType.Countersink;
                summary.Text = Lang.T(HoleWizard.Describe(spec));
            }

            type.SelectionChanged += (_, _) => Read();
            size.SelectionChanged += (_, _) => Read();
            fit.SelectionChanged += (_, _) => Read();
            depth.TextChanged += (_, _) => Read();
            Read();

            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Delik sihirbazı"), 440, panel, out var ok, "Yüzeye yerleştir");
            ok.Click += (_, _) =>
            {
                Read();
                LastHole.Type = spec.Type;
                LastHole.Size = spec.Size;
                LastHole.Fit = spec.Fit;
                LastHole.Depth = spec.Depth;
                dlg.DialogResult = true;
            };
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = System.Windows.Media.VisualTreeHelper.GetChildrenCount(dlg) > 0 ? System.Windows.Media.VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            if (dlg.ShowDialog() != true) return;
            StartHolePlacing(spec);
        }

        /// <summary>Each click on a surface drills the hole there; a right click stops.</summary>
        private void StartHolePlacing(DrillSpec spec)
        {
            var placed = new DrillSpec { Type = spec.Type, Size = spec.Size, Fit = spec.Fit, Depth = spec.Depth };
            _vp.SurfacePick = (id, point, normal) =>
            {
                if (_ed.Doc.Find(id) is { } target) _ed.Drill(target, point, normal, placed);
            };
            _ed.Status(Lang.T($"{HoleWizard.Describe(placed)}: deliği açmak için nesnenin yüzeyine tıklayın · sağ tık: bitir"));
        }

        private void StartShell()
        {
            if (_ed.Mode != EditorMode.Edit)
            {
                _ed.Status("Kabuk düzenleme modunda çalışır: Tab, sonra açılacak yüzleri seçin (3: yüz)");
                return;
            }

            if (_ed.SelectedFaceList().Count == 0) _ed.Status("Kabuk: açılacak yüz seçilmedi; gövde içi boş kapalı kabuk olur");
            _vp.StartModal(ModalKind.Solidify);
        }
    }
}
