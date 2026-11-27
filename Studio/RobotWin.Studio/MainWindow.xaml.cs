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
            "Parça kütüphanesi (*.rwlib)|*.rwlib|Robot tanımı (*.urdf, *.xacro)|*.urdf;*.xacro|" +
            "KiCad şeması (*.kicad_sch)|*.kicad_sch|FMU (*.fmu)|*.fmu|Tüm dosyalar (*.*)|*.*";

        private readonly HomePage _home;
        private readonly WorldPage _world;
        private readonly SchematicPage _schematic;
        private readonly PcbPage _pcb;
        private readonly ModelPage _model;
        private readonly CodePage _code;
        private LibraryPage Library => (LibraryPage)_pages["library"];
        private readonly PageSet _pages = new PageSet();

        /// <summary>
        /// [startup] The pages by key. A page registered with <see cref="Later"/> is built the first time it is shown
        /// (the parts library and the settings cost a tenth of a second at start and are rarely the first page).
        /// </summary>
        private sealed class PageSet
        {
            private readonly Dictionary<string, UserControl> _made = new Dictionary<string, UserControl>();
            private readonly Dictionary<string, Func<UserControl>> _makers = new Dictionary<string, Func<UserControl>>();

            public UserControl this[string key]
            {
                get
                {
                    if (_made.TryGetValue(key, out var page)) return page;
                    page = _makers[key]();
                    _made[key] = page;
                    Shell.Themes.Track(page);   // [theme] recoloured with the window even while it is not shown
                    return page;
                }
                set
                {
                    _made[key] = value;
                    Shell.Themes.Track(value);   // [theme]
                }
            }

            public void Later(string key, Func<UserControl> make) => _makers[key] = make;

            public bool ContainsKey(string key) => _made.ContainsKey(key) || _makers.ContainsKey(key);
        }
        private readonly Dictionary<string, string> _pageNames = new Dictionary<string, string>
        {
            ["world"] = "Dünya", ["schematic"] = "Devre", ["pcb"] = "PCB", ["code"] = "Kod", ["assembly"] = "Montaj", ["model"] = "Parça",
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

        /// <summary>[perf] The world scene's last load profile (for --bench).</summary>
        public string WorldLoadProfile => _world.Scene.LastLoadProfile;

        /// <summary>[perf] Where the world's last start went (for --bench).</summary>
        public string WorldStartProfile => _world.StartProfile;

        public void StopWorldForBench() => _world.StopSimulation();

        /// <summary>[perf] The running world's simulated time (for --bench: how close to real time it ran).</summary>
        public double WorldSimTime => _world.SimTime;

        /// <summary>[lang] Every event-log entry of the run (world and circuit), shown or not: --lang-missing checks them all.</summary>
        public System.Collections.Generic.IEnumerable<Simulation.SimLogEntry> RunLogs() => _world.SessionLog.Concat(W.Simulation.Log).ToList();

        /// <summary>--snapshot … --perf: the performance indicator on for this run only (nothing saved), and a world run with --simulate goes on live.</summary>
        public void ShowPerfForSnapshot()
        {
            _perf.Visibility = Visibility.Visible;
            _world.ResumeForSnapshot();
        }

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
            StartupTrace.Mark("Ana pencere", 0.3);
            InitializeComponent();
            LogoImage.Source = Logo.Image;
            Icon = Logo.Render(64);

            StartupTrace.Mark("Başlangıç sayfası", 0.36);
            _home = new HomePage(this);
            StartupTrace.Mark("Dünya ve 3B görünüm", 0.42);
            _world = new WorldPage(this);
            StartupTrace.Mark("Devre düzenleyici", 0.55);
            _schematic = new SchematicPage();
            StartupTrace.Mark("PCB düzenleyici", 0.63);
            _pcb = new PcbPage();
            StartupTrace.Mark("Parça modelleyici", 0.7);
            _model = new ModelPage(this);
            StartupTrace.Mark("Kod düzenleyici", 0.77);
            _code = new CodePage();
            StartupTrace.Mark("Menüler ve kısayollar", 0.83);
            _pages["home"] = _home;
            _pages["world"] = _world;
            _pages["schematic"] = _schematic;
            _pages["pcb"] = _pcb;
            _pages["model"] = _model;
            _pages["code"] = _code;
            _pages.Later("library", () => new LibraryPage(this));
            _pages.Later("settings", () => new SettingsPage());
            PageHost.Content = _home;
            SecondPageBox.ItemsSource = _pageNames.Where(kv => kv.Key != "home" && kv.Key != "settings").Select(kv => new ComboBoxItem { Content = kv.Value, Tag = kv.Key }).ToList();

            Tabs.ItemsSource = W.Documents;
            W.PreviewProvider = RenderPreview;
            Loaded += (_, _) => UpdateWorkspaceButton();   // [workspaces]
            W.WorldPreviewProvider = doc => W.ActiveWorld == doc.World ? _world.Scene.RenderPreview() : null;   // [home] drawn from the world, not the screen
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
            ContentRendered += (_, _) => Dispatcher.BeginInvoke(new Action(OfferGuideOnFirstStart), System.Windows.Threading.DispatcherPriority.ApplicationIdle);   // [guide]
            _behavior = new WindowBehavior(this, MaxButton);
            Activated += (_, _) => AppTitle.Opacity = 1;
            Deactivated += (_, _) => AppTitle.Opacity = 0.55;
            BuildMenuBar();
            Perf.Start();
            Grid.SetRowSpan(_perf, 3);
            Panel.SetZIndex(_perf, 900);
            Root.Children.Add(_perf);
            _perf.Visibility = AppSettings.Current.ShowPerf ? Visibility.Visible : Visibility.Collapsed;
            Command("view.perf", "Görünüm", "Performans göstergesi", "Ctrl+Shift+F", () => ShowPerf(_perf.Visibility != Visibility.Visible));
            Command("app.settings", "Uygulama", "Ayarlar", "Ctrl+,", () => Navigate("settings"));
            SetupTabDrag();
            SetupTabStrip();   // [tabs] MainWindow.Tabs.cs

            Command("file.newProject", "Dosya", "Yeni devre projesi", "Ctrl+N", () => NewProject());
            Command("file.newWorld", "Dosya", "Yeni dünya", "Ctrl+Shift+N", () => NewWorld());
            Command("file.open", "Dosya", "Aç", "Ctrl+O", () => OnOpen(this, new RoutedEventArgs()));
            Command("file.check", "Dosya", "Projeyi denetle", string.Empty, () => CheckProject(quiet: false));   // [integrity]
            Command("file.versions", "Dosya", "Önceki sürümler", string.Empty, ShowVersions);   // [versions]
            Command("file.save", "Dosya", "Kaydet", "Ctrl+S", () => OnSave(this, new RoutedEventArgs()));
            Command("file.saveAs", "Dosya", "Farklı kaydet", "Ctrl+Shift+S", () => SaveAs());
            Command("tabs.close", "Sekmeler", "Sekmeyi kapat", "Ctrl+W", () => CloseTab(W.ActiveDocument));
            Command("tabs.next", "Sekmeler", "Sonraki sekme", "Ctrl+Tab", () => CycleTab(1));
            Command("tabs.previous", "Sekmeler", "Önceki sekme", "Ctrl+Shift+Tab", () => CycleTab(-1));
            Command("view.fullscreen", "Görünüm", "Tam ekran", "F11", ToggleFullScreen);
            Command("view.fullview", "Görünüm", "Tam görünüm: panelleri gizle / geri getir", "Ctrl+Space", ToggleFullView);   // [dock-min]
            Command("view.split", "Görünüm", "Bölünmüş görünüm", "Ctrl+\\, Ctrl+<", ToggleSplit);
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
            Command("app.palette", "Uygulama", "Komut paleti", "Ctrl+K, Ctrl+Shift+P", OpenPalette);
            ApplyStartupSettings();
            SetupRail();
            SetupHelp();
            SetupDebugger();   // [debug] MainWindow.Debug.cs
            SetupProblems();   // [problems] MainWindow.Problems.cs
            Command("view.problems", "Görünüm", "Sorunlar", string.Empty, () => OnProblems(this, new RoutedEventArgs()));
            PageShortcuts.RegisterAll();   // [keys] the schematic and PCB editors' own keys, rebindable too
            ApplyShortcuts();   // [keys] every Command(...) above
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
            else _palette.Open(PaletteCommands, context: _current);
        }

        private IEnumerable<PaletteCommand> PaletteCommands()
        {
            const string go = "Git", file = "Dosya", view = "Görünüm", tab = "Sekmeler", sim = "Simülasyon";
            foreach (var c in WorkspaceCommands()) yield return c;   // [workspaces]
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
                }, keys) { Context = "schematic", Keywords = c == RobotWin.Studio.Editor.SchematicCommand.Erc ? "çalıştır denetle kontrol hata run check" : string.Empty };
            }

            // [palette] what the user means more than where a menu keeps it: cross-probing, checks, measuring
            yield return new PaletteCommand("Devre şeması", "Seçili parçayı PCB'de göster", () =>
            {
                if (_schematic.SelectedPart is { } part) ShowPartInPcb(part.RefDes);
            }, null, _schematic.SelectedPart?.RefDes)
            { Context = "schematic", Keywords = "bul çapraz cross probe show find", Ready = () => _current == "schematic" && _schematic.SelectedPart != null && W.Schematic.Board != null };
            yield return new PaletteCommand("PCB", "DRC çalıştır (tasarım kuralı denetimi)", () =>
            {
                Navigate("pcb");
                Dispatcher.BeginInvoke(new Action(_pcb.RunDrc), System.Windows.Threading.DispatcherPriority.Loaded);
            }) { Context = "pcb", Keywords = "denetle kontrol kural hata run check design rule", Ready = () => W.Schematic.Board != null };
            // [drc-policy] [board-stats] KiCad's Inspect menu
            yield return new PaletteCommand("PCB", "Sonraki DRC işareti", () => _pcb.NextMarker(), "F8")
            { Context = "pcb", Keywords = "next marker hata sorun git", Ready = () => _current == "pcb" };
            yield return new PaletteCommand("PCB", "DRC raporunu kaydet", () => _pcb.SaveDrcReport())
            { Context = "pcb", Keywords = "report rapor dışa aktar drc", Ready = () => _current == "pcb" };
            yield return new PaletteCommand("PCB", "Ayak izi atama", () =>   // [fp-assign] KiCad's Assign Footprints
            {
                Navigate("pcb");
                Dispatcher.BeginInvoke(new Action(_pcb.ShowFootprintTable), System.Windows.Threading.DispatcherPriority.Loaded);
            }) { Context = "pcb", Keywords = "footprint assign cvpcb ayak izi ped tablo parça", Ready = () => W.Schematic.Board != null };
            yield return new PaletteCommand("PCB", "Çizdir (SVG / PDF)", () =>   // [plot] KiCad's Plot
            {
                Navigate("pcb");
                Dispatcher.BeginInvoke(new Action(_pcb.ShowPlotDialog), System.Windows.Threading.DispatcherPriority.Loaded);
            }) { Context = "pcb", Keywords = "plot print yazdır pdf svg katman çizim toner film çıktı", Ready = () => W.Schematic.Board != null };
            yield return new PaletteCommand("PCB", "Aralık çözümlemesi", () => _pcb.ShowClearanceResolution())   // [clearance-res] KiCad's Inspect
            { Context = "pcb", Keywords = "clearance resolution kural aralık boşluk sınıf iki öğe incele inspect", Ready = () => _current == "pcb" };
            yield return new PaletteCommand("PCB", "Kart istatistikleri", () =>
            {
                Navigate("pcb");
                Dispatcher.BeginInvoke(new Action(_pcb.ShowStatistics), System.Windows.Threading.DispatcherPriority.Loaded);
            }) { Context = "pcb", Keywords = "statistics delik drill sayı ped via alan uzunluk incele inspect", Ready = () => W.Schematic.Board != null };
            yield return new PaletteCommand("Dünya", "Çakışma algıla", () =>   // [interference] SolidWorks' Interference Detection
            {
                Navigate("world");
                _world.CheckInterference();
            }) { Context = "world", Keywords = "interference çakışma iç içe girme hacim montaj collision overlap", Ready = () => W.ActiveWorld?.Objects.Count > 1 };
            yield return new PaletteCommand("Dünya", "Boşluk doğrula", () =>   // [clearance] SolidWorks' Clearance Verification
            {
                Navigate("world");
                _world.CheckClearance();
            }) { Context = "world", Keywords = "clearance boşluk mesafe yakın aralık tolerans montaj gap distance", Ready = () => W.ActiveWorld?.Objects.Count > 1 };
            yield return new PaletteCommand("Dünya", "Delik hizası", () =>   // [holes] SolidWorks' Hole Alignment
            {
                Navigate("world");
                _world.CheckHoleAlignment();
            }) { Context = "world", Keywords = "hole alignment delik hiza eksen kayık cıvata vida montaj misaligned bolt", Ready = () => W.ActiveWorld?.Objects.Count > 1 };
            yield return new PaletteCommand("Dünya", "Patlatılmış görünüm", () =>   // [explode]
            {
                Navigate("world");
                _world.OpenExplode();
            }) { Context = "world", Keywords = "exploded view patlat ayır montaj sırası explode animation", Ready = () => W.ActiveWorld?.Objects.Count > 1 };
            yield return new PaletteCommand("Dünya", "Montaj ilişkilerini denetle", () =>
            {
                Navigate("world");
                _world.CheckMates();
            }) { Context = "world", Keywords = "kontrol çelişki serbestlik mate check constraints", Ready = () => W.ActiveWorld?.Mates?.Count > 0 };
            foreach (var p in W.Schematic.Parts)
            {
                var def = W.Library.Find(p.PartId);
                if (def == null || def.Symbol is RobotTwin.CoreSim.Design.SymbolKind.Ground or RobotTwin.CoreSim.Design.SymbolKind.PowerRail) continue;
                string refDes = p.RefDes;
                yield return new PaletteCommand("Ölç", $"{refDes} akımını izle", () =>
                {
                    Navigate("schematic");
                    W.Simulation.AddCurrentProbe(refDes);
                    _schematic.ShowScope();
                    if (W.Simulation.State == SimState.Stopped) W.Status = $"{refDes} akımı osiloskopta; simülasyonu başlatın (F5)";
                }, null, def.Name)
                { Context = "schematic", OnlyWhenAsked = true, Keywords = "ölç osiloskop akım prob watch current probe monitor " + def.Name + " " + def.Category };
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
                yield return new PaletteCommand("Parça ekle", preset.Name, () => PlacePart(id), null, preset.Category)
                {
                    Context = "schematic",
                    OnlyWhenAsked = true,
                    Keywords = "ekle yerleştir koy add place " + preset.Category
                };
            }
        }

        // ================================================================ tab menu

        private void OnTabMenu(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not OpenDocument doc) return;
            var menu = new ContextMenu();
            // [tabs] pin, close (pinned tabs stay), open the last closed one again
            menu.Items.Add(MenuItem(doc.IsPinned ? "Sabitlemeyi kaldır" : "Sabitle", null, () => doc.IsPinned = !doc.IsPinned));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Kapat", "Ctrl+W", () => CloseTab(doc)));
            menu.Items.Add(MenuItem("Diğerlerini kapat", null, () => CloseOthers(doc)));
            menu.Items.Add(MenuItem("Sağdakileri kapat", null, () =>
            {
                int i = W.Documents.IndexOf(doc);
                foreach (var d in W.Documents.Skip(i + 1).Where(d => !d.IsPinned).ToList()) CloseTab(d);
            }));
            menu.Items.Add(MenuItem("Sabitlenmemişlerin hepsini kapat", null, () =>
            {
                foreach (var d in W.Documents.Where(d => !d.IsPinned).ToList()) CloseTab(d);
            }));
            var reopen = MenuItem("Kapatılan sekmeyi yeniden aç", "Ctrl+Shift+T", ReopenClosedTab);
            reopen.IsEnabled = W.ClosedFiles.Count > 0;
            menu.Items.Add(reopen);
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
            foreach (var d in W.Documents.Where(d => d != keep && !d.IsPinned && d.OwnerWorld != keep && keep.OwnerWorld != d).ToList()) CloseTab(d);
        }

        internal static void RevealInExplorer(string path, bool folder)
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
        public static string BackupFolder => Backups.Folder;

        /// <summary>[backup] Ayarlar → Şimdi yedekle: the unsaved tabs now, whatever the interval.</summary>
        public void AutosaveNow()
        {
            Autosave(force: true);
            if (!W.Documents.Any(d => d.IsDirty && !d.IsEmbedded && !d.IsMontaj)) W.Status = "Yedeklenecek kaydedilmemiş sekme yok";
        }

        private void Autosave(bool force = false)
        {
            int minutes = AppSettings.Current.AutosaveMinutes;
            if (Unattended.Active || (!force && (minutes <= 0 || (DateTime.Now - _lastAutosave).TotalMinutes < minutes))) return;
            _lastAutosave = DateTime.Now;
            int saved = 0;
            try
            {
                foreach (var doc in W.Documents.Where(d => d.IsDirty && !d.IsEmbedded && !d.IsMontaj).ToList())
                {
                    if (doc.Kind == DocumentKind.World && doc.World != null)
                    {
                        doc.World.Save(Backups.AutosavePath(doc.BaseTitle, WorldDocument.Extension));
                    }
                    else
                    {
                        if (doc == W.DesignDocument) W.FlushActiveDesign();
                        doc.Package.Save(Backups.AutosavePath(doc.BaseTitle, RobotWinFormats.ProjectExtension));
                    }

                    saved++;
                }

                Backups.Prune();   // [backup] each document's newest copies, the folder under its size
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                W.Status = "Otomatik yedek alınamadı: " + ex.Message;
                return;
            }

            if (saved > 0) W.Status = $"Otomatik yedek: {saved} sekme → {BackupFolder}";
        }

        private void Bind(Key key, ModifierKeys mods, Action action) => InputBindings.Add(new KeyBinding(new RelayCommand(action), key, mods));

        // [keys] rebindable shortcuts (Ayarlar → Klavye kısayolları)
        private readonly List<InputBinding> _shortcutBindings = new List<InputBinding>();

        private void Command(string id, string group, string title, string defaults, Action action)
        {
            Shortcuts.Register(id, group, title, defaults, action);
            if (_shortcutsWired) ApplyShortcuts();
        }

        private bool _shortcutsWired;

        /// <summary>Binds every registered shortcut to its keys now (again after the user changes one).</summary>
        private void ApplyShortcuts()
        {
            if (!_shortcutsWired)
            {
                _shortcutsWired = true;
                Shortcuts.Changed += ApplyShortcuts;
            }

            foreach (var b in _shortcutBindings) InputBindings.Remove(b);
            _shortcutBindings.Clear();
            foreach (var c in Shortcuts.All)
            {
                if (c.Action == null) continue;   // a page's own key: its page handles it (Shell/PageShortcuts.cs)
                foreach (var (key, mods) in Shortcuts.Gestures(c.Keys))
                {
                    try
                    {
                        var binding = new KeyBinding(new RelayCommand(c.Action), key, mods);
                        InputBindings.Add(binding);
                        _shortcutBindings.Add(binding);
                    }
                    catch (NotSupportedException)
                    {
                        // a plain letter without a modifier is not a window-wide key in WPF
                    }
                }
            }
        }

        // ================================================================ navigation

        public string CurrentPage => _current;

        /// <summary>
        /// A stage the user asked for (the rail, Ctrl+0…7, the Pencere menu). [assembly] Dünya is where robots are tried
        /// out: asked for while a Montaj is open, it goes to a test world (the last one, or a new one), never the Montaj.
        /// </summary>
        public void NavigateStage(string page)
        {
            if (page == "world" && W.ActiveDocument.IsMontaj)
            {
                var test = W.Documents.LastOrDefault(d => d.Kind == DocumentKind.World && !d.IsMontaj);
                if (test != null)
                {
                    W.Activate(test);
                    if (_current != "world") Navigate("world");
                }
                else
                {
                    NewWorld();
                    W.Status = "Yeni test dünyası açıldı. Robotu kendi montajında denemek için Montaj'da 'Denemek için ortam'ı kullanın; şablonlar Dosya → Yeni → Dünya şablonları'nda.";
                }

                return;
            }

            Navigate(page);
        }

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
            Rail.Active = RailKey(page);
            Rail.Refresh();
        }

        /// <summary>[assembly] The project's Montaj is a world, but the rail shows it as the Montaj stage.</summary>
        private string RailKey(string page) => page == "world" && W.ActiveWorld?.IsAssembly == true ? "assembly" : page;

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
            Rail.Navigate += NavigateStage;
            Rail.StatusProvider = StageStatus;
            Rail.IsExpanded = AppSettings.Current.NavExpanded;
            Rail.ExpandedChanged += (_, _) =>
            {
                if (_snapshotRail) return;
                AppSettings.Current.NavExpanded = Rail.IsExpanded;
                AppSettings.Current.Save();
            };
            Command("page.home", "Sayfalar", "Başlangıç", "Ctrl+0", () => Navigate("home"));
            Command("page.schematic", "Sayfalar", "Devre", "Ctrl+1", () => Navigate("schematic"));
            Command("page.pcb", "Sayfalar", "PCB", "Ctrl+2", () => Navigate("pcb"));
            Command("page.code", "Sayfalar", "Kod", "Ctrl+3", () => Navigate("code"));
            Command("page.model", "Sayfalar", "Parça", "Ctrl+4", () => Navigate("model"));
            Command("page.assembly", "Sayfalar", "Montaj", "Ctrl+5", () => Navigate("assembly"));
            Command("page.world", "Sayfalar", "Dünya", "Ctrl+6", () => NavigateStage("world"));
            Command("page.library", "Sayfalar", "Kütüphane", "Ctrl+7", () => Navigate("library"));
            Command("view.rail", "Görünüm", "Sol şeridi genişlet / daralt", "Ctrl+B", () => Rail.IsExpanded = !Rail.IsExpanded);
            Command("view.railHide", "Görünüm", "Sol şeridi göster / gizle", "Ctrl+Shift+B", () => SetRailHidden(!AppSettings.Current.NavHidden));   // [rail]
            Rail.HideRequested += (_, _) => SetRailHidden(true);
            SetRailHidden(AppSettings.Current.NavHidden, save: false);
            _railTimer.Tick += (_, _) =>
            {
                string key = RailKey(_current);   // another world tab may have become active
                if (Rail.Active != key && !_detached.ContainsKey(_current)) Rail.Active = key;
                Rail.Refresh();
            };
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
                {
                    var doc = _model.Editor.Doc;
                    int parts = doc.Objects.Count(o => o.ParentId == null);
                    return new RailStatus(parts == 0 ? "Parça yok" : $"{parts} parça · {doc.Title}");
                }

                case "assembly":
                    return MontajStatus();   // [assembly] MainWindow.Montaj.cs

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
            if (doc.IsMontaj) SyncMontaj(doc, quiet: true);   // [assembly] a Montaj follows its projects
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
            if (fault is "dim-group" or "label-group")
            {
                // [flyout] a tool group's list opened: the PCB's dimensions or the circuit's labels
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var list = Theme.ToolFlyout.OpenForSnapshot(fault == "dim-group" ? _pcb.ToolDimension : _schematic.ToolLabel);
                    list?.UpdateLayout();
                    App.SnapshotTarget = list;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            if (fault == "fold-demo")
            {
                // [fold] some inspector sections folded, as a user left them (the snapshot's settings are its own)
                AppSettings.Current.FoldedSections.AddRange(new[] { "schematic:Kısayollar", "schematic:Simülasyon: parça toleransı", "pcb.board:Kart", "model:Dönüşüm", "world:Fizik" });
                Shell.FoldingStack.ApplyAll();
            }

            if (page == "model") _model.PrepareSnapshot(fault);
            if (page == "library") Library.PrepareSnapshot(fault);
            if (page == "settings" && fault != null) ((Pages.SettingsPage)_pages["settings"]).PrepareSnapshot(fault);   // [settings] // [libs] part creator / manager / import previews
            if (page == "pcb") _pcb.AutoLayoutForSnapshot(fault);
            if (fault == "problems") SnapshotProblems();   // [problems] the status bar's counts and the list
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

            if (page == "pcb" && fault != null && fault.StartsWith("stackup", StringComparison.Ordinal) && W.Schematic.Board is { } stackBoard)
            {
                // "stackup:12": the board setup window with that many copper layers (not applied).
                stackBoard.Stackup.CopperLayers = fault.Length > 8 && int.TryParse(fault.Substring(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 8;
                RobotWin.Studio.Pcb.PcbDialogs.SnapshotPreview = true;
                try { RobotWin.Studio.Pcb.PcbDialogs.BoardSetup(this, stackBoard, out _); }
                finally { RobotWin.Studio.Pcb.PcbDialogs.SnapshotPreview = false; }
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
            if (fault == "montaj-e2e" && App.SnapshotPath != null) RunMontajCheck(Path.ChangeExtension(App.SnapshotPath, ".txt"));   // [assembly]
            if (fault == "guide") StartGuide();   // [guide] the card at its first step
            if (fault is "debug-e2e" or "debug-registers" or "debug-timeline" && App.SnapshotPath != null) RunDebugCheck(Path.ChangeExtension(App.SnapshotPath, ".txt"), fault.Substring("debug-".Length));   // [debug]
            if (fault == "tabs" && App.SnapshotPath != null) RunTabsCheck(Path.ChangeExtension(App.SnapshotPath, ".txt"));   // [tabs]
            if (fault == "keys" && App.SnapshotPath != null) RunKeysCheck(Path.ChangeExtension(App.SnapshotPath, ".txt"));   // [keys]
            if (fault == "debug-e2e-circuit" && App.SnapshotPath != null) RunCircuitDebugCheck(Path.ChangeExtension(App.SnapshotPath, ".txt"));
            if (fault != null && fault.StartsWith("guide-e2e", StringComparison.Ordinal) && App.SnapshotPath != null)   // guide-e2e[:N] — N steps only
            {
                RunGuideCheck(Path.ChangeExtension(App.SnapshotPath, ".txt"), fault.Length > 10 && int.TryParse(fault.Substring(10), out int steps) ? steps : int.MaxValue);
            }
            if (fault == "check-demo")
            {
                LoadCheckDemo();   // [integrity] the project check's window on a project from another computer
                CheckProject(quiet: false);
            }

            if (fault == "problems-check")
            {
                LoadCheckDemo();   // [problems] the project check's findings in the Problems panel
                SnapshotProblemsAfterCheck();
            }

            if (fault == "check-montaj")
            {
                LoadMontajCheckDemo();   // [integrity] the check's window on a project its Montaj is behind
                CheckProject(quiet: false);
            }

            if (fault == "versions-demo") LoadVersionsDemo();   // [versions]

            if (fault == "check-notice")
            {
                LoadCheckDemo();   // [integrity] the notice after opening it
                CheckProject(quiet: true);
            }

            if (fault == "montaj-dialog")
            {
                // [assembly] The Montaj set-up window with a second project open beside this one.
                var first = W.DesignDocument;
                W.Load(RobotTwin.CoreSim.Mechanics.VehicleTemplates.LineFollower(W.Library), null);
                W.Activate(first);
                Shell.MontajDialog.SnapshotPreview = true;
                try { Shell.MontajDialog.Ask(this, W, first, _model.PartCount() > 0 ? _model.PartCount() : 10, true); }
                finally { Shell.MontajDialog.SnapshotPreview = false; }
            }
            // The debugger check opened its own world above: the page's template preparation would replace it with the
            // first template ("debug-e2e" names none) and the upload would restart that one.
            if (page == "world" && fault != null && (fault.Split('#')[0].EndsWith(".urdf", StringComparison.OrdinalIgnoreCase) || fault.Split('#')[0].EndsWith(".xacro", StringComparison.OrdinalIgnoreCase)))
            {
                // [urdf] --fault <file.urdf|file.xacro>[#link]: the robot in a world of its own, that link selected.
                string file = fault.Split('#')[0];
                NewWorld(new WorldDocument { Title = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)) });
                _world.ImportRobot(file, quiet: true);
                if (fault.Contains('#') && W.ActiveWorld?.Objects.FirstOrDefault(o => o.Name == fault.Split('#')[1]) is { } link) _world.Scene.Select(link.Id);
                _world.RobotSnapshot();   // --arm gripper --arm-target x,y,z --arm-preview: its kinematics
            }
            else if (page == "world" && fault != null && fault.Split('#')[0].EndsWith(".rwworld", StringComparison.OrdinalIgnoreCase))
            {
                // --fault <file.rwworld>[#object] [--simulate s]: a saved world, that object selected, run for s seconds.
                NewWorld(WorldDocument.Load(fault.Split('#')[0]));
                _world.PrepareSavedWorldSnapshot(fault.Contains('#') ? fault.Split('#')[1] : null, simulate);
            }
            else if (page == "world" && fault is not ("debug-e2e" or "debug-registers" or "debug-timeline")) _world.PrepareSnapshot(fault, simulate);
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
            if (e.ChangedButton == MouseButton.Middle && (sender as FrameworkElement)?.DataContext is OpenDocument { IsPinned: false } doc)
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

        /// <summary>[code] Shows the world and runs it from the start (after code was written to one of its boards).</summary>
        public void RunWorld()
        {
            Navigate("world");
            _world.RestartSimulation();
        }

        /// <summary>[boards] Opens a board of the active world in the circuit / code pages (Pages/WorldPage.xaml.cs).</summary>
        public void OpenWorldBoard(RobotTwin.CoreSim.Sandbox.WorldObject board) => _world.OpenBoard(board);

        private void OnLogoClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Navigate("home");

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
                        CheckProject(quiet: true);   // [integrity]
                        return;

                    case ".kicad_sch":
                        ImportKiCadSchematic(path);   // [kicad-sch]
                        return;

                    case ".fmu":
                        OpenFmu(path);   // [fmi]
                        return;

                    case ".urdf":
                    case ".xacro":
                        // [urdf] A robot description opens in a new world of its own.
                        NewWorld(new WorldDocument { Title = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)) });
                        _world.ImportRobot(path);
                        return;

                    default:
                        W.Open(path);
                        Navigate("schematic");
                        CheckProject(quiet: true);   // [integrity]
                        return;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(this, "Dosya açılamadı", ex.Message, "Tamam");
            }
        }

        /// <summary>
        /// [kicad-sch] Opens a KiCad schematic (its sub-sheets from the same folder) as a new, unsaved circuit: its symbols on
        /// the simulation models, its nets as KiCad has them. What was left out or only drawn is said.
        /// </summary>
        private void ImportKiCadSchematic(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            string project = Path.ChangeExtension(path, ".kicad_pro");   // its bus aliases, when the project has some
            var result = KiCadSchematicImport.Import(File.ReadAllText(path), W.Library, f =>
            {
                string p = Path.Combine(dir, f);
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }, File.Exists(project) ? File.ReadAllText(project) : null);
            if (result.Design.Root.Title.Length == 0) result.Schematic.Title = Path.GetFileNameWithoutExtension(path);
            W.Load(result.Schematic, null);
            Navigate("schematic");
            W.Status = string.Format(System.Globalization.CultureInfo.InvariantCulture, RobotWin.Studio.Shell.Lang.T("KiCad şeması açıldı: {0} parça modele eşlendi, {1} genel parça, {2} alınmadı"),
                result.Mapped, result.Generic, result.Skipped);
            var issues = result.Report.Where(l => l.StartsWith("uyarı", StringComparison.Ordinal) || l.Contains("alınmadı", StringComparison.Ordinal)).ToList();
            if (issues.Count == 0) return;
            string more = issues.Count > 12 ? "\n" + string.Format(System.Globalization.CultureInfo.InvariantCulture, RobotWin.Studio.Shell.Lang.T("… ve {0} satır daha"), issues.Count - 12) : string.Empty;
            Dialog.Show(this, "KiCad şeması içe aktarıldı",
                string.Format(System.Globalization.CultureInfo.InvariantCulture, RobotWin.Studio.Shell.Lang.T("{0} parça modele eşlendi, {1} genel parça (bacakları netleri taşır, elektriksel modeli yok), {2} parça alınmadı."),
                    result.Mapped, result.Generic, result.Skipped) + "\n\n" + string.Join("\n", issues.Take(12)) + more, "Tamam");
        }

        /// <summary>
        /// [fmi] Opens an FMU as a new, unsaved circuit with the FMU's block placed on it: the FMU is a part the project
        /// carries (not added to the library), its inputs and outputs are pins, REF is the reference to join to ground.
        /// </summary>
        private void OpenFmu(string path)
        {
            RobotTwin.CoreSim.Formats.CustomPart part;
            try
            {
                part = RobotTwin.CoreSim.Fmi.FmuImport.ToCustomPart(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is InvalidDataException || ex is FormatException)
            {
                Dialog.Show(this, "FMU açılamadı", Path.GetFileName(path) + ": " + ex.Message, "Tamam");
                return;
            }

            part.Register(W.Library);
            var package = new RobotTwin.CoreSim.Formats.ProjectPackage { Schematic = new RobotTwin.CoreSim.Design.Schematic { Title = part.Name } };
            package.Parts.Add(part);
            package.Schematic.AddPart(W.Library, part.Id, new RobotTwin.CoreSim.Design.GridPoint(0, 0));
            W.LoadPackage(package, null);
            Navigate("schematic");
            W.Status = string.Format(System.Globalization.CultureInfo.InvariantCulture, RobotWin.Studio.Shell.Lang.T("FMU açıldı: {0}; girişleri ve çıkışları pinlerinde, REF'i toprağa bağlayın"), part.Name);
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
                Filter = RobotWin.Studio.Shell.Lang.Filter("Simülasyon kaydı (*.rwsim)|*.rwsim"),
                Title = RobotWin.Studio.Shell.Lang.T("Simülasyon kaydını kaydet"),
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
        /// <summary>
        /// [home] The circuit as a picture for its file: the whole sheet drawn from the design (no grid, selection or zoom of
        /// the moment) on a clear background, 640 × 400 at most; the start page's card puts its colour behind it.
        /// </summary>
        private byte[]? RenderPreview()
        {
            if (W.Schematic.Parts.Count == 0) return null;
            // Drawn at about the card's own size, so the lines keep their width instead of thinning out when shrunk.
            _schematic.Canvas.RenderSheet(out var full, 20, clearBackground: true);
            if (full.Width < 1 || full.Height < 1) return null;
            double ppu = 20 * Math.Min(1, Math.Min(320 / full.Width, 200 / full.Height));
            var drawing = _schematic.Canvas.RenderSheet(out var size, ppu, clearBackground: true);
            int w = Math.Max(1, (int)Math.Ceiling(size.Width)), h = Math.Max(1, (int)Math.Ceiling(size.Height));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawDrawing(drawing);

            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            return Logo.EncodePng(bmp);
        }

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter(OpenFilter), Title = RobotWin.Studio.Shell.Lang.T("Aç"), Multiselect = true };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames) OpenFile(f);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var doc = W.ActiveDocument;
            if (doc.IsMontaj)
            {
                SaveMontajProject(doc, saveAs: false);   // [assembly] a Montaj is saved inside its project
                return;
            }

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
            if (doc.IsMontaj) return SaveMontajProject(doc, saveAs: true);   // [assembly]
            if (doc.IsEmbedded)
            {
                // "Save as" on a board inside a world exports it as a stand-alone project.
                var export = new SaveFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter(ProjectFilter), Title = RobotWin.Studio.Shell.Lang.T("Kartı ayrı proje olarak kaydet"), FileName = doc.Package.Schematic.Title, DefaultExt = ".rwproj" };
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
                Filter = RobotWin.Studio.Shell.Lang.Filter(world ? WorldFilter : ProjectFilter),
                Title = RobotWin.Studio.Shell.Lang.T("Farklı kaydet"),
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
            if (!dirty || doc.IsEmbedded || doc.IsMontaj) return true;   // a Montaj is kept in its project, which asks
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

        /// <summary>
        /// [keys] --snapshot --fault keys: the page keys move and come back (the schematic's wire tool to K, the PCB's route
        /// to Ctrl+R, a clash between two schematic tools), in memory only. Results go to a log.
        /// </summary>
        private void RunKeysCheck(string logPath)
        {
            var log = new System.Text.StringBuilder();
            int failures = 0;
            void Check(string what, bool ok)
            {
                if (!ok) failures++;
                log.AppendLine((ok ? "OK   " : "FAIL ") + what);
            }

            (bool Ok, string Key) Press(string page, Key key, ModifierKeys mods = ModifierKeys.None)
            {
                bool ok = Shortcuts.Translate(page, ref key, ref mods);
                return (ok, Shortcuts.Format(key, mods));
            }

            Check("varsayılan: W kablo aracına gider", Press(PageShortcuts.Schematic, Key.W) == (true, "W"));
            Shortcuts.Set("devre.kablo", "K");
            Check("kablo K'ye taşındı: K, W olarak gider", Press(PageShortcuts.Schematic, Key.K) == (true, "W"));
            Check("eski W artık bir şey yapmaz", !Press(PageShortcuts.Schematic, Key.W).Ok);
            Check("başka tuşlar olduğu gibi (R döndür)", Press(PageShortcuts.Schematic, Key.R) == (true, "R"));
            Check("PCB sayfası etkilenmez (W yol genişliği)", Press(PageShortcuts.Pcb, Key.W) == (true, "W"));
            Shortcuts.Set("pcb.rota", "Ctrl+R");
            Check("PCB rota Ctrl+R'ye taşındı: X olarak gider", Press(PageShortcuts.Pcb, Key.R, ModifierKeys.Control) == (true, "X"));
            Shortcuts.Set("dunya.kablo", "Ctrl+K");
            Check("Dünya: kablo aracı Ctrl+K'ye taşındı, K olarak gider", Press(PageShortcuts.World, Key.K, ModifierKeys.Control) == (true, "K"));
            Shortcuts.Reset("dunya.kablo");
            Shortcuts.Set("devre.etiket", "K");
            var wire = Shortcuts.All.First(c => c.Id == "devre.kablo");
            Check("iki araç aynı tuşta: çakışma bildirilir", Shortcuts.Clashes(wire).Any(c => c.Id == "devre.etiket"));
            Check("başka sayfanın aynı tuşu çakışma sayılmaz", !Shortcuts.Clashes(wire).Any(c => c.Page != PageShortcuts.Schematic));
            Shortcuts.Reset("devre.kablo");
            Shortcuts.Reset("devre.etiket");
            Shortcuts.Reset("pcb.rota");
            Check("sıfırlayınca W yine kablo", Press(PageShortcuts.Schematic, Key.W) == (true, "W"));
            Check("sıfırlayınca K bir şey değildir (olduğu gibi geçer)", Press(PageShortcuts.Schematic, Key.K) == (true, "K"));
            Check("pencere bağlamalarında sayfa tuşu yok", !InputBindings.OfType<KeyBinding>().Any(b => b.Modifiers == ModifierKeys.None && b.Key == Key.W));
            log.AppendLine(failures == 0 ? "TÜMÜ GEÇTİ" : failures + " HATA");
            File.WriteAllText(logPath, log.ToString());
        }

        /// <summary>[lang] Closes as usual (asking about unsaved work) and, once closed, starts the studio again.</summary>
        public void Restart()
        {
            Close();
            if (IsVisible) return;   // the user chose to stay
            if (Environment.ProcessPath is { } exe)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
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

        private void OnRailButton(object sender, RoutedEventArgs e) => SetRailHidden(RailButton.IsChecked != true);

        /// <summary>[rail] Hides or shows the left rail (kept between sessions); the pages stay a click away in the title bar's button, Ctrl+1…7 and the palette.</summary>
        public void SetRailHidden(bool hidden, bool save = true)
        {
            if (save && !_snapshotRail)
            {
                AppSettings.Current.NavHidden = hidden;
                AppSettings.Current.Save();
            }

            bool shown = !hidden && !_focusMode;
            NavColumn.Width = shown ? GridLength.Auto : new GridLength(0);
            NavBar.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            RailButton.IsChecked = !hidden;
            if (save) W.Status = hidden ? "Sol şerit gizlendi: başlık çubuğundaki şerit düğmesi ya da Ctrl+Shift+B geri getirir" : "Sol şerit gösteriliyor";
        }

        /// <summary>Hides the navigation rail and status bar to give the editor the whole window.</summary>
        public void SetFocusMode(bool on)
        {
            _focusMode = on;
            bool rail = !on && !AppSettings.Current.NavHidden;
            NavColumn.Width = rail ? GridLength.Auto : new GridLength(0);
            NavBar.Visibility = rail ? Visibility.Visible : Visibility.Collapsed;
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
