using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [layers] Unity's Tag and Layer under the object's name: a tag to name what a part is (to scripts and searches), a
    /// layer for collisions, with "Etiket ekle…", "Katman ekle…" and the layer collision matrix (Unity's Physics settings)
    /// one choice away. A change applies to the whole selection, as in Unity. CoreSim Sandbox/WorldLayers.cs holds the rules.
    /// </summary>
    public partial class WorldPage
    {
        private const string AddTagItem = "Etiket ekle…";
        private const string AddLayerItem = "Katman ekle…";
        private const string MatrixItem = "Çarpışma matrisi…";

        private FrameworkElement TagLayerRow(WorldDocument world, WorldObject o)
        {
            // two short rows (a narrow inspector cut "Varsayılan" short when they shared one)
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            List<WorldObject> Targets() => SelectionIds().Select(world.Find).Where(x => x != null).Cast<WorldObject>().DefaultIfEmpty(o).ToList();

            // ---- tag
            var tags = new ComboBox { Margin = new Thickness(10, 0, 0, 4), ToolTip = Lang.T("Etiket: parçanın ne olduğunu betiklere ve aramalara söyleyen bir sözcük (engel, hedef …)") };
            tags.Items.Add(new ComboBoxItem { Content = Lang.T("Etiketsiz"), Tag = string.Empty });
            foreach (var t in WorldLayers.Tags(world)) tags.Items.Add(new ComboBoxItem { Content = t, Tag = t });
            tags.Items.Add(new Separator());
            tags.Items.Add(new ComboBoxItem { Content = Lang.T(AddTagItem), Tag = AddTagItem });
            string now = WorldLayers.TagOf(o) ?? string.Empty;
            tags.SelectedItem = tags.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, now, StringComparison.CurrentCultureIgnoreCase)) ?? tags.Items[0];
            tags.SelectionChanged += (_, _) =>
            {
                if (tags.SelectedItem is not ComboBoxItem item) return;
                string? tag = (string)item.Tag;
                if (tag == AddTagItem)
                {
                    tag = Window.GetWindow(this) is { } owner ? Dialog.Prompt(owner, Lang.T("Etiket ekle"), Lang.T("Yeni etiket:"), string.Empty) : null;
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        BuildInspector();
                        return;
                    }
                }

                if (string.Equals(tag ?? string.Empty, now, StringComparison.CurrentCulture)) return;
                Edit(string.IsNullOrEmpty(tag) ? "Etiket kaldırıldı" : $"Etiket: {tag}", () =>
                {
                    foreach (var x in Targets()) WorldLayers.SetTag(x, tag);
                });
                BuildInspector();
            };

            // ---- layer
            var layers = new ComboBox { Margin = new Thickness(10, 0, 0, 0), ToolTip = Lang.T("Katman: çarpışmalar için grup; çarpışma matrisi hangi katmanların birbirinin içinden geçtiğini söyler") };
            foreach (int l in WorldLayers.Used(world))
                layers.Items.Add(new ComboBoxItem { Content = Lang.T(WorldLayers.NameOf(world, l)), Tag = l, ToolTip = Lang.T("Katman") + " " + l });
            layers.Items.Add(new Separator());
            layers.Items.Add(new ComboBoxItem { Content = Lang.T(AddLayerItem), Tag = -1 });
            layers.Items.Add(new ComboBoxItem { Content = Lang.T(MatrixItem), Tag = -2 });
            int current = WorldLayers.LayerOf(o);
            layers.SelectedItem = layers.Items.OfType<ComboBoxItem>().First(i => (int)i.Tag == current);
            layers.SelectionChanged += (_, _) =>
            {
                if (layers.SelectedItem is not ComboBoxItem item) return;
                int pick = (int)item.Tag;
                if (pick == -2)
                {
                    BuildInspector();
                    ShowCollisionMatrix(world);
                    return;
                }

                if (pick == -1)
                {
                    string? name = Window.GetWindow(this) is { } owner ? Dialog.Prompt(owner, Lang.T("Katman ekle"), Lang.T("Yeni katmanın adı:"), string.Empty) : null;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        BuildInspector();
                        return;
                    }

                    Edit($"Katman eklendi: {name}", () =>
                    {
                        int added = WorldLayers.Add(world, name!);
                        if (added > 0)
                            foreach (var x in Targets())
                                WorldLayers.SetLayer(x, added);
                    });
                    BuildInspector();
                    return;
                }

                if (pick == current) return;
                Edit($"Katman: {WorldLayers.NameOf(world, pick)}", () =>
                {
                    foreach (var x in Targets()) WorldLayers.SetLayer(x, pick);
                });
                BuildInspector();
            };

            void Put(UIElement e, int row, int col)
            {
                Grid.SetRow(e, row);
                Grid.SetColumn(e, col);
                grid.Children.Add(e);
            }

            Put(new TextBlock { Text = Lang.T("Etiket", "nesne"), Foreground = (Brush)FindResource("B.TextSecondary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) }, 0, 0);
            Put(tags, 0, 1);
            Put(new TextBlock { Text = Lang.T("Katman"), Foreground = (Brush)FindResource("B.TextSecondary"), VerticalAlignment = VerticalAlignment.Center }, 1, 0);
            Put(layers, 1, 1);
            return grid;
        }

        /// <summary>Unity's Layer Collision Matrix: a box for every pair of layers in use; unticked, they pass through each other.</summary>
        private void ShowCollisionMatrix(WorldDocument world)
        {
            var used = WorldLayers.Used(world);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var _ in used) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            for (int r = 0; r <= used.Count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void Put(UIElement e, int row, int col)
            {
                Grid.SetRow(e, row);
                Grid.SetColumn(e, col);
                grid.Children.Add(e);
            }

            // columns from the last layer to the first, rows from the first: Unity's triangle
            var cols = used.Reverse().ToList();
            for (int c = 0; c < cols.Count; c++)
                Put(new TextBlock { Text = cols[c].ToString(), ToolTip = WorldLayers.NameOf(world, cols[c]), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.SemiBold }, 0, c + 1);
            for (int r = 0; r < used.Count; r++)
            {
                int a = used[r];
                Put(new TextBlock { Text = a + ": " + Lang.T(WorldLayers.NameOf(world, a)), Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center }, r + 1, 0);
                for (int c = 0; c < cols.Count; c++)
                {
                    int b = cols[c];
                    if (b < a) continue;   // each pair once
                    var box = new CheckBox { IsChecked = WorldLayers.Collide(world, a, b), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = $"{WorldLayers.NameOf(world, a)} ↔ {WorldLayers.NameOf(world, b)}" };
                    box.Click += (_, _) => Edit(box.IsChecked == true ? "Katmanlar çarpışıyor" : "Katmanlar birbirinin içinden geçiyor", () => WorldLayers.SetCollide(world, a, b, box.IsChecked == true));
                    Put(box, r + 1, c + 1);
                }
            }

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = Lang.T("İşaretli kutu iki katmanın parçalarının çarpıştığını gösterir; işareti kaldırınca birbirinin içinden geçerler (zemin, arazi ve kablolar her zaman çarpışır). Çalıştırmada uygulanır."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = (Brush)FindResource("B.TextSecondary")
            });
            panel.Children.Add(grid);
            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Çarpışma matrisi"), Math.Max(420, 200 + 34 * used.Count), panel, out var ok, Lang.T("Kapat"), withCancel: false);
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
