using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [interference] SolidWorks' Interference Detection (studied in place): the options it asks (touching counts,
    /// joined parts too, only the selected part), Hesapla, and the interfering pairs with their volume; a pair picked
    /// selects its first part and says where the overlap is.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>[palette] Opens the interference check.</summary>
        public void CheckInterference() => OnInterference(this, new RoutedEventArgs());

        private void OnInterference(object sender, RoutedEventArgs e)
        {
            var world = World;
            if (world == null) return;
            var panel = new StackPanel { Width = 460 };
            panel.Children.Add(new TextBlock { Text = Lang.T("Çakışma algılama"), Style = (Style)FindResource("Text.Title"), FontSize = Shell.TypeScale.Heading, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = Lang.T("Aynı yeri kaplayan parça çiftleri ve ortak hacimleri (parçaların dış şekilleriyle)."), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var touching = new CheckBox { Content = Lang.T("Değen yüzeyleri de çakışma say"), Margin = new Thickness(0, 0, 0, 4) };
            var joined = new CheckBox { Content = Lang.T("Eklemle bağlı parçaları da denetle"), Margin = new Thickness(0, 0, 0, 4), ToolTip = Lang.T("Servo koluyla servo, tekerle motor gibi bilerek iç içe olanlar") };
            string? selected = Scene.Selected;
            var only = new CheckBox { Content = Lang.T("Yalnız seçili parça"), Margin = new Thickness(0, 0, 0, 8), IsEnabled = selected != null };
            panel.Children.Add(touching);
            panel.Children.Add(joined);
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
                var options = new InterferenceOptions
                {
                    TouchingCounts = touching.IsChecked == true,
                    IncludeJoined = joined.IsChecked == true,
                    Only = only.IsChecked == true && selected != null ? new List<string> { selected } : null
                };
                var found = Interference.Find(world, new SceneGeometry(Scene), options);
                list.Items.Clear();
                summary.Text = found.Count == 0 ? Lang.T("Çakışma yok") : Lang.T($"{found.Count} çakışma");
                summary.SetResourceReference(TextBlock.ForegroundProperty, found.Count == 0 ? "B.Success" : "B.Warning");
                foreach (var p in found)
                {
                    var row = new DockPanel();
                    string volume = p.VolumeMm3 > 0 ? p.VolumeMm3.ToString(p.VolumeMm3 < 10 ? "0.##" : "0", CultureInfo.CurrentCulture) + " mm³" : Lang.T("değiyor");
                    var v = new TextBlock { Text = volume + (p.Joined ? " · " + Lang.T("bağlı") : string.Empty), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    v.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                    DockPanel.SetDock(v, Dock.Right);
                    row.Children.Add(v);
                    row.Children.Add(new TextBlock { Text = NameOf(p.A) + " ↔ " + NameOf(p.B), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                    list.Items.Add(new ListBoxItem { Content = row, Tag = p });
                }
            }

            run.Click += (_, _) => Run();
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem { Tag: InterferencePair p }) return;
                Scene.Select(p.A);
                W.Status = Lang.T($"{NameOf(p.A)} ↔ {NameOf(p.B)}: ortak hacim {p.VolumeMm3:0.#} mm³, ortası ({p.Center.X * 1000:0.#}, {p.Center.Y * 1000:0.#}, {p.Center.Z * 1000:0.#}) mm");
            };
            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Çakışma algılama"), 520, panel, out var ok, "Kapat", withCancel: false);
            ok.Click += (_, _) => dlg.Close();
            ok.IsCancel = true;
            dlg.Loaded += (_, _) => Run();
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = System.Windows.Media.VisualTreeHelper.GetChildrenCount(dlg) > 0 ? System.Windows.Media.VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            dlg.Show();   // modeless: the scene stays usable while the list is open (SolidWorks' PropertyManager)
        }

        /// <summary>The scene's own shapes (its 3D models' hulls) as world geometry.</summary>
        private sealed class SceneGeometry : RobotTwin.CoreSim.Automation.IWorldGeometry
        {
            private readonly World.WorldScene _scene;

            public SceneGeometry(World.WorldScene scene) => _scene = scene;

            public WorldBodyGeometry GeometryOf(WorldObject o) => _scene.GeometryOf(o);

            public double CableLength(WorldCable cable) => _scene.CableLength(cable);
        }
    }
}
