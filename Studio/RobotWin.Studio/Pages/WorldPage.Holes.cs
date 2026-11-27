using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [holes] SolidWorks' Hole Alignment: holes of two parts that run on into each other along one axis but stand a
    /// little apart (the bolt that would not go through), with the deviation drawn between the two axes where they meet.
    /// A view: the parts' poses do not change.
    /// </summary>
    public partial class WorldPage
    {
        private static double _holeDeviationMm = 1;

        /// <summary>[palette] Opens the hole alignment check.</summary>
        public void CheckHoleAlignment() => OnHoleAlignment(this, new RoutedEventArgs());

        private void OnHoleAlignment(object sender, RoutedEventArgs e)
        {
            var world = World;
            if (world == null) return;
            var panel = new StackPanel { Width = 460 };
            panel.Children.Add(new TextBlock { Text = Lang.T("Delik hizası"), Style = (Style)FindResource("Text.Title"), FontSize = Shell.TypeScale.Heading, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = Lang.T("Birbirinin devamı olan ama ekseni biraz kayık delikler: cıvatanın geçmeyeceği yerler. Delikler parçaların kendi şekillerinden bulunur."), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var deviation = new TextBox { Text = Fmt(_holeDeviationMm), Width = 80, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = Lang.T("Eksenleri bundan çok ayrık delikler ayrı delik sayılır (mm)") };
            panel.Children.Add(Field("En çok kayma (mm)", deviation));
            string? selected = Scene.Selected;
            var only = new CheckBox { Content = Lang.T("Yalnız seçili parça"), Margin = new Thickness(0, 2, 0, 8), IsEnabled = selected != null };
            panel.Children.Add(only);
            var run = new Button { Content = Lang.T("Hesapla"), Style = (Style)FindResource("Btn.Primary"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(run);
            var summary = new TextBlock { Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(summary);
            var list = new ListBox { MaxHeight = 320 };
            panel.Children.Add(list);

            string NameOf(string id) => world.Find(id)?.Name ?? id;
            void Run()
            {
                _holeDeviationMm = Mm(deviation, _holeDeviationMm);
                deviation.Text = Fmt(_holeDeviationMm);
                var options = new HoleAlignmentOptions
                {
                    MaxDeviationMm = _holeDeviationMm,
                    Only = only.IsChecked == true && selected != null ? new List<string> { selected } : null
                };
                var geometry = new SceneGeometry(Scene);
                int holes = 0;
                foreach (var o in world.Objects) holes += HoleAlignment.HolesOf(o, geometry).Count;
                var found = HoleAlignment.Find(world, geometry, options);
                list.Items.Clear();
                Scene.ShowGap(null, null);
                summary.Text = holes == 0 ? Lang.T("Parçalarda delik bulunmadı")
                    : found.Count == 0 ? Lang.T($"{holes} delik; kayık çift yok")
                    : Lang.T($"{holes} delik; {found.Count} çift kayık");
                summary.SetResourceReference(TextBlock.ForegroundProperty, found.Count == 0 ? "B.Success" : "B.Warning");
                foreach (var m in found)
                {
                    var row = new DockPanel();
                    var v = new TextBlock { Text = Fmt(m.DeviationMm) + " mm", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    v.SetResourceReference(TextBlock.ForegroundProperty, "B.Warning");
                    DockPanel.SetDock(v, Dock.Right);
                    row.Children.Add(v);
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{NameOf(m.A)} Ø{Fmt(m.DiameterAMm)} ↔ {NameOf(m.B)} Ø{Fmt(m.DiameterBMm)}",
                        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
                    });
                    list.Items.Add(new ListBoxItem { Content = row, Tag = m });
                }

                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }

            run.Click += (_, _) => Run();
            deviation.KeyDown += (_, k) =>
            {
                if (k.Key != System.Windows.Input.Key.Enter) return;
                Run();
                k.Handled = true;
            };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem { Tag: HoleMisalignment m }) return;
                Scene.Select(m.A);
                Scene.ShowGap(m.PointA, m.PointB);   // the two axes where the holes meet
                W.Status = Lang.T($"{NameOf(m.A)} ↔ {NameOf(m.B)}: delik eksenleri {Fmt(m.DeviationMm)} mm kayık");
            };
            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Delik hizası"), 520, panel, out var ok, "Kapat", withCancel: false);
            ok.Click += (_, _) => dlg.Close();
            ok.IsCancel = true;
            dlg.Closed += (_, _) => Scene.ShowGap(null, null);
            dlg.Loaded += (_, _) => Run();
            ShowTool(dlg);
        }
    }
}
