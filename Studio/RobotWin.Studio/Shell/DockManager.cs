using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RobotWin.Studio.Shell
{
    /// <summary>[dock] Where a panel lives.</summary>
    public enum DockPlace
    {
        Left,
        Right,
        Bottom,
        Float,
        Hidden
    }

    /// <summary>[dock] A panel the user can put on the left or right, as a bottom tab, in its own window, or hide.</summary>
    public sealed class DockItem
    {
        public DockItem(string key, string title, FrameworkElement content, DockPlace home)
        {
            Key = key;
            Title = title;
            Content = content;
            Home = home;
            Place = home;
            LastDocked = IsDocked(home) ? home : DockPlace.Right;
        }

        public string Key { get; }

        public string Title { get; }

        public FrameworkElement Content { get; }

        /// <summary>Where "Düzeni sıfırla" puts it.</summary>
        public DockPlace Home { get; }

        public DockPlace Place { get; internal set; }

        /// <summary>The last side or bottom it was docked to: closing its window brings it back there.</summary>
        public DockPlace LastDocked { get; internal set; }

        /// <summary>It can be a tab of the bottom panel (output-like panels; the palette and inspector cannot).</summary>
        public bool CanBottom { get; init; }

        /// <summary>Its tab button in the bottom panel's strip.</summary>
        public RadioButton? Chip { get; init; }

        /// <summary>Margin of the content inside a side frame or its own window (its own margin is kept for its home).</summary>
        public Thickness? FramedMargin { get; init; }

        internal Thickness HomeMargin;
        internal int Order;
        internal double Weight = 1;
        internal Rect FloatRect = Rect.Empty;
        internal DockWindow? Window;
        internal DockPanel? Frame;
        internal Border? Body;
        internal RowDefinition? Row;

        internal static bool IsDocked(DockPlace p) => p == DockPlace.Left || p == DockPlace.Right || p == DockPlace.Bottom;
    }

    /// <summary>
    /// [dock] Movable panels for a page laid out as [left column | centre | right column] over a bottom tab panel.
    /// Panels stack in the side columns (resizable), become bottom tabs, or tear off into their own windows by
    /// dragging their header or tab; dragging such a window over the page shows where it will dock. The layout
    /// is kept per page in %APPDATA%\RobotWinStudio\layout.json.
    /// </summary>
    public sealed class DockManager
    {
        private readonly string _layoutKey;
        private readonly FrameworkElement _owner;
        private readonly Grid _overlayHost, _main;
        private readonly int _leftCol, _rightCol;
        private readonly Grid _leftStack = new Grid(), _rightStack = new Grid();
        private readonly GridSplitter _leftSplit, _rightSplit;
        private readonly Panel _tabStrip;
        private readonly Grid _tabHost;
        private readonly FrameworkElement _bottomArea;
        private readonly RowDefinition _bottomRow, _bottomLineRow;
        private readonly FrameworkElement[] _bottomExtras;
        private readonly Canvas _overlay = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly List<DockItem> _items = new List<DockItem>();
        private readonly DispatcherTimer _saveTimer;
        private readonly double _defaultLeft, _defaultRight, _defaultBottom;
        private readonly HashSet<Window> _hooked = new HashSet<Window>();
        private double _leftWidth, _rightWidth, _bottomHeight;
        private bool _leftShown, _rightShown, _bottomShown;
        private bool _bottomHidden, _collapsed, _applying, _shutdown, _started;
        private DockPlace? _hot;

        public DockManager(string layoutKey, FrameworkElement owner, Grid overlayHost, Grid main, int leftCol, int rightCol,
                           Panel tabStrip, Grid tabHost, FrameworkElement bottomArea, RowDefinition bottomRow, RowDefinition bottomLineRow,
                           params FrameworkElement[] bottomExtras)
        {
            _layoutKey = layoutKey;
            _owner = owner;
            _overlayHost = overlayHost;
            _main = main;
            _leftCol = leftCol;
            _rightCol = rightCol;
            _tabStrip = tabStrip;
            _tabHost = tabHost;
            _bottomArea = bottomArea;
            _bottomRow = bottomRow;
            _bottomLineRow = bottomLineRow;
            _bottomExtras = bottomExtras;
            _leftWidth = _defaultLeft = main.ColumnDefinitions[leftCol].Width.IsAbsolute ? main.ColumnDefinitions[leftCol].Width.Value : 268;
            _rightWidth = _defaultRight = main.ColumnDefinitions[rightCol].Width.IsAbsolute ? main.ColumnDefinitions[rightCol].Width.Value : 300;
            _bottomHeight = _defaultBottom = bottomRow.Height.IsAbsolute ? bottomRow.Height.Value : 190;

            Grid.SetColumn(_leftStack, leftCol);
            Grid.SetColumn(_rightStack, rightCol);
            main.Children.Add(_leftStack);
            main.Children.Add(_rightStack);
            _leftSplit = ColumnSplitter(leftCol + 1);
            _rightSplit = ColumnSplitter(rightCol - 1);
            StripButtons();

            Grid.SetRowSpan(_overlay, 99);
            Grid.SetColumnSpan(_overlay, 99);
            Panel.SetZIndex(_overlay, 1000);
            overlayHost.Children.Add(_overlay);

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                Save();
            };
            owner.IsVisibleChanged += (_, _) => OnOwnerVisible();
            owner.Dispatcher.ShutdownStarted += (_, _) =>
            {
                if (!_shutdown) Save();
                _shutdown = true;
            };
        }

        /// <summary>Off for snapshots, so docs pictures always show the default layout (unless ROBOTWIN_LAYOUT names a test file).</summary>
        public static bool Persist { get; set; } = !Unattended.Active || LayoutStore.Overridden;

        /// <summary>A panel became visible (its tab was picked, it was docked or opened in a window): refresh it.</summary>
        public event Action<string>? Shown;

        public IReadOnlyList<DockItem> Items => _items;

        /// <summary>Hides the side columns and the bottom panel (a full-view mode), keeping their layout.</summary>
        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                if (_collapsed == value) return;
                CaptureSizes();
                _collapsed = value;
                Sizes();
            }
        }

        public bool BottomHidden
        {
            get => _bottomHidden;
            set
            {
                if (_bottomHidden == value) return;
                CaptureSizes();
                _bottomHidden = value;
                Sizes();
                if (!value) SyncTabs();
                ScheduleSave();
            }
        }

        public DockItem Add(DockItem item)
        {
            item.HomeMargin = item.Content.Margin;
            item.Order = _items.Count;
            _items.Add(item);
            if (item.Chip is { } chip)
            {
                chip.Checked += (_, _) =>
                {
                    if (_applying || !_started) return;
                    SyncTabs();
                    Shown?.Invoke(item.Key);
                    ScheduleSave();
                };
                Draggable(chip, item, 12);
                chip.MouseRightButtonUp += (_, e) =>
                {
                    OpenMenu(chip, item);
                    e.Handled = true;
                };
                chip.ToolTip ??= item.Title;
                if (chip.ToolTip is string tip) chip.ToolTip = tip + "\nSürükleyerek ayrı pencereye alın; sağ tık: yerleşim";
            }

            return item;
        }

        /// <summary>Applies the saved layout (or the default) once every panel is added.</summary>
        public void Start()
        {
            if (Persist) Load();
            _started = true;
            Apply();
            OnOwnerVisible();
            foreach (var it in _items.ToList())
            {
                if (IsShowing(it.Key)) Shown?.Invoke(it.Key);   // a restored tab or side panel is filled at once
            }
        }

        public DockPlace PlaceOf(string key) => Find(key)?.Place ?? DockPlace.Hidden;

        /// <summary>The panel is on screen: on a side, in its window, or the picked bottom tab.</summary>
        public bool IsShowing(string key)
        {
            var it = Find(key);
            if (it == null) return false;
            return it.Place switch
            {
                DockPlace.Left or DockPlace.Right => !_collapsed,
                DockPlace.Float => it.Window?.IsVisible == true,
                DockPlace.Bottom => !_collapsed && !_bottomHidden && it.Chip?.IsChecked == true,
                _ => false
            };
        }

        /// <summary>Brings a panel into view wherever it is (bottom tab picked, window raised, hidden panel docked back).</summary>
        public void Reveal(string key)
        {
            var it = Find(key);
            if (it == null) return;
            switch (it.Place)
            {
                case DockPlace.Float:
                    it.Window?.Activate();
                    break;
                case DockPlace.Hidden:
                    Dock(it, it.LastDocked);
                    break;
                case DockPlace.Bottom:
                    if (_bottomHidden) BottomHidden = false;
                    if (it.Chip != null) it.Chip.IsChecked = true;
                    SyncTabs();
                    break;
            }
        }

        // ================================================================ placing

        public void Dock(DockItem it, DockPlace place)
        {
            if (place == DockPlace.Float)
            {
                Float(it);
                return;
            }

            if (place == DockPlace.Hidden)
            {
                Hide(it);
                return;
            }

            if (place == DockPlace.Bottom && !it.CanBottom) return;
            if (it.Place != place)
            {
                var others = _items.Where(i => i != it && i.Place == place).ToList();
                it.Order = others.Select(i => i.Order).DefaultIfEmpty(-1).Max() + 1;
                it.Weight = others.Count > 0 ? others.Average(i => i.Weight) : 1;
            }

            it.Place = place;
            it.LastDocked = place;
            CloseWindow(it);
            if (place == DockPlace.Bottom)
            {
                _bottomHidden = false;
                _applying = true;
                if (it.Chip != null) it.Chip.IsChecked = true;
                _applying = false;
            }

            Apply();
            Shown?.Invoke(it.Key);
            ScheduleSave();
        }

        /// <summary>Opens the panel in its own window, next to where it was.</summary>
        public void Float(DockItem it) => Float(it, false);

        private void Float(DockItem it, bool keepRect)
        {
            if (it.Place == DockPlace.Float)
            {
                if (it.Window != null) it.Window.Activate();
                else OpenWindow(it);
                return;
            }

            if (!keepRect && (it.FloatRect.IsEmpty || !OnScreen(it.FloatRect)))
            {
                var size = SizeFor(it);
                Point at;
                if (it.Content.IsVisible && PresentationSource.FromVisual(it.Content) != null) at = DockWindow.ToDip(_owner, it.Content.PointToScreen(new Point(0, 0)));
                else if (Window.GetWindow(_owner) is { } w) at = new Point(w.Left + w.ActualWidth / 2 - size.Width / 2, w.Top + w.ActualHeight / 2 - size.Height / 2);
                else at = new Point(100, 100);
                it.FloatRect = new Rect(at.X + 24, at.Y + 24, size.Width, size.Height);
            }

            if (DockItem.IsDocked(it.Place)) it.LastDocked = it.Place;
            it.Place = DockPlace.Float;
            Apply();
            OpenWindow(it);
            ScheduleSave();
        }

        public void Hide(DockItem it)
        {
            if (DockItem.IsDocked(it.Place)) it.LastDocked = it.Place;
            it.Place = DockPlace.Hidden;
            CloseWindow(it);
            Apply();
            ScheduleSave();
        }

        /// <summary>Swaps everything on the left with everything on the right (and the column widths).</summary>
        public void SwapSides()
        {
            CaptureSizes();
            foreach (var it in _items)
            {
                it.Place = Mirror(it.Place);
                it.LastDocked = Mirror(it.LastDocked);
            }

            (_leftWidth, _rightWidth) = (_rightWidth, _leftWidth);
            Apply();
            ScheduleSave();

            static DockPlace Mirror(DockPlace p) => p == DockPlace.Left ? DockPlace.Right : p == DockPlace.Right ? DockPlace.Left : p;
        }

        /// <summary>Every panel in a window goes back to where it was docked.</summary>
        public void DockAll()
        {
            foreach (var it in _items.Where(i => i.Place == DockPlace.Float).ToList()) Dock(it, it.LastDocked);
        }

        /// <summary>Back to the default layout.</summary>
        public void Reset()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                CloseWindow(it);
                it.Place = it.Home;
                it.LastDocked = DockItem.IsDocked(it.Home) ? it.Home : DockPlace.Right;
                it.Order = i;
                it.Weight = 1;
                it.FloatRect = Rect.Empty;
            }

            _leftWidth = _defaultLeft;
            _rightWidth = _defaultRight;
            _bottomHeight = _defaultBottom;
            _bottomHidden = false;
            _leftShown = _rightShown = _bottomShown = false;   // do not capture the current sizes over the defaults
            var first = _items.FirstOrDefault(i => i.Place == DockPlace.Bottom && i.Chip != null);
            _applying = true;
            if (first?.Chip != null) first.Chip.IsChecked = true;
            _applying = false;
            Apply();
            Save();
        }

        private void Move(DockItem it, int step)
        {
            var list = _items.Where(i => i.Place == it.Place).OrderBy(i => i.Order).ToList();
            int k = list.IndexOf(it), j = k + step;
            if (k < 0 || j < 0 || j >= list.Count) return;
            (list[k].Order, list[j].Order) = (list[j].Order, list[k].Order);
            Apply();
            ScheduleSave();
        }

        // ================================================================ layout

        /// <summary>Puts every panel where its place says and sizes the columns and the bottom row.</summary>
        public void Apply()
        {
            _applying = true;
            try
            {
                CaptureSizes();
                foreach (var it in _items)
                {
                    if (it.Place == DockPlace.Hidden || (it.Place == DockPlace.Float && it.Window == null)) Detach(it.Content);
                }

                // both stacks are emptied first: a frame may be moving from one side to the other
                _leftStack.Children.Clear();
                _rightStack.Children.Clear();
                BuildSide(_leftStack, DockPlace.Left);
                BuildSide(_rightStack, DockPlace.Right);
                foreach (var it in _items)
                {
                    if (it.Chip == null) continue;
                    bool here = it.Place == DockPlace.Bottom;
                    it.Chip.Visibility = here ? Visibility.Visible : Visibility.Collapsed;
                    if (here && it.Content.Parent != _tabHost)
                    {
                        Detach(it.Content);
                        it.Content.Margin = it.HomeMargin;
                        _tabHost.Children.Add(it.Content);
                    }
                }

                Sizes();
            }
            finally
            {
                _applying = false;
            }

            SyncTabs();
        }

        /// <summary>Shows the picked bottom tab; panels elsewhere are always visible.</summary>
        public void SyncTabs()
        {
            var bottom = _items.Where(i => i.Place == DockPlace.Bottom && i.Chip != null).ToList();
            DockItem? picked = null;
            if (bottom.Count > 0 && bottom.All(i => i.Chip!.IsChecked != true))
            {
                picked = bottom[0];
                bool was = _applying;
                _applying = true;
                picked.Chip!.IsChecked = true;
                _applying = was;
            }

            foreach (var it in _items)
            {
                if (it.Place == DockPlace.Bottom) it.Content.Visibility = it.Chip?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                else if (it.Place != DockPlace.Hidden) it.Content.Visibility = Visibility.Visible;
            }

            if (picked != null && _started && !_applying) Shown?.Invoke(picked.Key);   // the tab that took over needs filling
        }

        private void BuildSide(Grid stack, DockPlace side)
        {
            var list = _items.Where(i => i.Place == side).OrderBy(i => i.Order).ToList();
            stack.Children.Clear();
            stack.RowDefinitions.Clear();
            foreach (var it in _items) if (it.Place != side && it.Row != null && stack.RowDefinitions.Contains(it.Row)) it.Row = null;
            for (int k = 0; k < list.Count; k++)
            {
                var it = list[k];
                if (k > 0)
                {
                    stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
                    int r = stack.RowDefinitions.Count - 1;
                    var line = new Border();
                    line.SetResourceReference(Border.BackgroundProperty, "B.Border");
                    Grid.SetRow(line, r);
                    stack.Children.Add(line);
                    var split = new GridSplitter
                    {
                        Margin = new Thickness(0, -3, 0, -3), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                        Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext
                    };
                    split.DragCompleted += (_, _) =>
                    {
                        CaptureWeights();
                        ScheduleSave();
                    };
                    Grid.SetRow(split, r);
                    Panel.SetZIndex(split, 10);
                    stack.Children.Add(split);
                }

                var row = new RowDefinition { Height = new GridLength(Math.Max(0.05, it.Weight), GridUnitType.Star), MinHeight = 90 };
                stack.RowDefinitions.Add(row);
                it.Row = row;
                var frame = FrameOf(it);
                if (it.Content.Parent != it.Body)
                {
                    Detach(it.Content);
                    it.Body!.Child = it.Content;
                }

                it.Content.Margin = it.FramedMargin ?? it.HomeMargin;
                Grid.SetRow(frame, stack.RowDefinitions.Count - 1);
                stack.Children.Add(frame);
            }
        }

        private void Sizes()
        {
            bool left = !_collapsed && _items.Any(i => i.Place == DockPlace.Left);
            bool right = !_collapsed && _items.Any(i => i.Place == DockPlace.Right);
            bool bottom = !_collapsed && !_bottomHidden && _items.Any(i => i.Place == DockPlace.Bottom);
            Column(_leftCol, left, _leftWidth, 180);
            Column(_leftCol + 1, left, 1, 0);
            Column(_rightCol, right, _rightWidth, 180);
            Column(_rightCol - 1, right, 1, 0);
            _leftSplit.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
            _rightSplit.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
            _leftStack.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
            _rightStack.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
            _bottomRow.MinHeight = bottom ? 36 : 0;
            _bottomRow.Height = new GridLength(bottom ? _bottomHeight : 0);
            _bottomLineRow.Height = new GridLength(bottom ? 1 : 0);
            _bottomArea.Visibility = bottom ? Visibility.Visible : Visibility.Collapsed;
            foreach (var e in _bottomExtras) e.Visibility = bottom ? Visibility.Visible : Visibility.Collapsed;
            _leftShown = left;
            _rightShown = right;
            _bottomShown = bottom;

            void Column(int i, bool on, double width, double min)
            {
                var c = _main.ColumnDefinitions[i];
                c.MinWidth = on ? min : 0;
                c.Width = new GridLength(on ? width : 0);
            }
        }

        /// <summary>Remembers sizes the user changed with the splitters before the layout is rebuilt.</summary>
        private void CaptureSizes()
        {
            if (_leftShown) _leftWidth = Math.Max(180, Width(_leftCol));
            if (_rightShown) _rightWidth = Math.Max(180, Width(_rightCol));
            if (_bottomShown) _bottomHeight = Math.Max(36, _bottomRow.Height.IsAbsolute ? _bottomRow.Height.Value : _bottomRow.ActualHeight);
            CaptureWeights();

            double Width(int i)
            {
                var c = _main.ColumnDefinitions[i];
                return c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth;
            }
        }

        private void CaptureWeights()
        {
            foreach (var side in new[] { DockPlace.Left, DockPlace.Right })
            {
                var list = _items.Where(i => i.Place == side && i.Row != null && i.Row.ActualHeight > 1).ToList();
                double total = list.Sum(i => i.Row!.ActualHeight);
                if (list.Count < 2 || total <= 0) continue;
                foreach (var it in list) it.Weight = it.Row!.ActualHeight / total * list.Count;
            }
        }

        private GridSplitter ColumnSplitter(int column)
        {
            var s = new GridSplitter
            {
                Margin = new Thickness(-3, 0, -3, 0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Visibility = Visibility.Collapsed
            };
            s.DragCompleted += (_, _) =>
            {
                CaptureSizes();
                ScheduleSave();
            };
            Grid.SetColumn(s, column);
            Panel.SetZIndex(s, 10);
            _main.Children.Add(s);
            return s;
        }

        /// <summary>
        /// Buttons at the right end of the bottom tab strip for the picked tab: its own window, its place (menu), hide it;
        /// and one that folds the whole bottom pane away (the View menu's "Alt bölmeyi göster" brings it back).
        /// </summary>
        private void StripButtons()
        {
            if (_tabStrip.Parent is not Panel parent) return;
            int at = parent.Children.IndexOf(_tabStrip);
            var row = new DockPanel { Margin = _tabStrip.Margin, LastChildFill = true };
            DockPanel.SetDock(row, DockPanel.GetDock(_tabStrip));
            _tabStrip.Margin = new Thickness(0);
            parent.Children.RemoveAt(at);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
            DockItem? Picked() => _items.FirstOrDefault(i => i.Place == DockPlace.Bottom && i.Chip?.IsChecked == true);
            var pop = IconButton("I.Window", 13, "Bu sekmeyi ayrı pencereye al");
            pop.Click += (_, _) =>
            {
                if (Picked() is { } it) Float(it);
            };
            var more = IconButton("I.More", 13, "Bu sekmenin yeri: sol, sağ, alt bölme, ayrı pencere, gizli");
            more.Click += (_, _) =>
            {
                if (Picked() is { } it) OpenMenu(more, it);
            };
            var close = IconButton("I.Close", 10, "Bu sekmeyi kapat (Görünüm menüsündeki panel düzeninden geri açılır)");
            close.Click += (_, _) =>
            {
                if (Picked() is { } it) Hide(it);
            };
            var fold = IconButton("I.ChevronDown", 12, "Alt bölmeyi gizle (Görünüm → panel düzeni → Alt bölmeyi göster)");
            fold.Click += (_, _) => BottomHidden = true;
            foreach (var b in new[] { pop, more, close, fold }) buttons.Children.Add(b);
            row.Children.Add(buttons);
            row.Children.Add(_tabStrip);
            parent.Children.Insert(at, row);
        }

        // ================================================================ side frames

        private DockPanel FrameOf(DockItem it)
        {
            if (it.Frame != null) return it.Frame;
            var header = new Border { Height = 28, Padding = new Thickness(10, 0, 2, 0), BorderThickness = new Thickness(0, 0, 0, 1), Cursor = Cursors.SizeAll };
            header.SetResourceReference(Border.BackgroundProperty, "B.Base");
            header.SetResourceReference(Border.BorderBrushProperty, "B.Border");
            header.ToolTip = it.Title + ": başlığı sürükleyerek ayrı pencereye alın, pencereyi sayfanın kenarına bırakarak yerleştirin. Sağ tık: yerleşim";
            var row = new DockPanel();
            var close = IconButton("I.Close", 10, "Gizle (Panel düzeni menüsünden geri açılır)");
            close.Click += (_, _) => Hide(it);
            var pop = IconButton("I.Window", 13, "Ayrı pencereye al");
            pop.Click += (_, _) => Float(it);
            var more = IconButton("I.More", 13, "Yerleşim: sol, sağ, alt bölme, ayrı pencere");
            more.Click += (_, _) => OpenMenu(more, it);
            foreach (var b in new[] { close, pop, more })
            {
                DockPanel.SetDock(b, System.Windows.Controls.Dock.Right);
                row.Children.Add(b);
            }

            var title = new TextBlock { Text = it.Title, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
            row.Children.Add(title);
            header.Child = row;
            header.MouseRightButtonUp += (_, e) =>
            {
                OpenMenu(header, it);
                e.Handled = true;
            };
            Draggable(header, it, 8);
            DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
            it.Body = new Border();
            it.Frame = new DockPanel();
            it.Frame.Children.Add(header);
            it.Frame.Children.Add(it.Body);
            return it.Frame;
        }

        private Button IconButton(string icon, double size, string tip)
        {
            var b = new Button { ToolTip = tip, Width = 24, Height = 24, Cursor = Cursors.Arrow, Content = new Theme.Icon { Data = (Geometry)_owner.FindResource(icon), Size = size } };
            b.SetResourceReference(FrameworkElement.StyleProperty, "Btn.Icon");
            return b;
        }

        // ================================================================ menus

        private void OpenMenu(FrameworkElement target, DockItem it)
        {
            var m = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (var e in ItemEntries(it)) m.Items.Add(e);
            m.IsOpen = true;
        }

        private IEnumerable<object> ItemEntries(DockItem it)
        {
            MenuItem Pick(string text, DockPlace p)
            {
                var mi = new MenuItem { Header = text, IsChecked = it.Place == p };
                mi.Click += (_, _) =>
                {
                    if (it.Place != p) Dock(it, p);
                };
                return mi;
            }

            yield return Pick("Solda", DockPlace.Left);
            yield return Pick("Sağda", DockPlace.Right);
            if (it.CanBottom) yield return Pick("Alt bölmede (sekme)", DockPlace.Bottom);
            yield return Pick("Ayrı pencerede", DockPlace.Float);
            if ((it.Place == DockPlace.Left || it.Place == DockPlace.Right) && _items.Count(i => i.Place == it.Place) > 1)
            {
                yield return new Separator();
                yield return Command("Yukarı taşı", () => Move(it, -1));
                yield return Command("Aşağı taşı", () => Move(it, +1));
            }

            yield return new Separator();
            yield return Pick("Gizli", DockPlace.Hidden);
        }

        private static MenuItem Command(string text, Action a, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += (_, _) => a();
            return mi;
        }

        private static string PlaceName(DockPlace p) => p switch
        {
            DockPlace.Left => "solda",
            DockPlace.Right => "sağda",
            DockPlace.Bottom => "alt bölmede",
            DockPlace.Float => "ayrı pencerede",
            _ => "gizli"
        };

        /// <summary>The page's "Panel düzeni" menu (toolbar button and the main window's View menu).</summary>
        public void FillMenu(ItemsControl menu)
        {
            foreach (var it in _items)
            {
                var sub = new MenuItem { Header = it.Title + " · " + PlaceName(it.Place) };
                foreach (var e in ItemEntries(it)) sub.Items.Add(e);
                menu.Items.Add(sub);
            }

            menu.Items.Add(new Separator());
            var bottom = new MenuItem { Header = "Alt bölmeyi göster", IsChecked = !_bottomHidden };
            bottom.Click += (_, _) => BottomHidden = !_bottomHidden;
            menu.Items.Add(bottom);
            menu.Items.Add(Command("Sol ve sağ panelleri yer değiştir", SwapSides));
            menu.Items.Add(Command("Ayrı pencereleri geri yerleştir", DockAll, _items.Any(i => i.Place == DockPlace.Float)));
            menu.Items.Add(Command("Düzeni sıfırla", Reset));
        }

        // ================================================================ tear-off and drop

        private void Draggable(FrameworkElement handle, DockItem it, double threshold)
        {
            Point? down = null;
            handle.PreviewMouseLeftButtonDown += (_, e) => down = handle is ButtonBase || !InButton(e.OriginalSource as DependencyObject, handle) ? e.GetPosition(handle) : null;
            handle.PreviewMouseLeftButtonUp += (_, _) => down = null;
            handle.PreviewMouseMove += (_, e) =>
            {
                if (down == null) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    down = null;
                    return;
                }

                var d = e.GetPosition(handle) - down.Value;
                if (Math.Abs(d.X) < threshold && Math.Abs(d.Y) < threshold) return;
                down = null;
                if (Mouse.Captured != null) Mouse.Capture(null);
                e.Handled = true;
                TearOff(it);
            };
        }

        private static bool InButton(DependencyObject? d, DependencyObject stop)
        {
            while (d != null && d != stop)
            {
                if (d is ButtonBase) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }

            return false;
        }

        /// <summary>The header or tab was dragged: the panel becomes a window under the cursor and the drag carries on moving it.</summary>
        private void TearOff(DockItem it)
        {
            var cursor = DockWindow.Cursor();
            if (double.IsNaN(cursor.X)) return;
            var size = SizeFor(it);
            var at = DockWindow.ToDip(_owner, cursor);
            it.FloatRect = new Rect(at.X - Math.Min(90, size.Width / 3), at.Y - 14, size.Width, size.Height);
            if (it.Place == DockPlace.Float) return;
            Float(it, true);
            var w = it.Window;
            if (w == null) return;
            w.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (Mouse.LeftButton == MouseButtonState.Pressed) w.DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }), DispatcherPriority.Input);
        }

        private Size SizeFor(DockItem it)
        {
            double w = it.Content.ActualWidth, h = it.Content.ActualHeight;
            if (it.Place == DockPlace.Bottom || w < 120 || h < 80)
            {
                w = it.Home == DockPlace.Bottom ? 640 : 320;
                h = it.Home == DockPlace.Bottom ? 340 : Math.Max(420, _main.ActualHeight * 0.7);
            }

            return new Size(Math.Clamp(w + 24, 280, 1000), Math.Clamp(h + 70, 220, 900));
        }

        private void OpenWindow(DockItem it)
        {
            if (it.Window != null || _shutdown) return;
            var owner = Window.GetWindow(_owner);
            if (owner == null || !_owner.IsVisible) return;   // opened when the page is shown
            var r = it.FloatRect;
            if (r.IsEmpty || !OnScreen(r)) r = new Rect(owner.Left + 80, owner.Top + 120, SizeFor(it).Width, SizeFor(it).Height);
            var w = new DockWindow(it.Title) { Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual, Left = r.X, Top = r.Y, Width = r.Width, Height = r.Height };
            foreach (InputBinding b in owner.InputBindings) w.InputBindings.Add(b);
            PlaceButtons(w, it);
            Detach(it.Content);
            it.Content.Margin = it.FramedMargin ?? it.HomeMargin;
            it.Content.Visibility = Visibility.Visible;
            w.Body.Child = it.Content;
            w.Moving += () => DragOver(it);
            w.MoveEnded += () => DragEnd(it);
            w.LocationChanged += (_, _) => Remember(it, w);
            w.SizeChanged += (_, _) => Remember(it, w);
            w.Closed += (_, _) =>
            {
                if (w.ClosedByManager || _shutdown) return;
                w.Body.Child = null;
                it.Window = null;
                Dock(it, it.LastDocked);
            };
            HookOwner(owner);
            it.Window = w;
            w.Show();
            Shown?.Invoke(it.Key);
        }

        /// <summary>Saves the layout as the window closes, before its owned panel windows close (which would dock them back).</summary>
        private void HookOwner(Window owner)
        {
            if (!_hooked.Add(owner)) return;
            owner.Closing += (_, e) =>
            {
                if (e.Cancel) return;
                Save();
                _shutdown = true;
            };
        }

        private void PlaceButtons(DockWindow w, DockItem it)
        {
            w.Places.Children.Clear();
            w.Places.Children.Add(new TextBlock
            {
                Text = "Yerleştir:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontSize = 11.5,
                ToolTip = "Pencereyi başlığından sürükleyip ana pencerenin sol, sağ veya alt kenarına bırakarak da yerleştirebilirsiniz"
            });
            ((TextBlock)w.Places.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
            foreach (var (label, place) in new[] { ("Sola", DockPlace.Left), ("Sağa", DockPlace.Right), ("Alta", DockPlace.Bottom) })
            {
                if (place == DockPlace.Bottom && !it.CanBottom) continue;
                var b = new Button
                {
                    Content = label, Height = 24, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(4, 0, 0, 0), FontSize = 11.5,
                    ToolTip = place == DockPlace.Left ? "Sol sütuna yerleştir" : place == DockPlace.Right ? "Sağ sütuna yerleştir" : "Alt bölmeye sekme olarak yerleştir"
                };
                b.Click += (_, _) => Dock(it, place);
                w.Places.Children.Add(b);
            }
        }

        private void Remember(DockItem it, Window w)
        {
            if (w.WindowState != WindowState.Normal || !w.IsLoaded) return;
            it.FloatRect = new Rect(w.Left, w.Top, w.ActualWidth > 0 ? w.ActualWidth : w.Width, w.ActualHeight > 0 ? w.ActualHeight : w.Height);
            ScheduleSave();
        }

        private void CloseWindow(DockItem it)
        {
            var w = it.Window;
            if (w == null) return;
            it.Window = null;
            w.ClosedByManager = true;
            w.Body.Child = null;
            w.Close();
        }

        private void OnOwnerVisible()
        {
            if (!_started) return;
            bool visible = _owner.IsVisible;
            if (visible && Window.GetWindow(_owner) is { } host) HookOwner(host);
            foreach (var it in _items.Where(i => i.Place == DockPlace.Float))
            {
                if (!visible)
                {
                    it.Window?.Hide();
                    continue;
                }

                if (it.Window == null)
                {
                    OpenWindow(it);
                    continue;
                }

                if (Window.GetWindow(_owner) is { } owner && it.Window.Owner != owner && owner != it.Window) it.Window.Owner = owner;
                it.Window.Show();
            }
        }

        private static bool OnScreen(Rect r)
        {
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var title = new Rect(r.X + 20, r.Y, Math.Max(40, r.Width - 40), 24);
            return screen.IntersectsWith(title) && r.Width > 100 && r.Height > 80;
        }

        // ---------------------------------------------------------------- drop targets while a window is dragged

        private sealed record Target(DockPlace Place, Rect Zone, Point Tile, Rect Preview, string Label);

        private List<Target> Targets(DockItem it)
        {
            var list = new List<Target>();
            if (!_main.IsVisible) return list;
            var area = _main.TransformToAncestor(_overlayHost).TransformBounds(new Rect(0, 0, _main.ActualWidth, _main.ActualHeight));
            double zone = Math.Min(160, area.Width * 0.22);
            double lw = _leftShown ? _main.ColumnDefinitions[_leftCol].ActualWidth : _leftWidth;
            double rw = _rightShown ? _main.ColumnDefinitions[_rightCol].ActualWidth : _rightWidth;
            list.Add(new Target(DockPlace.Left, new Rect(area.Left, area.Top, zone, area.Height), new Point(area.Left + 46, area.Top + area.Height / 2),
                new Rect(area.Left, area.Top, lw, area.Height), "Sol"));
            list.Add(new Target(DockPlace.Right, new Rect(area.Right - zone, area.Top, zone, area.Height), new Point(area.Right - 46, area.Top + area.Height / 2),
                new Rect(area.Right - rw, area.Top, rw, area.Height), "Sağ"));
            if (it.CanBottom)
            {
                Rect preview = _bottomShown && _bottomArea.IsVisible
                    ? _bottomArea.TransformToAncestor(_overlayHost).TransformBounds(new Rect(0, 0, _bottomArea.ActualWidth, _bottomArea.ActualHeight))
                    : new Rect(area.Left, area.Bottom - Math.Min(_bottomHeight, area.Height / 2), area.Width, Math.Min(_bottomHeight, area.Height / 2));
                double band = Math.Min(130, area.Height * 0.25);
                var z = new Rect(area.Left + zone, area.Bottom - band, Math.Max(10, area.Width - 2 * zone), band);
                z.Union(new Rect(preview.Left + zone, preview.Top, Math.Max(10, preview.Width - 2 * zone), preview.Height));
                list.Add(new Target(DockPlace.Bottom, z, new Point(area.Left + area.Width / 2, area.Bottom - 46), preview, "Alta"));   // not "Alt": that is also a key's name
            }

            return list;
        }

        /// <summary>Draws the drop guides as during a window drag, <paramref name="hot"/> lit (snapshots, docs pictures).</summary>
        public void ShowGuides(DockItem it, DockPlace? hot)
        {
            _overlayHost.UpdateLayout();
            Draw(Targets(it), hot);
            _overlay.Visibility = Visibility.Visible;
        }

        /// <summary>Where a window drag released at <paramref name="p"/> (in the page's coordinates) docks the panel; null = it stays a window.</summary>
        public DockPlace? TargetAt(DockItem it, Point p)
        {
            _overlayHost.UpdateLayout();
            return Targets(it).LastOrDefault(t => t.Zone.Contains(p))?.Place;
        }

        private void DragOver(DockItem it)
        {
            if (!_overlayHost.IsVisible || PresentationSource.FromVisual(_overlayHost) == null) return;
            var cursor = DockWindow.Cursor();
            if (double.IsNaN(cursor.X)) return;
            var p = _overlayHost.PointFromScreen(cursor);
            var targets = Targets(it);
            DockPlace? hot = null;
            foreach (var t in targets)
            {
                if (t.Zone.Contains(p)) hot = t.Place;
            }

            if (_overlay.Visibility != Visibility.Visible || hot != _hot || _overlay.Children.Count == 0) Draw(targets, hot);
            _hot = hot;
            _overlay.Visibility = Visibility.Visible;
        }

        private void DragEnd(DockItem it)
        {
            _overlay.Visibility = Visibility.Collapsed;
            _overlay.Children.Clear();
            var hot = _hot;
            _hot = null;
            if (hot is { } place) _owner.Dispatcher.BeginInvoke(new Action(() => Dock(it, place)));
        }

        private void Draw(List<Target> targets, DockPlace? hot)
        {
            _overlay.Children.Clear();
            var accent = _owner.TryFindResource("B.Accent") is SolidColorBrush a ? a.Color : Color.FromRgb(0x3B, 0x82, 0xC4);
            var fill = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            var line = new SolidColorBrush(accent);
            foreach (var t in targets)
            {
                bool on = t.Place == hot;
                if (on)
                {
                    var preview = new Rectangle { Width = t.Preview.Width, Height = t.Preview.Height, Fill = fill, Stroke = line, StrokeThickness = 2, RadiusX = 4, RadiusY = 4 };
                    Canvas.SetLeft(preview, t.Preview.Left);
                    Canvas.SetTop(preview, t.Preview.Top);
                    _overlay.Children.Add(preview);
                }

                var label = new TextBlock { Text = t.Label, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.ForegroundProperty, on ? "B.OnPrimary" : "B.Text");
                var tile = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(8), BorderBrush = line, BorderThickness = new Thickness(2), Child = label };
                if (on) tile.Background = line;
                else tile.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
                Canvas.SetLeft(tile, t.Tile.X - 30);
                Canvas.SetTop(tile, t.Tile.Y - 30);
                _overlay.Children.Add(tile);
            }
        }

        // ================================================================ persistence

        private void ScheduleSave()
        {
            if (!_started) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void Save()
        {
            if (!Persist || !_started) return;
            LayoutStore.Put(_layoutKey, Snapshot());
        }

        /// <summary>[workspaces] The layout as it is now: every panel's place, order and size, the columns, the bottom row.</summary>
        public DockLayoutState Snapshot()
        {
            CaptureSizes();
            var s = new DockLayoutState
            {
                LeftWidth = _leftWidth, RightWidth = _rightWidth, BottomHeight = _bottomHeight, BottomHidden = _bottomHidden,
                Tab = _items.FirstOrDefault(i => i.Place == DockPlace.Bottom && i.Chip?.IsChecked == true)?.Key
            };
            foreach (var it in _items)
            {
                s.Items[it.Key] = new DockItemState
                {
                    Place = it.Place.ToString(), LastDocked = it.LastDocked.ToString(), Order = it.Order, Weight = it.Weight,
                    X = it.FloatRect.IsEmpty ? 0 : it.FloatRect.X, Y = it.FloatRect.IsEmpty ? 0 : it.FloatRect.Y,
                    W = it.FloatRect.IsEmpty ? 0 : it.FloatRect.Width, H = it.FloatRect.IsEmpty ? 0 : it.FloatRect.Height
                };
            }

            return s;
        }

        /// <summary>[workspaces] Takes a saved layout (a workspace's): panels back where it had them, its sizes and bottom tab.</summary>
        public void Restore(DockLayoutState state)
        {
            foreach (var it in _items) CloseWindow(it);
            _leftShown = _rightShown = _bottomShown = false;   // the saved sizes, not the ones on screen now
            LoadState(state);
            Apply();
            Save();
        }

        /// <summary>[workspaces] The bottom row shown on one tab, or hidden (a built-in workspace asks only this).</summary>
        public void ShowBottom(bool show, string? tab = null)
        {
            _bottomHidden = !show;
            if (show && tab != null && Find(tab) is { Place: DockPlace.Bottom, Chip: { } chip })
            {
                _applying = true;
                chip.IsChecked = true;
                _applying = false;
            }

            Apply();
            ScheduleSave();
        }

        private void Load()
        {
            var s = LayoutStore.Get(_layoutKey);
            if (s != null) LoadState(s);
        }

        private void LoadState(DockLayoutState s)
        {
            if (s.LeftWidth >= 180) _leftWidth = s.LeftWidth;
            if (s.RightWidth >= 180) _rightWidth = s.RightWidth;
            if (s.BottomHeight >= 36) _bottomHeight = s.BottomHeight;
            _bottomHidden = s.BottomHidden;
            foreach (var it in _items)
            {
                if (!s.Items.TryGetValue(it.Key, out var st)) continue;
                if (Enum.TryParse(st.Place, out DockPlace p) && (p != DockPlace.Bottom || it.CanBottom)) it.Place = p;
                if (Enum.TryParse(st.LastDocked, out DockPlace l) && DockItem.IsDocked(l) && (l != DockPlace.Bottom || it.CanBottom)) it.LastDocked = l;
                it.Order = st.Order;
                it.Weight = st.Weight > 0 ? st.Weight : 1;
                if (st.W > 100 && st.H > 80) it.FloatRect = new Rect(st.X, st.Y, st.W, st.H);
            }

            if (s.Tab != null && Find(s.Tab) is { Place: DockPlace.Bottom, Chip: { } chip }) chip.IsChecked = true;
        }

        private DockItem? Find(string key) => _items.FirstOrDefault(i => i.Key == key);

        private static void Detach(FrameworkElement e)
        {
            switch (e.Parent)
            {
                case Panel p:
                    p.Children.Remove(e);
                    break;
                case Decorator d:
                    d.Child = null;
                    break;
                case ContentControl c:
                    c.Content = null;
                    break;
            }
        }
    }

    public sealed class DockLayoutState
    {
        public Dictionary<string, DockItemState> Items { get; set; } = new Dictionary<string, DockItemState>();

        public double LeftWidth { get; set; }

        public double RightWidth { get; set; }

        public double BottomHeight { get; set; }

        public bool BottomHidden { get; set; }

        public string? Tab { get; set; }
    }

    public sealed class DockItemState
    {
        public string Place { get; set; } = string.Empty;

        public string LastDocked { get; set; } = string.Empty;

        public int Order { get; set; }

        public double Weight { get; set; } = 1;

        public double X { get; set; }

        public double Y { get; set; }

        public double W { get; set; }

        public double H { get; set; }
    }

    /// <summary>[dock] Panel layouts of the pages, kept apart from the settings so moving a panel does not re-read every preference.</summary>
    internal static class LayoutStore
    {
        private static readonly string? TestPath = Environment.GetEnvironmentVariable("ROBOTWIN_LAYOUT");

        private static readonly string StorePath = string.IsNullOrWhiteSpace(TestPath)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "layout.json")
            : TestPath;

        /// <summary>ROBOTWIN_LAYOUT points the store at a test file (layout round-trip checks).</summary>
        public static bool Overridden => !string.IsNullOrWhiteSpace(TestPath);

        private static Dictionary<string, DockLayoutState>? _all;

        public static DockLayoutState? Get(string key)
        {
            Load();
            return _all!.TryGetValue(key, out var s) ? s : null;
        }

        public static void Put(string key, DockLayoutState state)
        {
            Load();
            _all![key] = state;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(_all, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void Load()
        {
            if (_all != null) return;
            try
            {
                if (File.Exists(StorePath)) _all = JsonSerializer.Deserialize<Dictionary<string, DockLayoutState>>(File.ReadAllText(StorePath));
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            _all ??= new Dictionary<string, DockLayoutState>();
        }
    }
}
