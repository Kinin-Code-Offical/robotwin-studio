using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [constraints] Unity's constraint cards: Aim, Position, Rotation and Parent constraint. Each picks its source from
    /// the scene and, like Unity's Activate, keeps the offset the part has from it then; the part is placed from its
    /// source after every edit (CoreSim Sandbox/WorldConstraints.cs, applied in Workspace.CommitWorldChange). A run moves
    /// parts by physics alone, so the card says so.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>
        /// [constraints] The snapshot "constraints": a camera that keeps looking at a cart and a flag that keeps above it,
        /// the cart then moved and turned; both followed it.
        /// </summary>
        private void ConstraintSnapshot()
        {
            var w = new WorldDocument { Title = "Kısıtlar" };
            var floor = w.AddBody("box", 0.8, 0.6, 0.01, new WorldPose { Z = -0.005 });
            floor.Name = "Zemin";
            floor.IsStatic = true;
            floor.Color = "#8A8F98";
            var cart = w.AddBody("box", 0.12, 0.08, 0.04, new WorldPose { X = 0.25, Y = 0.1, Z = 0.02 });
            cart.Name = "Araba";
            cart.Color = "#3B82C4";
            var camera = w.AddBody("box", 0.07, 0.03, 0.03, new WorldPose { X = -0.25, Y = -0.18, Z = 0.25 });
            camera.Name = "Kamera";
            camera.IsStatic = true;
            camera.Color = "#2A2D34";
            var flag = w.AddBody("box", 0.01, 0.01, 0.1, new WorldPose { X = 0.25, Y = 0.1, Z = 0.1 });
            flag.Name = "Bayrak";
            flag.Color = "#E87A2C";
            WorldConstraints.AddAim(camera, cart);
            WorldConstraints.AddPosition(flag, cart);
            cart.Pose.X = 0.05;
            cart.Pose.Y = 0.2;
            cart.Pose.Yaw = 30;
            WorldConstraints.Apply(w);
            _main.NewWorld(w);
            OnShown();
            Scene.FrameAll();
            Scene.Select(camera.Id);
            BuildInspector();
            InspectorPane.UpdateLayout();
            // the constraint card at the top of the inspector (the cards are rebuilt around their titles, so it is found by its title)
            string title = Shell.Lang.T(ConstraintTitle(WorldConstraints.AimKey));
            TextBlock? Find(DependencyObject at)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(at); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(at, i);
                    if (child is TextBlock tb && tb.Text == title) return tb;
                    if (Find(child) is { } found) return found;
                }

                return null;
            }

            if (Find(Inspector) is { } card)
            {
                var top = card.TransformToAncestor(Inspector).Transform(new Point(0, 0)).Y;
                InspectorPane.ScrollToVerticalOffset(Math.Max(0, top - 16));
            }
        }

        private static string ConstraintTitle(string key) => key switch
        {
            WorldConstraints.AimKey => "Hedefe bak kısıtı",
            WorldConstraints.PositionKey => "Konum kısıtı",
            WorldConstraints.RotationKey => "Dönüş kısıtı",
            _ => "Üst nesne kısıtı"
        };

        /// <summary>The "Kısıtlar" group of Add Component: the constraints the part does not carry yet.</summary>
        private MenuItem? ConstraintMenu(WorldObject o)
        {
            var group = new MenuItem
            {
                Header = "Kısıtlar",
                ToolTip = "Parça bir kaynağa göre yerleşir; kaynak taşınınca izler. Düzenlerken tutar, çalışırken parçayı fizik yürütür."
            };
            void Item(string key, string tip)
            {
                if (WorldComponents.Has(o, key)) return;
                var mi = new MenuItem { Header = ConstraintTitle(key), ToolTip = tip };
                mi.Click += (_, _) =>
                {
                    Edit(ConstraintTitle(key) + " eklendi", () =>
                    {
                        o.Parameters[key] = "";   // no source yet: chosen on its card
                        if (key == WorldConstraints.AimKey) o.Parameters[WorldConstraints.AimAxisKey] = "+X";
                    });
                    BuildInspector();
                };
                group.Items.Add(mi);
            }

            Item(WorldConstraints.AimKey, "Parçanın bir ekseni (kamerada, sensörde +X) hep hedefe bakar, yan yatmadan.");
            Item(WorldConstraints.PositionKey, "Parça kaynağıyla aynı uzaklıkta kalır; seçilen eksenlerde izler.");
            Item(WorldConstraints.RotationKey, "Parça kaynağıyla birlikte döner, aradaki açı korunur.");
            Item(WorldConstraints.ParentKey, "Parça kaynağına takılıymış gibi onunla taşınır ve döner, hiyerarşi değişmeden.");
            return group.Items.Count > 0 ? group : null;
        }

        private void ConstraintSections(WorldDocument world, WorldObject o)
        {
            foreach (var key in WorldConstraints.Keys)
            {
                if (!WorldComponents.Has(o, key)) continue;
                string title = ConstraintTitle(key);
                void Remove()
                {
                    foreach (var k in WorldConstraints.KeysOf(key)) o.Parameters.Remove(k);
                    WorldComponents.ForgetOff(o, key);
                }

                if (OffCard(o, key, title, title + " kaldırıldı", Remove)) continue;
                var head = FeatureHeader(title, title + " kaldırıldı", Remove, toggle: key);
                Inspector.Children.Add(head);
                SourceRow(world, o, key);
                var source = world.Find(o.Parameters.TryGetValue(key, out var id) ? id : null);
                if (key == WorldConstraints.AimKey) AimAxisRow(o);
                if (key == WorldConstraints.PositionKey && source != null) PositionRows(o);
                if (key == WorldConstraints.RotationKey && source != null)
                {
                    var r = WorldConstraints.TryVector(o.Parameters.TryGetValue(WorldConstraints.RotationOffsetKey, out var s) ? s : null) ?? Vec3.Zero;
                    Inspector.Children.Add(Vector3Row("Açı farkı (°) X · Y · Z", r.X, r.Y, r.Z, (x, y, z) => Edit("Açı farkı değişti", () =>
                        o.Parameters[WorldConstraints.RotationOffsetKey] = WorldConstraints.Text(new Vec3(x, y, z)))));
                }

                if (source != null && key != WorldConstraints.AimKey)
                    Inspector.Children.Add(Action("Şimdiki yerini koru", () => Edit(title + ": yeri korundu", () => Activate(o, key, source)),
                        "Parçanın kaynağa göre şimdiki yeri ve açısı yeni fark olur."));
                Inspector.Children.Add(Note(source == null
                    ? "Kaynak seçin: parça ona göre yerleşir."
                    : "Kaynak taşınınca ya da döndürülünce parça (ve ona takılı olanlar) izler. Çalışırken parçaları yalnız fizik yürütür: birlikte gitmesi gereken bir parçayı takın ya da hizalayın."));
            }
        }

        private static void Activate(WorldObject o, string key, WorldObject source)
        {
            switch (key)
            {
                case WorldConstraints.PositionKey:
                    WorldConstraints.AddPosition(o, source, o.Parameters.TryGetValue(WorldConstraints.PositionAxesKey, out var axes) ? axes : "xyz");
                    break;
                case WorldConstraints.RotationKey:
                    WorldConstraints.AddRotation(o, source);
                    break;
                case WorldConstraints.ParentKey:
                    WorldConstraints.AddParent(o, source);
                    break;
                default:
                    WorldConstraints.AddAim(o, source, o.Parameters.TryGetValue(WorldConstraints.AimAxisKey, out var ax) ? ax : "+X");
                    break;
            }
        }

        private void SourceRow(WorldDocument world, WorldObject o, string key)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = Shell.Lang.T("Parçanın göre yerleştiği nesne") };
            combo.Items.Add(new ComboBoxItem { Content = Shell.Lang.T("(kaynak yok)"), Tag = "" });
            string current = o.Parameters.TryGetValue(key, out var id) ? id : "";
            foreach (var s in world.Objects.Where(s => WorldConstraints.CanUse(world, o, s)))
            {
                var item = new ComboBoxItem { Content = s.Name, Tag = s.Id };
                combo.Items.Add(item);
                if (string.Equals(s.Id, current, StringComparison.OrdinalIgnoreCase)) combo.SelectedItem = item;
            }

            combo.SelectedItem ??= combo.Items[0];
            combo.SelectionChanged += (_, _) =>
            {
                string pick = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (string.Equals(pick, current, StringComparison.OrdinalIgnoreCase)) return;
                Edit(ConstraintTitle(key) + ": kaynak seçildi", () =>
                {
                    if (world.Find(pick) is { } source) Activate(o, key, source);
                    else o.Parameters[key] = "";
                });
                BuildInspector();
            };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var label = new TextBlock { Text = Shell.Lang.T("Kaynak"), Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(combo);
            Inspector.Children.Add(row);
        }

        private void AimAxisRow(WorldObject o)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = Shell.Lang.T("Hedefe bakan kendi ekseni: kamera ve mesafe sensörü +X boyunca bakar") };
            string current = o.Parameters.TryGetValue(WorldConstraints.AimAxisKey, out var a) ? a : "+X";
            foreach (var ax in WorldConstraints.Axes) combo.Items.Add(ax);
            combo.SelectedItem = WorldConstraints.Axes.Contains(current) ? current : "+X";
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string pick && pick != current) Edit("Bakan eksen değişti", () => o.Parameters[WorldConstraints.AimAxisKey] = pick);
            };
            var row = new DockPanel();
            var label = new TextBlock { Text = Shell.Lang.T("Bakan eksen"), Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(combo);
            Inspector.Children.Add(row);
        }

        private void PositionRows(WorldObject o)
        {
            string axes = o.Parameters.TryGetValue(WorldConstraints.PositionAxesKey, out var s) ? s.ToLowerInvariant() : "xyz";
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = Shell.Lang.T("İzlenen eksenler"), Width = 110, VerticalAlignment = VerticalAlignment.Center });
            foreach (char c in "xyz")
            {
                char axis = c;
                var box = new CheckBox { Content = char.ToUpperInvariant(axis).ToString(), IsChecked = axes.Contains(axis), Margin = new Thickness(0, 0, 12, 0) };
                box.Click += (_, _) => Edit("İzlenen eksenler değişti", () =>
                {
                    string now = string.Concat("xyz".Where(k => k == axis ? box.IsChecked == true : axes.Contains(k)));
                    o.Parameters[WorldConstraints.PositionAxesKey] = now;
                });
                row.Children.Add(box);
            }

            Inspector.Children.Add(row);
            var off = WorldConstraints.TryVector(o.Parameters.TryGetValue(WorldConstraints.PositionOffsetKey, out var t) ? t : null) ?? Vec3.Zero;
            Inspector.Children.Add(Vector3Row("Uzaklık (mm) X · Y · Z", off.X * 1000, off.Y * 1000, off.Z * 1000, (x, y, z) => Edit("Uzaklık değişti", () =>
                o.Parameters[WorldConstraints.PositionOffsetKey] = WorldConstraints.Text(new Vec3(x, y, z) / 1000))));
        }
    }
}
