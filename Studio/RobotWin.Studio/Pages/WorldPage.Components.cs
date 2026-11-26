using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [components] Unity's Inspector for Montaj and Dünya: every section of the inspector is a component card with its
    /// own header (fold it, its ⋮ menu: reset, copy and paste its values), folds remembered while the app runs; the
    /// transform's menu copies and pastes position and rotation between parts. "Bileşen ekle" opens Unity's Add
    /// Component: a search box over the features the part can be given, by category, a category opening its list.
    /// </summary>
    public partial class WorldPage
    {
        private static readonly HashSet<string> FoldedCards = new HashSet<string>(StringComparer.Ordinal);
        private static Vec3? _copiedPosition;
        private static (double Roll, double Pitch, double Yaw)? _copiedRotation;
        private WorldObject? _inspected;
        private Popup? _lastPicker;

        private bool IsCaption(UIElement e) => e is TextBlock t && ReferenceEquals(t.Style, TryFindResource("Text.Overline"));

        /// <summary>Turns the inspector's captions and what follows each into component cards (after it is built).</summary>
        private void CardifyInspector()
        {
            var kids = Inspector.Children.Cast<UIElement>().ToList();
            int first = kids.FindIndex(IsCaption);
            if (first < 0) return;
            Inspector.Children.Clear();
            foreach (var k in kids.Take(first)) Inspector.Children.Add(k);   // the name and kind stay on top, as Unity's header
            StackPanel? body = null;
            foreach (var k in kids.Skip(first))
            {
                if (IsCaption(k))
                {
                    var (card, b) = Card(((TextBlock)k).Text);
                    Inspector.Children.Add(card);
                    body = b;
                    continue;
                }

                body!.Children.Add(k);
            }
        }

        private (Border Card, StackPanel Body) Card(string title)
        {
            bool folded = FoldedCards.Contains(title);
            var body = new StackPanel { Margin = new Thickness(0, 4, 0, 8), Visibility = folded ? Visibility.Collapsed : Visibility.Visible };
            var chevron = new Theme.Icon { Data = Geometry.Parse(folded ? "M9,6 L15,12 L9,18" : "M6,9 L12,15 L18,9"), Size = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            chevron.SetResourceReference(Theme.Icon.ForegroundProperty, "B.TextMuted");
            var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var more = new Button { Width = 22, Height = 22, Cursor = Cursors.Arrow, ToolTip = Shell.Lang.T("Bileşen menüsü"), Content = new Theme.Icon { Data = (Geometry)FindResource("I.More"), Size = 12 } };
            more.SetResourceReference(StyleProperty, "Btn.Icon");
            var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 6, 0, 0), MinHeight = 26 };
            DockPanel.SetDock(more, Dock.Right);
            head.Children.Add(more);
            head.Children.Add(chevron);
            head.Children.Add(name);
            head.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInside(d, more)) return;
                bool fold = body.Visibility == Visibility.Visible;
                body.Visibility = fold ? Visibility.Collapsed : Visibility.Visible;
                chevron.Data = Geometry.Parse(fold ? "M9,6 L15,12 L9,18" : "M6,9 L12,15 L18,9");
                if (fold) FoldedCards.Add(title);
                else FoldedCards.Remove(title);
            };
            more.Click += (_, _) =>
            {
                var menu = CardMenu(title);
                menu.PlacementTarget = more;
                menu.IsOpen = true;
            };
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(body);
            var card = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = stack, Margin = new Thickness(-4, 4, -4, 0), Padding = new Thickness(4, 0, 4, 0) };
            card.SetResourceReference(Border.BorderBrushProperty, "B.Border");
            return (card, body);
        }

        private static bool IsInside(DependencyObject d, DependencyObject parent)
        {
            for (var x = d; x != null; x = VisualTreeHelper.GetParent(x))
                if (ReferenceEquals(x, parent)) return true;
            return false;
        }

        /// <summary>A card's ⋮: what this component can do (the transform's copy and paste, a reset), then folding.</summary>
        private ContextMenu CardMenu(string title)
        {
            var menu = new ContextMenu();
            var o = _inspected;
            var world = World;
            if (o != null && world != null && title == "Konum ve yön")
            {
                menu.Items.Add(MenuEntry("Konumu kopyala", null, () => _copiedPosition = o.Pose.Position));
                menu.Items.Add(MenuEntry("Dönüşü kopyala", null, () => _copiedRotation = (o.Pose.Roll, o.Pose.Pitch, o.Pose.Yaw)));
                menu.Items.Add(MenuEntry("Konumu yapıştır", null, () =>
                {
                    var p = _copiedPosition!.Value;
                    Edit(Shell.Lang.T($"{o.Name}: konum yapıştırıldı"), () => world.MoveAssembly(o, p.X - o.Pose.X, p.Y - o.Pose.Y, p.Z - o.Pose.Z));
                }, _copiedPosition != null));
                menu.Items.Add(MenuEntry("Dönüşü yapıştır", null, () =>
                {
                    var r = _copiedRotation!.Value;
                    Edit(Shell.Lang.T($"{o.Name}: dönüş yapıştırıldı"), () => TurnTo(world, o, r.Roll, r.Pitch, r.Yaw));
                }, _copiedRotation != null));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuEntry("Dönüşü sıfırla", null, () => Edit(Shell.Lang.T($"{o.Name}: dönüş sıfırlandı"), () => TurnTo(world, o, 0, 0, 0))));
                menu.Items.Add(MenuEntry("Yüksekliği sıfırla (zemine)", null, () => Edit(Shell.Lang.T($"{o.Name}: zemine indi"), () => world.MoveAssembly(o, 0, 0, -o.Pose.Z))));
                menu.Items.Add(new Separator());
            }
            else if (o != null && world != null && title == "Fizik")
            {
                menu.Items.Add(MenuEntry("Sıfırla", null, () => Edit(Shell.Lang.T($"{o.Name}: fizik sıfırlandı"), () =>
                {
                    o.IsStatic = false;
                    o.MassKg = 0;   // the mass from the shape and the material again
                })));
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(MenuEntry("Hepsini katla", null, () => FoldAll(true)));
            menu.Items.Add(MenuEntry("Hepsini aç", null, () => FoldAll(false)));
            return menu;
        }

        private void FoldAll(bool fold)
        {
            foreach (var t in Inspector.Children.OfType<Border>().Select(b => b.Child).OfType<StackPanel>()
                         .Select(s => s.Children.OfType<DockPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault()?.Text).Where(t => t != null))
            {
                if (fold) FoldedCards.Add(t!);
                else FoldedCards.Remove(t!);
            }

            BuildInspector();
        }

        /// <summary>Turns a part (with what is mounted on it) to an orientation, about its pivot.</summary>
        private static void TurnTo(WorldDocument world, WorldObject o, double roll, double pitch, double yaw)
        {
            var target = new WorldPose { Roll = roll, Pitch = pitch, Yaw = yaw }.Orientation;
            var delta = (target * o.Pose.Orientation.Conjugate).Normalized();
            world.RotateAssembly(o, delta, o.Pose.Position);
        }

        // ================================================================ Add Component

        /// <summary>
        /// Unity's Add Component over a menu of features: a search box, the categories (a submenu each) and the loose
        /// items; a category opens its list with a way back; Enter takes the first match.
        /// </summary>
        private void ShowComponentPicker(FrameworkElement anchor, ContextMenu features)
        {
            var leaves = new List<(string Category, MenuItem Item)>();
            void Collect(ItemCollection items, string category)
            {
                foreach (var it in items.OfType<MenuItem>())
                {
                    string head = it.Header?.ToString() ?? string.Empty;
                    if (it.Items.Count > 0) Collect(it.Items, head);
                    else if (it.IsEnabled) leaves.Add((category, it));
                }
            }

            Collect(features.Items, string.Empty);
            var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = false };
            var search = new TextBox { Margin = new Thickness(6, 6, 6, 4), ToolTip = Shell.Lang.T("Bileşen ara") };
            var title = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 2, 8, 4), HorizontalAlignment = HorizontalAlignment.Center };
            var list = new StackPanel();
            var scroll = new ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Width = 300 };
            root.Children.Add(search);
            root.Children.Add(title);
            root.Children.Add(scroll);
            var frame = new Border { Child = root, BorderThickness = new Thickness(1), Padding = new Thickness(0, 0, 0, 6) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            popup.Child = frame;
            string? category = null;

            Button Row(string text, bool chevron, Action click)
            {
                var row = new DockPanel();
                if (chevron)
                {
                    var c = new Theme.Icon { Data = Geometry.Parse("M9,6 L15,12 L9,18"), Size = 11, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(c, Dock.Right);
                    row.Children.Add(c);
                }

                row.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis });
                var b = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0) };
                b.SetResourceReference(StyleProperty, "Btn.Ghost");
                b.Click += (_, _) => click();
                return b;
            }

            void Take(MenuItem it)
            {
                popup.IsOpen = false;
                it.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }

            void Fill()
            {
                list.Children.Clear();
                string q = search.Text.Trim();
                if (q.Length > 0)
                {
                    title.Text = Shell.Lang.T("Arama");
                    foreach (var (cat, it) in leaves.Where(l => (l.Item.Header?.ToString() ?? string.Empty).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                                                                 || l.Category.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0))
                        list.Children.Add(Row((cat.Length > 0 ? cat + " › " : string.Empty) + it.Header, false, () => Take(it)));
                    if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = Shell.Lang.T("Eşleşen bileşen yok"), Margin = new Thickness(10, 4, 10, 4) });
                    return;
                }

                if (category != null)
                {
                    title.Text = category;
                    list.Children.Add(Row("‹ " + Shell.Lang.T("Geri"), false, () =>
                    {
                        category = null;
                        Fill();
                    }));
                    foreach (var (_, it) in leaves.Where(l => l.Category == category)) list.Children.Add(Row(it.Header?.ToString() ?? string.Empty, false, () => Take(it)));
                    return;
                }

                title.Text = Shell.Lang.T("Bileşen");
                foreach (var cat in leaves.Select(l => l.Category).Where(c => c.Length > 0).Distinct())
                {
                    string c = cat;
                    list.Children.Add(Row(c, true, () =>
                    {
                        category = c;
                        Fill();
                    }));
                }

                foreach (var (_, it) in leaves.Where(l => l.Category.Length == 0)) list.Children.Add(Row(it.Header?.ToString() ?? string.Empty, false, () => Take(it)));
            }

            search.TextChanged += (_, _) => Fill();
            search.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                var firstMatch = list.Children.OfType<Button>().FirstOrDefault(b => b.Content is DockPanel d && d.Children.OfType<TextBlock>().FirstOrDefault()?.Text.StartsWith("‹", StringComparison.Ordinal) != true);
                firstMatch?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                e.Handled = true;
            };
            Fill();
            popup.Opened += (_, _) => search.Focus();
            _lastPicker = popup;
            popup.IsOpen = true;
        }
    }
}
