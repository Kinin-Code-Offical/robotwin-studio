using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// Scene tree: every object of the world as a hierarchy of what is mounted on what
    /// (a servo on its base, an arm on the servo horn, a wheel on the motor shaft), with
    /// its kind, the port it hangs on, live state while simulating and quick actions.
    /// </summary>
    public partial class WorldPage
    {
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);

        private void BuildScenePalette()
        {
            var world = World;
            if (world == null) return;
            Palette.Children.Add(Note($"{world.Objects.Count} nesne · {world.Cables.Count} kablo · {world.Joints.Count} mekanik bağlantı. " +
                                      "Bir nesneye bağlanan (vidalanan, kola / mile takılan) her şey onun altında görünür."));
            if (world.Objects.Count == 0)
            {
                Palette.Children.Add(Note("Dünya boş. Kartlar, Parçalar veya Mekanik sekmesinden nesne ekleyin."));
                return;
            }

            // Parent of an object = what it is joined to (ObjectA hangs on ObjectB).
            var parent = new Dictionary<string, (string Parent, string Port)>(StringComparer.Ordinal);
            foreach (var j in world.Joints)
            {
                if (string.IsNullOrEmpty(j.ObjectB) || parent.ContainsKey(j.ObjectA)) continue;
                if (Creates(parent, j.ObjectA, j.ObjectB!)) continue;   // never build a cycle
                parent[j.ObjectA] = (j.ObjectB!, j.PortB);
            }

            Palette.ContextMenu = SceneMenu(null);   // [hierarchy] paste, create
            var children = world.Objects.ToLookup(o => parent.TryGetValue(o.Id, out var p) ? p.Parent : string.Empty);
            Palette.Children.Add(Caption("Sahne", 6));
            foreach (var root in children[string.Empty].OrderBy(o => Rank(o.Kind)).ThenBy(o => o.Name, StringComparer.CurrentCulture))
            {
                AddNode(root, 0, children, parent);
            }

            if (world.Cables.Count > 0)
            {
                Palette.Children.Add(Caption("Kablolar"));
                foreach (var c in world.Cables.Take(60))
                {
                    var a = world.Find(c.A.ObjectId);
                    var b = world.Find(c.B.ObjectId);
                    Palette.Children.Add(TreeRow(0, Kind.Cable, c.Name.Length > 0 ? c.Name : "Kablo",
                        $"{a?.Name ?? "?"}.{c.A.Pin} → {b?.Name ?? "?"}.{c.B.Pin} · {c.Awg:0} AWG", false, false, null, () => Scene.Select(c.A.ObjectId), null));
                }
            }
        }

        private static bool Creates(Dictionary<string, (string Parent, string Port)> parent, string child, string newParent)
        {
            for (string? p = newParent; p != null; p = parent.TryGetValue(p, out var q) ? q.Parent : null)
            {
                if (p == child) return true;
            }

            return false;
        }

        private static int Rank(WorldObjectKind k) => k switch
        {
            WorldObjectKind.Board => 0,
            WorldObjectKind.Component => 1,
            WorldObjectKind.Body => 2,
            _ => 3
        };

        private enum Kind
        {
            Board,
            Part,
            Body,
            Model,
            Cable
        }

        private void AddNode(WorldObject o, int depth, ILookup<string, WorldObject> children, Dictionary<string, (string Parent, string Port)> parent)
        {
            var kids = children[o.Id].ToList();
            bool open = !_collapsed.Contains(o.Id);
            var kind = o.Kind switch
            {
                WorldObjectKind.Board => Kind.Board,
                WorldObjectKind.Component => Kind.Part,
                WorldObjectKind.Model => Kind.Model,
                _ => Kind.Body
            };
            string sub = o.Kind switch
            {
                WorldObjectKind.Board => "Kart" + (World?.FindDesign(o.DesignId) is { } d ? " · " + d.Package.Schematic.Parts.Count + " parça" : string.Empty),
                WorldObjectKind.Component => Lib.Find(o.PartId ?? string.Empty)?.Name ?? o.PartId ?? "Parça",
                WorldObjectKind.Model => "3D model",
                _ => RobotTwin.CoreSim.Sandbox.ReadyParts.Of(o)?.Name ?? RobotTwin.CoreSim.Sandbox.DriveParts.Of(o)?.Name ?? o.Shape + (string.IsNullOrEmpty(o.Material) ? string.Empty : " · " + o.Material)   // [objs] [drive]
            };
            if (parent.TryGetValue(o.Id, out var pp) && !string.IsNullOrEmpty(pp.Port)) sub += pp.Port == "horn" ? " · servo koluna takılı" : pp.Port == "shaft" ? " · mile takılı" : " · " + pp.Port;
            if (o.IsStatic) sub += " · sabit";
            if (_session.State != Simulation.SimState.Stopped)
            {
                double t = _session.Temperature(o);
                if (_session.IsFailed(o.Id)) sub = "ARIZALI · " + sub;
                else if (!double.IsNaN(t) && t > 60) sub = $"{t:0} °C · " + sub;
            }

            Palette.Children.Add(TreeRow(depth, kind, o.Name, sub, kids.Count > 0, open, o.Id,
                () => Scene.Select(o.Id),
                kids.Count == 0 ? null : () =>
                {
                    if (!_collapsed.Remove(o.Id)) _collapsed.Add(o.Id);
                    BuildPalette();
                }));
            if (!open) return;
            foreach (var k in kids.OrderBy(x => Rank(x.Kind)).ThenBy(x => x.Name, StringComparer.CurrentCulture)) AddNode(k, depth + 1, children, parent);
        }

        private FrameworkElement TreeRow(int depth, Kind kind, string title, string subtitle, bool hasKids, bool open, string? id, Action click, Action? toggle)
        {
            bool selected = id != null && Scene.Selected == id;
            var row = new Grid { Cursor = Cursors.Hand, Background = selected ? (Brush)FindResource("B.AccentSoft") : Brushes.Transparent, Margin = new Thickness(0, 0, 4, 1) };
            if (id != null && World is { } tw && tw.Find(id) is { } to && !WorldBuilding.ActiveInHierarchy(tw, to)) row.Opacity = 0.5;   // [active] Unity greys inactive rows
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8 + depth * 16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Guide lines of the hierarchy.
            if (depth > 0)
            {
                var guide = new Rectangle { Width = 1, Fill = (Brush)FindResource("B.BorderStrong"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
                row.Children.Add(guide);
            }

            if (hasKids)
            {
                var chevron = new Theme.Icon
                {
                    Data = Geometry.Parse(open ? "M6,9 L12,15 L18,9" : "M9,6 L15,12 L9,18"),
                    Size = 12,
                    Foreground = (Brush)FindResource("B.TextMuted"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var hit = new Border { Background = Brushes.Transparent, Child = chevron, ToolTip = open ? "Daralt" : "Genişlet" };
                hit.MouseLeftButtonUp += (_, e) =>
                {
                    toggle?.Invoke();
                    e.Handled = true;
                };
                Grid.SetColumn(hit, 1);
                row.Children.Add(hit);
            }

            var (glyph, color) = kind switch
            {
                Kind.Board => ("I.Pcb", Color.FromRgb(0x4C, 0xC0, 0x7E)),
                Kind.Part => ("I.Chip", Color.FromRgb(0x5B, 0x8D, 0xFF)),
                Kind.Model => ("I.Cube", Color.FromRgb(0xF0, 0x8A, 0x3C)),
                Kind.Cable => ("I.Cable", Color.FromRgb(0xA0, 0xA0, 0xA8)),
                _ => ("I.Cube", Color.FromRgb(0xA7, 0x7B, 0xFF))
            };
            var icon = new Theme.Icon { Data = (Geometry)FindResource(glyph), Size = 14, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 2);
            row.Children.Add(icon);

            var text = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            if (id != null && id == _renaming && World?.Find(id) is { } renamed) text.Children.Add(RenameBox(renamed));   // [hierarchy] F2
            else text.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource(selected ? "B.Text" : "B.TextSecondary") });
            text.Children.Add(new TextBlock { Text = subtitle, Style = (Style)FindResource("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 3);
            row.Children.Add(text);

            row.MouseEnter += (_, _) => { if (!selected) row.Background = (Brush)FindResource("B.Hover"); };
            row.MouseLeave += (_, _) => { if (!selected) row.Background = Brushes.Transparent; };
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.ClickCount >= 2 && id != null)
                {
                    Scene.Select(id);
                    Scene.FrameCurrent();
                }
                else
                {
                    click();
                }
            };
            if (id != null && World?.Find(id) != null)
            {
                // [hierarchy] Unity's menu (Pages/WorldPage.SceneMenu.cs); a right click also selects the row
                var menu = SceneMenu(id);
                if (hasKids)
                {
                    var fold = new MenuItem { Header = open ? "Alt nesneleri gizle" : "Alt nesneleri göster" };
                    fold.Click += (_, _) => toggle?.Invoke();
                    menu.Items.Insert(0, new Separator());
                    menu.Items.Insert(0, fold);
                }

                row.ContextMenu = menu;
                row.MouseRightButtonDown += (_, _) => Scene.Select(id);
            }

            return row;
        }
    }
}
