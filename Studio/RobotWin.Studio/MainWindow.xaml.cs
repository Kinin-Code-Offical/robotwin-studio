using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Branding;
using RobotWin.Studio.Pages;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;
using AutoPlacer = RobotTwin.CoreSim.Pcb.AutoPlacer;
using AutoRouter = RobotTwin.CoreSim.Pcb.AutoRouter;
using BoardDocument = RobotTwin.CoreSim.Pcb.BoardDocument;

namespace RobotWin.Studio
{
    public partial class MainWindow : Window
    {
        private const string ProjectFilter = "RobotWin projesi (*.rwproj)|*.rwproj|Tüm dosyalar (*.*)|*.*";
        private const string WorldFilter = "RobotWin dünyası (*.rwworld)|*.rwworld|Tüm dosyalar (*.*)|*.*";

        public const string OpenFilter =
            "RobotWin dosyaları (*.rwproj, *.rwworld, *.rwsim, *.rwpart, *.rwlib)|*.rwproj;*.rwworld;*.rwsim;*.rwpart;*.rwlib|" +
            "RobotWin projesi (*.rwproj)|*.rwproj|RobotWin dünyası (*.rwworld)|*.rwworld|Simülasyon kaydı (*.rwsim)|*.rwsim|Parça (*.rwpart)|*.rwpart|" +
            "Parça kütüphanesi (*.rwlib)|*.rwlib|Tüm dosyalar (*.*)|*.*";

        private readonly HomePage _home;
        private readonly WorldPage _world;
        private readonly SchematicPage _schematic;
        private readonly PcbPage _pcb;
        private readonly ModelPage _model;
        private readonly CodePage _code;
        private readonly LibraryPage _library;
        private readonly SettingsPage _settings;
        private readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();
        private readonly Dictionary<string, string> _pageNames = new Dictionary<string, string>
        {
            ["world"] = "Dünya", ["schematic"] = "Devre", ["pcb"] = "PCB", ["code"] = "Kod", ["assembly"] = "Montaj", ["model"] = "Modelle",
            ["library"] = "Kütüphane", ["home"] = "Başla", ["settings"] = "Ayarlar"
        };

        private readonly Dictionary<string, Window> _detached = new Dictionary<string, Window>();
        private string _current = "home";
        private string? _second;
        private bool _forceClose;
        private bool _focusMode;
        private bool _fullScreen;
        private WindowState _stateBeforeFull;
        private WindowBehavior? _behavior;
        private readonly PerfOverlay _perf = new PerfOverlay();

        /// <summary>--overlap-report: interpenetrating parts of every world template.</summary>
        public string OverlapReportAllTemplates()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in Examples.WorldTemplates.All)
            {
                NewWorld(t.Build());
                Navigate("world");
                UpdateLayout();
                var list = _world.OverlapReport();
                int problems = list.Count(l => !l.StartsWith("geo ", StringComparison.Ordinal));
                sb.AppendLine($"{t.Id}: {(problems == 0 ? "temiz" : problems + " sorun")}");
                foreach (var l in list) sb.AppendLine("    " + l);
            }

            return sb.ToString();
        }

        /// <summary>--bench: run / stop the active world.</summary>
        public void RunWorldForBench() => _world.ToggleRun();

        public void StopWorldForBench() => _world.StopSimulation();

        /// <summary>FPS / frame-time overlay (Görünüm → Performans göstergesi, Ctrl+Shift+F).</summary>
        public void ShowPerf(bool on)
        {
            _perf.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            AppSettings.Current.ShowPerf = on;
            AppSettings.Current.Save();
        }

        private static Workspace W => Workspace.Current;

        public MainWindow()
        {
            InitializeComponent();
            LogoImage.Source = Logo.Image;
            Icon = Logo.Render(64);

            _home = new HomePage(this);
            _world = new WorldPage(this);
            _schematic = new SchematicPage();
            _pcb = new PcbPage();
            _model = new ModelPage(this);
            _code = new CodePage();
            _library = new LibraryPage(this);
            _settings = new SettingsPage();
            _pages["home"] = _home;
            _pages["world"] = _world;
            _pages["schematic"] = _schematic;
            _pages["pcb"] = _pcb;
            _pages["model"] = _model;
            _pages["code"] = _code;
            _pages["library"] = _library;
            _pages["settings"] = _settings;
            PageHost.Content = _home;
            SecondPageBox.ItemsSource = _pageNames.Where(kv => kv.Key != "home" && kv.Key != "settings").Select(kv => new ComboBoxItem { Content = kv.Value, Tag = kv.Key }).ToList();

            Tabs.ItemsSource = W.Documents;
            W.PreviewProvider = RenderPreview;
            W.PropertyChanged += OnWorkspaceChanged;
            W.ActiveDocumentChanged += (_, _) => OnActiveDocumentChanged();
            W.Simulation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State)) UpdateSimDot();
            };
            W.DocumentChanged += (_, _) => UpdateStats();
            W.WorldChanged += (_, _) => UpdateStats();
            UpdateTitle();
            UpdateStats();
            UpdateSimDot();
            StatusText.Text = W.Status;

            StateChanged += (_, _) => OnStateChanged();
            SourceInitialized += (_, _) => ApplyWindowsCorners();
            _behavior = new WindowBehavior(this, MaxButton);
            Activated += (_, _) => AppTitle.Opacity = 1;
            Deactivated += (_, _) => AppTitle.Opacity = 0.55;
            BuildMenuBar();
            Perf.Start();
            Grid.SetRowSpan(_perf, 3);
            Panel.SetZIndex(_perf, 900);
            Root.Children.Add(_perf);
            _perf.Visibility = AppSettings.Current.ShowPerf ? Visibility.Visible : Visibility.Collapsed;
            Bind(Key.F, ModifierKeys.Control | ModifierKeys.Shift, () => ShowPerf(_perf.Visibility != Visibility.Visible));
            Bind(Key.OemComma, ModifierKeys.Control, () => Navigate("settings"));
            SetupTabDrag();

            Bind(Key.N, ModifierKeys.Control, () => NewProject());
            Bind(Key.N, ModifierKeys.Control | ModifierKeys.Shift, () => NewWorld());
            Bind(Key.O, ModifierKeys.Control, () => OnOpen(this, new RoutedEventArgs()));
            Bind(Key.S, ModifierKeys.Control, () => OnSave(this, new RoutedEventArgs()));
            Bind(Key.S, ModifierKeys.Control | ModifierKeys.Shift, () => SaveAs());
            Bind(Key.W, ModifierKeys.Control, () => CloseTab(W.ActiveDocument));
            Bind(Key.Tab, ModifierKeys.Control, () => CycleTab(1));
            Bind(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, () => CycleTab(-1));
            Bind(Key.F11, ModifierKeys.None, ToggleFullScreen);
            Bind(Key.OemBackslash, ModifierKeys.Control, ToggleSplit);
            Bind(Key.Oem5, ModifierKeys.Control, ToggleSplit);
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && _fullScreen)
                {
                    ToggleFullScreen();
                    e.Handled = true;
                }
            };
            Grid.SetRowSpan(_palette, 3);
            Panel.SetZIndex(_palette, 1000);
            Root.Children.Add(_palette);
            Bind(Key.K, ModifierKeys.Control, OpenPalette);
            Bind(Key.P, ModifierKeys.Control | ModifierKeys.Shift, OpenPalette);
            ApplyStartupSettings();
            SetupRail();
            SetupHelp();
            AppSettings.Changed += (_, _) => ApplyUiScale(AppSettings.Current.UiScale);
            _autosave.Tick += (_, _) => Autosave();
            _autosave.Start();
        }

        // ================================================================ command palette

        private readonly CommandPalette _palette = new CommandPalette();

        private void OnPalette(object sender, RoutedEventArgs e) => OpenPalette();

        public void OpenPalette()
        {
            if (_palette.IsOpen) _palette.Close();
            else _palette.Open(PaletteCommands);
        }

        private IEnumerable<PaletteCommand> PaletteCommands()
        {
            const string go = "Git", file = "Dosya", view = "Görünüm", tab = "Sekmeler", sim = "Simülasyon";
            foreach (var kv in _pageNames)
            {
                string page = kv.Key;
                yield return new PaletteCommand(go, kv.Value, () => Navigate(page));
            }

            yield return new PaletteCommand(file, "Yeni devre / kart", () => NewProject(), "Ctrl+N");
            yield return new PaletteCommand(file, "Yeni dünya", () => NewWorld(), "Ctrl+Shift+N");
            yield return new PaletteCommand(file, "Aç…", () => OnOpen(this, new RoutedEventArgs()), "Ctrl+O");
            yield return new PaletteCommand(file, "Kaydet", () => OnSave(this, new RoutedEventArgs()), "Ctrl+S");
            yield return new PaletteCommand(file, "Farklı kaydet…", () => SaveAs(), "Ctrl+Shift+S");
            yield return new PaletteCommand(file, "Simülasyon kaydını kaydet (.rwsim)", SaveRecording);
            yield return new PaletteCommand(file, "Yedek klasörünü aç", () => RevealInExplorer(BackupFolder, true));
            yield return new PaletteCommand(tab, "Sekmeyi kapat", () => CloseTab(W.ActiveDocument), "Ctrl+W");
            yield return new PaletteCommand(tab, "Diğer sekmeleri kapat", () => CloseOthers(W.ActiveDocument));
            yield return new PaletteCommand(tab, "Sonraki sekme", () => CycleTab(1), "Ctrl+Tab");
            foreach (var d in W.Documents.ToList())
            {
                var doc = d;
                yield return new PaletteCommand(tab, "Sekmeye geç: " + d.BaseTitle, () => W.Activate(doc), null, d.KindText);
            }

            yield return new PaletteCommand(view, "Bölünmüş görünüm aç/kapat", ToggleSplit, "Ctrl+\\");
            yield return new PaletteCommand(view, "Bu sayfayı ayrı pencereye al", () => Detach(_current));
            yield return new PaletteCommand(view, "Odak modu", () => SetFocusMode(!_focusMode));
            yield return new PaletteCommand(view, "Tam ekran", ToggleFullScreen, "F11");
            foreach (var c in HelpPaletteCommands()) yield return c;
            foreach (var (label, scale) in new[] { ("Arayüz ölçeği %90", 0.9), ("Arayüz ölçeği %100", 1.0), ("Arayüz ölçeği %125", 1.25) })
            {
                double sc = scale;
                yield return new PaletteCommand(view, label, () =>
                {
                    AppSettings.Current.UiScale = sc;
                    AppSettings.Current.Save();
                });
            }

            yield return new PaletteCommand(sim, "Devre simülasyonunu başlat/duraklat", () =>
            {
                Navigate("schematic");
                if (W.Simulation.State == SimState.Running) W.Simulation.Pause();
                else if (W.Simulation.State == SimState.Paused) W.Simulation.Resume();
                else W.Simulation.Start();
            }, "F5");
            yield return new PaletteCommand(sim, "Simülasyonu durdur", () => W.Simulation.Stop(), "Shift+F5");
            foreach (var (title, cmd, keys) in Pages.SchematicPage.PaletteEntries)
            {
                var c = cmd;
                yield return new PaletteCommand("Devre şeması", title, () =>
                {
                    Navigate("schematic");
                    _schematic.RunEditorCommand(c);
                }, keys);
            }
            foreach (var t in Examples.WorldTemplates.All)
            {
                var template = t;
                yield return new PaletteCommand("Dünya şablonu", t.Title, () => NewWorld(template.Build()), null, t.Description);
            }

            foreach (var ex in Examples.ExampleProjects.All)
            {
                var example = ex;
                yield return new PaletteCommand("Örnek", ex.Title, () =>
                {
                    W.Load(example.Build(), null);
                    Navigate(example.Page);
                });
            }

            foreach (var path in RecentFiles.Load())
            {
                var p = path;
                yield return new PaletteCommand("Son dosya", Path.GetFileName(path), () => OpenFile(p), null, Path.GetDirectoryName(path));
            }

            foreach (var preset in W.Library.Presets)
            {
                var id = preset.Id;
                yield return new PaletteCommand("Parça ekle", preset.Name, () => PlacePart(id), null, preset.Category);
            }
        }

        // ================================================================ tab menu

        private void OnTabMenu(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not OpenDocument doc) return;
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Kapat", "Ctrl+W", () => CloseTab(doc)));
            menu.Items.Add(MenuItem("Diğerlerini kapat", null, () => CloseOthers(doc)));
            menu.Items.Add(MenuItem("Sağdakileri kapat", null, () =>
            {
                int i = W.Documents.IndexOf(doc);
                foreach (var d in W.Documents.Skip(i + 1).ToList()) CloseTab(d);
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(doc.IsEmbedded ? "Dünyaya işle" : "Kaydet", "Ctrl+S", () =>
            {
                W.Activate(doc);
                OnSave(this, new RoutedEventArgs());
            }));
            menu.Items.Add(MenuItem(doc.IsEmbedded ? "Ayrı proje olarak kaydet…" : "Farklı kaydet…", "Ctrl+Shift+S", () =>
            {
                W.Activate(doc);
                SaveAs();
            }));
            if (doc.FilePath != null)
            {
                string path = doc.FilePath;
                menu.Items.Add(MenuItem("Klasörde göster", null, () => RevealInExplorer(path, false)));
                menu.Items.Add(MenuItem("Dosya yolunu kopyala", null, () => Clipboard.SetText(path)));
            }

            if (doc.Kind == DocumentKind.Design && !doc.IsEmbedded)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Bu devreyi yeni bir dünyaya koy", null, () =>
                {
                    W.Activate(doc);
                    W.FlushActiveDesign();
                    var world = new WorldDocument { Title = doc.BaseTitle + " dünyası" };
                    world.AddBoard(world.AddDesign(doc.Package, doc.FilePath), new WorldPose());
                    NewWorld(world);
                }));
            }

            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void CloseOthers(OpenDocument keep)
        {
            foreach (var d in W.Documents.Where(d => d != keep && d.OwnerWorld != keep && keep.OwnerWorld != d).ToList()) CloseTab(d);
        }

        private static void RevealInExplorer(string path, bool folder)
        {
            try
            {
                if (folder) Directory.CreateDirectory(path);
                string args = folder ? "\"" + path + "\"" : "/select,\"" + path + "\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                W.Status = "Açılamadı: " + ex.Message;
            }
        }

        // ================================================================ autosave

        private readonly System.Windows.Threading.DispatcherTimer _autosave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private DateTime _lastAutosave = DateTime.Now;

        /// <summary>Backup folder for unsaved work (the user's own files are never overwritten).</summary>
        public static string BackupFolder => Path.Combine(AppSettings.Current.ProjectFolder, "Yedekler");

        private void Autosave()
        {
            int minutes = AppSettings.Current.AutosaveMinutes;
            if (minutes <= 0 || (DateTime.Now - _lastAutosave).TotalMinutes < minutes) return;
            _lastAutosave = DateTime.Now;
            int saved = 0;
            try
            {
                Directory.CreateDirectory(BackupFolder);
                foreach (var doc in W.Documents.Where(d => d.IsDirty && !d.IsEmbedded).ToList())
                {
                    string safe = new string(doc.BaseTitle.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
                    if (doc.Kind == DocumentKind.World && doc.World != null)
                    {
                        doc.World.Save(Path.Combine(BackupFolder, $"{safe}-{stamp}{WorldDocument.Extension}"));
                    }
                    else
                    {
                        if (doc == W.DesignDocument) W.FlushActiveDesign();
                        doc.Package.Save(Path.Combine(BackupFolder, $"{safe}-{stamp}{RobotWinFormats.ProjectExtension}"));
                    }

                    saved++;
                }

                // Keep the newest 30 backups.
                foreach (var old in new DirectoryInfo(BackupFolder).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).Skip(30)) old.Delete();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                W.Status = "Otomatik yedek alınamadı: " + ex.Message;
                return;
            }

            if (saved > 0) W.Status = $"Otomatik yedek: {saved} sekme → {BackupFolder}";
        }

        private void Bind(Key key, ModifierKeys mods, Action action) => InputBindings.Add(new KeyBinding(new RelayCommand(action), key, mods));

        // ================================================================ navigation

        public string CurrentPage => _current;

        public void Navigate(string page)
        {
            if (page == "assembly")
            {
                OpenProjectAssembly();   // [assembly] the real assembly, not a vehicle template
                return;
            }

            if (!_pages.ContainsKey(page)) page = "home";
            if (_detached.TryGetValue(page, out var floating))
            {
                floating.Activate();
                UpdateNav(page);
                return;
            }

            if (_second == page)
            {
                // Already visible on the right: swap the two sides.
                SwapSplit();
                return;
            }

            _current = page;
            var target = _pages[page];
            PageHost.Content = target;
            UpdateNav(page);
            Shown(page);
        }

        private void UpdateNav(string page)
        {
            Rail.Active = page;
            Rail.Refresh();
        }

        private readonly System.Windows.Threading.DispatcherTimer _railTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private DateTime _ercAt;
        private (int Errors, int Warnings) _erc;

        /// <summary>Wires the design-to-real line: navigation, shortcuts and the live status of every stage.</summary>
        private bool _snapshotRail;

        /// <summary>Expands the rail for a snapshot without saving the preference.</summary>
        public void ExpandRailForSnapshot()
        {
            _snapshotRail = true;
            Rail.IsExpanded = true;
        }

        private void SetupRail()
        {
            Rail.Navigate += Navigate;
            Rail.StatusProvider = StageStatus;
            Rail.IsExpanded = AppSettings.Current.NavExpanded;
            Rail.ExpandedChanged += (_, _) =>
            {
                if (_snapshotRail) return;
                AppSettings.Current.NavExpanded = Rail.IsExpanded;
                AppSettings.Current.Save();
            };
            Bind(Key.D0, ModifierKeys.Control, () => Navigate("home"));
            Bind(Key.D1, ModifierKeys.Control, () => Navigate("schematic"));
            Bind(Key.D2, ModifierKeys.Control, () => Navigate("pcb"));
            Bind(Key.D3, ModifierKeys.Control, () => Navigate("code"));
            Bind(Key.D4, ModifierKeys.Control, () => Navigate("model"));
            Bind(Key.D5, ModifierKeys.Control, () => Navigate("assembly"));
            Bind(Key.D6, ModifierKeys.Control, () => Navigate("world"));
            Bind(Key.D7, ModifierKeys.Control, () => Navigate("library"));
            Bind(Key.B, ModifierKeys.Control, () => Rail.IsExpanded = !Rail.IsExpanded);
            _railTimer.Tick += (_, _) => Rail.Refresh();
            _railTimer.Start();
        }

        private RailStatus StageStatus(string stage)
        {
            var sch = W.Schematic;
            var lib = W.Library;
            switch (stage)
            {
                case "schematic":
                {
                    if ((DateTime.UtcNow - _ercAt).TotalSeconds > 3)
                    {
                        _ercAt = DateTime.UtcNow;
                        try
                        {
                            var v = RobotTwin.CoreSim.Design.ElectricalRulesChecker.Check(sch, lib);
                            _erc = (v.Count(x => x.Severity == RobotTwin.CoreSim.Design.ErcSeverity.Error), v.Count(x => x.Severity == RobotTwin.CoreSim.Design.ErcSeverity.Warning));
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException || ex is KeyNotFoundException)
                        {
                            _erc = (0, 0);
                        }
                    }

                    bool running = W.Simulation.State == SimState.Running;
                    string text = sch.Parts.Count == 0 ? "Boş şema" : $"{sch.Parts.Count} parça · {sch.Wires.Count + sch.Labels.Count} bağlantı";
                    if (_erc.Errors > 0) text += $" · {_erc.Errors} ERC hatası";
                    else if (_erc.Warnings > 0) text += $" · {_erc.Warnings} uyarı";
                    if (running) text = "Simülasyon çalışıyor · " + text;
                    var sev = sch.Parts.Count == 0 ? RailSeverity.None : _erc.Errors > 0 ? RailSeverity.Error : _erc.Warnings > 0 ? RailSeverity.Warning : RailSeverity.Ok;
                    return new RailStatus(text, sev, running);
                }

                case "pcb":
                {
                    var b = sch.Board;
                    if (b == null || b.Components.Count == 0)
                    {
                        return new RailStatus(sch.Parts.Count == 0 ? "Kart yok" : "Yerleşim bekliyor", sch.Parts.Count == 0 ? RailSeverity.None : RailSeverity.Warning);
                    }

                    return new RailStatus($"{b.Width:0}×{b.Height:0} mm · {b.Components.Count} parça · {b.Tracks.Count} yol", b.Tracks.Count > 0 ? RailSeverity.Ok : RailSeverity.Warning);
                }

                case "code":
                {
                    int boards = sch.Parts.Count(p => lib.Find(p.PartId)?.Symbol == RobotTwin.CoreSim.Design.SymbolKind.Board);
                    return new RailStatus(boards == 0 ? "Programlanabilir kart yok" : $"{boards} programlanabilir kart", boards == 0 ? RailSeverity.None : RailSeverity.Ok);
                }

                case "model":
                    return new RailStatus("Parça ve gövde modelle, dünyaya gönder");

                case "assembly":
                {
                    // [assembly] the circuit's parts as real 3D parts in a world
                    int physical = sch.Parts.Count(p => W.Library.Find(p.PartId) is { } d && d.Symbol != RobotTwin.CoreSim.Design.SymbolKind.Ground && d.Pins.Count > 0);
                    return new RailStatus(physical == 0 ? "Devrede parça yok" : $"{physical} parça gerçek montaja", physical == 0 ? RailSeverity.None : RailSeverity.Ok);
                }

                case "world":
                {
                    var world = W.ActiveWorld ?? W.Documents.FirstOrDefault(d => d.Kind == DocumentKind.World)?.World;
                    if (world == null) return new RailStatus("Açık dünya yok");
                    var (state, time) = _world.SimulationState;
                    bool live = state == SimState.Running;
                    string text = live ? $"Çalışıyor · t = {time:0.0} s" : state == SimState.Paused ? $"Duraklatıldı · t = {time:0.0} s" : $"{world.Objects.Count} nesne · {world.Cables.Count} kablo";
                    return new RailStatus(text, state == SimState.Paused ? RailSeverity.Warning : RailSeverity.None, live);
                }
            }

            return new RailStatus(string.Empty);
        }

        private void Shown(string page)
        {
            switch (page)
            {
                case "pcb": _pcb.OnShown(); break;
                case "code": _code.OnShown(); break;
                case "home": _home.OnShown(); break;
                case "world": _world.OnShown(); break;
            }
        }

        /// <summary>Switches to the schematic with a part selected and centred (PCB "show in schematic").</summary>
        public void ShowPartInSchematic(string refDes)
        {
            Navigate("schematic");
            _schematic.RevealPart(refDes);
        }

        /// <summary>Switches to the schematic and starts placing a library part.</summary>
        public void PlacePart(string presetId)
        {
            if (W.ActiveDocument.Kind == DocumentKind.World)
            {
                Navigate("world");
                _world.AddComponent(presetId);
                return;
            }

            Navigate("schematic");
            _schematic.BeginPlace(presetId);
        }


        private void OnActiveDocumentChanged()
        {
            UpdateTitle();
            UpdateStats();
            var doc = W.ActiveDocument;
            if (doc.Kind == DocumentKind.World)
            {
                if (_current != "world" && _second != "world") Navigate("world");
                else _world.OnShown();
            }
            else if (_current == "world" || _current == "home")
            {
                Navigate("schematic");
            }
            else
            {
                Shown(_current);
            }

            Tabs.UpdateLayout();
        }

        /// <summary>Cross-probe from the schematic: opens the PCB page centred on a component.</summary>
        public void ShowPartInPcb(string refDes)
        {
            CrossProbe.ProbePart(this, refDes);
            Navigate("pcb");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var comp = W.Schematic.Board?.Components.FirstOrDefault(c => string.Equals(c.RefDes, refDes, StringComparison.OrdinalIgnoreCase));
                if (comp == null)
                {
                    W.Status = $"{refDes} PCB'de yok (önce PCB sayfasında kartı güncelleyin)";
                    return;
                }

                _pcb.Canvas.ZoomTo(comp.X, comp.Y);
                W.Status = $"PCB: {refDes} ({comp.X:0.0}, {comp.Y:0.0}) mm";
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Used by --snapshot: puts the page in a representative state.</summary>
        public void PrepareSnapshot(string page, string? simulate, string? fault = null)
        {
            if (page == "model") _model.PrepareSnapshot(fault);
            if (page == "library") _library.PrepareSnapshot(fault); // [libs] part creator / manager / import previews
            if (page == "pcb") _pcb.AutoLayoutForSnapshot(fault);
            if (page == "pcb" && fault == "fpeditor")
            {
                // The footprint editor is a dialog: render it instead of the main window.
                var editor = new RobotWin.Studio.Pcb.FootprintEditorWindow(RobotTwin.CoreSim.Pcb.FootprintLibrary.Get("DIP-8"), "U1")
                {
                    Owner = this, WindowStartupLocation = WindowStartupLocation.Manual, Left = Left, Top = 0, ShowActivated = false, Width = Width, Height = Height
                };
                editor.Show();
                editor.SelectFirstPad();
                App.SnapshotTarget = editor.Content as FrameworkElement;
            }

            if (page == "pcb" && fault == "module")
            {
                // Package the loaded example (with an auto-placed board) as a module and plug two of them into a carrier board.
                var design = W.Schematic;
                design.Board = BoardDocument.Synchronize(design, W.Library);
                AutoPlacer.Place(design.Board);
                new AutoRouter(design.Board).Route();
                var part = CustomPart.FromDesign(design, W.Library, "snapshot-module", design.Title + " modülü");
                part.Register(W.Library);
                var carrier = new RobotTwin.CoreSim.Design.Schematic { Title = "Taşıyıcı kart" };
                carrier.AddPart(W.Library, "snapshot-module", new RobotTwin.CoreSim.Design.GridPoint(0, 0));
                carrier.AddPart(W.Library, "snapshot-module", new RobotTwin.CoreSim.Design.GridPoint(30, 0));
                carrier.AddPart(W.Library, "term-2", new RobotTwin.CoreSim.Design.GridPoint(-20, 0));
                W.Load(carrier, null);
                var board = BoardDocument.Synchronize(carrier, W.Library);
                board.Width = 110;
                board.Height = 70;
                AutoPlacer.Place(board);
                carrier.Board = board;
                _pcb.OnShown();
                _pcb.Show3D(true);
            }

            if (page == "pcb" && fault == "3d")
            {
                if (simulate != null && double.TryParse(simulate, NumberStyles.Float, CultureInfo.InvariantCulture, out double s3) && W.Simulation.Start())
                {
                    W.Simulation.Pause();
                    for (int i = 0; i < Math.Max(1, (int)(s3 / 2e-3)); i++) W.Simulation.StepOnce(2e-3);
                }

                _pcb.Show3D(true);
            }

            if (page == "code") _code.SnapshotBuildAndRun(fault);
            if (page == "world") _world.PrepareSnapshot(fault, simulate);
            if (page == "help") _help?.PrepareSnapshot(fault);

            if (page == "schematic" && simulate != null && double.TryParse(simulate, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                _schematic.PrepareSnapshotCircuit(fault);
                if (W.Simulation.Start())
                {
                    _schematic.PrepareSnapshotSimulation(fault);
                    W.Simulation.Pause();
                    for (int i = 0; i < Math.Max(1, (int)(seconds / 2e-3)); i++) W.Simulation.StepOnce(2e-3);
                    _schematic.FinishSnapshot(fault);
                }
            }

            if (page == "schematic" && simulate == null) _schematic.PrepareEditorSnapshot(fault);

            if (fault == "split")
            {
                ShowSplit(page == "pcb" ? "schematic" : "pcb");
            }
        }

        // ================================================================ tabs

        private void OnTabClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
            if ((sender as FrameworkElement)?.DataContext is OpenDocument doc) W.Activate(doc);
        }

        private void OnTabMiddle(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && (sender as FrameworkElement)?.DataContext is OpenDocument doc)
            {
                CloseTab(doc);
                e.Handled = true;
            }
        }

        private void OnTabClose(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is OpenDocument doc) CloseTab(doc);
            e.Handled = true;
        }

        private void OnTabsWheel(object sender, MouseWheelEventArgs e)
        {
            TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        public void CloseTab(OpenDocument doc)
        {
            if (!ConfirmDiscard(doc)) return;
            if (doc.Kind == DocumentKind.World && doc == W.ActiveDocument) _world.StopSimulation();
            W.Close(doc);
        }

        private void CycleTab(int step)
        {
            int n = W.Documents.Count;
            if (n < 2) return;
            int i = W.Documents.IndexOf(W.ActiveDocument);
            W.Activate(W.Documents[((i + step) % n + n) % n]);
        }

        private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }

            return null;
        }

        // ================================================================ files

        public void NewProject()
        {
            W.NewDocument();
            Navigate("schematic");
        }

        /// <summary>
        /// [assembly] Montaj: the current project's circuit as a real assembly in a world tab — the PCB
        /// with what is soldered on it, every other part as its own real 3D part, cabled pin to pin by
        /// the nets. Built once per project; afterwards the same tab is brought back.
        /// </summary>
        public void OpenProjectAssembly()
        {
            var sch = W.Schematic;
            string title = (string.IsNullOrWhiteSpace(sch.Title) ? "Proje" : sch.Title) + " — montaj";
            var open = W.Documents.FirstOrDefault(d => d.Kind == DocumentKind.World && d.World?.Title == title);
            if (open != null)
            {
                W.Activate(open);
                Navigate("world");
                return;
            }

            var package = new ProjectPackage { Schematic = sch, Firmware = W.Project.Firmware, Parts = W.Project.Parts };
            var world = RobotTwin.CoreSim.Sandbox.ProjectAssembly.Build(package, W.Library, W.FilePath);
            NewWorld(world);
            W.Status = $"Montaj: {world.Objects.Count} gerçek parça, {world.Cables.Count} kablo. Gövdeyi, eklemleri ve kabukları dünya araçlarıyla kurun; her şey gerçek fizikle çalışır.";
        }

        public void NewWorld(WorldDocument? world = null)
        {
            W.NewWorld(world);
            Navigate("world");
        }

        private void OnNewMenu(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = NewButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.Items.Add(MenuItem("Yeni devre projesi", "Ctrl+N", () => NewProject()));
            menu.Items.Add(MenuItem("Yeni dünya", "Ctrl+Shift+N", () => NewWorld()));
            menu.Items.Add(new Separator());
            var worlds = new MenuItem { Header = "Dünya şablonları" };
            foreach (var t in Examples.WorldTemplates.All) worlds.Items.Add(MenuItem(t.Title, null, () => NewWorld(t.Build())));
            menu.Items.Add(worlds);
            var circuits = new MenuItem { Header = "Devre örnekleri" };
            foreach (var ex in Examples.ExampleProjects.All)
            {
                var x = ex;
                circuits.Items.Add(MenuItem(x.Title, null, () =>
                {
                    W.Load(x.Build(), null);
                    Navigate(x.Page);
                }));
            }

            menu.Items.Add(circuits);
            menu.IsOpen = true;
        }

        private static MenuItem MenuItem(string header, string? gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
            item.Click += (_, _) => action();
            return item;
        }

        public void OpenFile(string path)
        {
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                switch (ext)
                {
                    case RobotWinFormats.PartExtension:
                    case RobotWinFormats.LibraryExtension:
                    {
                        var added = UserLibrary.Import(W.Library, path);
                        W.Status = added.Count == 0
                            ? "Bu parçalar kütüphanede zaten var: " + Path.GetFileName(path)
                            : $"{added.Count} parça kütüphaneye eklendi: " + string.Join(", ", added.Select(p => p.Name));
                        Navigate("library");
                        return;
                    }

                    case RobotWinFormats.RecordingExtension:
                    {
                        var rec = SimulationRecording.Load(path);
                        if (rec.Project != null) W.LoadPackage(rec.Project, null);
                        W.Simulation.ShowRecording(rec);
                        W.Status = $"Kayıt açıldı: {rec.Title} ({rec.Channels.Count} kanal, {rec.Events.Count} olay)";
                        RecentFiles.Add(path);
                        Navigate("schematic");
                        return;
                    }

                    case WorldDocument.Extension:
                        W.OpenWorld(path);
                        Navigate("world");
                        return;

                    default:
                        W.Open(path);
                        Navigate("schematic");
                        return;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(this, "Dosya açılamadı", ex.Message, "Tamam");
            }
        }

        /// <summary>Saves the current run (probes, log, serial, trajectory, project) as a .rwsim recording.</summary>
        public void SaveRecording()
        {
            var sim = W.Simulation;
            if (sim.Time <= 0 && sim.Probes.Count == 0)
            {
                Dialog.Show(this, "Kayıt yok", "Önce simülasyonu çalıştırın; kanallar, olaylar ve araç yörüngesi kayda alınır.", "Tamam");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Simülasyon kaydı (*.rwsim)|*.rwsim",
                Title = "Simülasyon kaydını kaydet",
                FileName = (string.IsNullOrWhiteSpace(W.Schematic.Title) ? "kayit" : W.Schematic.Title) + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm"),
                DefaultExt = RobotWinFormats.RecordingExtension
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                sim.CreateRecording().Save(dlg.FileName);
                W.Status = "Kayıt kaydedildi: " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(this, "Kaydedilemedi", ex.Message, "Tamam");
            }
        }

        /// <summary>PNG preview of the schematic for the project file.</summary>
        private byte[]? RenderPreview()
        {
            var el = _schematic.PreviewElement;
            if (el == null || el.ActualWidth < 20 || el.ActualHeight < 20) return null;
            int w = 640, h = (int)Math.Round(640 * el.ActualHeight / el.ActualWidth);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.Uniform }, null, new Rect(0, 0, w, h));
            }

            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            return Logo.EncodePng(bmp);
        }

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = OpenFilter, Title = "Aç", Multiselect = true };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames) OpenFile(f);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var doc = W.ActiveDocument;
            if (doc.IsEmbedded)
            {
                W.SaveEmbedded();
                return;
            }

            if (W.FilePath == null) SaveAs();
            else Save(W.FilePath);
        }

        private bool SaveAs()
        {
            var doc = W.ActiveDocument;
            if (doc.IsEmbedded)
            {
                // "Save as" on a board inside a world exports it as a stand-alone project.
                var export = new SaveFileDialog { Filter = ProjectFilter, Title = "Kartı ayrı proje olarak kaydet", FileName = doc.Package.Schematic.Title, DefaultExt = ".rwproj" };
                if (export.ShowDialog(this) != true) return false;
                W.FlushActiveDesign();
                try
                {
                    doc.Package.Save(export.FileName);
                    W.Status = "Kart ayrı proje olarak kaydedildi: " + Path.GetFileName(export.FileName);
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Dialog.Show(this, "Kaydedilemedi", ex.Message, "Tamam");
                    return false;
                }
            }

            bool world = doc.Kind == DocumentKind.World;
            var dlg = new SaveFileDialog
            {
                Filter = world ? WorldFilter : ProjectFilter,
                Title = "Farklı kaydet",
                FileName = doc.BaseTitle,
                DefaultExt = world ? WorldDocument.Extension : ".rwproj"
            };
            return dlg.ShowDialog(this) == true && Save(dlg.FileName);
        }

        private bool Save(string path)
        {
            try
            {
                W.Save(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(this, "Kaydedilemedi", ex.Message, "Tamam");
                return false;
            }
        }

        /// <summary>Asks what to do with the active tab's unsaved changes. Returns false if the user cancelled.</summary>
        public bool ConfirmDiscard() => ConfirmDiscard(W.ActiveDocument);

        public bool ConfirmDiscard(OpenDocument doc)
        {
            bool dirty = doc.IsDirty || (doc.Kind == DocumentKind.World && W.Documents.Any(d => d.OwnerWorld == doc && d.IsDirty));
            if (!dirty || doc.IsEmbedded) return true;
            if (doc != W.ActiveDocument) W.Activate(doc);
            var answer = Dialog.Show(this, "Kaydedilmemiş değişiklikler",
                $"'{doc.BaseTitle}' içinde kaydedilmemiş değişiklikler var.", "Kaydet", "Kaydetme", "Vazgeç");
            if (answer == 0) return doc.FilePath != null ? Save(doc.FilePath) : SaveAs();
            return answer == 1;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_forceClose)
            {
                // The 3D modeller keeps its own document outside the tabs.
                if (!_model.ConfirmCloseApp(this))
                {
                    e.Cancel = true;
                    return;
                }

                foreach (var doc in W.Documents.Where(d => !d.IsEmbedded).ToList())
                {
                    if (!ConfirmDiscard(doc))
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            W.Simulation.Stop();
            _world.StopSimulation();
            foreach (var w in _detached.Values.ToList()) w.Close();
            AppSettings.Current.WindowMaximized = WindowState == WindowState.Maximized && !_fullScreen;
            // Normal-state bounds even when closed maximised or minimised, so the next start opens where it was.
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (!bounds.IsEmpty && bounds.Width > 400 && bounds.Height > 300)
            {
                AppSettings.Current.WindowWidth = bounds.Width;
                AppSettings.Current.WindowHeight = bounds.Height;
                AppSettings.Current.WindowLeft = bounds.Left;
                AppSettings.Current.WindowTop = bounds.Top;
            }

            AppSettings.Current.Save();
            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        // ================================================================ window modes

        private void OnSplitMenu(object sender, RoutedEventArgs e) => ToggleSplit();

        private void ToggleSplit()
        {
            if (_second != null) CloseSplit();
            else ShowSplit(_current == "pcb" ? "schematic" : _current == "schematic" ? "pcb" : _current == "world" ? "schematic" : "world");
        }

        /// <summary>Shows a second page to the right of the current one.</summary>
        public void ShowSplit(string page)
        {
            if (page == _current || !_pages.ContainsKey(page) || _detached.ContainsKey(page)) return;
            if (_second != null) SecondHost.Content = null;
            _second = page;
            SecondHost.Content = _pages[page];
            SplitterColumn.Width = new GridLength(5);
            SecondColumn.Width = new GridLength(1, GridUnitType.Star);
            SecondColumn.MinWidth = 320;
            Splitter.Visibility = Visibility.Visible;
            SecondPane.Visibility = Visibility.Visible;
            SelectSecond(page);
            Shown(page);
            W.Status = $"Bölünmüş görünüm: {_pageNames[_current]} | {_pageNames[page]} (sağdaki sayfayı üstteki listeden değiştirin)";
        }

        private void CloseSplit()
        {
            SecondHost.Content = null;
            _second = null;
            SplitterColumn.Width = new GridLength(0);
            SecondColumn.Width = new GridLength(0);
            SecondColumn.MinWidth = 0;
            Splitter.Visibility = Visibility.Collapsed;
            SecondPane.Visibility = Visibility.Collapsed;
        }

        private void OnCloseSplit(object sender, RoutedEventArgs e) => CloseSplit();

        private bool _selectingSecond;

        private void SelectSecond(string page)
        {
            _selectingSecond = true;
            SecondPageBox.SelectedItem = SecondPageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == page);
            _selectingSecond = false;
        }

        private void OnSwapSplit(object sender, RoutedEventArgs e) => SwapSplit();

        private void SwapSplit()
        {
            if (_second == null) return;
            string left = _current, right = _second;
            PageHost.Content = null;
            SecondHost.Content = null;
            _current = right;
            _second = left;
            PageHost.Content = _pages[right];
            SecondHost.Content = _pages[left];
            SelectSecond(left);
            UpdateNav(right);
            Shown(right);
        }

        private void OnSecondPageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectingSecond || SecondPageBox.SelectedItem is not ComboBoxItem item || _second == null) return;
            string page = (string)item.Tag;
            if (page == _second) return;
            if (page == _current)
            {
                SwapSplit();
                return;
            }

            if (_detached.ContainsKey(page)) return;
            SecondHost.Content = null;
            _second = page;
            SecondHost.Content = _pages[page];
            Shown(page);
        }

        /// <summary>Moves the current page into its own window (e.g. PCB on a second monitor while the schematic stays here).</summary>
        private void OnDetach(object sender, RoutedEventArgs e) => Detach(_current);

        public void Detach(string page)
        {
            if (page == "home" || _detached.ContainsKey(page) || !_pages.ContainsKey(page)) return;
            var control = _pages[page];
            if (PageHost.Content == control) PageHost.Content = new DetachedPlaceholder(_pageNames[page], () => _detached[page].Activate(), () => _detached[page].Close());
            if (SecondHost.Content == control) CloseSplit();

            var window = new Window
            {
                Title = "RobotWin — " + _pageNames[page],
                Width = Math.Max(900, ActualWidth * 0.8),
                Height = Math.Max(600, ActualHeight * 0.8),
                Background = (Brush)FindResource("B.Canvas"),
                Icon = Icon,
                Content = control,
                Owner = null,
                ShowInTaskbar = true
            };
            window.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
            window.FontFamily = FontFamily;
            window.FontSize = FontSize;
            window.SourceInitialized += (_, _) => ApplyDarkTitle(window);
            window.Closed += (_, _) =>
            {
                window.Content = null;
                _detached.Remove(page);
                if (PageHost.Content is DetachedPlaceholder) Navigate(page);
                W.Status = _pageNames[page] + " ana pencereye geri döndü";
            };
            foreach (InputBinding b in InputBindings) window.InputBindings.Add(b);
            _detached[page] = window;
            window.Show();
            Shown(page);
            W.Status = _pageNames[page] + " ayrı pencerede. Pencereyi kapatınca ana pencereye döner.";
        }

        private void OnFocusMode(object sender, RoutedEventArgs e) => SetFocusMode(!_focusMode);

        /// <summary>Hides the navigation rail and status bar to give the editor the whole window.</summary>
        public void SetFocusMode(bool on)
        {
            _focusMode = on;
            NavColumn.Width = on ? new GridLength(0) : GridLength.Auto;
            NavBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            StatusRow.Height = new GridLength(on ? 0 : 26);
            StatusBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on) W.Status = "Odak modu: gezinme çubuğu gizlendi (tekrar tıklayın veya F11 ile tam ekran)";
        }

        private void ToggleFullScreen()
        {
            _fullScreen = !_fullScreen;
            if (_fullScreen)
            {
                _stateBeforeFull = WindowState;
                if (_behavior != null) _behavior.FullScreen = true;
                SetFocusMode(true);
                TitleRow.Height = new GridLength(0);
                TitleBar.Visibility = Visibility.Collapsed;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
                Root.Margin = new Thickness(0);
                W.Status = "Tam ekran (Esc veya F11 ile çık)";
            }
            else
            {
                TitleRow.Height = new GridLength(42);
                TitleBar.Visibility = Visibility.Visible;
                SetFocusMode(false);
                if (_behavior != null) _behavior.FullScreen = false;
                WindowState = WindowState.Normal;
                WindowState = _stateBeforeFull;
                OnStateChanged();
            }

            _world.OnFullScreenChanged(_fullScreen, _current == "world");   // [sense] full screen opens Scene + Game
        }

        private void ApplyStartupSettings()
        {
            var s = AppSettings.Current;
            if (s.WindowWidth > 400) Width = s.WindowWidth;
            if (s.WindowHeight > 300) Height = s.WindowHeight;
            if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop) && WindowBehavior.IsOnScreen(s.WindowLeft, s.WindowTop, Width))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowLeft;
                Top = s.WindowTop;
            }
            if (s.WindowMaximized) WindowState = WindowState.Maximized;
            ApplyUiScale(s.UiScale);
            RecentFiles.Limit = Math.Max(3, s.RecentFileCount);
        }

        /// <summary>Scales the whole interface (Ayarlar → Görünüm).</summary>
        public void ApplyUiScale(double scale)
        {
            scale = Math.Max(0.75, Math.Min(1.75, scale));
            Root.LayoutTransform = Math.Abs(scale - 1.0) < 1e-3 ? Transform.Identity : new ScaleTransform(scale, scale);
        }

        // ================================================================ chrome

        private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Workspace.Title):
                case nameof(Workspace.FilePath):
                    UpdateTitle();
                    break;
                case nameof(Workspace.Status):
                    StatusText.Text = W.Status;
                    break;
            }
        }

        private void UpdateTitle()
        {
            Title = "RobotWin Studio — " + W.Title + (W.FilePath != null ? "  ·  " + Path.GetFileName(W.FilePath) : string.Empty);
        }

        private void UpdateStats()
        {
            var world = W.ActiveWorld;
            if (world != null)
            {
                StatsText.Text = $"{world.Objects.Count} nesne · {world.Cables.Count} kablo · {world.Joints.Count} bağlantı";
                return;
            }

            var s = W.Schematic;
            StatsText.Text = $"{s.Parts.Count} parça · {s.Wires.Count} kablo";
        }

        private void UpdateSimDot()
        {
            SimDot.Fill = W.Simulation.State switch
            {
                SimState.Running => (Brush)FindResource("B.Accent"),
                SimState.Paused => (Brush)FindResource("B.TextSecondary"),
                _ => (Brush)FindResource("B.TextMuted")
            };
        }

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximize(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnStateChanged()
        {
            // WindowBehavior sizes the maximised window to the monitor's work area, so nothing overhangs.
            Root.Margin = new Thickness(0);
            MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Önceki boyuta getir" : "Ekranı kapla (üzerine gelince Windows yerleşim düzenleri)";
            MaxIcon.Data = (Geometry)FindResource(WindowState == WindowState.Maximized ? "I.Restore" : "I.Maximize");
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void ApplyWindowsCorners()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static void ApplyDarkTitle(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }
    }

    /// <summary>Shown in the main window while its page lives in a separate window.</summary>
    internal sealed class DetachedPlaceholder : UserControl
    {
        public DetachedPlaceholder(string page, Action focus, Action dock)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = page + " ayrı pencerede açık", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = "İkinci ekranda çalışabilirsiniz; tüm sayfalar aynı projeyi canlı olarak paylaşır.", Margin = new Thickness(0, 6, 0, 16), HorizontalAlignment = HorizontalAlignment.Center });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var show = new Button { Content = "Pencereye git", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
            show.Click += (_, _) => focus();
            var back = new Button { Content = "Ana pencereye geri al", Padding = new Thickness(14, 6, 14, 6) };
            back.Click += (_, _) => dock();
            buttons.Children.Add(show);
            buttons.Children.Add(back);
            panel.Children.Add(buttons);
            Content = panel;
            panel.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.TextSecondary");
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action _action;

        public RelayCommand(Action action)
        {
            _action = action;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _action();
    }
}
