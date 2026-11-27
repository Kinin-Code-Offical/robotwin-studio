using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Pcb;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;
using static RobotWin.Studio.Pcb.PcbForms;
using Track = RobotTwin.CoreSim.Pcb.Track;
using Path = System.IO.Path;
using Geometry = System.Windows.Media.Geometry;

namespace RobotWin.Studio.Pages
{
    public partial class PcbPage : UserControl
    {
        private static Workspace W => Workspace.Current;
        private List<DrcViolation> _violations = new List<DrcViolation>();
        private DfmReport? _dfm;   // [dfm] the last manufacturability and test check, when its switch was on
        private readonly DispatcherTimer _fillTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private bool _autoFill = true;
        private bool _narrow;
        private int _toolbarLevel = -1;
        private bool _syncingLayerBox;

        private sealed class DrcItem
        {
            public DrcItem(DrcViolation v, DrcExclusion? exclusion = null)
            {
                Violation = v;
                Exclusion = exclusion;
                Tag = exclusion != null ? PcbForms.B("B.TextMuted") : v.Severity == DrcSeverity.Error ? PcbForms.B("B.Danger") : PcbForms.B("B.Warning");
            }

            public DrcViolation Violation { get; }

            /// <summary>[drc-policy] The exclusion that accepted this marker, when it is one.</summary>
            public DrcExclusion? Exclusion { get; }

            public string Rule => Exclusion == null ? Violation.Rule : Shell.Lang.T("İstisna") + " · " + Violation.Rule;
            public string Message => Exclusion is { Comment.Length: > 0 } e ? Violation.Message + " — " + e.Comment : Violation.Message;
            public Brush Tag { get; }
        }

        public PcbPage()
        {
            InitializeComponent();
            Canvas.ShowGrid = AppSettings.Current.PcbGridShown;   // [grid]
            Canvas.ZoneOutlines = AppSettings.Current.PcbZoneOutlines;   // [zone-look]
            if (Enum.TryParse<MagnetMode>(AppSettings.Current.PcbMagnetPads, out var magnetPads)) Canvas.MagnetPads = magnetPads;   // [magnet]
            if (Enum.TryParse<MagnetMode>(AppSettings.Current.PcbMagnetTracks, out var magnetTracks)) Canvas.MagnetTracks = magnetTracks;
            Canvas.MagnetGraphics = AppSettings.Current.PcbMagnetGraphics;
            if (Enum.TryParse<CopperLayer>(AppSettings.Current.PcbLayerPairA, out var pairA)) Canvas.PairA = pairA;   // [layer-pair]
            if (Enum.TryParse<CopperLayer>(AppSettings.Current.PcbLayerPairB, out var pairB)) Canvas.PairB = pairB;
            Canvas.TrackOpacity = AppSettings.Current.PcbTrackOpacity;   // [opacity]
            Canvas.ViaOpacity = AppSettings.Current.PcbViaOpacity;
            Canvas.PadOpacity = AppSettings.Current.PcbPadOpacity;
            Canvas.ZoneOpacity = AppSettings.Current.PcbZoneOpacity;
            GridToggle.IsChecked = Canvas.ShowGrid;
            Canvas.Constrain45 = AppSettings.Current.PcbConstrain45;   // [angle45]
            Canvas.CurvedRatsnest = AppSettings.Current.PcbCurvedRatsnest;   // [rats-curve]
            Canvas.FullCrosshair = AppSettings.Current.PcbFullCrosshair;   // [crosshair]
            Canvas.OverrideLocks = AppSettings.Current.PcbOverrideLocks;   // [locks]
            Angle45Toggle.IsChecked = Canvas.Constrain45;
            InitDock();   // [dock-min]
            Canvas.SelectionChanged += (_, _) => UpdateSelection();
            Canvas.BeforeEdit += (_, _) => W.BeginChange();
            Canvas.EditCancelled += (_, _) => W.CancelChange();
            Canvas.BoardEdited += (_, msg) => AfterEdit(msg);
            Canvas.RouteFinished += OnRouteFinished;
            Canvas.ZoneDrawn += OnZoneDrawn;
            Canvas.OutlineDrawn += OnOutlineDrawn;
            Canvas.TextRequested += OnTextRequested;
            Canvas.EditRequested += (_, item) => EditItem(item);
            Canvas.UndoRequested += (_, _) => W.Undo();
            Canvas.RedoRequested += (_, _) => W.Redo();
            Canvas.ToolChanged += (_, _) => SyncTools();
            InitToolGroups();   // [flyout] Pages/PcbPage.Tools.cs
            Canvas.Hint += (_, text) => HintText.Text = text;
            Canvas.CursorMoved += (_, _) => UpdateCursorText();
            Canvas.ActiveLayerChanged += (_, _) => SyncActiveLayer();
            Canvas.HighlightChanged += (_, _) => UpdateLayersPanel();
            Canvas.RefillRequested += (_, _) => OnFillZones(this, new RoutedEventArgs());
            Canvas.UnfillRequested += (_, _) =>
            {
                var b = Board;
                if (b.Zones.Count == 0) return;
                Edit("Dolgular boşaltıldı", () => b.Zones.ForEach(z => z.ClearFill()));
            };
            Canvas.HelpRequested += (_, _) => ToggleHelp();
            Canvas.ShowInSchematicRequested += (_, refDes) => (Owner as MainWindow)?.ShowPartInSchematic(refDes);
            Canvas.ChangeFootprintRequested += (_, comp) => PickFootprint(comp);
            Canvas.EditFootprintRequested += (_, comp) => OpenFootprintEditor(comp);
            Canvas.DeleteComponentsRequested += (_, parts) => DeleteParts(parts);
            Canvas.RevertRequested += (_, _) => W.Undo();
            Canvas.GridChanged += (_, _) =>
            {
                UpdatePanel();
                UpdateCursorText();
            };
            Canvas.DuplicateComponents = DuplicateParts;
            PreviewKeyDown += (_, e) =>
            {
                if (HelpOverlay.Visibility == Visibility.Visible && (e.Key == Key.Escape || e.Key == Key.F1))
                {
                    HelpOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                else if (OnDrcKey(e))
                {
                    e.Handled = true;   // [drc-policy] F8 / Shift+F8: next / previous marker
                }
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    OnHeadsFind(this, new RoutedEventArgs());   // [headsup] KiCad's Find
                    e.Handled = true;
                }
            };
            _fillTimer.Tick += (_, _) => AutoFill();
            W.DocumentReplaced += (_, _) =>
            {
                ClearDrc();
                if (IsVisible) OnShown();
            };
            W.DocumentChanged += (_, _) =>
            {
                // Undo/redo replace the board object.
                if (!ReferenceEquals(Canvas.Board, W.Schematic.Board) && W.Schematic.Board != null && Canvas.Board != null)
                {
                    ClearDrc();
                    Canvas.Board = W.Schematic.Board;
                    UpdatePanel();
                }
            };
            SizeChanged += (_, _) => ApplyLayout();
            Loaded += (_, _) => ApplyLayout();
            HintText.Text = Canvas.ToolHint();
            SyncTools();
        }

        private Window? Owner => Window.GetWindow(this);

        // ================================================================ responsive layout

        /// <summary>
        /// Fits the toolbar to the page width: full labels, then icon-only secondary buttons,
        /// then icon-only everything, then rarely used actions in the "⋯" menu. Below 900 px
        /// the properties panel becomes a toggled overlay so the board keeps the space.
        /// </summary>
        private void ApplyLayout()
        {
            double width = ActualWidth;
            if (width <= 0) return;
            bool narrow = width < 900;
            if (narrow != _narrow || _toolbarLevel < 0)
            {
                // [dock-min] the properties panel is a dock panel: a narrow page hides it (the toggle brings it back), a wide one keeps what was chosen
                bool first = _toolbarLevel < 0;
                _narrow = narrow;
                if (_dock != null && (narrow || !first)) _dock.SetVisible("properties", !narrow);
            }
            StatusBar.Margin = new Thickness(width < 760 ? 70 : 120, 0, 10, 6);
            CursorText.Visibility = width < 680 ? Visibility.Collapsed : Visibility.Visible;

            double available = width - 20;   // toolbar grid margins
            for (int level = 0; level <= 4; level++)
            {
                ApplyToolbarLevel(level);
                // Measure is cached up the tree: flush layout so the new label visibility counts.
                ToolbarLeft.UpdateLayout();
                ToolbarRight.UpdateLayout();
                ToolbarLeft.Measure(new Size(double.PositiveInfinity, 44));
                ToolbarRight.Measure(new Size(double.PositiveInfinity, 44));
                if (ToolbarLeft.DesiredSize.Width + ToolbarRight.DesiredSize.Width + 16 <= available) break;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // Templates may not be applied on the first pass: re-check once layout has settled.
            Dispatcher.BeginInvoke(new Action(ApplyLayout), DispatcherPriority.Loaded);
        }

        private IEnumerable<(Button Button, int Level)> Labelled() => new[]
        {
            (AddPartButton, 1), (SyncButton, 1), (PlaceButton, 1), (FillButton, 1), (SetupButton, 1), (FootprintButton, 1),
            (ModuleButton, 1), (KiCadButton, 1), (AutoRouteButton, 2), (DrcButton, 2), (ExportButton, 2)
        };

        private void ApplyToolbarLevel(int level)
        {
            if (level == _toolbarLevel) return;
            _toolbarLevel = level;
            foreach (var (button, from) in Labelled())
            {
                foreach (var tb in FindLabels(button)) tb.Visibility = level >= from ? Visibility.Collapsed : Visibility.Visible;
                button.Padding = level >= from ? new Thickness(8, 5, 8, 5) : new Thickness(12, 5, 12, 5);
            }

            ActiveLayerBox.Width = level >= 2 ? 92 : 118;
            var overflow = new (UIElement Item, string Label, RoutedEventHandler Click)[]
            {
                (SyncButton, "Şematikten güncelle", OnSync), (PlaceButton, "Otomatik yerleştir", OnPlace), (FillButton, "Dolguları doldur", OnFillZones),
                (SetupButton, "Kart ayarları…", OnBoardSetup), (FootprintButton, "Footprint düzenleyici…", OnFootprintEditor),
                (ModuleButton, "Modül yap…", OnSaveAsModule), (KiCadButton, "KiCad'e aktar…", OnExportKiCad)
            };
            OverflowList.Children.Clear();
            foreach (var (item, label, click) in overflow)
            {
                item.Visibility = level >= 3 ? Visibility.Collapsed : Visibility.Visible;
                if (level < 3) continue;
                var b = new Button { Content = label, Style = S("Btn.Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 2) };
                b.Click += (s, e) =>
                {
                    OverflowPopup.IsOpen = false;
                    click(s, e);
                };
                OverflowList.Children.Add(b);
            }

            View3DToggle.Visibility = level >= 4 ? Visibility.Collapsed : Visibility.Visible;
            if (level >= 4)
            {
                var b = new Button { Content = "3D görünüm", Style = S("Btn.Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
                b.Click += (_, _) =>
                {
                    OverflowPopup.IsOpen = false;
                    Show3D(!(View3D.Visibility == Visibility.Visible));
                };
                OverflowList.Children.Add(b);
            }

            Sep1.Margin = level >= 2 ? new Thickness(6, 0, 6, 0) : new Thickness(10, 0, 10, 0);
            OverflowButton.Visibility = level >= 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static IEnumerable<TextBlock> FindLabels(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            if (n == 0 && root is ContentControl cc && cc.Content is DependencyObject content)
            {
                foreach (var t in FindLabels(content)) yield return t;
                yield break;
            }

            if (root is TextBlock tb && Equals(tb.Tag, "label")) yield return tb;
            if (root is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    foreach (var t in FindLabels(child)) yield return t;
                }

                yield break;
            }

            for (int i = 0; i < n; i++)
            {
                foreach (var t in FindLabels(VisualTreeHelper.GetChild(root, i))) yield return t;
            }
        }

        private void OnOverflow(object sender, RoutedEventArgs e) => OverflowPopup.IsOpen = !OverflowPopup.IsOpen;

        private void OnPanelToggle(object sender, RoutedEventArgs e) => _dock?.SetVisible("properties", PanelToggle.IsChecked == true);

        /// <summary>Shows the properties panel at a tab, wherever it is docked (out of its strip, back from hidden).</summary>
        private void ShowPanel(int tab)
        {
            Tabs.SelectedIndex = tab;
            if (_dock != null && (_dock.PlaceOf("properties") == DockPlace.Hidden || _dock.IsMinimized(_dock.PlaceOf("properties")) || _dock.Collapsed))
                _dock.SetVisible("properties", true);
        }

        // ================================================================ [dock-min] movable panels

        private DockManager? _dock;

        /// <summary>[workspaces] The page's panels; null before the page is built.</summary>
        public DockManager? Panels => _dock;

        /// <summary>The properties panel (board, layers, nets, DRC) docks left or right, minimizes to a strip or tears off into its own window.</summary>
        private void InitDock()
        {
            _dock = new DockManager("pcb", this, PageRoot, ContentGrid, 1, 5, DockTabs, DockHost, BottomArea, DockRow, DockLineRow);
            _dock.Add(new DockItem("properties", "Özellikler", SidePanel, DockPlace.Right));
            _dock.Changed += () => PanelToggle.IsChecked = _dock.PlaceOf("properties") != DockPlace.Hidden && !_dock.Collapsed && !_dock.IsMinimized(_dock.PlaceOf("properties"));
            _dock.Start();
        }

        // ================================================================ editing plumbing

        private void AfterEdit(string message)
        {
            W.CommitChange(message);
            ClearDrc();
            ScheduleFill();
            UpdatePanel();
        }

        /// <summary>Records an edit made by the page (dialogs, panel fields) as one undo step.</summary>
        private void Edit(string message, Action change)
        {
            W.BeginChange();
            change();
            Canvas.Refresh();
            AfterEdit(message);
        }

        private void ScheduleFill()
        {
            if (!_autoFill || Board.Zones.Count == 0) return;
            _fillTimer.Stop();
            _fillTimer.Start();
        }

        private void AutoFill()
        {
            _fillTimer.Stop();
            var board = Board;
            if (board.Zones.Count == 0) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int n = ZoneFiller.RefillOutdated(board);
            if (n == 0) return;
            W.MarkBoardChanged($"{n} dolgu yeniden dolduruldu ({watch.ElapsedMilliseconds} ms)");
            Canvas.Refresh();
            UpdatePanel();
        }

        // ================================================================ PCB-first design

        /// <summary>
        /// A finished manual route. When it joins pads, the connection is written back to the
        /// schematic as net labels, so the simulation and the PCB always agree.
        /// </summary>
        private void OnRouteFinished(object? sender, BoardCanvas.RouteResult r)
        {
            var board = Board;
            string net = r.Net;
            W.BeginChange();
            bool labelled = false;
            if (r.StartPad != null && r.EndPad != null || r.StartPad != null && net.Length == 0)
            {
                string a = r.StartPad?.Net ?? net, b = r.EndPad?.Net ?? string.Empty;
                var netlist = W.Schematic.BuildNetlist(W.Library);
                bool aAuto = a.Length == 0 || netlist.Nets.FirstOrDefault(n => n.Name == a)?.IsAutoNamed == true;
                bool bAuto = b.Length == 0 || netlist.Nets.FirstOrDefault(n => n.Name == b)?.IsAutoNamed == true;
                if (a.Length > 0 && b.Length > 0 && !string.Equals(a, b, StringComparison.OrdinalIgnoreCase) && !aAuto && !bAuto)
                {
                    W.CancelChange();
                    Dialog.Show(Owner!, "Kısa devre", $"{r.StartPad!.Value.Component.RefDes} pedi '{a}', {r.EndPad!.Value.Component.RefDes} pedi '{b}' hattında. Farklı hatlar yolla birleştirilemez.", "Tamam");
                    return;
                }

                // The surviving name: a named net wins; otherwise make a fresh one.
                net = !aAuto ? a : !bAuto ? b : a.Length > 0 && b.Length == 0 ? a : NewNetName(netlist);
                foreach (var pad in new[] { r.StartPad, r.EndPad })
                {
                    if (pad == null || string.Equals(pad.Value.Net, net, StringComparison.OrdinalIgnoreCase)) continue;
                    labelled |= LabelPin(pad.Value, net);
                    if (pad.Value.Net.Length > 0)
                    {
                        foreach (var t in board.Tracks.Where(t => t.Net == pad.Value.Net)) t.Net = net;
                        foreach (var v in board.Vias.Where(v => v.Net == pad.Value.Net)) v.Net = net;
                    }
                }

                if (labelled) BoardDocument.Synchronize(W.Schematic, W.Library, board);
            }

            var cls = board.ClassFor(net);
            foreach (var seg in r.Segments)
            {
                board.Tracks.Add(new Track { Net = net, Layer = seg.Layer, X1 = seg.X1, Y1 = seg.Y1, X2 = seg.X2, Y2 = seg.Y2, Width = r.Width > 0 ? r.Width : cls.TrackWidth });
            }

            var viaSize = Canvas.ViaFor(board, net);   // [flyout] the options bar's via, or the class's
            foreach (var v in r.Vias)
            {
                board.Vias.Add(new Via { Net = net, X = v.X, Y = v.Y, Diameter = viaSize.Diameter, Drill = viaSize.Drill, StartLayer = v.From, EndLayer = v.To });
            }

            string from = r.StartPad?.Component.RefDes ?? "yol", to = r.EndPad?.Component.RefDes ?? "yol";
            Canvas.Board = board;
            AfterEdit($"{from} → {to} yolu çizildi ({net}{(labelled ? ", şemaya işlendi" : string.Empty)}) · {r.Segments.Count} parça, {r.Vias.Count} via");
        }

        private static string NewNetName(Netlist netlist)
        {
            for (int i = 1; ; i++)
            {
                string n = "N$" + i.ToString(CultureInfo.InvariantCulture);
                if (netlist.Nets.All(x => !string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase))) return n;
            }
        }

        /// <summary>Drops a net label on the schematic pin behind a pad.</summary>
        private bool LabelPin(PlacedPad pad, string net)
        {
            var part = W.Schematic.FindPart(pad.Component.RefDes);
            var def = part != null ? W.Library.Find(part.PartId) : null;
            if (part == null || def == null) return false;
            var parameters = W.Schematic.ResolveParameters(W.Library, part);
            var pin = def.Pins.FirstOrDefault(p => string.Equals(def.ResolvePad(p, parameters), pad.Pad.Name, StringComparison.OrdinalIgnoreCase));
            if (pin == null) return false;
            W.Schematic.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pin.X, pin.Y) });
            return true;
        }

        // ================================================================ tools

        private void OnTool(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && Enum.TryParse<PcbTool>((string)fe.Tag, out var tool))
            {
                Show3D(false);
                Canvas.SetTool(tool);
                Canvas.Focus();
            }

            SyncTools();
        }

        private void SyncTools()
        {
            foreach (var tb in ToolStrip.Children.OfType<ToggleButton>())
            {
                Theme.ToolFlyout.Select(tb, Canvas.Tool.ToString());   // [flyout] a group shows the tool picked by its key too
                tb.IsChecked = Enum.TryParse<PcbTool>((string)tb.Tag, out var t) && t == Canvas.Tool;
            }

            UpdateToolOptions();

            if (Canvas.Tool == PcbTool.Outline || Canvas.Tool == PcbTool.Hole) ShowPanel(0);
        }

        private void OnZoneDrawn(object? sender, List<PcbPoint> pts)
        {
            var board = Board;
            var zone = new Zone { Net = Canvas.HighlightNet ?? "GND", Layer = Canvas.ActiveLayer, Outline = pts };
            if (!PcbDialogs.ZoneProperties(Owner, board, zone, true, out _)) return;
            Edit($"{zone.Net} dolgu alanı eklendi ({PcbLayers.DisplayName(zone.Layer)})", () =>
            {
                board.Zones.Add(zone);
                ZoneFiller.FillAll(board);
            });
            Canvas.Select(zone);
            Canvas.SetTool(PcbTool.Select);
        }

        private void OnOutlineDrawn(object? sender, List<PcbPoint> pts)
        {
            var board = Board;
            Edit("Kart kenarı çokgen olarak çizildi", () =>
            {
                board.Outline = pts.Select(p => new OutlinePoint(p.X, p.Y, Canvas.OutlineCornerRadius)).ToList();
                board.FitSizeToOutline();
            });
            Canvas.SetTool(PcbTool.Select);
            Canvas.Fit();
        }

        private void OnTextRequested(object? sender, PcbPoint at)
        {
            var board = Board;
            var text = new BoardText { Text = string.IsNullOrWhiteSpace(W.Schematic.Title) ? "ROBOTWIN" : W.Schematic.Title, X = at.X, Y = at.Y, Size = 1.5, Layer = Canvas.ActiveLayer == CopperLayer.Bottom ? GraphicLayer.SilkBottom : GraphicLayer.SilkTop };
            if (!PcbDialogs.TextProperties(Owner, text, true, out _)) return;
            Edit($"'{text.Text}' yazısı eklendi", () => board.Texts.Add(text));
            Canvas.Select(text);
        }

        private void EditItem(object item)
        {
            var board = Board;
            switch (item)
            {
                case Zone z:
                {
                    var copy = z.CloneSettings();
                    if (!PcbDialogs.ZoneProperties(Owner, board, copy, false, out bool delete)) return;
                    if (delete)
                    {
                        Edit($"{z.Net} dolgusu silindi", () => board.Zones.Remove(z));
                        return;
                    }

                    Edit($"{copy.Net} dolgusu güncellendi", () =>
                    {
                        z.Net = copy.Net;
                        z.Layer = copy.Layer;
                        z.Name = copy.Name;
                        z.Priority = copy.Priority;
                        z.Clearance = copy.Clearance;
                        z.MinWidth = copy.MinWidth;
                        z.PadConnection = copy.PadConnection;
                        z.ThermalGap = copy.ThermalGap;
                        z.ThermalSpokeWidth = copy.ThermalSpokeWidth;
                        z.IslandRemoval = copy.IslandRemoval;
                        z.MinIslandArea = copy.MinIslandArea;
                        ZoneFiller.FillAll(board);
                    });
                    return;
                }

                case BoardText t:
                {
                    var copy = new BoardText { Text = t.Text, X = t.X, Y = t.Y, Size = t.Size, Thickness = t.Thickness, Layer = t.Layer, Rotation = t.Rotation };
                    if (!PcbDialogs.TextProperties(Owner, copy, false, out bool delete)) return;
                    Edit(delete ? "Yazı silindi" : "Yazı güncellendi", () =>
                    {
                        if (delete)
                        {
                            board.Texts.Remove(t);
                            return;
                        }

                        t.Text = copy.Text;
                        t.Size = copy.Size;
                        t.Thickness = copy.Thickness;
                        t.Layer = copy.Layer;
                        t.Rotation = copy.Rotation;
                    });
                    return;
                }

                case BoardComponent c:
                {
                    if (!PcbDialogs.ComponentProperties(Owner, c, out var r)) return;
                    Edit($"{c.RefDes} özellikleri güncellendi", () =>
                    {
                        c.X = r.X;
                        c.Y = r.Y;
                        c.Rotation = r.Rotation;
                        c.Side = r.Side;
                        c.Locked = r.Locked;
                    });
                    return;
                }

                default:
                    ShowPanel(0);
                    return;
            }
        }

        // ================================================================ add part

        /// <summary>Places a library part straight onto the PCB (it is added to the schematic too).</summary>
        private void OnAddPart(object sender, RoutedEventArgs e)
        {
            AddPartPopup.IsOpen = true;
            AddPartSearch.Text = string.Empty;
            FilterAddPart();
            AddPartSearch.Focus();
        }

        private void OnAddPartSearch(object sender, TextChangedEventArgs e) => FilterAddPart();

        private void FilterAddPart()
        {
            var tokens = RobotWin.Studio.Shell.Lang.Fold(AddPartSearch.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            AddPartList.ItemsSource = W.Library.Presets
                .Where(p => W.Library.Find(p.PartId) is { } d && !string.IsNullOrEmpty(d.Footprint) && d.Symbol != SymbolKind.Ground)
                .Where(p => tokens.Length == 0 || RobotWin.Studio.Shell.Lang.Searchable(p.Name, p.Id, p.Category) is var hay && tokens.All(t => hay.Contains(t)))
                .Take(200)
                .Select(p => new ComboBoxItem { Content = p.Name + "  ·  " + p.Category, Tag = p.Id })
                .ToList();
        }

        private void OnAddPartPick(object sender, MouseButtonEventArgs e)
        {
            if (AddPartList.SelectedItem is not ComboBoxItem item) return;
            AddPartPopup.IsOpen = false;
            string preset = (string)item.Tag;
            var board = Board;
            W.BeginChange();
            double x = W.Schematic.Parts.Count == 0 ? 0 : W.Schematic.Parts.Max(p => p.Position.X) + 14;
            var part = W.Schematic.AddPart(W.Library, preset, new GridPoint(x, 0));
            BoardDocument.Synchronize(W.Schematic, W.Library, board);
            var comp = board.Find(part.RefDes);
            if (comp != null)
            {
                comp.X = Math.Round(board.Width / 2 / 0.635) * 0.635;
                comp.Y = Math.Round(board.Height / 2 / 0.635) * 0.635;
            }

            Canvas.Board = board;
            AfterEdit($"{part.RefDes} karta eklendi (şemaya da eklendi)");
            Canvas.Select(comp);
            Canvas.Focus();
        }

        // ================================================================ board lifecycle

        private BoardDocument Board
        {
            get
            {
                if (W.Schematic.Board == null)
                {
                    var board = BoardDocument.Synchronize(W.Schematic, W.Library);
                    SizeBoardToContent(board);
                    AutoPlacer.Place(board);
                    W.Schematic.Board = board;
                }

                return W.Schematic.Board;
            }
        }

        /// <summary>
        /// Called when the page becomes visible: keep nets in sync with the schematic. [integrity] A change that would take
        /// laid-out work away (footprints removed or swapped, tracks and vias deleted) waits for the user's go-ahead.
        /// </summary>
        public void OnShown() => BringUpToDate(false);

        /// <summary>[integrity] The schematic's changes the board is waiting for, until the user updates it.</summary>
        private BoardChanges? _pending;

        private void BringUpToDate(bool confirmed)
        {
            var board = Board;
            int before = board.Components.Count;
            var changes = BoardSync.Preview(W.Schematic, W.Library, board);   // [integrity] what bringing it up to date does
            if (changes.Destructive && !confirmed)
            {
                _pending = changes;
                Canvas.Board = board;
                Canvas.Violations = _violations;
                UpdatePanel();
                Dispatcher.BeginInvoke(new Action(Canvas.Fit), DispatcherPriority.Loaded);
                AskToUpdate(changes);
                return;
            }

            _pending = null;
            MarkSyncButton();
            BoardDocument.Synchronize(W.Schematic, W.Library, board);
            var unplaced = board.Components.Where(c => c.X == 0 && c.Y == 0).ToList();
            if (unplaced.Count > 0 && unplaced.Count < board.Components.Count)
            {
                foreach (var c in board.Components) c.Locked = !unplaced.Contains(c);
                AutoPlacer.Place(board);
                foreach (var c in board.Components) c.Locked = false;
            }

            Canvas.Board = board;
            Canvas.Violations = _violations;
            UpdatePanel();
            Dispatcher.BeginInvoke(new Action(Canvas.Fit), DispatcherPriority.Loaded);
            if (before != board.Components.Count) W.Status = $"PCB güncellendi: {board.Components.Count} bileşen";
            if (changes.Destructive) ShowChanges(changes);
        }

        /// <summary>
        /// [integrity] Laid-out work the schematic's changes took away (footprints removed or changed, tracks deleted): said
        /// over the page, with every change one click away. A part that comes back is put where it stood.
        /// </summary>
        private void ShowChanges(BoardChanges changes)
        {
            W.Status = "PCB şemaya göre güncellendi.";
            if (Window.GetWindow(this) is not MainWindow main) return;
            main.ShowNotice("PCB şemaya göre güncellendi:\n" + changes.Summary(done: true), "Ayrıntılar",
                () => Dialog.Show(main, "PCB şemaya göre güncellendi", string.Join("\n", changes.Lines(done: true)), "Tamam"));
        }

        /// <summary>
        /// [integrity] The board is left as it was, and the user is asked over the page: what updating it would take away,
        /// one click to do it (the Update button stays marked until then, its tooltip listing every change).
        /// </summary>
        private void AskToUpdate(BoardChanges changes)
        {
            MarkSyncButton();
            W.Status = "Kart şemanın gerisinde: güncellemek yerleşimden bir şeyler siler.";
            if (Window.GetWindow(this) is not MainWindow main) return;
            main.ShowNotice("Şema değişti. Kartı güncellemek şunları yapacak:\n" + changes.Summary(done: false), "Kartı güncelle",
                () => OnSync(this, new RoutedEventArgs()));
        }

        /// <summary>The Update button stands out while the board waits for the schematic's changes.</summary>
        private void MarkSyncButton()
        {
            if (_pending == null)
            {
                SyncButton.ClearValue(StyleProperty);
                SyncButton.ToolTip = "Şematikteki değişiklikleri karta aktar (yerleşim korunur)";
                return;
            }

            SyncButton.Style = (Style)FindResource("Btn.Primary");
            SyncButton.ToolTip = "Kart şemanın gerisinde. Güncellemek:\n" + string.Join("\n", _pending.Lines(done: false));
        }

        /// <summary>Used by --snapshot: auto-routes the example and puts the editor in a representative state.</summary>
        public void AutoLayoutForSnapshot(string? fault = null)
        {
            var board = Board;
            switch (fault)
            {
                case "zones":
                case "4layer":
                case "zone-outlines":   // [zone-look]
                    board.Stackup.CopperLayers = 4;
                    board.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.In1, Name = "GND düzlemi" });
                    board.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.Bottom });
                    break;
            }

            new AutoRouter(board).Route();
            if (board.Zones.Count > 0) ZoneFiller.FillAll(board);
            Canvas.Board = board;
            SetDrc(DesignRuleChecker.Check(board));
            UpdatePanel();
            Canvas.Fit();
            switch (fault)
            {
                case "zones":
                case "4layer":
                    board.Texts.Add(new BoardText { Text = "ROBOTWIN 4L", X = 22, Y = board.Height - 4, Size = 2 });
                    board.MountingHoles.Add(new MountingHole { X = 4, Y = 4, Drill = 3.2 });
                    board.Dimensions.Add(new BoardDimension { X1 = 0, Y1 = 0, X2 = board.Width, Y2 = 0, Offset = -4 });
                    ZoneFiller.FillAll(board);
                    Canvas.ActiveLayer = CopperLayer.Bottom;
                    Canvas.Refresh();
                    UpdatePanel();
                    ShowPanel(1);
                    break;
                case "teardrops":   // [teardrop] the routed board with teardrops, zoomed onto the busiest via
                {
                    board.Teardrops.Enabled = true;
                    if (board.Zones.Count > 0) ZoneFiller.FillAll(board);
                    var drops = Teardrops.Of(board);
                    var at = drops.Count > 0 ? RobotTwin.CoreSim.Pcb.Polygon.Bounds(drops[drops.Count / 2].Outline) : (0, 0, board.Width, board.Height);
                    Canvas.ZoomTo((at.MinX + at.MaxX) / 2, (at.MinY + at.MaxY) / 2);
                    Canvas.ZoomBy(2.5);
                    Canvas.Refresh();
                    W.Status = $"Damla: {drops.Count} bağlantıda";
                    break;
                }

                case "clearance-res":   // [clearance-res] the two closest tracks of different nets, resolved
                {
                    (Track A, Track B, double D)? closest = null;
                    foreach (var ta in board.Tracks)
                    foreach (var tb in board.Tracks)
                    {
                        if (ReferenceEquals(ta, tb) || ta.Layer != tb.Layer || ta.Net == tb.Net) continue;
                        var rep = ClearanceResolution.Between(board, ta, tb);
                        if (rep?.Layers.Count > 0 && (closest == null || rep.Layers[0].Actual < closest.Value.D)) closest = (ta, tb, rep.Layers[0].Actual);
                    }

                    if (closest is { } pair)
                    {
                        Canvas.SetSelection(new object[] { pair.A, pair.B });
                        ShowClearanceResolution();
                    }

                    break;
                }

                case "net-colors":   // [net-colors] the supply red, ground green, one signal blue; the Hatlar tab open
                {
                    foreach (var net in board.NetNames())
                    {
                        if (net.Contains("GND", StringComparison.OrdinalIgnoreCase)) board.NetColors[net] = "#46A758";
                        else if (net.StartsWith("V", StringComparison.OrdinalIgnoreCase) || net.Contains("5V") || net.Contains("12V")) board.NetColors[net] = "#E5484D";
                    }

                    var signal = board.NetNames().FirstOrDefault(n => !board.NetColors.ContainsKey(n));
                    if (signal != null) board.NetColors[signal] = "#3E7BFA";
                    Canvas.Refresh();
                    UpdatePanel();
                    ShowPanel(2);
                    break;
                }

                case "opacity":   // [opacity] tracks at half, the Katmanlar tab scrolled to its sliders
                    Canvas.TrackOpacity = 0.5;
                    Canvas.ZoneOpacity = 0.6;
                    UpdateLayersPanel();
                    ShowPanel(1);
                    if (LayersPanel.Parent is ScrollViewer osv) Dispatcher.BeginInvoke(new Action(osv.ScrollToEnd), System.Windows.Threading.DispatcherPriority.Loaded);
                    break;

                case "zone-outlines":   // [zone-look] the 4-layer board's pours drawn by their edges
                    ZoneFiller.FillAll(board);
                    Canvas.ActiveLayer = CopperLayer.Bottom;
                    Canvas.ZoneOutlines = true;
                    Canvas.Refresh();
                    break;

                case "dims":   // [dims] one of each kind: aligned across the top, orthogonal down the side, a mounting hole's radius and centre, a leader
                {
                    board.MountingHoles.Add(new MountingHole { X = 5, Y = 5, Drill = 3.2, PadDiameter = 6 });
                    board.Dimensions.Add(new BoardDimension { X1 = 0, Y1 = 0, X2 = board.Width, Y2 = 0, Offset = -4 });
                    board.Dimensions.Add(new BoardDimension { Kind = DimensionKind.Orthogonal, Vertical = true, X1 = board.Width, Y1 = 0, X2 = board.Width, Y2 = board.Height, Offset = 5 });
                    board.Dimensions.Add(new BoardDimension { Kind = DimensionKind.Center, X1 = 5, Y1 = 5, X2 = 9, Y2 = 5 });
                    board.Dimensions.Add(new BoardDimension { Kind = DimensionKind.Radial, X1 = 5, Y1 = 5, X2 = 5 + 3 / Math.Sqrt(2), Y2 = 5 + 3 / Math.Sqrt(2), Offset = 4 });
                    board.Dimensions.Add(new BoardDimension { Kind = DimensionKind.Leader, X1 = board.Width - 6, Y1 = board.Height - 4, X2 = board.Width + 3, Y2 = board.Height + 5, Text = "M3 VİDA" });
                    Canvas.ShowDrawings = true;
                    Canvas.Refresh();
                    Canvas.SelectAll();
                    Canvas.FitSelection();
                    Canvas.Select(null);
                    break;
                }

                case "origins":   // [origins] the drill/place origin on the bottom-left mounting hole, a 2.54 mm grid run from a hole at the top
                    board.MountingHoles.Add(new MountingHole { X = 4, Y = board.Height - 4, Drill = 3.2, PadDiameter = 6 });
                    board.MountingHoles.Add(new MountingHole { X = 4, Y = 4, Drill = 3.2, PadDiameter = 6 });
                    board.AuxOrigin = new RobotTwin.CoreSim.Pcb.PcbPoint(4, board.Height - 4);
                    board.GridOrigin = new RobotTwin.CoreSim.Pcb.PcbPoint(4, 4);
                    Canvas.GridSize = 2.54;
                    Canvas.Refresh();
                    break;

                case "textbox":   // [textbox] a framed note wrapped to 22 mm and a two-line label beside the parts
                    board.Texts.Add(new BoardText { Text = "MOTOR SURUCU KARTI - 6..12 V GIRIS, EN COK 2 A", X = 14, Y = 26, Size = 1.0, BoxWidth = 22, Border = true });
                    board.Texts.Add(new BoardText { Text = "REV B\nLOT 2026-11", X = 38, Y = 26.5, Size = 0.9 });
                    Canvas.Refresh();
                    break;

                case "viewports":   // [viewports] two saved views on the Layers tab, the second shown
                    board.Viewports = new System.Collections.Generic.List<BoardViewport>
                    {
                        new BoardViewport { Name = "Güç girişi", X = 8, Y = 12, Width = 22 },
                        new BoardViewport { Name = "MOSFET", X = 24, Y = 16, Width = 18 }
                    };
                    Tabs.SelectedIndex = 1;
                    UpdateLayersPanel();
                    UpdateLayout();
                    Canvas.ShowView(board.Viewports[1]);
                    break;

                case "local-rats":   // [local-rats] the board unrouted, its ratsnest off: only Q1's lines, KiCad's local ratsnest
                    board.Tracks.Clear();
                    board.Vias.Clear();
                    Canvas.ShowRatsnest = false;
                    Canvas.LocalRatsnest.Add(board.Components.FirstOrDefault(c => c.RefDes.StartsWith("Q", StringComparison.OrdinalIgnoreCase))?.RefDes ?? board.Components[0].RefDes);
                    Canvas.Refresh();
                    break;

                case "rats-curve":   // [rats-curve] the board unrouted, its ratsnest drawn as curves
                    board.Tracks.Clear();
                    board.Vias.Clear();
                    Canvas.CurvedRatsnest = true;
                    Canvas.Refresh();
                    break;

                case "angle45":   // [angle45] a line drawn with the H / V / 45° constraint: the pointer at 39°, the line at 45°
                    Canvas.Constrain45 = true;
                    Angle45Toggle.IsChecked = true;
                    Canvas.SimulateDrawForSnapshot(5, 26, 15, 18);
                    break;

                case "magnet":   // [magnet] a line from a pad's centre, the pointer a little off a track's end: caught there
                {
                    var pad = board.Pads().First();
                    var track = board.Tracks.OrderByDescending(t => t.X2).First();
                    Canvas.SimulateDrawForSnapshot(pad.X + 0.2, pad.Y - 0.2, track.X2 + 0.25, track.Y2 + 0.2);
                    ShowPanel(0);
                    break;
                }

                case "footprints":   // [fp-assign] the Assign Footprints table
                    ShowFootprintTable();
                    break;

                case "plot":   // [plot] the Plot window; --plot-out <folder> also writes what it plots, to look at
                    ShowPlotDialog();
                    int po = Array.IndexOf(Environment.GetCommandLineArgs(), "--plot-out");
                    if (po >= 0 && po + 1 < Environment.GetCommandLineArgs().Length)
                    {
                        string dir = Environment.GetCommandLineArgs()[po + 1];
                        var all = BoardPlot.Layers(Board);
                        var two = all.Where(l => l.Id is "F.Cu" or "B.Cu" or "F.Silkscreen" or "Edge.Cuts").ToList();
                        File.WriteAllText(Path.Combine(dir, "plot-F_Cu.svg"), BoardPlot.Svg(Board, all[0], new PlotOptions { Title = "Motor" }));
                        File.WriteAllText(Path.Combine(dir, "plot-mirror-bw.svg"), BoardPlot.Svg(Board, all[0], new PlotOptions { Title = "Motor", Mirror = true, Color = false }));
                        File.WriteAllText(Path.Combine(dir, "plot-all.svg"), BoardPlot.Svg(Board, two, new PlotOptions { Title = "Motor" }));
                        File.WriteAllBytes(Path.Combine(dir, "plot.pdf"), BoardPlot.Pdf(Board, two, new PlotOptions { Title = "Motor sürücü" }));
                    }

                    break;

                case "teardrops-panel":   // [teardrop] the settings panel, open, as the picture
                    OnTeardrops(TeardropButton, new RoutedEventArgs());
                    if (_teardropPanel?.Child is FrameworkElement panel)
                    {
                        panel.UpdateLayout();
                        App.SnapshotTarget = panel;
                    }

                    break;

                case "drc":
                case "problems":   // [problems] a DRC marker for the Problems window
                    board.Tracks.Add(new Track { Net = board.NetNames().First(), Layer = CopperLayer.Top, X1 = 2, Y1 = 2, X2 = board.Width / 2, Y2 = board.Height / 2, Width = 0.3 });
                    RunDrcNow();
                    if (DrcList.Items.Count > 0) DrcList.SelectedIndex = 0;
                    ShowPanel(3);
                    break;
                case "drc-policy":   // [drc-policy] one marker accepted with a note, a warning rule made an error, exclusions listed
                    board.Tracks.Add(new Track { Net = board.NetNames().First(), Layer = CopperLayer.Top, X1 = 2, Y1 = 2, X2 = board.Width / 2, Y2 = board.Height / 2, Width = 0.3 });
                    RunDrcNow();
                    if (_drc.Shown.FirstOrDefault(v => v.Severity == DrcSeverity.Error) is { } accepted) DrcPolicy.Exclude(board, accepted, "bilerek: kart kenarındaki test noktası");
                    DrcPolicy.SetSeverity(board, "Boşta yol ucu", DrcRuleSeverity.Error);
                    ShowExcluded.IsChecked = true;
                    ApplyDrcPolicy();
                    UpdatePanel();
                    ShowPanel(3);
                    break;
                case "drc-rules":   // [drc-policy] the severity of every rule
                    board.Tracks.Add(new Track { Net = board.NetNames().First(), Layer = CopperLayer.Top, X1 = 2, Y1 = 2, X2 = board.Width / 2, Y2 = board.Height / 2, Width = 0.3 });
                    DrcPolicy.SetSeverity(board, "Serigrafi pedde", DrcRuleSeverity.Ignore);
                    RunDrcNow();
                    ShowPanel(3);
                    DrcRulesToggle.IsChecked = true;
                    OnDrcRules(this, new RoutedEventArgs());
                    break;
                case "keepout":   // [keepout] an antenna's keepout in the free corner, a track run through it, DRC
                    board.RuleAreas.Add(new RuleArea { Name = "Anten", Outline = { new PcbPoint(board.Width - 14, board.Height - 11), new PcbPoint(board.Width - 3, board.Height - 11), new PcbPoint(board.Width - 3, board.Height - 3), new PcbPoint(board.Width - 14, board.Height - 3) } });
                    board.Tracks.Add(new Track { Net = board.NetNames().First(), Layer = CopperLayer.Top, X1 = board.Width - 20, Y1 = board.Height - 6, X2 = board.Width - 8, Y2 = board.Height - 6, Width = 0.3 });
                    Canvas.Refresh();
                    Canvas.SetTool(PcbTool.Keepout);
                    RunDrcNow();
                    ShowPanel(3);
                    break;
                case "tune":   // [tuning] two nets made a pair; the shorter one meandered to the other's length by the tuner
                {
                    // a signal pair in the free strip under the parts: D- runs 3 mm further (a jog), D+ is tuned to it
                    double y0 = board.Height - 7;
                    board.Tracks.Add(new Track { Net = "D+", Layer = CopperLayer.Top, X1 = 22, Y1 = y0, X2 = 42, Y2 = y0, Width = 0.2 });
                    board.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 22, Y1 = y0 + 3, X2 = 40, Y2 = y0 + 3, Width = 0.2 });
                    board.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 40, Y1 = y0 + 3, X2 = 42, Y2 = y0 + 5, Width = 0.2 });
                    board.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 42, Y1 = y0 + 5, X2 = 42, Y2 = y0 + 5.5, Width = 0.2 });
                    board.DiffPairs.Add(new DiffPair { Positive = "D+", Negative = "D-", MaxSkew = 0.5 });
                    var dp = board.Tracks.First(t => t.Net == "D+");
                    var tuned = LengthTuning.Tune(board, dp);
                    if (tuned.Ok && tuned.Replacement != null) LengthTuning.Apply(board, dp, tuned);
                    W.Status = tuned.Message;

                    Canvas.Refresh();
                    Canvas.SetTool(PcbTool.Tune);
                    UpdatePanel();
                    ShowPanel(2);
                    break;
                }
                case "diffpair":   // [diffroute] a USB pair between two headers, found from its names (D+/D−), laid by the pair router
                {
                    board.Tracks.Clear();
                    board.Vias.Clear();
                    double y = board.Height - 4;
                    foreach (var (refDes, x) in new[] { ("J8", 5.0), ("J9", board.Width - 5) })
                    {
                        var j = new BoardComponent { RefDes = refDes, FootprintId = "HDR-1x2", Value = "USB", X = x, Y = y };
                        j.PadNets["1"] = "USB_D+";
                        j.PadNets["2"] = "USB_D-";
                        board.Components.Add(j);
                    }

                    Canvas.Refresh();
                    var pair = DiffPairRouting.PairOf(board, "USB_D+");
                    if (pair != null)
                    {
                        pair.Gap = 0.25;
                        board.DiffPairs.Add(pair);
                        double m = DiffPairRouting.PadsNear(board, pair, 5, y) is { } two ? (two.P.Y + two.N.Y) / 2 : y;
                        Canvas.SnapshotDiffPair(pair, new PcbPoint(5, y), new PcbPoint(board.Width - 5, y),
                            new[] { new PcbPoint(14, m), new PcbPoint(17, m - 3), new PcbPoint(27, m - 3), new PcbPoint(30, m) });
                    }

                    UpdateToolOptions();
                    break;
                }
                case "draw":   // [graphics] a logo frame, a filled mark, an arc and a polygon drawn; the rectangle tool with its options
                    board.Graphics.Add(new BoardGraphic { Shape = GraphicShape.Rectangle, Points = { new PcbPoint(3, 3), new PcbPoint(18, 9) }, Width = 0.2 });
                    board.Graphics.Add(new BoardGraphic { Shape = GraphicShape.Circle, Points = { new PcbPoint(10.5, 6), new PcbPoint(12, 6) }, Filled = true });
                    board.Graphics.Add(new BoardGraphic { Shape = GraphicShape.Arc, Layer = GraphicLayer.FabTop, Points = { new PcbPoint(board.Width - 8, board.Height - 8), new PcbPoint(board.Width - 4, board.Height - 8), new PcbPoint(board.Width - 8, board.Height - 4) } });
                    board.Graphics.Add(new BoardGraphic { Shape = GraphicShape.Polygon, Layer = GraphicLayer.Drawings, Points = { new PcbPoint(board.Width - 16, 3), new PcbPoint(board.Width - 6, 3), new PcbPoint(board.Width - 11, 9) } });
                    // [bezier] a swoosh under the logo frame
                    board.Graphics.Add(new BoardGraphic { Shape = GraphicShape.Bezier, Width = 0.3, Points = { new PcbPoint(3, 12), new PcbPoint(8, 7), new PcbPoint(13, 17), new PcbPoint(18, 11) } });
                    Canvas.Refresh();
                    Canvas.SetTool(PcbTool.Rectangle);
                    break;
                case "float-tools":   // [floatbar] the tool strip in its own window, laid across; the picture is that window over the page
                {
                    var origin = PointToScreen(new Point(300, 200));
                    if (_toolStripFloat?.TearOffAt(origin, across: true) is { } fw)
                    {
                        fw.UpdateLayout();
                        W.Status = $"Araç şeridi ayrı pencerede: {fw.ActualWidth:0}×{fw.ActualHeight:0}, sütun {ContentGrid.ColumnDefinitions[0].Width.Value:0} px";
                    }

                    break;
                }
                case "dock-min":   // [dock-min] the properties panel minimized to its strip
                    _dock?.Minimize(DockPlace.Right, true);
                    break;
                case "dock-left":   // [dock-min] the properties panel moved to the left
                    if (_dock?.Items.FirstOrDefault(i => i.Key == "properties") is { } props) _dock.Dock(props, DockPlace.Left);
                    break;
                case "stats":   // [board-stats] the statistics dialog over the routed board
                    UpdatePanel();
                    OnStatistics(this, new RoutedEventArgs());
                    break;
                case "select-filter":   // [select-filter] vias and dimensions left out, the layers tab showing the filter
                    Canvas.SelectionFilter.Vias = false;
                    Canvas.SelectionFilter.Locked = false;
                    UpdatePanel();
                    ShowPanel(1);
                    if (LayersPanel.Parent is ScrollViewer lsv) Dispatcher.BeginInvoke(new Action(lsv.ScrollToEnd), System.Windows.Threading.DispatcherPriority.Loaded);
                    break;
                case "dfm":   // [dfm] the board as it is, with the manufacturing and test check on
                    DfmCheck.IsChecked = true;
                    RunDrcNow();
                    ShowPanel(3);
                    break;
                case "enclosure":   // [enclosure] a box made around it, its lid 4 mm too low and one opening
                    board.Enclosure = BoardEnclosure.Around(board);
                    board.Enclosure.InnerHeight -= 4;
                    board.Enclosure.Openings.Add(new EnclosureOpening { Name = "USB", Wall = EnclosureWall.Left, Position = board.Height / 2, Width = 12, Bottom = board.Enclosure.Standoff, Height = 7 });
                    RunDrcNow();
                    ShowPanel(3);
                    break;
                case "pairs":   // [pairs] two of its nets as a pair, three as a group; the Nets tab scrolled to them
                {
                    var nets = NetLengths.Measure(board).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
                    if (nets.Count >= 3)
                    {
                        board.DiffPairs.Add(new DiffPair { Positive = nets[0], Negative = nets[1], Gap = 0.3, MaxSkew = 2 });
                        board.LengthGroups.Add(new LengthGroup { Name = "Veri", Nets = nets.Take(3).ToList(), Tolerance = 5 });
                    }

                    UpdatePanel();
                    ShowPanel(2);
                    if (ClassesPanel.Parent is ScrollViewer sv) Dispatcher.BeginInvoke(new Action(sv.ScrollToEnd), System.Windows.Threading.DispatcherPriority.Loaded);
                    break;
                }
                case "classes":
                    if (board.NetClasses.All(c => c.Name != "Sinyal-HV")) board.NetClasses.Add(new NetClass { Name = "Sinyal-HV", Clearance = 0.5, TrackWidth = 0.5, ViaDiameter = 1.0, ViaDrill = 0.5 });
                    UpdatePanel();
                    ShowPanel(2);
                    break;
                case "route":
                {
                    // Keep half of the routing, then show the walkaround of one open connection.
                    board.Tracks.RemoveRange(board.Tracks.Count / 2, board.Tracks.Count - board.Tracks.Count / 2);
                    board.Vias.Clear();
                    var conn = board.Ratsnest().OrderByDescending(r => Math.Abs(r.A.X - r.B.X) + Math.Abs(r.A.Y - r.B.Y)).First();
                    Canvas.Refresh();
                    Canvas.Fit();
                    Canvas.SetTool(PcbTool.Route);
                    Canvas.SimulateRouteForSnapshot(conn.A.X, conn.A.Y, conn.B.X, conn.B.Y);
                    UpdatePanel();
                    break;
                }

                case "routecommit":
                {
                    // Route every open connection with the interactive router and check the result with DRC.
                    board.Tracks.Clear();
                    board.Vias.Clear();
                    Canvas.Refresh();
                    Canvas.SetTool(PcbTool.Route);
                    for (int guard = 0; guard < 40; guard++)
                    {
                        var open = board.Ratsnest();
                        if (open.Count <= guard / 4) break;
                        var c = open[guard % open.Count];
                        Canvas.SimulateRouteForSnapshot(c.A.X, c.A.Y, c.B.X, c.B.Y);
                        Canvas.RouteClickForSnapshot(c.B.X, c.B.Y);
                        Canvas.SetTool(PcbTool.Select);
                        Canvas.SetTool(PcbTool.Route);
                    }

                    Canvas.SetTool(PcbTool.Select);
                    RunDrcNow();
                    break;
                }

                case "shape3d":
                {
                    // A chamfered board with a connector notch, shown in 3D (outline → 3D and world).
                    double w = board.Width, h = board.Height, c = Math.Min(w, h) * 0.15, nw = w / 4, nd = h / 5, x0 = (w - nw) / 2;
                    board.Outline = new List<OutlinePoint> { new(c, 0), new(w - c, 0), new(w, c), new(w, h - c), new(w - c, h), new(x0 + nw, h, 1), new(x0 + nw, h - nd, 1), new(x0, h - nd, 1), new(x0, h, 1), new(c, h), new(0, h - c), new(0, c) };
                    Canvas.Refresh();
                    Show3D(true);
                    break;
                }

                case "reroute":
                    // [perf] The routing button's own path: the copper cleared, routed again on a worker (use --wait).
                    board.Tracks.Clear();
                    board.Vias.Clear();
                    Canvas.Refresh();
                    OnRoute(this, new RoutedEventArgs());
                    break;
                case "measure":
                    Canvas.SetTool(PcbTool.Measure);
                    Canvas.SimulateMeasureForSnapshot(5, 5, board.Width * 0.6, board.Height * 0.45);
                    break;
                case "select":
                    Canvas.Select(board.Components.FirstOrDefault());
                    ShowPanel(0);
                    break;
                case "help":
                    ToggleHelp();
                    break;
                case "multi":
                    Canvas.SetSelection(board.Components.Skip(1).Take(3).Cast<object>().Concat(board.Tracks.Take(4)));
                    break;
                case "ask":
                {
                    // [integrity] A routed part deleted from the schematic: the board waits, and asks before losing it.
                    var gone = board.Components.Where(c => c.RefDes.StartsWith("R", StringComparison.Ordinal))
                        .OrderByDescending(c => board.Tracks.Count(t => c.PadNets.ContainsValue(t.Net))).FirstOrDefault();
                    var part = gone == null ? null : W.Schematic.Parts.FirstOrDefault(p => p.RefDes == gone.RefDes);
                    if (part == null) break;
                    W.Schematic.Parts.Remove(part);
                    OnShown();
                    break;
                }
            }
        }

        private static void SizeBoardToContent(BoardDocument board)
        {
            double area = 0;
            foreach (var c in board.Components)
            {
                var fp = FootprintLibrary.Get(c.FootprintId);
                if (fp == null) continue;
                area += (fp.CourtyardMaxX - fp.CourtyardMinX + 2) * (fp.CourtyardMaxY - fp.CourtyardMinY + 2);
            }

            double side = Math.Sqrt(Math.Max(400, area * 2.6));
            board.Width = Math.Ceiling(side * 1.3 / 5) * 5;
            board.Height = Math.Ceiling(side / 1.3 / 5) * 5;
            foreach (var c in board.Components)
            {
                var fp = FootprintLibrary.Get(c.FootprintId);
                if (fp == null) continue;
                board.Width = Math.Max(board.Width, Math.Ceiling((fp.CourtyardMaxX - fp.CourtyardMinX + 6) / 5) * 5);
                board.Height = Math.Max(board.Height, Math.Ceiling((fp.CourtyardMaxY - fp.CourtyardMinY + 6) / 5) * 5);
            }
        }

        private void OnSync(object sender, RoutedEventArgs e)
        {
            BringUpToDate(true);   // the user's go-ahead
            W.MarkBoardChanged("Şematik karta aktarıldı");
        }

        private void OnPlace(object sender, RoutedEventArgs e)
        {
            var board = Board;
            PlacementResult result = new PlacementResult();
            Edit("Otomatik yerleşim", () =>
            {
                board.Tracks.Clear();
                board.Vias.Clear();
                result = AutoPlacer.Place(board);
            });
            W.Status = result.BoardResized
                ? $"{result.Placed} bileşen yerleştirildi; sığmadığı için kart {board.Width}×{board.Height} mm'ye büyütüldü"
                : $"{result.Placed} bileşen yerleştirildi";
            Canvas.Fit();
        }

        // [perf] Routing and DRC run on a worker, on a copy of the board: the board's editing waits for them (the canvas and
        // these buttons are off meanwhile), the rest of the app does not.
        private bool _working;

        private void Working(bool on)
        {
            _working = on;
            Canvas.IsEnabled = PlaceButton.IsEnabled = AutoRouteButton.IsEnabled = DrcButton.IsEnabled = !on;
        }

        /// <summary>The copper a worker laid on a copy of the board: its tracks, vias and zone fills.</summary>
        private static void TakeCopper(BoardDocument board, BoardDocument copy, bool tracks)
        {
            if (tracks)
            {
                board.Tracks = copy.Tracks;
                board.Vias = copy.Vias;
            }

            for (int i = 0; i < board.Zones.Count && i < copy.Zones.Count; i++)
            {
                board.Zones[i].Fill = copy.Zones[i].Fill;
                board.Zones[i].FillHash = copy.Zones[i].FillHash;
            }
        }

        private async void OnRoute(object sender, RoutedEventArgs e)
        {
            if (_working) return;
            var board = Board;
            var copy = board.Clone();
            Working(true);
            W.Status = "Otomatik rota çiziliyor…";
            RoutingResult result;
            List<DrcViolation> violations;
            try
            {
                (result, violations) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var r = new AutoRouter(copy).Route();
                    if (copy.Zones.Count > 0) ZoneFiller.FillAll(copy);
                    return (r, DesignRuleChecker.Check(copy));
                });
            }
            finally
            {
                Working(false);
            }

            if (Board != board) return;   // another project's board is open now: the result belongs to none
            Edit("Otomatik rota", () => TakeCopper(board, copy, tracks: true));
            SetDrc(violations);
            W.Status = result.Complete
                ? $"Rota tamam: {result.Routed} bağlantı ({result.PlaneConnections} düzleme), {result.ViasAdded} via, {result.TotalLength:0} mm yol"
                : $"{result.Failed.Count} bağlantı çizilemedi — kartı büyütün, parçaları açın veya katman ekleyin";
            UpdatePanel();
            Canvas.InvalidateVisual();
        }

        /// <summary>[palette] Runs the design rule check as its button does.</summary>
        public void RunDrc() => OnDrc(this, new RoutedEventArgs());

        private async void OnDrc(object sender, RoutedEventArgs e)
        {
            if (_working) return;
            var board = Board;
            var copy = board.Clone();
            bool refill = RefillBeforeDrc.IsChecked == true && board.Zones.Count > 0;
            var circuit = CircuitForCurrents();
            Working(true);
            W.Status = "DRC çalışıyor…";
            int refilled;
            Dictionary<string, double> currents;
            List<DrcViolation> violations;
            try
            {
                (refilled, currents, violations) = await System.Threading.Tasks.Task.Run(() =>
                {
                    int n = refill ? ZoneFiller.RefillOutdated(copy) : 0;
                    var flowing = EstimateNetCurrents(circuit);
                    return (n, flowing, DesignRuleChecker.Check(copy, flowing));
                });
            }
            finally
            {
                Working(false);
            }

            if (Board != board) return;
            if (refilled > 0)
            {
                TakeCopper(board, copy, tracks: false);
                Canvas.Refresh();
            }

            ShowDrc(violations, currents);
        }

        /// <summary>The DRC at once, on this thread (snapshots: the picture shows its result).</summary>
        private void RunDrcNow()
        {
            var board = Board;
            if (RefillBeforeDrc.IsChecked == true && board.Zones.Count > 0)
            {
                if (ZoneFiller.RefillOutdated(board) > 0) Canvas.Refresh();
            }

            var currents = EstimateNetCurrents(CircuitForCurrents());
            ShowDrc(DesignRuleChecker.Check(board, currents), currents);
        }

        private void ShowDrc(List<DrcViolation> violations, Dictionary<string, double> currents)
        {
            // [dfm] with the switch on, what the fab and the assembly line would flag joins the list (as warnings)
            _dfm = DfmCheck.IsChecked == true ? DfmChecker.Check(Board) : null;
            if (_dfm != null) violations = violations.Concat(_dfm.Issues).ToList();
            SetDrc(violations);   // [drc-policy] the user's severities and exclusions applied
            Canvas.FocusedViolation = null;
            int errors = _drc.Errors, warnings = _drc.Warnings;
            W.Status = (errors == 0
                ? "DRC: hata yok" + (warnings > 0 ? $", {warnings} uyarı" : string.Empty) + (currents.Count > 0 ? " (simülasyon akımları ile kontrol edildi)" : string.Empty)
                : $"DRC: {errors} hata, {warnings} uyarı") + (_drc.Excluded.Count > 0 ? $" · {_drc.Excluded.Count} istisna" : string.Empty);
            UpdatePanel();
            ShowPanel(3);
            Canvas.InvalidateVisual();
        }

        /// <summary>
        /// Runs the circuit briefly and takes, for every net, the largest current any
        /// part connected to it carries — the copper of that net must survive it.
        /// </summary>
        /// <summary>A copy of the circuit as it is fitted now (the active alternative), for a worker to run.</summary>
        private static Schematic CircuitForCurrents() => Schematic.FromJson(Variants.Active(W.Schematic).ToJson());   // [variants]

        private static Dictionary<string, double> EstimateNetCurrents(Schematic circuit)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var compiled = CircuitCompiler.Compile(circuit, W.Library);
                var sim = new AnalogSimulator(compiled.Circuit) { MaxSubstepsPerCall = 50_000 };
                sim.Initialize();
                for (int i = 0; i < 25; i++) sim.Step(4e-3);
                foreach (var net in compiled.Netlist.Nets)
                {
                    double max = 0;
                    foreach (var c in net.Connections)
                    {
                        var device = compiled.DevicesOf(c.RefDes).FirstOrDefault();
                        if (device == null) continue;
                        double i = SimulationSession.DeviceCurrent(device);
                        if (!double.IsNaN(i)) max = Math.Max(max, Math.Abs(i));
                    }

                    if (max > 0) result[net.Name] = max;
                }
            }
            catch (AnalogSimulationException)
            {
            }

            return result;
        }

        private void OnFillZones(object sender, RoutedEventArgs e)
        {
            var board = Board;
            if (board.Zones.Count == 0)
            {
                W.Status = "Dolgu alanı yok: Z ile çizin veya Katmanlar sekmesinden hızlı GND dolgu ekleyin";
                ShowPanel(1);
                return;
            }

            double ms = 0;
            Edit("Dolgular dolduruldu", () => ms = ZoneFiller.FillAll(board));
            W.Status = $"{board.Zones.Count} dolgu dolduruldu ({ms:0} ms)";
        }

        private void OnFit(object sender, RoutedEventArgs e) => Canvas.Fit();

        private void OnBoardSetup(object sender, RoutedEventArgs e)
        {
            var board = Board;
            W.BeginChange();
            if (!PcbDialogs.BoardSetup(Owner, board, out string summary))
            {
                W.CancelChange();
                return;
            }

            if (board.Zones.Count > 0) ZoneFiller.FillAll(board);
            Canvas.Refresh();
            AfterEdit(summary);
            RebuildLayerBox();
        }

        private void OnActiveLayerPick(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingLayerBox || ActiveLayerBox.SelectedItem is not ComboBoxItem item || item.Tag is not CopperLayer layer) return;
            Canvas.ActiveLayer = layer;
            Canvas.Focus();
        }

        private void RebuildLayerBox()
        {
            _syncingLayerBox = true;
            ActiveLayerBox.Items.Clear();
            foreach (var l in Board.CopperLayers())
            {
                ActiveLayerBox.Items.Add(new ComboBoxItem { Content = PcbLayers.DisplayName(l), Tag = l });
            }

            if (!Board.HasLayer(Canvas.ActiveLayer)) Canvas.ActiveLayer = CopperLayer.Top;
            _syncingLayerBox = false;
            SyncActiveLayer();
        }

        private void SyncActiveLayer()
        {
            _syncingLayerBox = true;
            foreach (ComboBoxItem item in ActiveLayerBox.Items)
            {
                if (item.Tag is CopperLayer l && l == Canvas.ActiveLayer) ActiveLayerBox.SelectedItem = item;
            }

            _syncingLayerBox = false;
            ActiveLayerDot.Fill = new SolidColorBrush(BoardCanvas.LayerColor(Canvas.ActiveLayer));
            UpdateLayersPanel();
            UpdateCursorText();
        }

        private void UpdateCursorText()
        {
            var (x, y) = Canvas.CursorPosition;
            string unit = Canvas.UseMil ? "mil" : "mm";
            double k = Canvas.UseMil ? 1 / 0.0254 : 1;
            if (AppSettings.Current.PcbPolarCursor)
            {
                // [polar] KiCad's polar coordinates: how far and at what angle from the board's origin (the angle counter-clockwise, Y up)
                double r = Math.Sqrt(x * x + y * y), angle = Math.Atan2(-y, x) * 180 / Math.PI;
                CursorText.Text = string.Format(CultureInfo.InvariantCulture, "r {0:0.00} {1}  θ {2:0.0}° · ızgara {3} · {4}",
                    r * k, unit, angle, Canvas.Length(Canvas.GridSize), PcbLayers.DisplayName(Canvas.ActiveLayer));
                return;
            }

            CursorText.Text = string.Format(CultureInfo.InvariantCulture, "x {0:0.00}  y {1:0.00} {2} · ızgara {3} · {4}",
                x * k, y * k, unit, Canvas.Length(Canvas.GridSize), PcbLayers.DisplayName(Canvas.ActiveLayer));
        }

        // ================================================================ exports and modules

        private string ProjectFileName()
        {
            string name = string.IsNullOrWhiteSpace(W.Schematic.Title) ? "robotwin" : W.Schematic.Title;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            var board = Board;
            var unrouted = DesignRuleChecker.UnroutedNets(board);
            if (unrouted.Count > 0)
            {
                int answer = Dialog.Show(Owner!, "Rota tamamlanmamış",
                    $"{unrouted.Count} hatta bağlanmamış ped var. Dosyalar yine de üretilsin mi?", "Üret", "Vazgeç");
                if (answer != 0) return;
            }

            string name = ProjectFileName();
            var dlg = new SaveFileDialog
            {
                Title = RobotWin.Studio.Shell.Lang.T($"Üretim dosyalarını kaydet ({board.LayerCount} katman Gerber + delik + BOM + CPL)"),
                Filter = RobotWin.Studio.Shell.Lang.Filter("ZIP arşivi (*.zip)|*.zip"),
                FileName = name + "-gerber.zip"
            };
            if (dlg.ShowDialog(Owner) != true) return;
            try
            {
                GerberExporter.WriteZip(board, dlg.FileName, name.Replace(' ', '_'), Variants.FittingOf(W.Schematic, W.Library));   // [variants] its BOM and pick-and-place
                Canvas.Refresh();

                // [gerber-check] What was written, read back by an independent reader and laid over the board.
                GerberCheckReport report;
                var cursor = System.Windows.Input.Mouse.OverrideCursor;
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                try { report = GerberCheck.Check(board, GerberCheck.ReadZip(dlg.FileName)); }
                finally { System.Windows.Input.Mouse.OverrideCursor = cursor; }
                W.Status = "Üretim dosyaları kaydedildi: " + Path.GetFileName(dlg.FileName) + " · " + report.Summary();
                if (!report.Ok)
                {
                    var lines = report.Layers.Where(l => !l.Ok)
                        .SelectMany(l => l.Differences.Take(5).Select(d => l.File + ": " + d).Concat(l.Warnings.Take(3).Select(w => l.File + ": " + w)))
                        .Concat(report.Drills.Where(d => !d.Ok).SelectMany(d => d.Problems.Take(5).Select(p => d.File + ": " + p)));
                    Dialog.Show(Owner!, "Üretim dosyaları kartla uyuşmuyor", string.Join("\n", lines), "Tamam");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Owner!, "Kaydedilemedi", ex.Message, "Tamam");
            }
        }

        /// <summary>
        /// [kicad-in] Brings back a board worked on in KiCad: its placements (by reference designator), tracks, vias, zones and
        /// holes, after listing what changed. Footprints and the schematic stay this project's.
        /// </summary>
        private void OnImportKiCadLayout(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = RobotWin.Studio.Shell.Lang.T("KiCad kartından yerleşim ve yolları al"),
                Filter = RobotWin.Studio.Shell.Lang.Filter("KiCad kartı (*.kicad_pcb)|*.kicad_pcb"),
            };
            if (dlg.ShowDialog(Owner) != true) return;
            KiCadLayout layout;
            try
            {
                layout = KiCadBoardImport.Read(File.ReadAllText(dlg.FileName));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Owner!, "Okunamadı", ex.Message, "Tamam");
                return;
            }

            var board = Board;
            var changes = BoardDiff.Compare(board, layout);
            if (changes.Count == 0 && layout.Warnings.Count == 0)
            {
                W.Status = "KiCad kartı bu kartla aynı: alınacak değişiklik yok.";
                return;
            }

            var lines = changes.Take(40).Select(c => (c.Applies ? "• " : "– ") + RobotWin.Studio.Shell.Lang.T(c.Text)).ToList();
            if (changes.Count > 40) lines.Add(RobotWin.Studio.Shell.Lang.T($"… ve {changes.Count - 40} değişiklik daha"));
            lines.AddRange(layout.Warnings.Select(w => "! " + RobotWin.Studio.Shell.Lang.T(w)));
            int applies = changes.Count(c => c.Applies);
            if (applies == 0)
            {
                Dialog.Show(Owner!, "KiCad'deki değişiklikler", string.Join("\n", lines), "Tamam");
                return;
            }

            int answer = Dialog.Show(Owner!, "KiCad'deki değişiklikler", string.Join("\n", lines), $"{applies} değişikliği al", "Vazgeç");
            if (answer != 0) return;
            Edit($"KiCad'den {applies} değişiklik alındı: {Path.GetFileName(dlg.FileName)}", () => BoardDiff.Apply(board, layout));
            UpdatePanel();
        }

        /// <summary>Whole project as a KiCad 8 project folder inside a zip (opens directly in KiCad).</summary>
        private void OnExportKiCad(object sender, RoutedEventArgs e)
        {
            var board = Board;
            string name = ProjectFileName().Replace(' ', '_');
            var dlg = new SaveFileDialog
            {
                Title = RobotWin.Studio.Shell.Lang.T("KiCad projesi olarak dışa aktar"),
                Filter = RobotWin.Studio.Shell.Lang.Filter("ZIP arşivi (*.zip)|*.zip"),
                FileName = name + "-kicad.zip"
            };
            if (dlg.ShowDialog(Owner) != true) return;
            try
            {
                KiCadExporter.WriteProjectZip(dlg.FileName, W.Schematic, W.Library, board, name);
                W.Status = "KiCad projesi kaydedildi: " + Path.GetFileName(dlg.FileName) + " — zip'i açıp " + name + ".kicad_pro dosyasını KiCad ile açın";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                Dialog.Show(Owner!, "Dışa aktarılamadı", ex.Message, "Tamam");
            }
        }

        private void OnSaveAsModule(object sender, RoutedEventArgs e)
        {
            if (W.Schematic.Parts.Count == 0)
            {
                Dialog.Show(Owner!, "Boş tasarım", "Modül yapmak için önce bir devre ve kart tasarlayın.", "Tamam");
                return;
            }

            W.Schematic.Board = Board;
            var dlg = new ModuleDialog(W.Schematic, W.Library) { Owner = Owner };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string id = UserLibrary.MakeId(W.Library, dlg.ModuleName);
                var part = RobotTwin.CoreSim.Formats.CustomPart.FromDesign(W.Schematic, W.Library, id, dlg.ModuleName, dlg.SelectedPorts);
                part.Author = Environment.UserName;
                UserLibrary.Save(W.Library, part);
                W.Status = $"'{part.Name}' modülü kütüphaneye eklendi ({part.Module!.Ports.Count} pin) — başka projelerde Kütüphane › Modüllerim'den ekleyin";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException)
            {
                Dialog.Show(Owner!, "Modül kaydedilemedi", ex.Message, "Tamam");
            }
        }

        // ================================================================ footprints

        private void OnFootprintEditor(object sender, RoutedEventArgs e)
        {
            if (Canvas.SelectedComponent is { } comp) OpenFootprintEditor(comp);
            else OpenFootprintEditor(null);
        }

        /// <summary>Opens the footprint editor; with a part, the saved footprint can be assigned to it.</summary>
        private void OpenFootprintEditor(BoardComponent? comp)
        {
            var source = comp != null ? FootprintLibrary.Get(comp.FootprintId) : null;
            var editor = new FootprintEditorWindow(source, comp?.RefDes) { Owner = Owner };
            if (editor.ShowDialog() != true || editor.Saved == null) return;
            UserLibrary.SaveFootprint(editor.Saved);
            W.Status = $"'{editor.Saved.Id}' footprint'i kullanıcı kütüphanesine kaydedildi";
            if (comp != null && editor.AssignToPart) AssignFootprint(comp, editor.Saved.Id);
            else
            {
                Canvas.Refresh();
                UpdatePanel();
            }
        }

        /// <summary>Gives a part another footprint (stored on the schematic part, so it survives re-sync).</summary>
        private void AssignFootprint(BoardComponent comp, string footprintId)
        {
            var fp = FootprintLibrary.Get(footprintId);
            var part = W.Schematic.FindPart(comp.RefDes);
            if (fp == null || part == null) return;
            var def = W.Library.Find(part.PartId);
            if (def != null)
            {
                var parameters = W.Schematic.ResolveParameters(W.Library, part);
                var missing = def.Pins.Select(p => def.ResolvePad(p, parameters)).Where(p => fp.FindPad(p) == null).Distinct().ToList();
                if (missing.Count > 0)
                {
                    int answer = Dialog.Show(Owner!, "Pedler uyuşmuyor",
                        $"{footprintId} footprint'inde şu pinlerin pedi yok: {string.Join(", ", missing)}. Bu pinlerin bağlantıları kartta görünmez. Yine de atansın mı?", "Ata", "Vazgeç");
                    if (answer != 0) return;
                }
            }

            var board = Board;
            Edit($"{comp.RefDes} footprint'i {footprintId} oldu", () =>
            {
                part.Parameters["footprint"] = footprintId;
                BoardDocument.Synchronize(W.Schematic, W.Library, board);
            });
            Canvas.Select(board.Find(comp.RefDes));
        }

        // ================================================================ parts: delete, duplicate, footprint picker, help

        /// <summary>Deleting a footprint removes its part from the schematic too (the board is synchronised from it).</summary>
        private void DeleteParts(List<BoardComponent> parts)
        {
            var board = Board;
            var names = parts.Select(c => c.RefDes).ToList();
            Edit(names.Count == 1 ? $"{names[0]} silindi (şemadan da)" : $"{names.Count} parça silindi (şemadan da)", () =>
            {
                W.Schematic.Parts.RemoveAll(p => names.Contains(p.RefDes, StringComparer.OrdinalIgnoreCase));
                BoardDocument.Synchronize(W.Schematic, W.Library, board);
            });
            Canvas.Select(null);
        }

        /// <summary>Copies of parts: new schematic parts with the same model and parameters, synchronised onto the board.</summary>
        private IReadOnlyList<BoardComponent> DuplicateParts(IReadOnlyList<BoardComponent> sources)
        {
            var board = Board;
            var created = new List<string>();
            foreach (var src in sources)
            {
                var part = W.Schematic.FindPart(src.RefDes);
                if (part == null) continue;
                var copy = W.Schematic.AddPart(W.Library, part.PresetId ?? part.PartId, new GridPoint(part.Position.X + 4, part.Position.Y + 4), part.Rotation);
                foreach (var kv in part.Parameters) copy.Parameters[kv.Key] = kv.Value;
                copy.Mirror = part.Mirror;
                created.Add(copy.RefDes);
            }

            if (created.Count == 0) return Array.Empty<BoardComponent>();
            BoardDocument.Synchronize(W.Schematic, W.Library, board);
            return created.Select(r => board.Find(r)).Where(c => c != null).Select(c => c!).ToList();
        }

        private void PickFootprint(BoardComponent comp)
        {
            var part = W.Schematic.FindPart(comp.RefDes);
            var def = part != null ? W.Library.Find(part.PartId) : null;
            var required = new List<string>();
            if (part != null && def != null)
            {
                var parameters = W.Schematic.ResolveParameters(W.Library, part);
                required = def.Pins.Select(p => def.ResolvePad(p, parameters)).Distinct().ToList();
            }

            var id = PcbDialogs.PickFootprint(Owner, comp.FootprintId, required);
            if (id != null && !string.Equals(id, comp.FootprintId, StringComparison.OrdinalIgnoreCase)) AssignFootprint(comp, id);
        }

        private void OnHelpClose(object sender, MouseButtonEventArgs e) => HelpOverlay.Visibility = Visibility.Collapsed;

        private void ToggleHelp()
        {
            if (HelpOverlay.Visibility == Visibility.Visible)
            {
                HelpOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (HelpColumns.Children.Count == 0)
            {
                var groups = new (string Title, (string Key, string Text)[] Rows)[]
                {
                    ("Seçim ve düzen", new[]
                    {
                        ("Tık / Shift+tık", "seç / seçime ekle-çıkar"), ("Sürükle (boşta)", "kutu seçimi: sağa = içindekiler, sola = değenler"), ("Ctrl+A", "tümünü seç"),
                        ("M", "taşı (yollar yerinde kalır)"), ("G", "sürükle (yol uçları gelir)"), ("D", "yol parçasını açıyı koruyarak sürükle"),
                        ("Oklar / Shift+oklar", "1 / 10 ızgara kaydır"), ("R / Shift+R", "döndür"), ("F", "alt/üst yüze çevir (seçim yoksa sığdır)"),
                        ("L", "kilitle / aç"), ("E", "özellikler"), ("Del / Geri", "seçimi sil (parça şemadan da silinir)"), ("U", "tüm yolu seç"),
                        ("W / Shift+W", "yol genişliği: sonraki / önceki"), ("Ctrl+C / X / V / D", "kopyala / kes / yapıştır / çoğalt"), ("Ctrl+Z / Ctrl+Y", "geri al / yinele")
                    }),
                    ("Yol çizme (X)", new[]
                    {
                        ("X", "yol aracı"), ("Tık", "noktayı sabitle; ped/yol/via üstünde bitir"), ("V", "via koy ve katman değiştir"), ("PgUp / PgDn", "via ile üst / alt katmana"),
                        ("/", "açı yönü (önce çapraz / önce düz)"), ("Boşluk", "45° / 90° mod"), ("W / Shift+W", "genişlik"), ("Geri", "son parçayı geri al"),
                        ("Enter / çift tık", "bitir"), ("Esc", "iptal · sağ tık: rota menüsü")
                    }),
                    ("Görünüm ve katmanlar", new[]
                    {
                        ("Home (veya F)", "sığdır"), ("+ / − / tekerlek", "yakınlaştır / uzaklaştır"), ("Orta tuş, sağ sürükle", "kaydır"), ("PgUp / PgDn", "etkin katman"),
                        ("V", "üst / alt katman"), ("H", "yalnızca etkin katman / hepsi"), ("`", "imleçteki hattı vurgula"), ("B / Ctrl+B", "dolguları doldur / boşalt")
                    }),
                    ("Araçlar", new[]
                    {
                        ("Z", "dolgu alanı çiz"), ("T", "serigrafi yazısı"), ("Ctrl+Shift+M", "ölç"), ("Ctrl+Shift+H", "ölçü çizgisi"), ("F8 / Shift+F8", "sonraki / önceki DRC işareti"), ("Sol çubuk", "kart kenarı, montaj deliği"),
                        ("Sağ tık", "bağlam menüsü (parça, yol, via, dolgu, boş alan)"), ("F1 / ?", "bu liste")
                    })
                };
                foreach (var (title, rows) in groups)
                {
                    var col = new StackPanel { Width = 400, Margin = new Thickness(0, 0, 24, 14) };
                    col.Children.Add(new TextBlock { Text = title, Style = S("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
                    foreach (var (key, text) in rows)
                    {
                        var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                        row.ColumnDefinitions.Add(new ColumnDefinition());
                        row.Children.Add(new TextBlock { Text = key, Style = S("Text.Mono"), Foreground = B("B.Text"), FontSize = 12 });
                        var t = new TextBlock { Text = text, Style = S("Text.Body"), FontSize = 12 };
                        Grid.SetColumn(t, 1);
                        row.Children.Add(t);
                        col.Children.Add(row);
                    }

                    HelpColumns.Children.Add(col);
                }
            }

            HelpOverlay.Visibility = Visibility.Visible;
        }


        // ================================================================ 3D

        private void OnToggle3D(object sender, RoutedEventArgs e) => Show3D(View3DToggle.IsChecked == true);

        /// <summary>Switches between the 2D editor and the 3D board view (rebuilt from the current layout).</summary>
        public void Show3D(bool on)
        {
            View3DToggle.IsChecked = on;
            if (on) View3D.Build(Board, W.Schematic, W.Library);
            View3D.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Canvas.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            StatusBar.Visibility = Canvas.Visibility;
        }

        private void OnDrcPick(object sender, SelectionChangedEventArgs e)
        {
            if (DrcList.SelectedItem is not DrcItem item) return;
            var v = item.Violation;
            Show3D(false);
            if (v.Layer.HasValue && Board.HasLayer(v.Layer.Value)) Canvas.ActiveLayer = v.Layer.Value;
            Canvas.FocusedViolation = v;
            Canvas.ZoomTo(v.X, v.Y);
            HintText.Text = $"{v.Rule}: {v.Message}";
        }

        // ================================================================ side panel

        private void UpdatePanel()
        {
            var board = Board;
            if (ActiveLayerBox.Items.Count != board.LayerCount) RebuildLayerBox();
            UpdateBoardPanel(board);
            UpdateLayersPanel();
            UpdateClassesPanel(board);
            UpdateDrcPanel(board);
            UpdateSelection();
        }

        private StackPanel? _selectionHost;

        private void UpdateBoardPanel(BoardDocument board)
        {
            BoardPanel.Children.Clear();
            BoardPanel.Children.Add(Overline("Seçim", 8));
            _selectionHost = new StackPanel();
            BoardPanel.Children.Add(_selectionHost);

            BoardPanel.Children.Add(Overline("Kart"));
            BoardPanel.Children.Add(Caption($"{board.LayerCount} bakır katman · {board.Stackup.Thickness:0.##} mm · dış bakır {BoardStackup.UmToOz(board.Rules.CopperThicknessUm):0.#} oz{(board.Stackup.AllowBlindBuriedVias ? " · kör/gömülü via" : string.Empty)}", 0));
            BoardPanel.Children.Add(Buttons(Button("Kart ayarları…", OnBoardSetup), Button("İstatistikler…", OnStatistics, tip: "Parçalar, pedler, vialar, delik listesi, yol uzunlukları ve alan (KiCad'in kart istatistikleri gibi)")));
            bool polygon = board.Outline.Count >= 3;
            if (!polygon)
            {
                var size = new Grid { Margin = new Thickness(0, 4, 0, 6) };
                size.ColumnDefinitions.Add(new ColumnDefinition());
                size.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                size.ColumnDefinitions.Add(new ColumnDefinition());
                var wPanel = new StackPanel();
                wPanel.Children.Add(Caption("Genişlik (mm)"));
                wPanel.Children.Add(Number(board.Width, v => Edit($"Kart genişliği {Fmt(v)} mm", () => board.Width = v), 10, 500));
                var hPanel = new StackPanel();
                hPanel.Children.Add(Caption("Yükseklik (mm)"));
                hPanel.Children.Add(Number(board.Height, v => Edit($"Kart yüksekliği {Fmt(v)} mm", () => board.Height = v), 10, 500));
                Grid.SetColumn(hPanel, 2);
                size.Children.Add(wPanel);
                size.Children.Add(hPanel);
                BoardPanel.Children.Add(size);
                BoardPanel.Children.Add(Row("Köşe yarıçapı (mm)", Number(board.CornerRadius, v => Edit("Köşe yarıçapı değişti", () => board.CornerRadius = v), 0, 50, true)));
            }
            else
            {
                BoardPanel.Children.Add(Caption($"Çokgen kenar: {board.Outline.Count} köşe, {board.Width:0.##}×{board.Height:0.##} mm. Kenara tıklayıp köşeleri sürükleyin.", 2));
                BoardPanel.Children.Add(Row("Tüm köşe yarıçapları (mm)", Number(board.Outline.Max(p => p.Radius), v => Edit("Kenar köşe yarıçapı değişti", () => board.Outline.ForEach(p => p.Radius = v)), 0, 50, true)));
                BoardPanel.Children.Add(Buttons(Button("Dikdörtgene dön", (_, _) => Edit("Dikdörtgen kart kenarı", () => board.Outline.Clear()))));
            }

            // Ready outlines on the current width × height (then drag their corners, or draw your own with the edge tool).
            BoardPanel.Children.Add(Caption("Hazır kart şekli", 8));
            void Shape(string name, Func<double, double, List<OutlinePoint>> make) =>
                Edit("Kart şekli: " + name, () => board.Outline = make(board.Width, board.Height));
            BoardPanel.Children.Add(Buttons(
                Button("Pah kırık köşe", (_, _) => Shape("pah kırık köşe", (w, h) =>
                {
                    double c = Math.Min(w, h) * 0.15;
                    return new List<OutlinePoint> { new(c, 0), new(w - c, 0), new(w, c), new(w, h - c), new(w - c, h), new(c, h), new(0, h - c), new(0, c) };
                })),
                Button("Daire / oval", (_, _) => Shape("daire / oval", (w, h) =>
                {
                    double r = Math.Min(w, h) / 2 - 0.01;
                    return new List<OutlinePoint> { new(0, 0, r), new(w, 0, r), new(w, h, r), new(0, h, r) };
                })),
                Button("Altıgen", (_, _) => Shape("altıgen", (w, h) =>
                    new List<OutlinePoint> { new(w * 0.25, 0), new(w * 0.75, 0), new(w, h / 2), new(w * 0.75, h), new(w * 0.25, h), new(0, h / 2) }))));
            BoardPanel.Children.Add(Buttons(
                Button("Kenarda çentik (konnektör / servo)", (_, _) => Shape("çentikli", (w, h) =>
                {
                    double nw = w / 3, nd = h / 4, x0 = (w - nw) / 2;
                    return new List<OutlinePoint> { new(0, 0), new(w, 0), new(w, h), new(x0 + nw, h, 1), new(x0 + nw, h - nd, 1), new(x0, h - nd, 1), new(x0, h, 1), new(0, h) };
                })),
                Button("L şekli", (_, _) => Shape("L", (w, h) =>
                    new List<OutlinePoint> { new(0, 0), new(w * 0.45, 0), new(w * 0.45, h * 0.55), new(w, h * 0.55), new(w, h), new(0, h) }))));
            BoardPanel.Children.Add(Row("Yeni kenar köşe yarıçapı", Number(Canvas.OutlineCornerRadius, v => Canvas.OutlineCornerRadius = v, 0, 50, true), tip: "Kart kenarı aracıyla çizilen çokgenin köşelerine uygulanır"));
            BoardPanel.Children.Add(Row("Montaj deliği Ø (mm)", Number(Canvas.HoleDrill, v => Canvas.HoleDrill = v, 0.3, 20)));
            BoardPanel.Children.Add(Row("Delik pedi Ø (0 = kaplamasız)", Number(Canvas.HolePad, v => Canvas.HolePad = v, 0, 30, true)));

            BoardPanel.Children.Add(Overline("Izgara ve birim"));
            var units = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            ToggleButton? mm = null, mil = null;
            mm = Chip("mm", !Canvas.UseMil, (_, _) => { Canvas.UseMil = false; mil!.IsChecked = false; mm!.IsChecked = true; Canvas.InvalidateVisual(); UpdateCursorText(); });
            mil = Chip("mil", Canvas.UseMil, (_, _) => { Canvas.UseMil = true; mm.IsChecked = false; mil!.IsChecked = true; Canvas.InvalidateVisual(); UpdateCursorText(); });
            units.Children.Add(mm);
            units.Children.Add(mil);
            BoardPanel.Children.Add(units);
            var grids = new (double Mm, string Label)[]
            {
                (0.1, "0.1 mm"), (0.127, "5 mil"), (0.25, "0.25 mm"), (0.254, "10 mil"), (0.5, "0.5 mm"), (0.635, "25 mil"),
                (1.0, "1 mm"), (1.27, "50 mil"), (2.54, "100 mil")
            };
            double currentGrid = grids.Select(g => g.Mm).OrderBy(g => Math.Abs(g - Canvas.GridSize)).First();
            BoardPanel.Children.Add(Row("Yakalama ızgarası", Combo(grids.Select(g => g.Mm), currentGrid, v => { Canvas.GridSize = v; Canvas.InvalidateVisual(); UpdateCursorText(); }, v => grids.First(g => g.Mm == v).Label), 110));

            // [magnet] KiCad's Magnetic items: before the grid, the pointer goes to what is near it
            var modes = new[] { MagnetMode.Never, MagnetMode.Creating, MagnetMode.Always };
            string ModeName(MagnetMode m) => Lang.T(m switch { MagnetMode.Never => "Hiç", MagnetMode.Creating => "Çizerken", _ => "Her zaman" });
            void SaveMagnet()
            {
                AppSettings.Current.PcbMagnetPads = Canvas.MagnetPads.ToString();
                AppSettings.Current.PcbMagnetTracks = Canvas.MagnetTracks.ToString();
                AppSettings.Current.PcbMagnetGraphics = Canvas.MagnetGraphics;
                AppSettings.Current.Save();
            }

            BoardPanel.Children.Add(Row("Pedlere yakala", Combo(modes, Canvas.MagnetPads, m => { Canvas.MagnetPads = m; SaveMagnet(); }, ModeName), 110));
            BoardPanel.Children.Add(Row("Yollara yakala", Combo(modes, Canvas.MagnetTracks, m => { Canvas.MagnetTracks = m; SaveMagnet(); }, ModeName), 110));
            BoardPanel.Children.Add(Switch("Çizimlere ve kart kenarına yakala", Canvas.MagnetGraphics, v => { Canvas.MagnetGraphics = v; SaveMagnet(); }));

            BoardPanel.Children.Add(Overline("Özet"));
            var conn = BoardConnectivity.Build(board);
            int unrouted = conn.Unrouted().Count;
            void Stat(string label, string value)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var v = new TextBlock { Text = value, Style = S("Text.Mono"), HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
                DockPanel.SetDock(v, Dock.Right);
                row.Children.Add(v);
                row.Children.Add(new TextBlock { Text = label, Style = S("Text.Body") });
                BoardPanel.Children.Add(row);
            }

            Stat("Bileşen", board.Components.Count.ToString(CultureInfo.InvariantCulture));
            Stat("Hat", $"{board.NetNames().Count()} ({unrouted} bağlanmamış)");
            Stat("Ratsnest", conn.Ratsnest().Count.ToString(CultureInfo.InvariantCulture));
            Stat("Yol uzunluğu", $"{board.Tracks.Sum(t => t.Length):0} mm");
            Stat("Via", $"{board.Vias.Count}{(board.Vias.Any(v => !v.IsThrough(board.LayerCount)) ? $" ({board.Vias.Count(v => !v.IsThrough(board.LayerCount))} kör/gömülü)" : string.Empty)}");
            Stat("Dolgu", $"{board.Zones.Count}{(board.Zones.Any(z => ZoneFiller.IsOutdated(board, z)) ? " (güncel değil)" : string.Empty)}");
            Stat("Montaj deliği", board.MountingHoles.Count.ToString(CultureInfo.InvariantCulture));

            BoardPanel.Children.Add(Overline("Üretim kuralları (en az)"));
            var r = board.Rules;
            BoardPanel.Children.Add(Row("Yol genişliği (mm)", Number(r.MinTrackWidth, v => Edit("Kural: en az yol", () => r.MinTrackWidth = v), 0.05, 5)));
            BoardPanel.Children.Add(Row("Delik (mm)", Number(r.MinDrill, v => Edit("Kural: en az delik", () => r.MinDrill = v), 0.05, 6)));
            BoardPanel.Children.Add(Row("Halka (mm)", Number(r.MinAnnularRing, v => Edit("Kural: halka", () => r.MinAnnularRing = v), 0, 2, true)));
            BoardPanel.Children.Add(Row("Delik-delik (mm)", Number(r.MinHoleToHole, v => Edit("Kural: delik-delik", () => r.MinHoleToHole = v), 0, 5, true)));
            BoardPanel.Children.Add(Row("Kart kenarına (mm)", Number(r.EdgeClearance, v => Edit("Kural: kenar boşluğu", () => r.EdgeClearance = v), 0, 10, true)));
            BoardPanel.Children.Add(Row("Dış bakır (µm)", Number(r.CopperThicknessUm, v => Edit("Bakır kalınlığı", () => r.CopperThicknessUm = v), 5, 210)));
            var power = board.FindClass(NetClass.PowerName) ?? board.DefaultClass;
            BoardPanel.Children.Add(Caption($"{power.TrackWidth} mm ({power.Name}) dış yol ≈ {board.CurrentCapacity(power.TrackWidth):0.0} A, iç katmanda ≈ {board.CurrentCapacity(power.TrackWidth, CopperLayer.In1):0.0} A taşır (IPC-2221, ΔT {r.AllowedTempRiseC} °C).", 4));
        }

        private void UpdateSelection()
        {
            var host = _selectionHost;
            if (host == null) return;
            host.Children.Clear();
            var board = Board;
            if (Canvas.SelectionItems.Count > 1)
            {
                var kinds = Canvas.SelectionItems.GroupBy(o => o switch
                {
                    BoardComponent => "parça",
                    Track => "yol parçası",
                    Via => "via",
                    Zone => "dolgu",
                    BoardText => "yazı",
                    MountingHole => "delik",
                    BoardDimension => "ölçü",
                    _ => "öğe"
                }).Select(g => $"{g.Count()} {g.Key}");
                host.Children.Add(new TextBlock { Text = $"{Canvas.SelectionItems.Count} öğe seçili", Style = S("Text.Subtitle") });
                host.Children.Add(Caption(string.Join(" · ", kinds), 2));
                host.Children.Add(Buttons(
                    Button("Taşı (M)", (_, _) => Canvas.BeginMoveSelection()),
                    Button("Kopyala", (_, _) => Canvas.CopySelection()),
                    Button("Çoğalt", (_, _) => Canvas.Duplicate()),
                    Button("Sil", (_, _) => Canvas.DeleteSelection())));
                host.Children.Add(Caption("M: taşı · G: yollarla sürükle · R: döndür · F: çevir · oklar: kaydır · sağ tık: menü", 2));
                return;
            }

            switch (Canvas.Selected)
            {
                case BoardComponent c:
                {
                    var fp = FootprintLibrary.Get(c.FootprintId);
                    host.Children.Add(new TextBlock { Text = $"{c.RefDes} · {c.Value}", Style = S("Text.Subtitle") });
                    host.Children.Add(Caption(fp?.Description ?? c.FootprintId, 2));
                    host.Children.Add(new TextBlock { Text = $"x {c.X:0.00}  y {c.Y:0.00} mm  {c.Rotation}°  {(c.Side == CopperLayer.Top ? "üst" : "alt")} yüz{(c.Locked ? "  · kilitli" : string.Empty)}", Style = S("Text.Mono"), Margin = new Thickness(0, 4, 0, 6) });
                    var ids = FootprintLibrary.AllIds.ToList();
                    if (!ids.Contains(c.FootprintId, StringComparer.OrdinalIgnoreCase)) ids.Insert(0, c.FootprintId);
                    host.Children.Add(Row("Footprint", Combo(ids, ids.First(i => string.Equals(i, c.FootprintId, StringComparison.OrdinalIgnoreCase)), id =>
                    {
                        if (!string.Equals(id, c.FootprintId, StringComparison.OrdinalIgnoreCase)) Dispatcher.BeginInvoke(new Action(() => AssignFootprint(c, id)));
                    }), 150));
                    host.Children.Add(Buttons(Button("Footprint'i düzenle…", (_, _) => OpenFootprintEditor(c), tip: "Bu footprint'in bir kopyasını düzenleyip kaydedin ve bu parçaya atayın")));
                    foreach (var kv in c.PadNets.OrderBy(k => k.Key, StringComparer.Ordinal).Take(24))
                    {
                        host.Children.Add(Caption($"ped {kv.Key} → {kv.Value}  ({board.ClassNameFor(kv.Value)})", 0));
                    }

                    break;
                }

                case Track t:
                {
                    host.Children.Add(new TextBlock { Text = $"Yol · {t.Net}", Style = S("Text.Subtitle") });
                    host.Children.Add(Caption($"{PcbLayers.DisplayName(t.Layer)} · {Canvas.Length(t.Length)} · sınıf {board.ClassNameFor(t.Net)} · {board.CurrentCapacity(t.Width, t.Layer):0.0} A", 2));
                    host.Children.Add(Row("Genişlik (mm)", Number(t.Width, v => Edit($"{t.Net} yol genişliği {Fmt(v)} mm", () => t.Width = v), 0.05, 10)));
                    host.Children.Add(Row("Katman", Combo(board.CopperLayers(), t.Layer, l => Edit($"{t.Net} yolu {PcbLayers.DisplayName(l)} katmanına", () => t.Layer = l), PcbLayers.DisplayName), 130));
                    host.Children.Add(Buttons(
                        Button("Hattı vurgula", (_, _) => Canvas.SetHighlight(t.Net)),
                        Button("Sil", (_, _) => Canvas.DeleteSelection())));
                    host.Children.Add(Caption("Sürükleyerek kaydırın (komşu parçalar uzar), U: tüm yolu seç.", 2));
                    break;
                }

                case Via v:
                {
                    var span = v.Span(board.LayerCount);
                    host.Children.Add(new TextBlock { Text = $"Via · {v.Net}", Style = S("Text.Subtitle") });
                    host.Children.Add(Caption($"{(v.IsThrough(board.LayerCount) ? "Geçişli" : "Kör/gömülü")} · {PcbLayers.KiCadName(span.From)} → {PcbLayers.KiCadName(span.To)}", 2));
                    host.Children.Add(Row("Çap (mm)", Number(v.Diameter, d => Edit("Via çapı", () => v.Diameter = d), 0.1, 10)));
                    host.Children.Add(Row("Delik (mm)", Number(v.Drill, d => Edit("Via deliği", () => v.Drill = d), 0.05, 10)));
                    host.Children.Add(Buttons(Button("Sil", (_, _) => Canvas.DeleteSelection())));
                    break;
                }

                case Zone z:
                {
                    host.Children.Add(new TextBlock { Text = $"Dolgu · {z.Net}", Style = S("Text.Subtitle") });
                    string state = !z.IsFilled ? "doldurulmadı" : ZoneFiller.IsOutdated(board, z) ? "güncel değil" : $"{z.FilledRegions.Count} ada, {z.FilledRegions.Sum(rg => rg.Area):0} mm²";
                    host.Children.Add(Caption($"{PcbLayers.DisplayName(z.Layer)} · öncelik {z.Priority} · {state}", 2));
                    host.Children.Add(Buttons(
                        Button("Özellikler…", (_, _) => EditItem(z), true),
                        Button("Doldur", (_, _) => Edit($"{z.Net} dolgusu dolduruldu", () => ZoneFiller.FillAll(board))),
                        Button("Sil", (_, _) => Canvas.DeleteSelection())));
                    break;
                }

                case MountingHole h:
                    host.Children.Add(new TextBlock { Text = h.Plated ? "Kaplamalı montaj deliği" : "Montaj deliği (NPTH)", Style = S("Text.Subtitle") });
                    host.Children.Add(Row("Delik Ø (mm)", Number(h.Drill, d => Edit("Delik çapı", () => h.Drill = d), 0.3, 20)));
                    host.Children.Add(Row("Ped Ø (0 = yok)", Number(h.PadDiameter, d => Edit("Delik pedi", () => h.PadDiameter = d), 0, 30, true)));
                    host.Children.Add(Row("Hat", Text(h.Net, n => Edit("Delik hattı", () => h.Net = n.Trim())), 120));
                    host.Children.Add(Buttons(Button("Sil", (_, _) => Canvas.DeleteSelection())));
                    break;
                case BoardText x:
                    host.Children.Add(new TextBlock { Text = $"Yazı · {x.Text}", Style = S("Text.Subtitle") });
                    host.Children.Add(Buttons(Button("Özellikler…", (_, _) => EditItem(x), true), Button("Sil", (_, _) => Canvas.DeleteSelection())));
                    break;
                case BoardDimension d:
                    host.Children.Add(new TextBlock { Text = $"{Shell.Lang.T(DimensionKindName(d.Kind))} · {d.Label(Canvas.UseMil)}", Style = S("Text.Subtitle") });
                    if (d.Kind != DimensionKind.Center)   // [dims] a leader's note, or a value written instead of the measured one (KiCad's override)
                    {
                        var note = new TextBox { Text = d.Text ?? string.Empty, ToolTip = Shell.Lang.T(d.Kind == DimensionKind.Leader ? "Kılavuzun notu" : "Boşsa ölçülen değer yazılır") };
                        note.LostFocus += (_, _) =>
                        {
                            string t = note.Text.Trim();
                            if (t == (d.Text ?? string.Empty)) return;
                            Edit("Ölçü yazısı", () => d.Text = t.Length == 0 ? null : t);
                        };
                        host.Children.Add(Row(d.Kind == DimensionKind.Leader ? "Not" : "Yazı", note));
                    }

                    host.Children.Add(Row("Yazı boyu (mm)", Number(d.TextSize, s => Edit("Ölçü yazısı", () => d.TextSize = s), 0.3, 10)));
                    host.Children.Add(Buttons(Button("Sil", (_, _) => Canvas.DeleteSelection())));
                    break;
                case BoardCanvas.OutlineSelection:
                    host.Children.Add(new TextBlock { Text = "Kart kenarı", Style = S("Text.Subtitle") });
                    host.Children.Add(Caption(board.Outline.Count >= 3 ? "Köşeleri sürükleyin; Del: dikdörtgene dön." : "Dikdörtgen kart: ölçüleri aşağıdan değiştirin veya kenar aracıyla çokgen çizin.", 2));
                    break;
                default:
                    host.Children.Add(Body("Bir parçaya, yola, viaya, dolguya veya yazıya tıklayın."));
                    break;
            }
        }

        /// <summary>[presets] Shows the layers a preset names: copper by side, the legend and drawings with them.</summary>
        private string _layerPreset = "Tüm katmanlar";

        private void ApplyLayerPreset(string preset, BoardDocument board)
        {
            _layerPreset = preset;
            var stack = board.CopperLayers();
            Canvas.HiddenLayers.Clear();
            foreach (var l in stack)
            {
                bool show = preset switch
                {
                    "Üst taraf" => l == CopperLayer.Top,
                    "Alt taraf" => l == CopperLayer.Bottom,
                    "Yalnız etkin katman" => l == Canvas.ActiveLayer,
                    _ => true
                };
                if (!show) Canvas.HiddenLayers.Add(l);
            }

            bool copperOnly = preset is "Yalnız bakır" or "Yalnız etkin katman";
            Canvas.ShowSilk = !copperOnly;
            Canvas.ShowDrawings = !copperOnly;
            Canvas.ShowZones = true;
            if (preset == "Üst taraf" && stack.Contains(CopperLayer.Top)) Canvas.ActiveLayer = CopperLayer.Top;
            if (preset == "Alt taraf" && stack.Contains(CopperLayer.Bottom)) Canvas.ActiveLayer = CopperLayer.Bottom;
            Canvas.InvalidateVisual();
            W.Status = Shell.Lang.T("Görünüm: " + preset);
            Dispatcher.BeginInvoke(new Action(UpdateLayersPanel), System.Windows.Threading.DispatcherPriority.Background);   // the rows' eyes follow
        }

        private void UpdateLayersPanel()
        {
            var board = Board;
            LayersPanel.Children.Clear();
            // [presets] KiCad's layer presets: which layers are shown, in one choice
            var presets = new[] { "Tüm katmanlar", "Üst taraf", "Alt taraf", "Yalnız bakır", "Yalnız etkin katman" };
            var presetBox = Combo(presets, _layerPreset, p => ApplyLayerPreset(p, board), p => Shell.Lang.T(p));
            presetBox.ToolTip = Shell.Lang.T("Görünüm ön ayarı: hangi katmanların gösterileceği tek seçimde");
            LayersPanel.Children.Add(Row("Ön ayar", presetBox, 150));
            ViewportsSection(board);   // [viewports] Pages/PcbPage.Viewports.cs
            LayersPanel.Children.Add(Overline("Bakır katmanları", 8));
            foreach (var layer in board.CopperLayers())
            {
                var l = layer;
                bool active = l == Canvas.ActiveLayer;
                var row = new Grid { Background = active ? B("B.Pressed") : Brushes.Transparent, Margin = new Thickness(-6, 0, -6, 2), Cursor = Cursors.Hand };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(BoardCanvas.LayerColor(l)), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                var name = new StackPanel { Margin = new Thickness(6, 5, 0, 5) };
                name.Children.Add(new TextBlock { Text = PcbLayers.DisplayName(l) + (active ? "  · etkin" : string.Empty), Style = S("Text.Body"), Foreground = active ? B("B.Text") : B("B.TextSecondary") });
                int tracks = board.Tracks.Count(t => t.Layer == l), zones = board.Zones.Count(z => z.Layer == l);
                name.Children.Add(new TextBlock { Text = $"{PcbLayers.KiCadName(l)} · {tracks} yol{(zones > 0 ? $" · {zones} dolgu" : string.Empty)}", Style = S("Text.Caption") });
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var eye = new CheckBox { Style = S("Switch"), IsChecked = !Canvas.HiddenLayers.Contains(l), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Görünür" };
                eye.Click += (_, e) =>
                {
                    if (eye.IsChecked == true) Canvas.HiddenLayers.Remove(l);
                    else if (l != Canvas.ActiveLayer) Canvas.HiddenLayers.Add(l);
                    else eye.IsChecked = true;
                    Canvas.InvalidateVisual();
                    e.Handled = true;
                };
                Grid.SetColumn(eye, 2);
                row.Children.Add(eye);
                row.MouseLeftButtonUp += (_, e) =>
                {
                    if (e.OriginalSource is DependencyObject d && IsInside(d, eye)) return;
                    Canvas.ActiveLayer = l;
                };
                LayersPanel.Children.Add(row);
            }

            LayersPanel.Children.Add(Caption("Satıra tıklayınca katman etkin olur; diğerleri soluk çizilir. PgUp/PgDn ile de değişir.", 4));
            LayersPanel.Children.Add(Overline("Görünüm"));
            LayersPanel.Children.Add(Switch("Serigrafi", Canvas.ShowSilk, v => { Canvas.ShowSilk = v; Canvas.InvalidateVisual(); }));
            LayersPanel.Children.Add(Switch("Ratsnest", Canvas.ShowRatsnest, v => { Canvas.ShowRatsnest = v; Canvas.InvalidateVisual(); }));
            LayersPanel.Children.Add(Caption("Kapalıyken seçili parçaların ve sağ tık ▸ Yerel ratsnest'i göster ile işaretlenenlerin çizgileri görünür (KiCad'deki yerel ratsnest).", 4));   // [local-rats]
            LayersPanel.Children.Add(Switch("Kilitleri geçersiz kıl", Canvas.OverrideLocks, v =>   // [locks] KiCad's Override locks
            {
                Canvas.OverrideLocks = v;
                AppSettings.Current.PcbOverrideLocks = v;
                AppSettings.Current.Save();
            }));
            LayersPanel.Children.Add(Switch("Tam ekran artı imleç", Canvas.FullCrosshair, v =>   // [crosshair]
            {
                Canvas.FullCrosshair = v;
                AppSettings.Current.PcbFullCrosshair = v;
                AppSettings.Current.Save();
                Canvas.InvalidateVisual();
            }));
            LayersPanel.Children.Add(Switch("İmleç kutupsal (uzaklık ve açı)", AppSettings.Current.PcbPolarCursor, v =>   // [polar]
            {
                AppSettings.Current.PcbPolarCursor = v;
                AppSettings.Current.Save();
                UpdateCursorText();
            }));
            LayersPanel.Children.Add(Switch("Ratsnest eğri çizilsin", Canvas.CurvedRatsnest, v =>   // [rats-curve] KiCad's curved ratsnest
            {
                Canvas.CurvedRatsnest = v;
                AppSettings.Current.PcbCurvedRatsnest = v;
                AppSettings.Current.Save();
                Canvas.InvalidateVisual();
            }));
            LayersPanel.Children.Add(Switch("Dolgular", Canvas.ShowZones, v => { Canvas.ShowZones = v; Canvas.InvalidateVisual(); }));
            LayersPanel.Children.Add(Switch("Dolguları yalnız kenarıyla göster", Canvas.ZoneOutlines, v =>   // [zone-look] KiCad's zone display
            {
                Canvas.ZoneOutlines = v;
                AppSettings.Current.PcbZoneOutlines = v;
                AppSettings.Current.Save();
                Canvas.InvalidateVisual();
            }));
            LayersPanel.Children.Add(Switch("Çizimler ve ölçüler", Canvas.ShowDrawings, v => { Canvas.ShowDrawings = v; Canvas.InvalidateVisual(); }));
            LayersPanel.Children.Add(Switch("Ped numaraları ve hatları", Canvas.ShowPadNames, v => { Canvas.ShowPadNames = v; Canvas.InvalidateVisual(); }));
            LayersPanel.Children.Add(Switch("Yüksek kontrast (diğer katmanlar soluk)", Canvas.HighContrast, v => { Canvas.HighContrast = v; Canvas.InvalidateVisual(); }));
            OpacityRows();   // [opacity] KiCad's Objects tab (Pages/PcbPage.Opacity.cs)
            LayerPairRow();   // [layer-pair] KiCad's layer pair for V (Pages/PcbPage.Opacity.cs)
            if (Canvas.HighlightNet != null)
            {
                LayersPanel.Children.Add(Buttons(Button($"'{Canvas.HighlightNet}' vurgusunu kaldır", (_, _) => Canvas.SetHighlight(null))));
            }

            AddSelectionFilter();   // [select-filter]
            LayersPanel.Children.Add(Overline("Dolgu alanları"));
            foreach (var zone in ZoneFiller.FillOrder(board))
            {
                var z = zone;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var edit = new Button { Content = "Düzenle", Style = S("Btn.Ghost"), Padding = new Thickness(8, 2, 8, 2), MinHeight = 24 };
                edit.Click += (_, _) =>
                {
                    Canvas.Select(z);
                    EditItem(z);
                };
                DockPanel.SetDock(edit, Dock.Right);
                row.Children.Add(edit);
                var info = new StackPanel();
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(new Rectangle { Width = 8, Height = 8, Fill = new SolidColorBrush(BoardCanvas.LayerColor(z.Layer)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                head.Children.Add(new TextBlock { Text = $"{z.Net}{(z.Name.Length > 0 ? " · " + z.Name : string.Empty)}", Style = S("Text.Body"), Foreground = B("B.Text") });
                info.Children.Add(head);
                string state = !z.IsFilled ? "doldurulmadı" : ZoneFiller.IsOutdated(board, z) ? "güncel değil" : $"{z.FilledRegions.Count} ada";
                info.Children.Add(new TextBlock { Text = $"{PcbLayers.DisplayName(z.Layer)} · öncelik {z.Priority} · {(z.IsWholeBoard ? "tüm kart" : "çokgen")} · {state}", Style = S("Text.Caption") });
                row.Children.Add(info);
                row.MouseLeftButtonUp += (_, _) => Canvas.Select(z);
                LayersPanel.Children.Add(row);
            }

            if (board.Zones.Count == 0) LayersPanel.Children.Add(Caption("Henüz dolgu yok. Z ile çokgen çizin veya hızlı ekleyin:", 0));
            var buttons = new List<UIElement>
            {
                Button("+ GND (alt)", (_, _) => QuickZone(CopperLayer.Bottom)),
                Button("+ GND (üst)", (_, _) => QuickZone(CopperLayer.Top))
            };
            if (board.LayerCount >= 4) buttons.Add(Button("+ GND düzlemi (İç 1)", (_, _) => QuickZone(CopperLayer.In1)));
            if (board.Zones.Count > 0) buttons.Add(Button("Tümünü doldur", OnFillZones, true));
            LayersPanel.Children.Add(Buttons(buttons.ToArray()));
            LayersPanel.Children.Add(Switch("Düzenlemeden sonra otomatik doldur", _autoFill, v => _autoFill = v));
        }

        private static bool IsInside(DependencyObject d, DependencyObject container)
        {
            for (var x = d; x != null; x = x is Visual || x is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            {
                if (ReferenceEquals(x, container)) return true;
            }

            return false;
        }

        private void QuickZone(CopperLayer layer)
        {
            var board = Board;
            string net = board.NetNames().FirstOrDefault(n => string.Equals(n, "GND", StringComparison.OrdinalIgnoreCase)) ?? "GND";
            if (board.Zones.Any(z => z.IsWholeBoard && z.Layer == layer && string.Equals(z.Net, net, StringComparison.OrdinalIgnoreCase)))
            {
                W.Status = $"{PcbLayers.DisplayName(layer)} katmanında zaten tüm kart {net} dolgusu var";
                return;
            }

            Edit($"{PcbLayers.DisplayName(layer)} katmanına {net} dolgu eklendi", () =>
            {
                board.Zones.Add(new Zone { Net = net, Layer = layer, Name = PcbLayers.IsOuter(layer) ? string.Empty : net + " düzlemi" });
                ZoneFiller.FillAll(board);
            });
        }

        private void UpdateClassesPanel(BoardDocument board)
        {
            ClassesPanel.Children.Clear();
            ClassesPanel.Children.Add(Overline("Hat sınıfları", 8));
            ClassesPanel.Children.Add(Caption("Router, etkileşimli yol, dolgular ve DRC sınıf değerlerini kullanır. İki hat arasındaki boşluk, sınıflarından büyük olanıdır.", 0));
            foreach (var cls in board.NetClasses.ToList())
            {
                var c = cls;
                var card = new Border { BorderBrush = B("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 6), Margin = new Thickness(0, 8, 0, 0) };
                var body = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                bool fixedName = c.Name == NetClass.DefaultName || c.Name == NetClass.PowerName;
                if (!fixedName)
                {
                    var del = new Button { Content = "Sil", Style = S("Btn.Ghost"), Padding = new Thickness(8, 1, 8, 1), MinHeight = 22 };
                    del.Click += (_, _) => Edit($"'{c.Name}' sınıfı silindi", () =>
                    {
                        board.NetClasses.Remove(c);
                        foreach (var k in board.NetClassAssignments.Where(kv => kv.Value == c.Name).Select(kv => kv.Key).ToList()) board.NetClassAssignments.Remove(k);
                    });
                    DockPanel.SetDock(del, Dock.Right);
                    head.Children.Add(del);
                }

                int members = board.NetNames().Count(n => board.ClassFor(n) == c);
                head.Children.Add(fixedName
                    ? (UIElement)new TextBlock { Text = $"{c.Name}  ({members} hat)", Style = S("Text.Subtitle") }
                    : Text(c.Name, v =>
                    {
                        string name = v.Trim();
                        if (name.Length == 0 || board.FindClass(name) != null) return;
                        Edit($"Sınıf adı: {name}", () =>
                        {
                            foreach (var k in board.NetClassAssignments.Where(kv => kv.Value == c.Name).Select(kv => kv.Key).ToList()) board.NetClassAssignments[k] = name;
                            c.Name = name;
                        });
                    }));
                body.Children.Add(head);
                body.Children.Add(Row("Boşluk (mm)", Number(c.Clearance, v => Edit($"{c.Name} boşluğu {Fmt(v)} mm", () => c.Clearance = v), 0.05, 10), 80));
                body.Children.Add(Row("Yol genişliği (mm)", Number(c.TrackWidth, v => Edit($"{c.Name} yol genişliği {Fmt(v)} mm", () => c.TrackWidth = v), 0.05, 10), 80));
                body.Children.Add(Row("Via Ø / delik (mm)", ViaEditor(board, c), 110));
                card.Child = body;
                ClassesPanel.Children.Add(card);
            }

            ClassesPanel.Children.Add(Buttons(Button("+ Sınıf ekle", (_, _) =>
            {
                string name = "Sınıf" + (board.NetClasses.Count + 1).ToString(CultureInfo.InvariantCulture);
                Edit($"'{name}' sınıfı eklendi", () => board.NetClasses.Add(board.DefaultClass.Clone().Also(x => x.Name = name)));
            })));

            ClassesPanel.Children.Add(Overline("Hat atamaları"));
            var names = board.NetClasses.Select(c => c.Name).ToList();
            foreach (var net in board.NetNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                string n = net;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                row.Children.Add(new TextBlock { Text = n, Style = S("Text.Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                var swatch = NetColorSwatch(board, n);   // [net-colors] KiCad's net colour (Pages/PcbPage.NetColors.cs)
                Grid.SetColumn(swatch, 2);
                row.Children.Add(swatch);
                var hl = new Button { Style = S("Btn.Icon"), Width = 24, Height = 24, MinHeight = 24, Padding = new Thickness(0), ToolTip = "Hattı vurgula", Content = new RobotWin.Studio.Theme.Icon { Data = Res<Geometry>("I.Eye"), Size = 13 } };
                hl.Click += (_, _) => Canvas.SetHighlight(string.Equals(Canvas.HighlightNet, n, StringComparison.OrdinalIgnoreCase) ? null : n);
                Grid.SetColumn(hl, 1);
                row.Children.Add(hl);
                var combo = Combo(names, board.ClassNameFor(n), v => Edit($"{n} → {v} sınıfı", () => board.AssignClass(n, v)));
                Grid.SetColumn(combo, 3);
                row.Children.Add(combo);
                ClassesPanel.Children.Add(row);
            }

            PairsSection(board);   // [pairs]
        }

        /// <summary>
        /// [pairs] Differential pairs and length-matched groups (CoreSim Pcb/NetLengths.cs): each pair's routed lengths, their
        /// difference, how much runs side by side at its gap; each group's spread; the DRC holds the routing to them.
        /// </summary>
        private void PairsSection(BoardDocument board)
        {
            var lengths = NetLengths.Measure(board);
            static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);   // lengths to a tenth of a millimetre
            string L(string net) => lengths.TryGetValue(net, out var l) ? F1(l) : "—";
            ClassesPanel.Children.Add(Overline("Fark çiftleri"));
            ClassesPanel.Children.Add(Caption("Birlikte, aynı aralıkla ve aynı uzunlukta gitmesi gereken iki hat (USB D+/D−, CAN H/L). DRC uzunluk farkını ve yan yana gidişi denetler.", 0));
            foreach (var p in board.DiffPairs.ToList())
            {
                var pair = p;
                var card = new Border { BorderBrush = B("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 6), Margin = new Thickness(0, 8, 0, 0) };
                var body = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var del = new Button { Content = "Sil", Style = S("Btn.Ghost"), Padding = new Thickness(8, 1, 8, 1), MinHeight = 22 };
                del.Click += (_, _) => Edit($"{pair} çifti kaldırıldı", () => board.DiffPairs.Remove(pair));
                DockPanel.SetDock(del, Dock.Right);
                head.Children.Add(del);
                head.Children.Add(new TextBlock { Text = pair.ToString(), Style = S("Text.Subtitle") });
                body.Children.Add(head);
                var r = NetLengths.Analyze(board, pair, lengths);
                string state = r.PositiveLength > 0 && r.NegativeLength > 0
                    ? Shell.Lang.T($"{F1(r.PositiveLength)} / {F1(r.NegativeLength)} mm · fark {F1(r.Skew)} mm ({Math.Abs(LengthTuning.Delay(board, pair.Positive) - LengthTuning.Delay(board, pair.Negative)):0.#} ps) · %{r.Coupled * 100:0} yan yana")
                    : Shell.Lang.T("Henüz yollanmadı.");
                body.Children.Add(new TextBlock
                {
                    Text = state, Style = S("Text.Caption"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap,
                    Foreground = r.Skew > pair.MaxSkew || (r.PositiveLength > 0 && r.Coupled < 0.6) ? B("B.Warning") : B("B.TextSecondary")
                });
                body.Children.Add(Row("Aralık (mm)", Number(pair.Gap, v => Edit($"{pair} aralığı {Fmt(v)} mm", () => pair.Gap = v), 0.05, 5), 80));
                body.Children.Add(Row("En çok fark (mm)", Number(pair.MaxSkew, v => Edit($"{pair} en çok farkı {Fmt(v)} mm", () => pair.MaxSkew = v), 0.01, 100), 80));
                card.Child = body;
                ClassesPanel.Children.Add(card);
            }

            ClassesPanel.Children.Add(Buttons(Button("Adlarından bul", (_, _) =>
            {
                var found = NetLengths.Detect(board).Where(d => !board.DiffPairs.Any(e => string.Equals(e.Positive, d.Positive, StringComparison.OrdinalIgnoreCase))).ToList();
                if (found.Count == 0)
                {
                    W.Status = "Adından çift olduğu anlaşılan hat yok (D+/D−, _P/_N, H/L).";
                    return;
                }

                Edit($"{found.Count} fark çifti eklendi", () => board.DiffPairs.AddRange(found));
            })));

            ClassesPanel.Children.Add(Overline("Uzunluk grupları"));
            ClassesPanel.Children.Add(Caption("Aynı anda varması gereken hatlar (paralel veri yolu, saat ve verisi): en uzunla en kısa arasındaki fark toleransı aşarsa DRC uyarır.", 0));
            foreach (var g in board.LengthGroups.ToList())
            {
                var group = g;
                var card = new Border { BorderBrush = B("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 6), Margin = new Thickness(0, 8, 0, 0) };
                var body = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var del = new Button { Content = "Sil", Style = S("Btn.Ghost"), Padding = new Thickness(8, 1, 8, 1), MinHeight = 22 };
                del.Click += (_, _) => Edit($"'{group.Name}' grubu kaldırıldı", () => board.LengthGroups.Remove(group));
                DockPanel.SetDock(del, Dock.Right);
                head.Children.Add(del);
                head.Children.Add(Text(group.Name, v => Edit($"Grup adı: {v.Trim()}", () => group.Name = v.Trim())));
                body.Children.Add(head);
                var measured = group.Nets.Where(n => lengths.ContainsKey(n)).Select(n => lengths[n]).ToList();
                double spread = measured.Count >= 2 ? measured.Max() - measured.Min() : 0;
                body.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", group.Nets.Select(n => $"{n} {L(n)}")) + (measured.Count >= 2 ? Shell.Lang.T($" · fark {F1(spread)} mm") : string.Empty),
                    Style = S("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                    Foreground = spread > group.Tolerance ? B("B.Warning") : B("B.TextSecondary")
                });
                body.Children.Add(Row("Hatlar", Text(string.Join(", ", group.Nets), v => Edit($"'{group.Name}' hatları", () =>
                {
                    group.Nets = v.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                })), 220));
                body.Children.Add(Row("Tolerans (mm)", Number(group.Tolerance, v => Edit($"'{group.Name}' toleransı {Fmt(v)} mm", () => group.Tolerance = v), 0.01, 1000), 80));
                card.Child = body;
                ClassesPanel.Children.Add(card);
            }

            ClassesPanel.Children.Add(Buttons(Button("+ Grup ekle", (_, _) =>
            {
                string name = "Grup" + (board.LengthGroups.Count + 1).ToString(CultureInfo.InvariantCulture);
                Edit($"'{name}' grubu eklendi", () => board.LengthGroups.Add(new LengthGroup { Name = name }));
            })));

            if (lengths.Count > 0)
            {
                ClassesPanel.Children.Add(Overline("Yol uzunlukları"));
                foreach (var (net, len) in lengths.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)))
                    ClassesPanel.Children.Add(new TextBlock { Text = $"{net}  {F1(len)} mm", Style = S("Text.Caption"), Margin = new Thickness(0, 0, 0, 2) });
            }
        }

        private UIElement ViaEditor(BoardDocument board, NetClass c)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var d = Number(c.ViaDiameter, v => Edit($"{c.Name} via çapı", () => c.ViaDiameter = v), 0.1, 10);
            var dr = Number(c.ViaDrill, v => Edit($"{c.Name} via deliği", () => c.ViaDrill = v), 0.05, 10);
            d.Padding = dr.Padding = new Thickness(4, d.Padding.Top, 2, d.Padding.Bottom);
            Grid.SetColumn(dr, 2);
            g.Children.Add(d);
            g.Children.Add(dr);
            return g;
        }

        private void UpdateDrcPanel(BoardDocument board)
        {
            int errors = _drc.Errors, warnings = _drc.Warnings;
            DrcTab.Header = errors + warnings == 0 ? "DRC" : $"DRC · {errors + warnings}";
            UpdateDrcFilters();   // [drc-policy]
            if (errors + warnings == 0)
            {
                bool routed = board.Tracks.Count > 0 && DesignRuleChecker.UnroutedNets(board).Count == 0;
                DrcSummary.Text = routed ? "Son denetimden beri sorun bildirilmedi. Değişiklikten sonra yeniden çalıştırın." : "DRC'yi çalıştırın: aralıklar (hat sınıfı), halka, delik, kenar, serigrafi, bağlantı, dolgu ve akım.";
                DrcSummary.Foreground = B("B.TextSecondary");
            }
            else
            {
                DrcSummary.Text = $"{errors} hata, {warnings} uyarı · bir satıra tıklayınca yerine gider (F8 sonraki)";
                DrcSummary.Foreground = errors > 0 ? B("B.Danger") : B("B.Warning");
            }

            if (_dfm is { Nets: > 0 } dfm)   // [dfm] how much of the board a test probe reaches
                DrcSummary.Text += Shell.Lang.T($"\nTest kapsamı %{dfm.Coverage * 100:0} ({dfm.TestableNets}/{dfm.Nets} hat)");

            DrcList.ItemsSource = DrcItems();
            if (DrcRulesScroll.Visibility == Visibility.Visible) FillDrcRules();
            UpdateEnclosurePanel(board);   // [enclosure]
        }
    }

    internal static class PcbPageExtensions
    {
        public static T Also<T>(this T value, Action<T> action)
        {
            action(value);
            return value;
        }
    }
}
