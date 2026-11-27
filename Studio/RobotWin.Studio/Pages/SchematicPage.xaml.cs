using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Instruments;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Pages
{
    public partial class SchematicPage : UserControl
    {
        private string _category = "Tümü";
        private readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private readonly Dictionary<string, (TextBlock Volts, TextBlock Level)> _pinRows = new Dictionary<string, (TextBlock, TextBlock)>();
        private int _lastLogCount;
        private ScopePanel _scopePanel = null!;
        private readonly DispatcherTimer _diagTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        private string? _toastPart;
        private bool _libraryOpen = true, _inspectorOpen = true;
        private bool? _narrow;
        private System.Threading.CancellationTokenSource? _mcCancel;

        /// <summary>Below this width the side panels collapse into overlays so the canvas keeps the room.</summary>
        public const double NarrowWidth = 1000;

        private static Workspace W => Workspace.Current;

        /// <summary>The drawing used for the project preview picture.</summary>
        public FrameworkElement PreviewElement => Canvas;

        public SchematicPage()
        {
            InitializeComponent();
            BuildCategories();
            FilterLibrary();

            InitEditor();
            _scopePanel = new ScopePanel(Scope);
            ScopePanelHost.Child = _scopePanel;
            BuildMeterPanel();
            BuildMonteCarloPanel();
            Canvas.MeterPartClicked += (_, part) =>
            {
                W.Simulation.MeterPart = part.RefDes;
                ShowDock(1);
                BuildMeterPanel();
                Meter.InvalidateVisual();
            };
            Canvas.LeadPlaced += (_, _) => BuildMeterPanel();
            W.Simulation.InstrumentsChanged += (_, _) => UpdateMeterPanel();
            _diagTimer.Tick += (_, _) =>
            {
                if (W.Simulation.State == SimState.Running && DiagAuto.IsChecked == true && DockShowing(2)) RunDiagnosis();
            };
            _diagTimer.Start();
            BuildAcDock();   // [ac] Pages/SchematicPage.Ac.cs
            BuildNetsDock();   // [net-navigator] Pages/SchematicPage.Nets.cs
            InitLayout();   // [dock] Pages/SchematicPage.Layout.cs
            SizeChanged += (_, _) => ApplyLayout();
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible) ListWorldBoards();
            };

            Canvas.SelectionChanged += (_, _) => BuildInspector();
            // [crossprobe] one part picked here is picked on the PCB too; one picked there is picked here (the view stays)
            Canvas.SelectionChanged += (_, _) =>
            {
                if (!_crossSelecting && Canvas.Selection.Count == 1 && SelectedPart is { } p) CrossProbe.SelectPart(this, p.Uid, p.RefDes);
            };
            // [decode] a wire picked elsewhere (a decoded byte on the scope) is highlighted on the sheet
            CrossProbe.NetHighlighted += (sender, net) =>
            {
                if (!ReferenceEquals(sender, Canvas) && net != null) Canvas.HighlightedNet = net;
            };
            CrossProbe.PartSelected += (sender, _) =>
            {
                if (ReferenceEquals(sender, this)) return;
                var part = W.Schematic.Parts.FirstOrDefault(x => CrossProbe.IsSelected(x.Uid, x.RefDes));
                if (part == null || (Canvas.Selection.Count == 1 && ReferenceEquals(SelectedPart, part))) return;
                _crossSelecting = true;
                try
                {
                    Canvas.SelectItems(new object[] { part });
                }
                finally
                {
                    _crossSelecting = false;
                }
            };
            Canvas.ToolChanged += (_, _) => SyncTools();
            InitSheets();   // [sheets] Pages/SchematicPage.Sheets.cs
            Canvas.PartDoubleClicked += (_, _) => BuildInspector();
            W.DocumentChanged += (_, _) =>
            {
                UpdateChrome();
                BuildInspector();
                RefreshVariantBox();   // [variants]
            };
            W.DocumentReplaced += (_, _) =>
            {
                UpdateChrome();   // a project or an example opened: "Boş sayfa" only over an empty sheet
                RefreshVariantBox();
            };
            Loaded += (_, _) => RefreshVariantBox();
            W.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Workspace.CanUndo) or nameof(Workspace.CanRedo)) UpdateChrome();
            };
            W.Simulation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State)) OnSimStateChanged();
                if (e.PropertyName == nameof(SimulationSession.StatusText)) SimStatus.Text = W.Simulation.State == SimState.Stopped ? string.Empty : W.Simulation.StatusText;
            };
            W.Simulation.Updated += (_, _) => OnSimFrame();
            W.Simulation.SerialReceived += (_, text) =>
            {
                SerialOutput.AppendText(text.Replace("\r\n", "\n").Replace('\r', '\n'));
                if (SerialOutput.Text.Length > 100_000) SerialOutput.Text = SerialOutput.Text.Substring(SerialOutput.Text.Length - 80_000);
                SerialOutput.ScrollToEnd();
            };
            W.Simulation.Failure += (_, entry) =>
            {
                ShowToast($"{entry.Device}: {entry.Message}", W.Simulation.PartOfDevice(entry.Device) ?? (entry.Device == "Multimetre" ? "" : null));
                MarkDiagnosisDirty();
            };
            LogList.ItemsSource = W.Simulation.Log;
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                ToastHost.Visibility = Visibility.Collapsed;
            };

            PreviewKeyDown += OnPreviewKey;
            Loaded += (_, _) =>
            {
                UpdateChrome();
                BuildInspector();
                OnSimStateChanged();
                ApplyLayout();
            };
        }

        /// <summary>Selects and centres a part (after the page is shown).</summary>
        public void RevealPart(string refDes) =>
            Dispatcher.BeginInvoke(new Action(() => Canvas.RevealPart(refDes)), System.Windows.Threading.DispatcherPriority.Loaded);

        public void BeginPlace(string presetId)
        {
            if (W.Simulation.IsActive) W.Simulation.Stop();
            Canvas.BeginPlace(presetId);
        }

        /// <summary>Used by --snapshot: press buttons, drive the board, probe something interesting.</summary>
        public void PrepareSnapshotSimulation(string? fault = null)
        {
            PrepareSnapshotFault(fault);
            AutoProbe();
            foreach (var p in W.Schematic.Parts)
            {
                var def = W.Library.Find(p.PartId);
                if (def?.Symbol == SymbolKind.PushButton) W.Simulation.SetSwitch(p.RefDes, true);
                if (def?.Symbol == SymbolKind.Board && W.Simulation.FirmwareOf(p.RefDes) == null) W.Simulation.SetBoardPwm(p.RefDes, "D13", 0.75);
            }

            BuildPinPanel();
            if (W.Simulation.Firmware.Count > 0) ShowDock(2);
        }

        // ================================================================ library

        /// <summary>[libs] Main categories with their sub-categories indented under them, each with its part count.</summary>
        private void BuildCategories()
        {
            CategoryBox.Items.Clear();
            CategoryBox.Items.Add(new ComboBoxItem { Content = $"Tüm parçalar ({W.Library.Presets.Count})", Tag = "Tümü" });
            foreach (var (main, count, subs) in RobotTwin.CoreSim.Design.PartCategories.Tree(W.Library.Presets))
            {
                CategoryBox.Items.Add(new ComboBoxItem { Content = $"{main} ({count})", Tag = main, FontWeight = FontWeights.SemiBold });
                foreach (var (s, n) in subs)
                    CategoryBox.Items.Add(new ComboBoxItem { Content = $"{s} ({n})", Tag = main + RobotTwin.CoreSim.Design.PartCategories.Separator + s, Padding = new Thickness(22, 3, 6, 3) });
            }

            CategoryBox.SelectedIndex = 0;
        }

        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            _category = CategoryBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "Tümü";
            if (IsLoaded || PartList != null) FilterLibrary();
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e) => FilterLibrary();

        private void FilterLibrary()
        {
            string q = SearchBox.Text.Trim();
            var tokens = RobotWin.Studio.Shell.Lang.Fold(q).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var items = W.Library.Presets.Where(p =>
            {
                if (!RobotTwin.CoreSim.Design.PartCategories.Matches(p, _category)) return false;
                if (tokens.Length == 0) return true;
                string hay = RobotWin.Studio.Shell.Lang.Searchable(p.Name, p.Description, RobotTwin.CoreSim.Design.PartCategories.PathOf(p), p.Id);
                return tokens.All(t => hay.Contains(t));
            }).OrderBy(p => LibraryPage.IsSeriesResistor(p) && tokens.Length == 0 ? 1 : 0).ToList();
            PartList.ItemsSource = items;
            LibraryCount.Text = items.Count.ToString(CultureInfo.InvariantCulture);
        }

        private void OnPartPicked(object sender, SelectionChangedEventArgs e)
        {
        }

        private void OnPartListClick(object sender, MouseButtonEventArgs e)
        {
            if (PartList.SelectedItem is PartPreset preset)
            {
                if (W.Simulation.IsActive)
                {
                    ShowToast("Parça eklemek için önce simülasyonu durdurun.");
                    return;
                }

                Canvas.BeginPlace(preset.Id);
                W.Status = $"{preset.Name}: tuvale tıklayarak yerleştirin (R döndür, Esc bitir)";
            }
        }

        // ================================================================ toolbar

        /// <summary>[shapes] The drawing tools (KiCad's graphic tools) open as a strip of their own beside the tools, and close again.</summary>
        private void OnDrawTool(object sender, RoutedEventArgs e)
        {
            if (_drawPanel == null) return;
            bool open = !_drawPanel.IsOpen;
            _drawPanel.Toggle(open);
            if (!open && IsDrawing) Canvas.Tool = EditorTool.Select;
            SyncTools();
            Canvas.Focus();
        }

        private bool IsDrawing => Canvas.Tool is EditorTool.Line or EditorTool.Rectangle or EditorTool.Circle or EditorTool.Arc or EditorTool.Bezier or EditorTool.Table or EditorTool.RuleArea;

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            var tool = (string)((FrameworkElement)sender).Tag switch
            {
                "Wire" => EditorTool.Wire,
                "Label" => EditorTool.Label,
                "GlobalLabel" => EditorTool.GlobalLabel,   // [flyout] the label group
                "HierLabel" => EditorTool.HierLabel,
                "NoConnect" => EditorTool.NoConnect,
                "Text" => EditorTool.Text,   // [notes]
                "Highlight" => EditorTool.Highlight,   // [highlight]
                "Junction" => EditorTool.Junction,   // [junction]
                "Erase" => EditorTool.Erase,   // [erase]
                _ => EditorTool.Select
            };
            Canvas.Tool = tool;
            Canvas.Focus();
        }

        private void SyncTools()
        {
            ToolSelect.IsChecked = Canvas.Tool == EditorTool.Select || Canvas.Tool == EditorTool.Place;
            ToolWire.IsChecked = Canvas.Tool == EditorTool.Wire;
            ToolLabel.IsChecked = Canvas.Tool is EditorTool.Label or EditorTool.GlobalLabel or EditorTool.HierLabel;
            Theme.ToolFlyout.Select(ToolLabel, Canvas.Tool.ToString());   // [flyout] the group's face follows a label tool picked by its key
            ToolNoConnect.IsChecked = Canvas.Tool == EditorTool.NoConnect;
            ToolText.IsChecked = Canvas.Tool == EditorTool.Text;   // [notes]
            ToolHighlight.IsChecked = Canvas.Tool == EditorTool.Highlight;   // [highlight]
            ToolJunction.IsChecked = Canvas.Tool == EditorTool.Junction;   // [junction]
            ToolSheet.IsChecked = Canvas.Tool is EditorTool.Sheet or EditorTool.SheetPin or EditorTool.HierLabel or EditorTool.Bus or EditorTool.BusEntry;   // [sheets] [bus]
            ToolErase.IsChecked = Canvas.Tool == EditorTool.Erase;   // [erase]
            ToolDraw.IsChecked = IsDrawing || _drawPanel?.IsOpen == true;   // [shapes] the drawing strip open, or a drawing tool in use
            _drawPanel?.Sync();
        }

        private void OnRotate(object sender, RoutedEventArgs e) => Canvas.RotateSelection();
        private void OnMirror(object sender, RoutedEventArgs e) => Canvas.MirrorSelection();
        private void OnDelete(object sender, RoutedEventArgs e) => Canvas.DeleteSelection();
        private void OnUndo(object sender, RoutedEventArgs e) => W.Undo();
        private void OnRedo(object sender, RoutedEventArgs e) => W.Redo();
        private void OnFit(object sender, RoutedEventArgs e) => Canvas.FitToContent();

        private void OnZoomIn(object sender, RoutedEventArgs e) => Canvas.ZoomBy(1.25);   // [zoom] about the middle of the view

        private void OnZoomOut(object sender, RoutedEventArgs e) => Canvas.ZoomBy(0.8);

        private void UpdateChrome()
        {
            UndoButton.IsEnabled = W.CanUndo;
            RedoButton.IsEnabled = W.CanRedo;
            EmptyHint.Visibility = !W.Schematic.Parts.Any(p => Schematic.SameSheet(p.Sheet, Canvas.CurrentSheet)) && !W.Schematic.SheetsOn(Canvas.CurrentSheet).Any() ? Visibility.Visible : Visibility.Collapsed;   // [sheets] the shown sheet
            ListWorldBoards();
        }

        /// <summary>
        /// [boards] This page draws the project's circuit. A world (a template, a test bench) is a document of its own; when
        /// one is open and this page is empty, its boards are listed here, each opened for its circuit and code in one click.
        /// </summary>
        private void ListWorldBoards()
        {
            WorldBoards.Children.Clear();
            var world = W.ActiveWorld;
            var boards = world == null || W.Schematic.Parts.Count > 0
                ? new List<RobotTwin.CoreSim.Sandbox.WorldObject>()
                : world.Objects.Where(o => o.Kind == RobotTwin.CoreSim.Sandbox.WorldObjectKind.Board
                                           || (o.PartId != null && W.Library.Find(o.PartId)?.Symbol == SymbolKind.Board)).ToList();
            WorldBoards.Visibility = boards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (boards.Count == 0) return;
            WorldBoards.Children.Add(new TextBlock
            {
                Text = $"Açık dünya \u201C{world!.Title}\u201D projeden ayrı bir belgedir. Kartları:",
                Style = (Style)FindResource("Text.Caption"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            foreach (var o in boards.Take(8))
            {
                var b = new Button { Content = o.Name + " \u2014 devresini ve kodunu aç", Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 4, 12, 4) };
                var target = o;
                b.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.OpenWorldBoard(target);
                WorldBoards.Children.Add(b);
            }
        }

        private void OnPreviewKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) W.Simulation.Stop();
                else OnRun(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ================================================================ simulation

        private void OnRun(object sender, RoutedEventArgs e)
        {
            var sim = W.Simulation;
            switch (sim.State)
            {
                case SimState.Stopped:
                    Canvas.Tool = EditorTool.Select;
                    if (W.Schematic.Parts.Count == 0)
                    {
                        ShowToast("Simüle edilecek parça yok. Önce kütüphaneden parça ekleyin.");
                        return;
                    }

                    if (sim.Start())
                    {
                        AutoProbe();
                        W.Status = "Simülasyon çalışıyor: butonlara tıklayın, potları tekerlekle çevirin, kablolara tıklayıp ölçün";
                    }

                    break;
                case SimState.Running:
                    sim.Pause();
                    break;
                case SimState.Paused:
                    sim.Resume();
                    break;
            }
        }

        /// <summary>First run with no probes: watch the most interesting net automatically.</summary>
        private void AutoProbe()
        {
            var sim = W.Simulation;
            if (sim.Probes.Count > 0 || sim.Compiled == null) return;
            int Score(Net n)
            {
                int score = 0;
                foreach (var c in n.Connections)
                {
                    var def = W.Library.Find(W.Schematic.FindPart(c.RefDes)?.PartId ?? string.Empty);
                    if (def == null) continue;
                    bool supply = def.Symbol is SymbolKind.Battery or SymbolKind.VoltageSource || (def.Symbol == SymbolKind.Regulator && c.Pin == "OUT");
                    if (supply) score -= 10;
                    if (def.Symbol == SymbolKind.Led && c.Pin == "A") score += 4;
                    if (c.Pin is "D" or "C" or "OUT" or "Y" or "SIG") score += 3;
                    if (def.Symbol is SymbolKind.Capacitor or SymbolKind.PolarCapacitor or SymbolKind.Motor) score += 2;
                    score += 1;
                }

                return score;
            }

            var net = sim.Compiled.Netlist.Nets
                .Where(n => !n.IsGround && n.Connections.Count >= 2)
                .OrderByDescending(Score)
                .FirstOrDefault();
            if (net != null) sim.AddVoltageProbe(net.Name);
        }

        private void OnStep(object sender, RoutedEventArgs e) => W.Simulation.StepOnce(1e-3);
        private void OnStop(object sender, RoutedEventArgs e) => W.Simulation.Stop();

        private void OnPlaceLeads(object sender, RoutedEventArgs e)
        {
            Canvas.BeginLeadPlacement(true);
            ShowDock(1);
        }

        private void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedBox.SelectedItem is ComboBoxItem item && double.TryParse((string)item.Tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed))
            {
                W.Simulation.Speed = speed;
            }
        }

        private void OnSimStateChanged()
        {
            EmptyHint.Visibility = !W.Schematic.Parts.Any(p => Schematic.SameSheet(p.Sheet, Canvas.CurrentSheet)) && !W.Schematic.SheetsOn(Canvas.CurrentSheet).Any() ? Visibility.Visible : Visibility.Collapsed;   // [sheets] the shown sheet   // a circuit being simulated is never an empty sheet
            var state = W.Simulation.State;
            RunText.Text = state == SimState.Running ? "Duraklat" : state == SimState.Paused ? "Devam" : "Simüle et";
            RunButton.ToolTip = RobotWin.Studio.Shell.Lang.T(RunText.Text) + " (F5)";   // [toolbar] an icon: its word is the tooltip
            RunIcon.Data = (Geometry)FindResource(state == SimState.Running ? "I.Pause" : "I.Play");
            RunIcon.Filled = state != SimState.Running;
            StopButton.IsEnabled = state != SimState.Stopped;
            SimStatus.Text = state == SimState.Stopped ? string.Empty : W.Simulation.StatusText;   // [toolbar] nothing to say while it is off
            BuildPinPanel();
            BuildInspector();
            if (state == SimState.Running && W.Simulation.Time == 0) SerialOutput.Clear();
            Canvas.InvalidateVisual();
        }

        private void OnSimFrame()
        {
            UpdatePinPanel();
            RefreshLive(throttled: true);   // [live] the selected part's values
            var fw = W.Simulation.Firmware.FirstOrDefault();
            var esp = W.Simulation.Esp32Boards.FirstOrDefault();   // [esp32hw] ESP32 boards, either mode
            double baud = fw != null ? fw.Mcu.BaudRate : esp?.SerialBaud ?? 0;
            BaudText.Text = fw == null && esp == null ? "Firmware yok" : baud > 0 ? $"{baud:0} baud" : "Serial kapalı";
            if (W.Simulation.Log.Count != _lastLogCount)
            {
                _lastLogCount = W.Simulation.Log.Count;
                LogHeader.Text = _lastLogCount > 0 ? $"Olaylar ({_lastLogCount})" : "Olaylar";
                if (_lastLogCount > 0) LogList.ScrollIntoView(W.Simulation.Log[^1]);
            }
        }

        private void ShowToast(string text, string? part = null)
        {
            ToastText.Text = text;
            ToastHost.Visibility = Visibility.Visible;
            _toastPart = part;
            ToastAction.Visibility = part != null ? Visibility.Visible : Visibility.Collapsed;
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromSeconds(part != null ? 8 : 5);
            _toastTimer.Start();
        }

        /// <summary>"Neden?" on the failure toast: open the diagnosis with the burnt part highlighted.</summary>
        private void OnToastAction(object sender, RoutedEventArgs e)
        {
            ToastHost.Visibility = Visibility.Collapsed;
            ShowDock(2);
            RunDiagnosis();
            if (!string.IsNullOrEmpty(_toastPart)) Canvas.HighlightParts(new[] { _toastPart! });
        }

        // ================================================================ board pin panel

        private void OnSerialSend(object sender, RoutedEventArgs e)
        {
            if (SerialInput.Text.Length == 0) return;
            W.Simulation.SendSerial(SerialInput.Text + "\n");
            SerialInput.Clear();
        }

        private void OnSerialKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnSerialSend(sender, e);
        }

        private void OnSerialClear(object sender, RoutedEventArgs e) => SerialOutput.Clear();

        private void BuildPinPanel()
        {
            PinPanel.Children.Clear();
            _pinRows.Clear();
            var boards = W.Schematic.Parts.Where(p => W.Library.Find(p.PartId)?.Symbol == SymbolKind.Board).ToList();
            if (boards.Count == 0)
            {
                PinPanel.Children.Add(Hint("Devrede Arduino kartı yok. Kütüphanedeki 'Geliştirme Kartı' kategorisinden ekleyebilirsiniz."));
                return;
            }

            foreach (var board in boards)
            {
                PinPanel.Children.Add(FirmwareBar(board));
                var cosim = W.Simulation.FirmwareOf(board.RefDes);
                if (!W.Simulation.IsActive)
                {
                    PinPanel.Children.Add(Hint(board.Parameters.ContainsKey("firmware")
                        ? "Simülasyonu başlatınca kart bu firmware'i gerçek ATmega328P komut seti ile çalıştırır; pinler devreyi sürer, ADC devredeki gerilimi okur."
                        : "Firmware yoksa pinleri simülasyon sırasında buradan elle sürebilirsiniz (giriş, pull-up, LOW, HIGH, PWM)."));
                    continue;
                }

                var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                var def = W.Library.Get(board.PartId);
                foreach (var pin in def.Pins.Where(p => W.Simulation.BoardPin(board.RefDes, p.Name) != null))
                {
                    wrap.Children.Add(PinCard(board.RefDes, pin.Name, cosim == null));
                }

                PinPanel.Children.Add(wrap);
            }

            UpdatePinPanel();
        }

        private UIElement FirmwareBar(SchematicPart board)
        {
            var bar = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            bool running = W.Simulation.IsActive;
            var load = new Button { Content = "HEX yükle…", IsEnabled = !running, ToolTip = "Arduino IDE: Taslak → Derlenmiş ikiliyi dışa aktar (.hex)" };
            load.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter("Intel HEX (*.hex)|*.hex"), Title = RobotWin.Studio.Shell.Lang.T(board.RefDes + " için firmware seçin") };
                if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
                try
                {
                    RobotTwin.CoreSim.Firmware.IntelHex.Parse(System.IO.File.ReadAllText(dlg.FileName));
                }
                catch (System.IO.InvalidDataException ex)
                {
                    ShowToast("Geçersiz HEX dosyası: " + ex.Message);
                    return;
                }

                W.BeginChange();
                board.Parameters["firmware"] = dlg.FileName;
                W.CommitChange($"{board.RefDes}: firmware {System.IO.Path.GetFileName(dlg.FileName)}");
                BuildPinPanel();
            };
            var remove = new Button { Content = "Kaldır", Margin = new Thickness(6, 0, 0, 0), IsEnabled = !running && board.Parameters.ContainsKey("firmware") };
            remove.Click += (_, _) =>
            {
                W.BeginChange();
                board.Parameters.Remove("firmware");
                W.CommitChange($"{board.RefDes}: firmware kaldırıldı");
                BuildPinPanel();
            };
            var reset = new Button { Content = "Sıfırla", ToolTip = "Kartı sıfırlar (RESET düğmesi)", Margin = new Thickness(6, 0, 0, 0), IsEnabled = W.Simulation.FirmwareOf(board.RefDes) != null };
            reset.Click += (_, _) => W.Simulation.ResetBoard(board.RefDes);
            buttons.Children.Add(load);
            buttons.Children.Add(remove);
            buttons.Children.Add(reset);
            bar.Children.Add(buttons);

            var title = new StackPanel();
            title.Children.Add(new TextBlock { Text = board.RefDes + " · " + W.Library.Get(board.PartId).Name, Style = (Style)FindResource("Text.Subtitle") });
            string fw = board.Parameters.TryGetValue("firmware", out var f) ? f : string.Empty;
            string name = fw.Length == 0 ? "Firmware yok — pinler elle sürülür" : "Firmware: " + System.IO.Path.GetFileName(fw.Replace("builtin:", string.Empty));
            var cosim = W.Simulation.FirmwareOf(board.RefDes);
            if (cosim != null) name += $"   ·   {cosim.Mcu.Cycles / 1e6:0.0} M döngü" + (cosim.InBrownOut ? "   ·   BROWN-OUT (Vcc < 2.7 V)" : string.Empty);
            title.Children.Add(new TextBlock { Text = name, Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 2, 0, 0) });
            bar.Children.Add(title);
            return bar;
        }

        private UIElement PinCard(string board, string pin, bool manual = true)
        {
            var card = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = (Brush)FindResource("B.Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 8),
                Margin = new Thickness(0, 0, 8, 8),
                Width = 228
            };
            var stack = new StackPanel();
            var head = new DockPanel();
            var name = new TextBlock { Text = pin, Foreground = (Brush)FindResource("B.Text"), FontSize = 13 };
            var volts = new TextBlock { Foreground = (Brush)FindResource("B.Text"), FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
            var level = new TextBlock { Foreground = (Brush)FindResource("B.TextMuted"), FontSize = 12, Margin = new Thickness(8, 1, 0, 0) };
            DockPanel.SetDock(volts, Dock.Right);
            head.Children.Add(volts);
            head.Children.Add(name);
            head.Children.Add(level);
            stack.Children.Add(head);

            var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            var buttons = new List<ToggleButton>();
            void ModeButton(string text, Action apply, bool initial)
            {
                var b = new ToggleButton { Content = text, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(7, 2, 7, 2), FontSize = 12, IsChecked = initial };
                b.Click += (_, _) =>
                {
                    foreach (var other in buttons) other.IsChecked = ReferenceEquals(other, b);
                    apply();
                };
                buttons.Add(b);
                modes.Children.Add(b);
            }

            var slider = new Slider { Minimum = 0, Maximum = 255, Value = 128, SmallChange = 1, LargeChange = 16, VerticalAlignment = VerticalAlignment.Center };
            var pwmRow = new DockPanel { Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
            var duty = RobotWin.Studio.Theme.SliderInput.Create(slider, "0.#", " %", v => v / 2.55, x => x * 2.55, width: 64);   // [slider-input] the duty typed
            duty.ToolTip = "Görev oranı (%): yazıp Enter";
            DockPanel.SetDock(duty, Dock.Right);
            pwmRow.Children.Add(duty);
            pwmRow.Children.Add(slider);
            ModeButton("IN", () => { pwmRow.Visibility = Visibility.Collapsed; W.Simulation.SetBoardPin(board, pin, PinMode.Input); }, true);
            ModeButton("IN↑", () => { pwmRow.Visibility = Visibility.Collapsed; W.Simulation.SetBoardPin(board, pin, PinMode.InputPullup); }, false);
            ModeButton("LOW", () => { pwmRow.Visibility = Visibility.Collapsed; W.Simulation.SetBoardPin(board, pin, PinMode.OutputLow); }, false);
            ModeButton("HIGH", () => { pwmRow.Visibility = Visibility.Collapsed; W.Simulation.SetBoardPin(board, pin, PinMode.OutputHigh); }, false);
            ModeButton("PWM", () => { pwmRow.Visibility = Visibility.Visible; W.Simulation.SetBoardPwm(board, pin, slider.Value / 255.0); }, false);
            slider.ValueChanged += (_, _) => W.Simulation.SetBoardPwm(board, pin, slider.Value / 255.0);
            if (manual)
            {
                stack.Children.Add(modes);
                stack.Children.Add(pwmRow);
            }
            card.Child = stack;
            _pinRows[board + "." + pin] = (volts, level);
            return card;
        }

        private void UpdatePinPanel()
        {
            if (_pinRows.Count == 0 || !IsVisible) return;
            foreach (var kv in _pinRows)
            {
                int dot = kv.Key.IndexOf('.');
                string board = kv.Key.Substring(0, dot), pin = kv.Key.Substring(dot + 1);
                var dev = W.Simulation.BoardPin(board, pin);
                if (dev == null) continue;
                kv.Value.Volts.Text = SiValue.Format(dev.Voltage, "V", 3);
                string mode = dev.Mode switch
                {
                    PinMode.OutputHigh => "çıkış ",
                    PinMode.OutputLow => "çıkış ",
                    PinMode.InputPullup => "giriş↑ ",
                    _ => "giriş "
                };
                string level = mode + (dev.DigitalValue ? "HIGH" : "LOW");
                if (dev.PwmActive) level = $"PWM %{dev.PwmDuty * 100:F0}";
                if (dev.IsFailed) level = "HASARLI";
                kv.Value.Level.Text = level + (Math.Abs(dev.Current) > 1e-4 ? "  " + SiValue.Format(dev.Current, "A", 3) : string.Empty);
            }
        }

        private TextBlock Hint(string text) => new TextBlock { Text = text, Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 6, 0, 0), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

        // ================================================================ inspector

        private void BuildInspector()
        {
            Inspector.Children.Clear();
            _liveRefresh.Clear();   // [live] the rows belong to the inspector they were built in
            var selection = Canvas.Selection;
            if (selection.Count == 1 && selection.First() is SchematicPart part)
            {
                BuildPartInspector(part);
            }
            else if (selection.Count == 1 && selection.First() is SchematicShape { Kind: SheetShapeKind.RuleArea } area)
            {
                // [rule-area] the class its nets take
                Section("Kural alanı");
                var box = new TextBox { Text = area.NetClass };
                Commit(box, value =>
                {
                    if (string.IsNullOrWhiteSpace(value) || value == area.NetClass) return;
                    W.BeginChange();
                    area.NetClass = value.Trim();
                    W.CommitChange($"Kural alanı: {value.Trim()}");
                    Canvas.InvalidateVisual();
                });
                Inspector.Children.Add(box);
                Inspector.Children.Add(Hint("İçindeki teller, pinler ve etiketlerin hatları PCB'de bu net sınıfını alır; alandaki bir net sınıfı yönergesi ondan önce gelir."));
            }
            else if (selection.Count == 1 && selection.First() is NetLabel { IsDirective: true } directive)
            {
                // [directive] KiCad's netclass flag: the class its net takes on the board
                Section("Net sınıfı yönergesi");
                var box = new TextBox { Text = directive.NetClass };
                Commit(box, value =>
                {
                    if (string.IsNullOrWhiteSpace(value) || value == directive.NetClass) return;
                    W.BeginChange();
                    directive.NetClass = value.Trim();
                    W.CommitChange($"Net sınıfı yönergesi: {value.Trim()}");
                });
                Inspector.Children.Add(box);
                Inspector.Children.Add(Hint("Değdiği hat PCB'de bu net sınıfını alır (Power, Default ya da yeni bir ad: yoksa Default'un kopyası olarak açılır). Hattın adını değiştirmez."));
            }
            else if (selection.Count == 1 && selection.First() is NetLabel label)
            {
                Section("Hat etiketi");
                var box = new TextBox { Text = label.Name };
                Commit(box, value =>
                {
                    if (string.IsNullOrWhiteSpace(value) || value == label.Name) return;
                    W.BeginChange();
                    label.Name = value.Trim();
                    W.CommitChange("Etiket yeniden adlandırıldı");
                });
                Inspector.Children.Add(box);
                Inspector.Children.Add(Hint("Aynı adı taşıyan tüm etiketler kablo çekmeden birbirine bağlanır. 'GND' adı toprağı belirtir."));
            }
            else if (selection.Count > 1)
            {
                Section("Seçim");
                Inspector.Children.Add(new TextBlock { Text = $"{selection.Count} öğe seçili", Style = (Style)FindResource("Text.Subtitle") });
                BuildMultiSelectionInspector(selection);
            }
            else
            {
                BuildDocumentInspector();
            }
        }

        private void Section(string text) =>
            Inspector.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, Inspector.Children.Count == 0 ? 0 : 18, 0, 8) });

        private void Commit(TextBox box, Action<string> apply)
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    apply(box.Text);
                    e.Handled = true;
                }
            };
            box.LostKeyboardFocus += (_, _) => apply(box.Text);
        }

        private void BuildPartInspector(SchematicPart part)
        {
            var def = W.Library.Find(part.PartId);
            if (def == null) return;
            var preset = part.PresetId != null ? W.Library.FindPreset(part.PresetId) : null;
            var parameters = W.Schematic.ResolveParameters(W.Library, part);

            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            var preview = new Border { BorderBrush = (Brush)FindResource("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Height = 52, Margin = new Thickness(0, 0, 12, 0) };
            preview.Child = new SymbolPreview { PresetId = preset?.Id ?? def.Id, Margin = new Thickness(6) };
            header.Children.Add(preview);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(titles, 1);
            titles.Children.Add(new TextBlock { Text = preset?.Name ?? def.Name, Style = (Style)FindResource("Text.Subtitle"), TextWrapping = TextWrapping.Wrap });
            titles.Children.Add(new TextBlock { Text = def.Category, Style = (Style)FindResource("Text.Caption") });
            header.Children.Add(titles);
            Inspector.Children.Add(header);
            if (preset != null) Inspector.Children.Add(Hint(preset.Description));
            // [limits] What the model leaves out, folded away until asked for.
            var limits = RobotTwin.CoreSim.Design.ModelLimits.Of(def.Id);
            if (limits.Count > 0)
            {
                var notes = new StackPanel();
                foreach (string note in limits)
                    notes.Children.Add(RobotWin.Studio.Pages.LibraryPage.Bullet(note, 2));
                Inspector.Children.Add(new Expander { Header = $"Modelin sınırları ({limits.Count})", Content = notes, Margin = new Thickness(0, 6, 0, 0) });
            }

            LiveSection(part, def);   // [live] while it runs, its values first (Pages/SchematicPage.Live.cs)
            Section("Tanımlayıcı");
            var refBox = new TextBox { Text = part.RefDes };
            Commit(refBox, value =>
            {
                value = value.Trim();
                if (value.Length == 0 || value == part.RefDes) return;
                if (W.Schematic.FindPart(value) != null)
                {
                    ShowToast($"'{value}' zaten kullanılıyor.");
                    refBox.Text = part.RefDes;
                    return;
                }

                W.BeginChange();
                var change = new RefDesChange(part, part.RefDes, value);
                part.RefDes = value;
                Annotator.ApplyToLinkedData(W.Schematic, new[] { change });   // [integrity] the vehicle's motors and sensors; the code follows on commit
                W.CommitChange("Yeniden adlandırıldı");
            });
            Inspector.Children.Add(refBox);

            var editable = def.Parameters.Where(p => p.Key != "pinmap" && p.Key != "pinout" && p.Key != "footprint" && p.Key != "model").ToList();
            if (editable.Count > 0) Section("Parametreler");
            bool locked = W.Simulation.IsActive;
            foreach (var p in editable)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                var label = new TextBlock { Text = p.Label + (string.IsNullOrEmpty(p.Unit) ? string.Empty : $" ({p.Unit})"), Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
                row.Children.Add(label);
                string current = parameters.TryGetValue(p.Key, out var v) ? v : p.DefaultValue;
                FrameworkElement editor;
                if (p.Choices != null)
                {
                    var combo = new ComboBox { IsEnabled = !locked };
                    foreach (var choice in p.Choices) combo.Items.Add(choice);
                    combo.SelectedItem = p.Choices.FirstOrDefault(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase)) ?? current;
                    combo.SelectionChanged += (_, _) => SetParameter(part, p.Key, combo.SelectedItem as string ?? string.Empty);
                    editor = combo;
                }
                else
                {
                    var box = new TextBox { Text = current, IsEnabled = !locked };
                    Commit(box, value => SetParameter(part, p.Key, value.Trim()));
                    editor = box;
                }

                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);
                Inspector.Children.Add(row);
            }

            if (locked && editable.Count > 0) Inspector.Children.Add(Hint("Parametreleri değiştirmek için simülasyonu durdurun."));
            BuildSpiceSection(part, locked);   // [spice] Pages/SchematicPage.Spice.cs
            BuildVariantSection(part, def, parameters, locked);
            BuildToleranceSection(part, def);

            Section("Konum");
            Inspector.Children.Add(new TextBlock
            {
                Text = $"x {part.Position.X:0}  y {part.Position.Y:0}   ·   {part.Rotation}°{(part.Mirror ? "  aynalı" : string.Empty)}",
                Style = (Style)FindResource("Text.Mono")
            });

            string fp = def.ResolveFootprint(parameters);
            if (!string.IsNullOrEmpty(fp))
            {
                Section("PCB kılıfı");
                Inspector.Children.Add(new TextBlock { Text = fp, Style = (Style)FindResource("Text.Mono") });
            }

            if (W.Simulation.IsActive && def.Pins.Count == 2)
            {
                var probe = new Button { Content = "Akımını osiloskopta izle", Margin = new Thickness(0, 16, 0, 0) };
                probe.Click += (_, _) => W.Simulation.AddCurrentProbe(part.RefDes);
                Inspector.Children.Add(probe);
            }

            if (W.Simulation.IsActive && W.Simulation.MainDevice(part.RefDes) is BenchSupply psu)
            {
                Section("Ön panel (canlı)");
                void Knob(string label, Func<double> get, Action<double> set, string unit)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = $"{label} ({unit})", Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center });
                    var box = new TextBox { Text = get().ToString("0.###", CultureInfo.InvariantCulture) };
                    Commit(box, v =>
                    {
                        if (SiValue.TryParse(v, out double x) && x >= 0)
                        {
                            set(x);
                            W.Simulation.Simulator?.NotifyDiscontinuity();
                        }
                    });
                    Grid.SetColumn(box, 1);
                    row.Children.Add(box);
                    Inspector.Children.Add(row);
                }

                Knob("Gerilim", () => psu.Voltage, v => psu.Voltage = v, "V");
                Knob("Akım sınırı", () => psu.CurrentLimit, v => psu.CurrentLimit = v, "A");
                Knob("OVP (0 = kapalı)", () => psu.OvpLevel, v => psu.OvpLevel = v, "V");
                var output = new CheckBox { Style = (Style)FindResource("Switch"), Content = psu.OvpTripped ? "Çıkış (OVP attı — açınca sıfırlanır)" : "Çıkış açık", IsChecked = psu.OutputEnabled && !psu.OvpTripped };
                output.Click += (_, _) =>
                {
                    psu.OutputEnabled = false;
                    if (output.IsChecked == true) psu.OutputEnabled = true;
                    W.Simulation.Simulator?.NotifyDiscontinuity();
                };
                Inspector.Children.Add(output);
                Inspector.Children.Add(Hint("Ön paneldeki ayarlar çalışan simülasyona hemen uygulanır (kaydedilmez). CC: yük, akım sınırından fazlasını istiyor."));
            }

            if (def.Symbol == SymbolKind.Multimeter)
            {
                var open = new Button { Content = "Multimetre panelinde aç", Margin = new Thickness(0, 10, 0, 0) };
                open.Click += (_, _) =>
                {
                    W.Simulation.MeterPart = part.RefDes;
                    ShowDock(1);
                    BuildMeterPanel();
                };
                Inspector.Children.Add(open);
                Inspector.Children.Add(Hint("Akım ölçmek için mA (≤ 400 mA) veya A girişini devreye SERİ bağlayın; gerilim için VΩ ve COM'u ölçülecek iki noktaya bağlayın."));
            }

            if (W.Simulation.IsActive && W.Simulation.IsPartFailed(part.RefDes) && W.Simulation.Compiled is { } compiled)
            {
                Section("Hasar");
                foreach (var device in compiled.DevicesOf(part.RefDes).Where(d => d.IsFailed))
                {
                    Inspector.Children.Add(new TextBlock
                    {
                        Text = CircuitDiagnostics.ExplainFailure(compiled, device, W.Simulation.Stress),
                        Style = (Style)FindResource("Text.Body"),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("B.TextSecondary")
                    });
                }

                var repair = new Button { Content = "Onar (yenisiyle değiştir)", Margin = new Thickness(0, 10, 0, 0) };
                repair.Click += (_, _) =>
                {
                    W.Simulation.RepairPart(part.RefDes);
                    BuildInspector();
                };
                Inspector.Children.Add(repair);
            }
        }

        /// <summary>"Gerçek değer": what this particular part measures in the realistic/Monte Carlo board.</summary>
        /// <summary>
        /// [variants] While an alternative is active, what it does with this part: left off, or another value. Shown only
        /// then; the parameters above are the design as drawn, shared by every alternative.
        /// </summary>
        private bool _fillingVariants;

        /// <summary>[variants] The alternative chooser beside Simulate: there once the design has alternatives.</summary>
        private void RefreshVariantBox()
        {
            var s = W.Schematic;
            _fillingVariants = true;
            try
            {
                VariantBox.Items.Clear();
                VariantBox.Visibility = s.Variants.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                VariantBox.Items.Add(new ComboBoxItem { Content = "Çizildiği gibi", Tag = string.Empty });
                foreach (var v in s.Variants) VariantBox.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Name, ToolTip = v.Note });
                VariantBox.Items.Add(new ComboBoxItem { Content = "Alternatifler…" });
                var current = Variants.Find(s, s.ActiveVariant);
                VariantBox.SelectedIndex = current == null ? 0 : s.Variants.IndexOf(current) + 1;
            }
            finally
            {
                _fillingVariants = false;
            }
        }

        private void OnVariantPicked(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingVariants || VariantBox.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string name)
            {
                RefreshVariantBox();
                ShowVariants();
                return;
            }

            SelectVariant(name.Length == 0 ? null : name);
        }

        /// <summary>
        /// [variants] Chooses the alternative that is simulated, listed and put together (null: the design as drawn); undoable.
        /// A running simulation starts again with it.
        /// </summary>
        public void SelectVariant(string? name)
        {
            var s = W.Schematic;
            if (string.Equals(s.ActiveVariant ?? string.Empty, name ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                RefreshVariantBox();
                return;
            }

            bool running = W.Simulation.IsActive;
            if (running) W.Simulation.Stop();
            W.BeginChange();
            s.ActiveVariant = name;
            W.CommitChange(name == null ? "Tasarım: çizildiği gibi" : "Tasarım alternatifi: " + name);
            RefreshVariantBox();
            Canvas.InvalidateVisual();
            BuildInspector();
            if (running) W.Simulation.Start();
        }

        /// <summary>[variants] The window of the alternatives: made, named, copied, removed, chosen, compared.</summary>
        public void ShowVariants()
        {
            if (Window.GetWindow(this) is not Window owner) return;
            var window = new VariantsWindow(owner, SelectVariant);
            if (App.IsSnapshot)
            {
                window.ShowActivated = false;
                window.Show();
                window.UpdateLayout();
                App.SnapshotTarget = System.Windows.Media.VisualTreeHelper.GetChildrenCount(window) > 0 ? System.Windows.Media.VisualTreeHelper.GetChild(window, 0) as FrameworkElement : null;
                return;
            }

            window.ShowDialog();
            RefreshVariantBox();
            Canvas.InvalidateVisual();
            BuildInspector();
        }

        private void BuildVariantSection(SchematicPart part, PartDefinition def, IReadOnlyDictionary<string, string> drawn, bool locked)
        {
            var variant = Variants.Find(W.Schematic, W.Schematic.ActiveVariant);
            if (variant == null || def.Symbol == SymbolKind.Ground || def.Symbol == SymbolKind.PowerRail) return;
            Section("Alternatif: " + variant.Name);
            var over = variant.For(part);
            var off = new CheckBox { Content = "Bu alternatifte takılmaz", IsChecked = over?.NotFitted == true, IsEnabled = !locked, Margin = new Thickness(0, 0, 0, 8) };
            off.Click += (_, _) => EditOverride(part, variant, o => o.NotFitted = off.IsChecked == true, off.IsChecked == true ? $"{part.RefDes}: {variant.Name} alternatifinde takılmaz" : $"{part.RefDes}: {variant.Name} alternatifinde takılır");
            Inspector.Children.Add(off);
            if (!string.IsNullOrEmpty(def.ValueKey) && over?.NotFitted != true)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = "Bu alternatifte değer", Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                string drawnValue = drawn.TryGetValue(def.ValueKey, out var dv) ? dv : string.Empty;
                var box = new TextBox { Text = over != null && over.Parameters.TryGetValue(def.ValueKey, out var ov) ? ov : string.Empty, Tag = drawnValue, IsEnabled = !locked };
                Grid.SetColumn(box, 1);
                row.Children.Add(box);
                Commit(box, value =>
                {
                    value = value.Trim();
                    EditOverride(part, variant, o =>
                    {
                        if (value.Length == 0 || value == drawnValue) o.Parameters.Remove(def.ValueKey);
                        else o.Parameters[def.ValueKey] = value;
                    }, $"{part.RefDes}: {variant.Name} alternatifinde değer {(value.Length == 0 ? drawnValue : value)}");
                });
                Inspector.Children.Add(row);
            }

            Inspector.Children.Add(Hint("Boş bırakılan çizildiği gibidir. Yukarıdaki parametreler bütün alternatiflerindir."));
        }

        private void EditOverride(SchematicPart part, DesignVariant variant, Action<PartOverride> change, string status)
        {
            W.BeginChange();
            change(variant.Edit(part));
            variant.Tidy(W.Schematic);
            W.CommitChange(status);
            Canvas.InvalidateVisual();
            BuildInspector();
        }

        private void BuildToleranceSection(SchematicPart part, PartDefinition def)
        {
            var settings = W.Schematic.Tolerances;
            List<ToleranceDraw> draws;
            if (W.Simulation.Compiled != null && W.Simulation.Compiled.ToleranceDraws.TryGetValue(part.RefDes, out var live)) draws = live;
            else draws = ToleranceModel.Preview(W.Schematic, W.Library, part, settings, 0, AppSettings.Current.AmbientTempC);
            var probe = ToleranceModel.Preview(W.Schematic, W.Library, part, new ToleranceSettings { Mode = ToleranceMode.Realistic });
            if (probe.Count == 0) return;
            Section("Gerçek değer");
            if (draws.Count == 0)
            {
                Inspector.Children.Add(Hint("Simülasyon nominal değerlerle çalışıyor. Projenin 'Tolerans' ayarını 'Gerçekçi' yaparak bu parçanın üretim toleransından çekilen gerçek değerini kullanın."));
                return;
            }

            foreach (var d in draws)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = d.Label, Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center });
                var value = new TextBlock
                {
                    Text = SiValue.Format(d.Actual, d.Unit, 4) + "  " + d.DeviationPercent.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " %",
                    FontFamily = (FontFamily)FindResource("F.Mono"),
                    FontSize = 12,
                    Foreground = (Brush)FindResource(Math.Abs(d.DeviationPercent) > 10 ? "B.Warning" : "B.Text")
                };
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                Inspector.Children.Add(row);
                Inspector.Children.Add(new TextBlock
                {
                    Text = $"nominal {SiValue.Format(d.Nominal, d.Unit, 4)} · {d.Note}",
                    Style = (Style)FindResource("Text.Caption"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            Inspector.Children.Add(Hint($"Tohum {settings?.Seed ?? 1}: aynı tohum her zaman aynı kartı üretir."));
        }

        private void SetParameter(SchematicPart part, string key, string value)
        {
            var current = W.Schematic.ResolveParameters(W.Library, part);
            if (current.TryGetValue(key, out var old) && old == value) return;
            W.BeginChange();
            part.Parameters[key] = value;
            W.CommitChange($"{part.RefDes}: {key} = {value}");
        }

        private void BuildDocumentInspector()
        {
            Section("Proje");
            var title = new TextBox { Text = W.Schematic.Title };
            Commit(title, value =>
            {
                if (value == W.Schematic.Title) return;
                W.BeginChange();
                W.Schematic.Title = value;
                W.CommitChange("Başlık değişti");
            });
            Inspector.Children.Add(title);

            BuildToleranceSettings();

            var netlist = W.Schematic.BuildNetlist(W.Library);
            Section("Özet");
            Inspector.Children.Add(new TextBlock
            {
                Text = $"{W.Schematic.Parts.Count} parça · {W.Schematic.Wires.Count} kablo · {netlist.Nets.Count} hat",
                Style = (Style)FindResource("Text.Body")
            });

            if (netlist.Warnings.Count > 0)
            {
                Section("Uyarılar");
                foreach (var w in netlist.Warnings.Take(12))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
                    row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = (Brush)FindResource("B.Warning"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 9, 0) });
                    row.Children.Add(new TextBlock { Text = w, Style = (Style)FindResource("Text.Body"), MaxWidth = 240 });
                    Inspector.Children.Add(row);
                }
            }

            Section("Kısayollar");
            // [keys] the editor's own keys as they are set now (Ayarlar → Klavye kısayolları can move them)
            static string K(params string[] ids) => string.Join(" · ", ids.Select(id => Shell.Shortcuts.All.FirstOrDefault(c => c.Id == "devre." + id)?.Keys ?? "?").Select(k => k.Length == 0 ? "—" : k.Split(',')[0].Trim()));
            foreach (var (key, what) in new[]
            {
                (K("parca", "guc"), "Parça · güç sembolü ekle"), (K("kablo", "etiket", "nc"), "Kablo · etiket · NC"), (K("tasi", "surukle"), "Taşı · sürükle"),
                (K("dondur", "aynala-x", "aynala-y"), "Döndür · aynala"), (K("ozellik", "deger"), "Özellikler · değer"), ("Ctrl+C/V", "Kopyala / yapıştır"),
                ("Ctrl+D", "Çoğalt"), ("Ctrl+F", "Bul / değiştir"), (K("hat"), "Hattı vurgula"), ("Ctrl+Z / Y", "Geri al / Yinele"),
                (K("sigdir") + " · Home", "Sığdır"), ("F5", "Simülasyon"), ("F1 · ?", "Tüm kısayollar")
            })
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = key, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, Foreground = (Brush)FindResource("B.TextMuted") });
                var t = new TextBlock { Text = what, Style = (Style)FindResource("Text.Body") };
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                Inspector.Children.Add(row);
            }
        }
            // ================================================================ simulation settings: tolerances

        private void BuildToleranceSettings()
        {
            Section("Simülasyon: parça toleransı");
            var settings = W.Schematic.Tolerances ?? new ToleranceSettings();
            bool locked = W.Simulation.IsActive;
            var mode = new ComboBox { IsEnabled = !locked, Margin = new Thickness(0, 0, 0, 7) };
            mode.Items.Add("Nominal (ideal değerler)");
            mode.Items.Add("Gerçekçi (sabit tohum)");
            mode.Items.Add("Monte Carlo (N koşu)");
            mode.SelectedIndex = (int)settings.Mode;
            mode.SelectionChanged += (_, _) => SetTolerances(s => s.Mode = (ToleranceMode)mode.SelectedIndex);
            Inspector.Children.Add(mode);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = "Tohum", Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center });
            var seed = new TextBox { Text = settings.Seed.ToString(CultureInfo.InvariantCulture), IsEnabled = !locked };
            Grid.SetColumn(seed, 1);
            Commit(seed, v =>
            {
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n != (W.Schematic.Tolerances?.Seed ?? 1)) SetTolerances(s => s.Seed = n);
            });
            row.Children.Add(seed);
            var dice = new Button { Content = "Yeni", Margin = new Thickness(6, 0, 0, 0), IsEnabled = !locked, ToolTip = "Rastgele yeni bir tohum: başka bir 'üretilmiş kart'" };
            Grid.SetColumn(dice, 2);
            dice.Click += (_, _) => SetTolerances(s => s.Seed = new Random().Next(1, 100000));
            row.Children.Add(dice);
            Inspector.Children.Add(row);
            Inspector.Children.Add(Hint(settings.Mode switch
            {
                ToleranceMode.Realistic => "Her parça toleransı içinde (direnç ±%5, seramik ±%10, elektrolitik −20/+80 %, hFE, LED Vf...) sabit bir gerçek değer alır. Seramiklerde sıcaklık etkisi de eklenir.",
                ToleranceMode.MonteCarlo => "Etkileşimli simülasyon tohum 0 kartıyla çalışır; dağılımı görmek için alttaki 'Monte Carlo' sekmesini kullanın.",
                _ => "Tüm parçalar tam etiket değerinde. Gerçek devrelerde hiçbir parça tam nominal değildir."
            }));
        }

        private void SetTolerances(Action<ToleranceSettings> change)
        {
            W.BeginChange();
            var s = W.Schematic.Tolerances ?? new ToleranceSettings();
            var copy = new ToleranceSettings { Mode = s.Mode, Seed = s.Seed, Runs = s.Runs };
            change(copy);
            W.Schematic.Tolerances = copy.Mode == ToleranceMode.Nominal && copy.Seed == 1 && copy.Runs == 50 ? null : copy;
            W.CommitChange($"Tolerans: {copy.Mode}, tohum {copy.Seed}");
        }

        // ================================================================ responsive layout

        private void OnToggleLibrary(object sender, RoutedEventArgs e)
        {
            _libraryOpen = LibraryToggle.IsChecked == true;
            if (_narrow == true && _libraryOpen) _dock?.SetVisible("inspector", false);
            _dock?.SetVisible("library", _libraryOpen);   // [dock-min] wherever it is docked, minimized or not
        }

        private void OnToggleInspector(object sender, RoutedEventArgs e)
        {
            _inspectorOpen = InspectorToggle.IsChecked == true;
            if (_narrow == true && _inspectorOpen) _dock?.SetVisible("library", false);
            _dock?.SetVisible("inspector", _inspectorOpen);
        }

        /// <summary>
        /// Wide pages dock the library and the inspector beside the canvas. Below
        /// <see cref="NarrowWidth"/> both collapse; opening one shows it as an overlay on top of the
        /// canvas so the drawing keeps the full width.
        /// </summary>
        private void ApplyLayout(bool force = false)
        {
            double width = ActualWidth;
            if (width <= 0) return;
            bool narrow = width < NarrowWidth;
            if (_narrow != narrow)
            {
                // Crossing the threshold resets the panels to the default for that width; the first layout keeps what was saved.
                bool first = _narrow == null;
                _libraryOpen = first && _dock != null ? _dock.PlaceOf("library") != DockPlace.Hidden && !narrow : !narrow;
                _inspectorOpen = first && _dock != null ? _dock.PlaceOf("inspector") != DockPlace.Hidden && !narrow : !narrow;
                _narrow = narrow;
            }
            else if (!force)
            {
                return;
            }

            // [dock-min] the library and the inspector are dock panels: shown, hidden, minimized or torn off through the dock manager
            if (_dock != null)
            {
                _dock.SetVisible("library", _libraryOpen);
                _dock.SetVisible("inspector", _inspectorOpen);
            }

            // The toolbar and the instrument dock compress too.
            EditTools.Visibility = width < 820 ? Visibility.Collapsed : Visibility.Visible;
            EditorExtras.Visibility = width < 1150 ? Visibility.Collapsed : Visibility.Visible;
            SimStatus.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            SpeedBox.Width = 76;   // [toolbar] "1×" and the like: no longer a word to make room for
            ScopePanelColumn.Width = new GridLength(narrow ? 262 : 318);
            MeterColumn.Width = new GridLength(width < 900 ? 300 : 470);
        }

        // ================================================================ multimeter panel

        private readonly List<Action> _meterRefresh = new List<Action>();

        private void BuildMeterPanel()
        {
            MeterPanel.Children.Clear();
            _meterRefresh.Clear();
            var sim = W.Simulation;
            var meters = W.Schematic.Parts.Where(p => W.Library.Find(p.PartId)?.Symbol == SymbolKind.Multimeter).ToList();

            MeterPanel.Children.Add(new TextBlock { Text = "Ölçü aleti", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
            var choose = new ComboBox { Margin = new Thickness(0, 0, 0, 10), MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
            choose.Items.Add("Prob multimetre (kırmızı / siyah uçlar)");
            foreach (var m in meters) choose.Items.Add($"{m.RefDes} · devredeki multimetre");
            choose.SelectedIndex = sim.MeterPart == null ? 0 : Math.Max(0, meters.FindIndex(m => m.RefDes == sim.MeterPart) + 1);
            choose.SelectionChanged += (_, _) =>
            {
                sim.MeterPart = choose.SelectedIndex <= 0 ? null : meters[choose.SelectedIndex - 1].RefDes;
                Meter.InvalidateVisual();
                BuildMeterPanel();
            };
            MeterPanel.Children.Add(choose);

            if (sim.MeterPart == null)
            {
                foreach (bool red in new[] { true, false })
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                    row.Children.Add(new System.Windows.Shapes.Ellipse
                    {
                        Width = 9,
                        Height = 9,
                        Fill = new SolidColorBrush(red ? Color.FromRgb(0xD9, 0x3A, 0x3A) : Color.FromRgb(0x50, 0x50, 0x56)),
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var text = new TextBlock { Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Width = 190 };
                    row.Children.Add(text);
                    var place = new Button { Content = "Tak", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
                    var remove = new Button { Content = "Çıkar", Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
                    bool isRed = red;
                    place.Click += (_, _) => Canvas.BeginLeadPlacement(isRed);
                    remove.Click += (_, _) =>
                    {
                        sim.SetMeterLead(isRed, null, null);
                        BuildMeterPanel();
                    };
                    row.Children.Add(place);
                    row.Children.Add(remove);
                    _meterRefresh.Add(() =>
                    {
                        string? net = isRed ? sim.MeterRedNet : sim.MeterBlackNet;
                        text.Text = (isRed ? "Kırmızı uç: " : "Siyah uç (COM): ") + (net ?? "takılı değil");
                    });
                    MeterPanel.Children.Add(row);
                }

                MeterPanel.Children.Add(Hint("Uçları şemada bir kabloya veya pine takın: 'Tak'a basıp noktaya tıklayın (ya da kabloya sağ tıklayın). Kırmızı ucun takılı olduğu giriş (VΩ / mA / 10A) multimetrenin altındaki soketlerden seçilir."));
            }
            else
            {
                MeterPanel.Children.Add(Hint($"{sim.MeterPart}, şemadaki bağlantılarıyla ölçer: VΩ–COM gerilim/direnç, mA veya A–COM devreye seri akım. Fonksiyonu multimetrenin döner anahtarından seçin."));
            }

            var spec = new TextBlock { Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            var warning = new TextBlock { Foreground = (Brush)FindResource("B.Warning"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12.5 };
            _meterRefresh.Add(() =>
            {
                var meter = sim.ActiveMeter;
                var mode = meter?.Mode ?? sim.MeterMode;
                var (pct, digits) = MultimeterSpec.Accuracy(mode);
                string range = meter != null ? meter.Range.Label : MultimeterSpec.Ranges(mode)[0].Label;
                string what = mode switch
                {
                    MultimeterMode.DcVoltage or MultimeterMode.AcVoltage => "Giriş empedansı 10 MΩ: yüksek dirençli noktalarda ölçülen gerilim düşük çıkar (yükleme etkisi).",
                    MultimeterMode.DcCurrent or MultimeterMode.AcCurrent => $"Seri bağlanır. Şönt (yük gerilimi): mA girişi 1.8 Ω, 10 A girişi 0.03 Ω{(meter != null ? $" · şu an {SiValue.Format(meter.BurdenVoltage, "V")}" : string.Empty)}. Sigortalar: F 440 mA, F 11 A.",
                    MultimeterMode.Resistance => "Test gerilimi ≤ 1 V. Yalnızca enerjisiz devrede ölçün; paralel parçalar okumayı düşürür.",
                    MultimeterMode.Continuity => "30 Ω altında bip sesi.",
                    MultimeterMode.Diode => "≈1 mA test akımı, açık devrede 3.3 V: ileri yönde Vf, ters yönde OL.",
                    MultimeterMode.Capacitance => "Bilinen dirençle şarj süresinden ölçer; büyük kapasitelerde okuma yavaşlar. Kondansatör boşaltılmış olmalı.",
                    _ => "Frekans ve doluluk oranı (karşılıklı sayım); düşük genlikte 0 Hz okur."
                };
                spec.Text = $"{MultimeterSpec.ModeName(mode)} · aralık {range} · doğruluk ±({pct.ToString("0.##", CultureInfo.InvariantCulture)} % + {digits} sayım) · 6000 sayım · 3 okuma/s\n{what}";
                string? warn = meter?.Reading.Warning;
                if (meter != null && (meter.MilliAmpFuseBlown || meter.AmpFuseBlown)) warn = "Sigorta atık! Kırmızı uç akım girişindeyken iki noktaya paralel bağlandı (kısa devre). Sağ tık → 'Hasarlı parçaları onar' ile sigortayı değiştirin.";
                if (meter != null && sim.Compiled != null && (mode is MultimeterMode.Resistance or MultimeterMode.Continuity or MultimeterMode.Diode or MultimeterMode.Capacitance) &&
                    meter.IsConnected && Multimeter.CircuitIsEnergized(sim.Compiled.Circuit))
                {
                    warn ??= "Devre enerjili: Ω / diyot / kapasite ölçümü yalnızca enerjisiz devrede doğrudur.";
                }

                if (sim.MeterPart == null && sim.MeterJack != MultimeterJack.VoltOhm && !MultimeterSpec.IsCurrent(mode))
                {
                    warn ??= "Kırmızı uç akım girişinde ama fonksiyon akım değil.";
                }

                warning.Text = warn ?? string.Empty;
                warning.Visibility = warn == null ? Visibility.Collapsed : Visibility.Visible;
            });
            MeterPanel.Children.Add(spec);
            MeterPanel.Children.Add(warning);
            UpdateMeterPanel();
        }

        private void UpdateMeterPanel()
        {
            foreach (var r in _meterRefresh) r();
            Meter.InvalidateVisual();
        }

        // ================================================================ fault diagnosis

        private void OnAnalyze(object sender, RoutedEventArgs e) => RunDiagnosis();

        private void MarkDiagnosisDirty()
        {
            if (DockShowing(2)) RunDiagnosis();
            else DiagHeader.Text = "Arıza tespiti •";
        }

        public void RunDiagnosis()
        {
            List<Diagnosis> list;
            try
            {
                list = W.Simulation.Diagnose();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                DiagSummary.Text = "Analiz yapılamadı: " + ex.Message;
                return;
            }

            DiagList.Children.Clear();
            int errors = list.Count(d => d.Severity == DiagnosisSeverity.Error), warnings = list.Count(d => d.Severity == DiagnosisSeverity.Warning);
            DiagHeader.Text = errors + warnings > 0 ? $"Arıza tespiti ({errors + warnings})" : "Arıza tespiti";
            DiagSummary.Text = list.Count == 0
                ? (W.Simulation.IsActive ? "Sorun bulunmadı: tüm parçalar sınırları içinde." : "Yapısal sorun bulunmadı. Değer sınırlarını görmek için simülasyonu çalıştırın.")
                : $"{errors} hata · {warnings} uyarı · {list.Count - errors - warnings} bilgi" + (W.Simulation.IsActive ? $" · t = {SiValue.Format(W.Simulation.Time, "s")}" : " · statik analiz");
            foreach (var d in list) DiagList.Children.Add(DiagnosisCard(d));
        }

        private UIElement DiagnosisCard(Diagnosis d)
        {
            var brush = (Brush)FindResource(d.Severity == DiagnosisSeverity.Error ? "B.Danger" : d.Severity == DiagnosisSeverity.Warning ? "B.Warning" : "B.TextMuted");
            var card = new Border
            {
                BorderBrush = (Brush)FindResource("B.Border"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 8, 4, 9),
                Background = Brushes.Transparent,
                Cursor = d.Parts.Count > 0 ? Cursors.Hand : Cursors.Arrow,
                ToolTip = d.Parts.Count > 0 ? "Tıkla: şemada göster" : null
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = brush, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) });
            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            text.Children.Add(new TextBlock { Text = d.Title, Foreground = (Brush)FindResource("B.Text"), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = d.Detail, Style = (Style)FindResource("Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Foreground = (Brush)FindResource("B.TextSecondary") });
            if (!string.IsNullOrEmpty(d.Suggestion))
            {
                var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), FontSize = 12 };
                tip.Inlines.Add(new System.Windows.Documents.Run("Öneri: ") { Foreground = (Brush)FindResource("B.Accent") });
                tip.Inlines.Add(new System.Windows.Documents.Run(d.Suggestion) { Foreground = (Brush)FindResource("B.TextSecondary") });
                text.Children.Add(tip);
            }

            grid.Children.Add(text);
            if (d.MarginPercent is double margin && !double.IsNaN(margin))
            {
                var badge = new TextBlock
                {
                    Text = "marj " + (margin > -1000 ? margin.ToString("+0;-0", CultureInfo.InvariantCulture) + " %" : "≪ 0"),
                    FontFamily = (FontFamily)FindResource("F.Mono"),
                    FontSize = 12,
                    Foreground = brush,
                    Margin = new Thickness(10, 2, 0, 0)
                };
                Grid.SetColumn(badge, 2);
                grid.Children.Add(badge);
            }

            card.Child = grid;
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (d.Parts.Count == 0) return;
                if (W.Simulation.IsActive && W.Simulation.Compiled != null)
                {
                    // [path] while it runs: the way the part's current goes round, its parts and nets lit
                    var (parts, nets) = CurrentPath.Trace(W.Schematic.BuildNetlist(W.Library), d.Parts[0], r => W.Simulation.MainDevice(r) is { } dev ? SimulationSession.DeviceCurrent(dev) : double.NaN, IsSupply);
                    foreach (var p in d.Parts) parts.Add(p);
                    Canvas.HighlightPath(parts, nets);
                }
                else Canvas.HighlightParts(d.Parts);
            };
            card.MouseEnter += (_, _) => card.Background = (Brush)FindResource("B.Hover");
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
            return card;
        }

        // ================================================================ Monte Carlo

        private ComboBox _mcNet = null!, _mcMetric = null!;
        private TextBox _mcRuns = null!, _mcDuration = null!, _mcFrom = null!;
        private Button _mcRun = null!;
        private TextBlock _mcProgress = null!;

        private void BuildMonteCarloPanel()
        {
            McPanel.Children.Clear();
            McPanel.Children.Add(new TextBlock { Text = "Monte Carlo tolerans analizi", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
            FrameworkElement Field(string label, FrameworkElement editor)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);
                McPanel.Children.Add(row);
                return editor;
            }

            _mcNet = (ComboBox)Field("Hat", new ComboBox());
            _mcMetric = (ComboBox)Field("Ölçüm", new ComboBox());
            foreach (MonteCarloMetric m in Enum.GetValues(typeof(MonteCarloMetric))) _mcMetric.Items.Add(MonteCarlo.MetricName(m));
            _mcMetric.SelectedIndex = 0;
            _mcDuration = (TextBox)Field("Süre", new TextBox { Text = "20m" });
            _mcFrom = (TextBox)Field("Ölçüm başı", new TextBox { Text = "10m" });
            _mcRuns = (TextBox)Field("Koşu (N)", new TextBox { Text = (W.Schematic.Tolerances?.Runs ?? 50).ToString(CultureInfo.InvariantCulture) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            _mcRun = new Button { Content = "Çalıştır", Style = (Style)FindResource("Btn.Primary"), Padding = new Thickness(14, 3, 14, 3) };
            _mcRun.Click += (_, _) => RunMonteCarlo();
            var cancel = new Button { Content = "İptal", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(6, 0, 0, 0) };
            cancel.Click += (_, _) => _mcCancel?.Cancel();
            buttons.Children.Add(_mcRun);
            buttons.Children.Add(cancel);
            McPanel.Children.Add(buttons);
            _mcProgress = new TextBlock { Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap };
            McPanel.Children.Add(_mcProgress);
            McPanel.Children.Add(Hint("Her koşuda tüm parçalar toleranslarından yeniden çekilir (projenin tohumuyla); seçilen hattaki ölçümün dağılımı çizilir. Firmware çalıştırılmaz."));
            RefreshMonteCarloNets();
        }

        private void RefreshMonteCarloNets()
        {
            if (_mcNet == null) return;
            string? old = _mcNet.SelectedItem as string;
            _mcNet.Items.Clear();
            var netlist = W.Schematic.BuildNetlist(W.Library);
            foreach (var n in netlist.Nets.Where(n => !n.IsGround).OrderBy(n => n.IsAutoNamed ? 1 : 0).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)) _mcNet.Items.Add(n.Name);
            int idx = old != null ? _mcNet.Items.IndexOf(old) : -1;
            if (idx < 0 && W.Simulation.ScopeProbes.FirstOrDefault(p => p?.Kind == ProbeKind.Voltage) is { } probe) idx = _mcNet.Items.IndexOf(probe.Name);
            _mcNet.SelectedIndex = idx >= 0 ? idx : (_mcNet.Items.Count > 0 ? 0 : -1);
        }

        /// <summary>Runs the analysis on a worker thread (on the UI thread when <paramref name="blocking"/>, for snapshots).</summary>
        public void RunMonteCarlo(bool blocking = false)
        {
            if (_mcNet.SelectedItem is not string net)
            {
                _mcProgress.Text = "Önce ölçülecek hattı seçin.";
                return;
            }

            var options = new MonteCarloOptions
            {
                Net = net,
                Metric = (MonteCarloMetric)Math.Max(0, _mcMetric.SelectedIndex),
                Duration = Math.Max(1e-5, SiValue.Parse(_mcDuration.Text, 20e-3)),
                MeasureFrom = Math.Max(0, SiValue.Parse(_mcFrom.Text, 0)),
                Runs = Math.Max(2, Math.Min(2000, (int)SiValue.Parse(_mcRuns.Text, 50))),
                Seed = W.Schematic.Tolerances?.Seed ?? 1,
                MaxStep = Math.Min(AppSettings.Current.MaxStep, 1e-4),
                AmbientC = AppSettings.Current.AmbientTempC
            };
            if (options.MeasureFrom >= options.Duration) options.MeasureFrom = 0;
            var schematic = Schematic.FromJson(Variants.Active(W.Schematic).ToJson());   // [variants] the active alternative, a copy for the other thread
            var lib = W.Library;
            _mcCancel?.Cancel();
            _mcCancel = new System.Threading.CancellationTokenSource();
            var token = _mcCancel.Token;
            _mcRun.IsEnabled = false;
            _mcProgress.Text = $"0 / {options.Runs} koşu…";
            var progress = new Progress<int>(n => _mcProgress.Text = $"{n} / {options.Runs} koşu…");
            void Done(MonteCarloResult result)
            {
                _mcRun.IsEnabled = true;
                Histogram.Unit = "V";
                Histogram.Result = result;
                int ok = result.Values.Count(v => !double.IsNaN(v));
                _mcProgress.Text = result.Cancelled ? $"İptal edildi ({ok} koşu)." : $"{ok} koşu tamamlandı." + (result.Errors.Count > 0 ? $" {result.Errors.Count} hata: {result.Errors[0]}" : string.Empty);
                double spread = result.Max - result.Min;
                McStats.Text = ok == 0
                    ? string.Empty
                    : $"{options.Net} · {MonteCarlo.MetricName(options.Metric)}   nominal {SiValue.Format(result.Nominal, "V", 4)}   ortalama {SiValue.Format(result.Mean, "V", 4)}   σ {SiValue.Format(result.StdDev, "V", 3)}\n" +
                      $"en düşük {SiValue.Format(result.Min, "V", 4)} (koşu {result.MinRun})   en yüksek {SiValue.Format(result.Max, "V", 4)} (koşu {result.MaxRun})   yayılım {SiValue.Format(spread, "V", 3)}" +
                      (Math.Abs(result.Nominal) > 1e-12 ? $" ({spread / Math.Abs(result.Nominal) * 100:0.0} %)".Replace(',', '.') : string.Empty);
            }

            if (blocking)
            {
                Done(MonteCarlo.Run(schematic, lib, options, token));
                return;
            }

            System.Threading.Tasks.Task.Run(() => MonteCarlo.Run(schematic, lib, options, token, progress))
                .ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        _mcRun.IsEnabled = true;
                        _mcProgress.Text = "Analiz başarısız: " + t.Exception.GetBaseException().Message;
                        return;
                    }

                    Done(t.Result);
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ================================================================ snapshot hooks

        /// <summary>--fault before the run: break the loaded example the way a beginner would, or switch tolerances on.</summary>
        public void PrepareSnapshotCircuit(string? fault)
        {
            W.BeginChange();
            var sch = W.Schematic;
            switch (fault)
            {
                case "layers":
                case "path:D1":
                    // [layers] the LED's resistor much too small: the LED is driven far past its current and the resistor past its power
                    foreach (var p in sch.Parts.Where(p => p.PartId == "resistor")) p.Parameters["resistance"] = "120";
                    break;
                case "burn":
                case "diag":
                    foreach (var p in sch.Parts)
                    {
                        string preset = p.PresetId ?? string.Empty;
                        // Electrolytic soldered the wrong way round.
                        if (preset.StartsWith("ecap-", StringComparison.Ordinal)) p.Rotation = (p.Rotation + 180) % 360;
                        // LED resistor far too small; load resistor of too low a power rating.
                        if (preset == "res-1000") p.Parameters["resistance"] = "4.7";
                        if (preset == "res-47")
                        {
                            p.Parameters["resistance"] = "22";
                            p.Parameters["power"] = "0.25";
                        }
                    }

                    W.Simulation.SetMeterLead(true, null, null);
                    break;
                case "fuse":
                    // A fuse instead of the LED resistor and a 1 Ω "load" instead of the LED: the fuse blows.
                    foreach (var p in sch.Parts)
                    {
                        if (p.PresetId == "res-470")
                        {
                            p.PartId = "fuse";
                            p.PresetId = "fuse-500mA";
                            p.Parameters.Clear();
                        }
                        else if (p.PartId == "led")
                        {
                            p.PartId = "resistor";
                            p.PresetId = "res-1R-1W";
                            p.Parameters.Clear();
                        }
                    }

                    break;
                case "instruments":
                {
                    void Tie(SchematicPart part, string pin, string net)
                    {
                        var pd = W.Library.Get(part.PartId).FindPin(pin)!;
                        sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pd.X, pd.Y) });
                    }

                    var dmm = sch.AddPart(W.Library, "dmm", new GridPoint(28, -2));
                    Tie(dmm, "VΩ", "ÇIKIŞ");
                    Tie(dmm, "COM", "GND");
                    var psu = sch.AddPart(W.Library, "bench-psu-5v-100ma", new GridPoint(-14, 14));
                    var load = sch.AddPart(W.Library, "res-22", new GridPoint(-4, 14), 270);
                    Tie(psu, "+", "PSU");
                    Tie(psu, "-", "GND");
                    Tie(load, "A", "PSU");
                    Tie(load, "B", "GND");
                    var amm = sch.AddPart(W.Library, "dmm-ampere", new GridPoint(44, -2));
                    Tie(amm, "mA", "5V_PSU");
                    Tie(amm, "COM", "GND");
                    break;
                }

                case "decode":
                case "decode-pick":
                {
                    // [decode] A 4800 Hz square wave reads, at 9600 baud 8N1, as an endless run of 'U' (0x55).
                    var gen = sch.AddPart(W.Library, "siggen-square", new GridPoint(-30, 22));
                    gen.Parameters["frequency"] = "4800";
                    var r = sch.AddPart(W.Library, "res-1000", new GridPoint(-14, 22));
                    void At(SchematicPart part, int index, string net)
                    {
                        var pd = W.Library.Get(part.PartId).Pins[index];
                        sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pd.X, pd.Y) });
                    }

                    At(gen, 0, "TX");
                    At(gen, 1, "GND");
                    At(r, 0, "TX");
                    At(r, 1, "GND");
                    break;
                }

                case "ac":
                {
                    // [ac] An RC low-pass driven by a sine generator, its frequency response computed in the tab.
                    void Pin(SchematicPart part, int index, string net)
                    {
                        var pd = W.Library.Get(part.PartId).Pins[index];
                        sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pd.X, pd.Y) });
                    }

                    var gen = sch.AddPart(W.Library, "siggen-sine", new GridPoint(-34, 22));
                    var r = sch.AddPart(W.Library, "res-1000", new GridPoint(-18, 22));
                    var cap = sch.AddPart(W.Library, "cap-100n", new GridPoint(-6, 26), 270);
                    Pin(gen, 0, "GIRIS");
                    Pin(gen, 1, "GND");
                    Pin(r, 0, "GIRIS");
                    Pin(r, 1, "CIKIS");
                    Pin(cap, 0, "CIKIS");
                    Pin(cap, 1, "GND");
                    ShowDock(7);
                    RefreshAcChoices();
                    if (_acSource?.Items.Cast<string>().FirstOrDefault(i => i.StartsWith(gen.RefDes, StringComparison.OrdinalIgnoreCase)) is { } src) _acSource.SelectedItem = src;
                    if (_acOut != null) _acOut.SelectedItem = "CIKIS";
                    RunAc();
                    break;
                }

                case "montecarlo":
                case "tolerance":
                    sch.Tolerances = new ToleranceSettings { Mode = fault == "montecarlo" ? ToleranceMode.MonteCarlo : ToleranceMode.Realistic, Seed = 7, Runs = 80 };
                    break;
            }

            W.CommitChange();   // like an edit: the empty-sheet hint, the status bar and the inspector see the circuit
            Canvas.Refresh();
            Canvas.FitToContent();
        }

        /// <summary>--fault for the schematic page: set up a broken circuit or an instrument before the run.</summary>
        private void PrepareSnapshotFault(string? fault)
        {
            var sim = W.Simulation;
            switch (fault)
            {
                case "decode":
                case "decode-pick":
                    sim.ClearProbes();
                    sim.AddVoltageProbe("TX", 0);
                    sim.Scope.DecodeProtocol = "UART";
                    sim.Scope.DecodeA = 0;
                    sim.Scope.DecodeBaud = 9600;
                    ShowDock(0);
                    break;
                case "scope-power":
                    // [scope] the supply's voltage and the motor's current: their product is the power the motor branch draws
                    sim.ClearProbes();
                    sim.AddVoltageProbe("VBAT", 0);
                    if (W.Schematic.Parts.FirstOrDefault(p => W.Library.Find(p.PartId)?.Symbol == SymbolKind.Motor) is { } motor) sim.AddCurrentProbe(motor.RefDes, 1);
                    ShowDock(0);
                    break;
                case "scope":
                    sim.ClearProbes();
                    foreach (var name in new[] { "GİRİŞ", "ÇIKIŞ", "VOUT" })
                    {
                        if (sim.Compiled?.NodeByNet.ContainsKey(name) == true) sim.AddVoltageProbe(name);
                    }

                    ShowDock(0);
                    break;
                case "dmm":
                {
                    var net = sim.Compiled?.Netlist.Nets.Where(n => !n.IsGround && n.Connections.Count >= 2)
                        .OrderByDescending(n => n.Name == "ÇIKIŞ" || n.Name == "5V" ? 1 : 0).FirstOrDefault();
                    var gnd = sim.Compiled?.Netlist.Nets.FirstOrDefault(n => n.IsGround);
                    if (net != null && gnd != null)
                    {
                        sim.SetMeterLead(true, net.Name, WireAnchor(net.Name));
                        sim.SetMeterLead(false, gnd.Name, WireAnchor(gnd.Name));
                    }

                    ShowDock(1);
                    BuildMeterPanel();
                    break;
                }

                case "diag":
                    ShowDock(2);
                    break;
                case "montecarlo":
                    ShowDock(3);
                    break;
                case "library":
                    LibraryToggle.IsChecked = true;
                    OnToggleLibrary(LibraryToggle, new RoutedEventArgs());
                    break;
            }
        }

        /// <summary>A point on the first wire of a net (where a snapshot clips a lead).</summary>
        private GridPoint? WireAnchor(string net)
        {
            var netlist = W.Schematic.BuildNetlist(W.Library);
            for (int i = 0; i < W.Schematic.Wires.Count && i < netlist.WireNets.Count; i++)
            {
                if (netlist.WireNets[i] != net) continue;
                var pts = W.Schematic.Wires[i].Points;
                if (pts.Count >= 2) return new GridPoint((pts[0].X + pts[1].X) / 2, (pts[0].Y + pts[1].Y) / 2);
            }

            int l = netlist.LabelNets.IndexOf(net);
            return l >= 0 ? W.Schematic.Labels[l].Position : null;
        }

        /// <summary>--snapshot: after the simulated time ran, put instruments and panels in their final state.</summary>
        /// <summary>[path] A supply part: on every current loop it touches.</summary>
        private bool IsSupply(string refDes) => W.Simulation.Compiled?.DevicesOf(refDes).Any(d => d is VoltageSource or Battery or CurrentSource or BenchSupply) == true;

        public void FinishSnapshot(string? fault)
        {
            var sim = W.Simulation;
            if (fault != null && fault.StartsWith("path:", StringComparison.Ordinal))
            {
                // [path] --fault path:D1 (with the layers circuit): that part's current path lit, as a diagnosis click does
                var (parts, nets) = CurrentPath.Trace(W.Schematic.BuildNetlist(W.Library), fault.Substring(5), r => sim.MainDevice(r) is { } dev ? SimulationSession.DeviceCurrent(dev) : double.NaN, IsSupply);
                Canvas.HighlightPath(parts, nets);
                return;
            }

            if (fault != null && fault.StartsWith("live:", StringComparison.Ordinal))
            {
                // [live] --fault live:Q1: that part selected while the circuit runs, its live values in the inspector
                string refDes = fault.Substring(5);
                if (W.Schematic.Parts.FirstOrDefault(p => p.RefDes == refDes) is { } livePart)
                {
                    Canvas.SelectItems(new object[] { livePart });
                    BuildInspector();
                    Inspector.UpdateLayout();
                    RefreshLive();
                }

                return;
            }

            switch (fault)
            {
                case "decode":
                case "decode-pick":
                    sim.Scope.Autoset(sim.Time);
                    sim.Scope.SecondsPerDiv = 5e-4;   // five UART frames across the screen
                    _scopePanel.Refresh();
                    for (int i = 0; i < 30; i++) sim.StepOnce(1e-3);
                    if (fault == "decode-pick")
                    {
                        // [ref] [decode] the screen kept as the reference, a little more run over it, a byte picked
                        sim.Scope.KeepReference();
                        sim.Scope.MaskDivisions = 0.25;   // [mask] the band round it
                        for (int i = 0; i < 5; i++) sim.StepOnce(1e-3);
                        sim.Scope.Acquire(sim.Time);
                        _scopePanel.PickDecodedForSnapshot(2);
                    }

                    break;
                case "scope-power":
                    sim.Scope.Autoset(sim.Time);
                    sim.Scope.MathFunction = ScopeMathFunction.Multiply;
                    sim.Scope.MathSourceA = 0;
                    sim.Scope.MathSourceB = 1;
                    sim.Scope.MathAutoScale = true;
                    Scope.MeasureChannel = 4;   // the measurements of the math: its mean is the average power
                    _scopePanel.Refresh();
                    for (int i = 0; i < 40; i++) sim.StepOnce(1e-3);
                    Scope.ForceAcquire();
                    break;
                case "scope":
                    sim.Scope.Autoset(sim.Time);
                    _scopePanel.Refresh();
                    Scope.CursorMode = ScopeCursorMode.Time;
                    Scope.Cursors[0] = 5.0;
                    Scope.Cursors[1] = 7.0;
                    for (int i = 0; i < 40; i++) sim.StepOnce(1e-3);
                    Scope.ForceAcquire();
                    break;
                case "dmm":
                    UpdateMeterPanel();
                    break;
                case "serial":
                    DockSerial.IsChecked = true;   // [esp32hw] what the boards printed
                    break;
                case "log":
                    DockLog.IsChecked = true;      // the run's events
                    break;
                case "burn":
                case "diag":
                case "fuse":
                    Canvas.AdvanceEffects(1.4);
                    if (fault == "diag") RunDiagnosis();
                    break;
                case "tolerance":
                {
                    var r = W.Schematic.Parts.FirstOrDefault(p => p.PartId == "resistor");
                    if (r != null) Canvas.HighlightParts(new[] { r.RefDes });
                    break;
                }

                case "montecarlo":
                    RefreshMonteCarloNets();
                    int i0 = _mcNet.Items.IndexOf("ÇIKIŞ");
                    if (i0 >= 0) _mcNet.SelectedIndex = i0;
                    _mcMetric.SelectedIndex = (int)MonteCarloMetric.PeakToPeak;
                    _mcRuns.Text = "80";
                    RunMonteCarlo(blocking: true);
                    var r1 = W.Schematic.Parts.FirstOrDefault(p => p.PartId == "resistor");
                    if (r1 != null) Canvas.HighlightParts(new[] { r1.RefDes });
                    break;
            }
        }
    }
}
