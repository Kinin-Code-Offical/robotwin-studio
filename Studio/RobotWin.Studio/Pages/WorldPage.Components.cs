using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        // [fold] which cards are folded, kept in the settings between sessions (Shell/FoldingStack.cs)
        private static bool IsCardFolded(string title) => Shell.FoldingStack.IsFolded("world:" + title);

        private static void FoldCard(string title, bool fold) => Shell.FoldingStack.SetFolded("world:" + title, fold);
        private static Vec3? _copiedPosition;
        private static (double Roll, double Pitch, double Yaw)? _copiedRotation;
        private WorldObject? _inspected;
        private Popup? _lastPicker;
        private ContextMenu? _lastCardMenu;

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
                    var (card, b) = Card(((TextBlock)k).Text, ((TextBlock)k).Tag as ComponentRemoval);
                    Inspector.Children.Add(card);
                    body = b;
                    continue;
                }

                body!.Children.Add(k);
            }
        }

        private (Border Card, StackPanel Body) Card(string title, ComponentRemoval? removal = null)
        {
            bool folded = IsCardFolded(title);
            var body = new StackPanel { Margin = new Thickness(0, 4, 0, 8), Visibility = folded ? Visibility.Collapsed : Visibility.Visible };
            var chevron = new Theme.Icon { Data = Geometry.Parse(folded ? "M9,6 L15,12 L9,18" : "M6,9 L12,15 L18,9"), Size = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            chevron.SetResourceReference(Theme.Icon.ForegroundProperty, "B.TextMuted");
            var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var more = new Button { Width = 22, Height = 22, Cursor = Cursors.Arrow, ToolTip = Shell.Lang.T("Bileşen menüsü"), Content = new Theme.Icon { Data = (Geometry)FindResource("I.More"), Size = 12 } };
            more.SetResourceReference(StyleProperty, "Btn.Icon");
            var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 6, 0, 0), MinHeight = 26 };
            DockPanel.SetDock(more, Dock.Right);
            head.Children.Add(more);
            // Unity's ? on a component: the guide at this component's section (Pages/WorldPage.CardHelp.cs)
            var help = new Button { Width = 22, Height = 22, Cursor = Cursors.Arrow, ToolTip = Shell.Lang.T("Bu bileşenin kılavuzdaki bölümü"), Content = new Theme.Icon { Data = (Geometry)FindResource("I.Help"), Size = 12 } };
            help.SetResourceReference(StyleProperty, "Btn.Icon");
            help.Click += (_, _) => OpenCardHelp(title);
            DockPanel.SetDock(help, Dock.Right);
            head.Children.Add(help);
            Button? drop = null;
            if (removal != null)
            {
                // Unity's Remove Component, one click away on the card itself
                drop = new Button { Content = Shell.Lang.T("Kaldır"), Padding = new Thickness(8, 0, 8, 0), MinHeight = 0, Height = 22, FontSize = 12, Cursor = Cursors.Arrow, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = Shell.Lang.T("Bu bileşeni nesneden kaldırır (Ctrl+Z ile geri alınır)") };
                drop.SetResourceReference(StyleProperty, "Btn.Ghost");
                drop.Click += (_, _) => RemoveComponent(removal);
                DockPanel.SetDock(drop, Dock.Right);
                head.Children.Add(drop);
            }

            head.Children.Add(chevron);
            CheckBox? onBox = null;
            if (removal?.Toggle is { } key && _inspected is { } carrier)
            {
                // Unity's component checkbox: off, the component is not in the run and keeps its settings
                bool on = !WorldComponents.IsOff(carrier, key);
                onBox = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Arrow,
                    ToolTip = Shell.Lang.T("Bileşen açık / kapalı: kapalıyken çalıştırmada yok, ayarları saklanır") };
                onBox.Click += (_, _) =>
                {
                    bool turnOn = onBox.IsChecked == true;
                    Edit(Shell.Lang.T(turnOn ? $"{title} bileşeni açıldı" : $"{title} bileşeni kapatıldı"), () => WorldComponents.SetOn(carrier, key, turnOn));
                    BuildInspector();
                };
                if (!on) name.Opacity = 0.6;
                head.Children.Add(onBox);
            }

            head.Children.Add(name);
            head.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && (IsInside(d, more) || IsInside(d, help) || (drop != null && IsInside(d, drop)) || (onBox != null && IsInside(d, onBox)))) return;
                bool fold = body.Visibility == Visibility.Visible;
                body.Visibility = fold ? Visibility.Collapsed : Visibility.Visible;
                chevron.Data = Geometry.Parse(fold ? "M9,6 L15,12 L9,18" : "M6,9 L12,15 L18,9");
                FoldCard(title, fold);
            };
            more.Click += (_, _) =>
            {
                var menu = CardMenu(title, removal);
                menu.PlacementTarget = more;
                menu.IsOpen = true;
                _lastCardMenu = menu;
            };
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(body);
            var card = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = stack, Margin = new Thickness(-4, 4, -4, 0), Padding = new Thickness(4, 0, 4, 0), Tag = title };   // the source title: the shown one may be translated
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
        private ContextMenu CardMenu(string title, ComponentRemoval? removal = null)
        {
            var menu = new ContextMenu();
            if (removal != null)
            {
                menu.Items.Add(MenuEntry("Bileşeni kaldır", null, () => RemoveComponent(removal)));
                menu.Items.Add(new Separator());
            }

            ComponentClipboardItems(menu, title, removal);   // [components] Unity's Copy Component / Paste Values / Paste As New

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
            foreach (var t in Inspector.Children.OfType<Border>().Select(b => b.Tag as string).Where(t => t != null))
            {
                FoldCard(t!, fold);
            }

            BuildInspector();
        }

        // ================================================================ the added component, shown

        /// <summary>Every card's title and every joint feature's ("sub:" title) in the inspector now.</summary>
        private HashSet<string> FeatureTitles()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var card in Inspector.Children.OfType<Border>())
            {
                if (card.Tag is string t) set.Add(t);
                foreach (var sub in Descendants(card).OfType<DockPanel>()) if (sub.Tag is SubFeature f) set.Add("sub:" + f.Title);
            }

            return set;
        }

        /// <summary>The first component that was not there before: a new card, or a new feature on a joint inside its card.</summary>
        private (FrameworkElement? Element, string? Card) NewComponent(HashSet<string> before)
        {
            foreach (var card in Inspector.Children.OfType<Border>())
            {
                if (card.Tag is not string t) continue;
                if (!before.Contains(t)) return (card, t);
                foreach (var sub in Descendants(card).OfType<DockPanel>())
                    if (sub.Tag is SubFeature f && !before.Contains("sub:" + f.Title))
                        return (sub, t);
            }

            return (null, null);
        }

        private static IEnumerable<UIElement> Descendants(UIElement e)
        {
            IEnumerable<UIElement> kids = e switch
            {
                Panel p => p.Children.Cast<UIElement>(),
                Decorator d when d.Child != null => new[] { d.Child },
                ContentControl c when c.Content is UIElement u => new[] { u },
                _ => Array.Empty<UIElement>()
            };
            foreach (var k in kids)
            {
                yield return k;
                foreach (var g in Descendants(k)) yield return g;
            }
        }

        /// <summary>Opens the new component's card (unfolded), scrolls it into view and marks it for a moment.</summary>
        private void RevealNewComponent(HashSet<string> before)
        {
            var (element, card) = NewComponent(before);
            if (element == null) return;
            if (card != null && IsCardFolded(card))
            {
                FoldCard(card, false);
                BuildInspector();
                (element, _) = NewComponent(before);
                if (element == null) return;
            }

            var target = element;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                target.BringIntoView(new Rect(0, 0, target.ActualWidth, Math.Min(Math.Max(target.ActualHeight, 40), 360)));
                Brush? was = target is Border b ? b.Background : (target as Panel)?.Background;
                var mark = (Brush)FindResource("B.AccentSoft");
                if (target is Border bb) bb.Background = mark;
                else if (target is Panel pp) pp.Background = mark;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (target is Border b2) b2.Background = was;
                    else if (target is Panel p2) p2.Background = was;
                };
                timer.Start();
            }), DispatcherPriority.Loaded);
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
            var categoryTips = new Dictionary<string, string>(StringComparer.Ordinal);
            void Collect(ItemCollection items, string category)
            {
                foreach (var it in items.OfType<MenuItem>())
                {
                    string head = it.Header?.ToString() ?? string.Empty;
                    if (it.Items.Count > 0 && it.ToolTip is string catTip) categoryTips[head] = catTip;
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
            // Unity 6's Add Component describes the item under the pointer: what it adds and does
            var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 6, 10, 0), MinHeight = 34 };
            info.SetResourceReference(StyleProperty, "Text.Caption");
            var rule = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 0) };
            rule.SetResourceReference(Border.BackgroundProperty, "B.Border");
            var root = new StackPanel { Width = 320 };
            root.Children.Add(search);
            root.Children.Add(title);
            root.Children.Add(scroll);
            root.Children.Add(rule);
            root.Children.Add(info);
            string idle = Shell.Lang.T("Bir bileşenin üzerine gelin: ne eklediği burada yazar. Eklenince kendi kartı açılır.");
            info.Text = idle;
            var frame = new Border { Child = root, BorderThickness = new Thickness(1), Padding = new Thickness(0, 0, 0, 6) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            popup.Child = frame;
            string? category = null;

            Button Row(string text, bool chevron, Action click, string? tip = null)
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
                b.MouseEnter += (_, _) => info.Text = tip != null ? Shell.Lang.T(tip) : idle;
                return b;
            }

            void Take(MenuItem it)
            {
                popup.IsOpen = false;
                var before = FeatureTitles();
                it.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                RevealNewComponent(before);   // Unity: the added component's card, open, in view
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
                        list.Children.Add(Row((cat.Length > 0 ? cat + " › " : string.Empty) + it.Header, false, () => Take(it), it.ToolTip as string));
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
                    foreach (var (_, it) in leaves.Where(l => l.Category == category)) list.Children.Add(Row(it.Header?.ToString() ?? string.Empty, false, () => Take(it), it.ToolTip as string));
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
                    }, categoryTips.TryGetValue(c, out var ct) ? ct : null));
                }

                foreach (var (_, it) in leaves.Where(l => l.Category.Length == 0)) list.Children.Add(Row(it.Header?.ToString() ?? string.Empty, false, () => Take(it), it.ToolTip as string));
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
