using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [hierarchy] Unity's hierarchy menu for Montaj and Dünya: cut, copy, paste (also onto the selected part, mounted
    /// where it is), rename in place (F2), duplicate and delete with what is mounted on the part, move to the view, find
    /// what refers to it, and create by kind; the same commands from the keyboard. The clipboard holds a world fragment
    /// (the parts, their inner joints, cables, mates and couplings), so a copied arm pastes whole into another world too.
    /// </summary>
    public partial class WorldPage
    {
        private static WorldFragment? _clipboard;
        private string? _renaming;

        private static MenuItem MenuEntry(string header, string? keys, Action click, bool enabled = true)
        {
            var m = new MenuItem { Header = header, InputGestureText = keys ?? string.Empty, IsEnabled = enabled };
            m.Click += (_, _) => click();
            return m;
        }

        /// <summary>The right-click menu of a scene tree row (an object), or of the tree's empty room (id null).</summary>
        private ContextMenu SceneMenu(string? id)
        {
            var menu = new ContextMenu();
            var world = World;
            var o = id != null ? world?.Find(id) : null;
            if (o != null)
            {
                menu.Items.Add(MenuEntry("Kes", "Ctrl+X", () => { Scene.Select(o.Id); CutSelection(); }));
                menu.Items.Add(MenuEntry("Kopyala", "Ctrl+C", () => { Scene.Select(o.Id); CopySelection(); }));
            }

            menu.Items.Add(MenuEntry("Yapıştır", "Ctrl+V", () => PasteClipboard(false), _clipboard != null));
            if (o != null)
            {
                menu.Items.Add(MenuEntry("Bunun üstüne takılı olarak yapıştır", "Ctrl+Shift+V", () => { Scene.Select(o.Id); PasteClipboard(true); }, _clipboard != null));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuEntry("Yeniden adlandır", "F2", () => StartRename(o.Id)));
                menu.Items.Add(MenuEntry(WorldBuilding.IsActive(o) ? "Devre dışı bırak" : "Etkinleştir", "Alt+Shift+A", () => ToggleActive(o)));   // [active]
                menu.Items.Add(MenuEntry("Çoğalt", "Ctrl+D", () => { Scene.Select(o.Id); Duplicate(); }));
                menu.Items.Add(MenuEntry("Desenle çoğalt…", string.Empty, () =>   // [pattern] a row or a ring of copies
                {
                    if (!SelectionIds().Contains(o.Id)) Scene.Select(o.Id);
                    ShowPatternDialog();
                }));
                menu.Items.Add(MenuEntry("Sil (takılı olanlarla)", "Del", () => { Scene.Select(o.Id); Delete(); }));
                menu.Items.Add(MenuEntry("Yalnız bunu sil (takılı olanlar kalır)", null, () => DeleteOnly(o)));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuEntry("Odakla", "F", () =>
                {
                    Scene.Select(o.Id);
                    Scene.FrameSelected();
                }));
                menu.Items.Add(MenuEntry("Görünüme taşı", "Ctrl+Alt+F", () => { Scene.Select(o.Id); MoveToView(); }));
                menu.Items.Add(MenuEntry("Görünümle hizala", "Ctrl+Shift+F", () => { Scene.Select(o.Id); AlignWithView(); }));   // [align-view]
                menu.Items.Add(MenuEntry("Görünümü seçime hizala", string.Empty, () => { Scene.Select(o.Id); AlignViewToSelected(); }));
                menu.Items.Add(MenuEntry("Sahnede referansları bul", null, () => FindReferences(o)));
                menu.Items.Add(MenuEntry("Alt nesneleri seç", null, () => SelectChildren(o.Id)));   // [multiselect] Unity's Select Children
                menu.Items.Add(MenuEntry("Seçimi tersine çevir", "Ctrl+I", InvertSelection));
                // [default-parent] Unity's Set as Default Parent: new objects are mounted on it as they are made
                bool isDefault = string.Equals(World?.DefaultParent, o.Id, StringComparison.OrdinalIgnoreCase);
                menu.Items.Add(MenuEntry(isDefault ? "Varsayılan üst nesne olmaktan çıkar" : "Varsayılan üst nesne yap", null, () =>
                    Edit(isDefault ? "Varsayılan üst nesne kaldırıldı" : $"'{o.Name}' varsayılan üst nesne: yeni nesneler ona takılır", () => World!.DefaultParent = isDefault ? null : o.Id)));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenu());
            if (id != null) AddToolItems(menu, id);   // [pid] PID tuner, arm kinematics, drive kinematics (Pages/WorldPage.Pid.cs)
            return menu;
        }

        /// <summary>Unity's Create ▸: the kinds the palette's tabs hold, by name.</summary>
        private MenuItem CreateMenu()
        {
            var create = new MenuItem { Header = "Oluştur" };
            var body = new MenuItem { Header = "Gövde" };
            body.Items.Add(MenuEntry("Kutu", null, () => AddBody("box", 0.05, 0.05, 0.05)));
            body.Items.Add(MenuEntry("Plaka", null, () => AddBody("box", 0.10, 0.08, 0.004)));
            body.Items.Add(MenuEntry("Silindir", null, () => AddBody("cylinder", 0.04, 0.04, 0.06)));
            body.Items.Add(MenuEntry("Küre", null, () => AddBody("sphere", 0.04, 0.04, 0.04)));
            create.Items.Add(body);

            // [create] Unity's Create ▸ by kind: every ready part of the library under its category, filled when opened
            // (each tree row has its own menu, so the few hundred items are not built for every row)
            var parts = new MenuItem { Header = "Parça" };
            parts.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
            parts.SubmenuOpened += (_, e) =>
            {
                if (e.OriginalSource != parts || parts.Tag is true) return;
                parts.Tag = true;
                parts.Items.Clear();
                foreach (var group in Lib.Presets.GroupBy(x => x.Category).OrderBy(g => g.Key, StringComparer.CurrentCulture))
                {
                    var category = new MenuItem { Header = group.Key };
                    foreach (var preset in group.OrderBy(x => x.Name, StringComparer.CurrentCulture))
                    {
                        string id = preset.Id;
                        var item = MenuEntry(preset.Name, null, () => AddComponent(id));
                        item.ToolTip = preset.Description;
                        category.Items.Add(item);
                    }

                    parts.Items.Add(category);
                }
            };
            create.Items.Add(parts);
            void Tab(string header, System.Windows.Controls.Primitives.ToggleButton tab)
            {
                create.Items.Add(MenuEntry(header, null, () =>
                {
                    tab.IsChecked = true;
                    OnPaletteTab(tab, new RoutedEventArgs());
                }));
            }

            Tab("Kart… (Kartlar sekmesi)", TabBoards);
            Tab("Parça… (Parçalar sekmesi)", TabParts);
            Tab("Mekanik… (Mekanik sekmesi)", TabMech);
            Tab("Ortam… (Ortam sekmesi)", TabEnv);
            EmptyMenuItems(create);   // [empty] Unity's Create Empty / Child / Parent on top (Pages/WorldPage.Empty.cs)
            return create;
        }

        // ================================================================ clipboard

        private void CopySelection()
        {
            var world = World;
            if (world == null || Scene.Selected == null) return;
            _clipboard = WorldFragment.Take(world, SelectionIds());   // [multiselect] the whole selection
            W.Status = Shell.Lang.T($"Kopyalandı: {string.Join(", ", _clipboard.Objects.Select(o => o.Name))}");
        }

        private void CutSelection()
        {
            var world = World;
            if (world == null || world.Find(Scene.Selected) is not { } o) return;
            var ids = SelectionIds().Where(id => world.Find(id) is { } x && !DeleteBlocked(x)).ToList();   // [multiselect]
            if (ids.Count == 0) return;
            var fragment = WorldFragment.Take(world, ids);
            Edit(Shell.Lang.T(ids.Count > 1 ? $"{ids.Count} nesne kesildi" : $"{o.Name} kesildi"), () => WorldFragment.RemoveFrom(world, ids));
            _clipboard = fragment;
            Scene.Select(null);
        }

        /// <summary>Pastes the clipboard a little aside (or, onto the selected part, mounted where it lands).</summary>
        private void PasteClipboard(bool ontoSelected)
        {
            var world = World;
            var fragment = _clipboard;
            if (world == null || fragment == null || fragment.IsEmpty) return;
            var host = ontoSelected ? world.Find(Scene.Selected) : null;
            if (ontoSelected && host == null)
            {
                W.Status = "Önce üstüne takılacak parçayı seçin";
                return;
            }

            // into the same world: beside the original; into another: where the view looks
            var first = fragment.Objects[0];
            Vec3 offset;
            if (world.Objects.Any(x => string.Equals(x.Name, first.Name, StringComparison.OrdinalIgnoreCase)))
                offset = new Vec3(0.05, 0, 0);
            else
            {
                var t = Scene.ViewTarget;
                offset = new Vec3(t.X - first.Pose.X, t.Y - first.Pose.Y, 0);
            }

            System.Collections.Generic.List<WorldObject> roots = new System.Collections.Generic.List<WorldObject>();
            Edit(Shell.Lang.T($"Yapıştırıldı: {string.Join(", ", fragment.Objects.Select(x => x.Name))}"), () =>
            {
                roots = WorldFragment.FromJson(fragment.ToJson())!.PasteInto(world, offset);
                if (host == null) return;
                foreach (var r in roots)
                    world.Joints.Add(new WorldJoint { Id = world.NextId("J"), ObjectA = r.Id, ObjectB = host.Id, Kind = WorldJointKind.Fixed });
            });
            if (roots.Count > 0) Scene.Select(roots[0].Id);
        }

        // ================================================================ rename, delete, view

        /// <summary>F2: the row's name becomes a box; Enter keeps it, Esc leaves it.</summary>
        private void StartRename(string? id)
        {
            if (id == null || World?.Find(id) == null) return;
            if (TabScene.IsChecked != true)
            {
                TabScene.IsChecked = true;
                OnPaletteTab(TabScene, new RoutedEventArgs());
            }

            _renaming = id;
            BuildPalette();
        }

        /// <summary>The box a row's name turns into while renaming.</summary>
        private TextBox RenameBox(WorldObject o)
        {
            var box = new TextBox { Text = o.Name, Margin = new Thickness(0, 2, 0, 2) };
            bool done = false;
            void Finish(bool keep)
            {
                if (done) return;
                done = true;
                _renaming = null;
                string name = box.Text.Trim();
                if (keep && name.Length > 0 && name != o.Name) Edit(Shell.Lang.T($"Ad değişti: {name}"), () => o.Name = name);
                else Dispatcher.BeginInvoke(new Action(BuildPalette));
            }

            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Finish(true);
                else if (e.Key == Key.Escape) Finish(false);
                else return;
                e.Handled = true;
            };
            box.LostKeyboardFocus += (_, _) => Finish(true);
            return box;
        }

        private void DeleteOnly(WorldObject o)
        {
            var world = World;
            if (world == null || DeleteBlocked(o)) return;
            Edit(Shell.Lang.T($"{o.Name} silindi"), () => world.Remove(o));
            Scene.Select(null);
        }

        /// <summary>Unity's Move To View: the part (with what is mounted on it) to where the view looks, on its own height.</summary>
        /// <summary>[align-view] Unity's Align With View (Ctrl+Shift+F): the part where the camera stands, looking where it looks (its +X ahead).</summary>
        private void AlignWithView()
        {
            var world = World;
            if (world == null || world.Find(Scene.Selected) is not { } o) return;
            var (eye, yaw, pitch) = Scene.ViewPose;
            Edit(Shell.Lang.T($"{o.Name} görünümle hizalandı"), () =>
            {
                world.MoveAssembly(o, eye.X - o.Pose.X, eye.Y - o.Pose.Y, Math.Max(0, eye.Z) - o.Pose.Z);
                TurnTo(world, o, 0, pitch * 180 / Math.PI, (yaw * 180 / Math.PI + 360) % 360);
            });
        }

        /// <summary>[align-view] Unity's Align View to Selected: the camera looks out of the part, along its +X (a camera part's view).</summary>
        private void AlignViewToSelected()
        {
            if (World?.Find(Scene.Selected) is not { } o) return;
            var q = o.Pose.Orientation;
            var ahead = q.Rotate(new RobotTwin.CoreSim.Mechanics.Vec3(1, 0, 0));
            var at = o.Pose.Position + q.Rotate(new RobotTwin.CoreSim.Mechanics.Vec3(0, 0, o.SizeZ / 2));
            Scene.LookFrom(new System.Windows.Media.Media3D.Point3D(at.X, at.Y, at.Z), new System.Windows.Media.Media3D.Vector3D(ahead.X, ahead.Y, ahead.Z));
            W.Status = Shell.Lang.T($"Görünüm {o.Name} nesnesinin gözünden");
        }

        private void MoveToView()
        {
            var world = World;
            if (world == null || world.Find(Scene.Selected) is not { } o) return;
            var t = Scene.ViewTarget;
            Edit(Shell.Lang.T($"{o.Name} görünüme taşındı"), () => world.MoveAssembly(o, t.X - o.Pose.X, t.Y - o.Pose.Y, 0));
        }

        /// <summary>What refers to a part: cables, joints, mates; said on the status line, the first cable picked.</summary>
        private void FindReferences(WorldObject o)
        {
            var world = World;
            if (world == null) return;
            var cables = world.Cables.Where(c => c.A.ObjectId == o.Id || c.B.ObjectId == o.Id).ToList();
            var joints = world.Joints.Where(j => j.ObjectA == o.Id || j.ObjectB == o.Id).ToList();
            int mates = world.Mates?.Count(m => m.A.Object == o.Id || m.B.Object == o.Id) ?? 0;
            var names = joints.Select(j => world.Find(j.ObjectA == o.Id ? j.ObjectB : j.ObjectA)?.Name).Where(n => n != null).Distinct().ToList();
            W.Status = Shell.Lang.T($"{o.Name}: {cables.Count} kablo, {joints.Count} bağlantı, {mates} ilişki") + (names.Count > 0 ? " · " + string.Join(", ", names) : string.Empty);
            if (cables.Count > 0) Scene.SelectCable(cables[0].Id);
        }
    }
}
