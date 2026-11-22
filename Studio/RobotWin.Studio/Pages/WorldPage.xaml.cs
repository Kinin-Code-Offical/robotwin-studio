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
using Microsoft.Win32;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Examples;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// "Dünya" page: an open sandbox. Place any number of designed boards, catalogue
    /// parts, mechanical bodies and imported models; wire pins with cables, join bodies
    /// with joints and run everything together.
    /// </summary>
    public partial class WorldPage : UserControl
    {
        private static readonly string[] Materials = { "PLA", "PETG", "ABS", "AKRİLİK", "ALÜMİNYUM", "ÇELİK", "KARBON", "AHŞAP", "BALSA", "KURŞUN", "MDF", "KAUÇUK", "FR4" };

        private readonly MainWindow _main;
        private readonly WorldSession _session = new WorldSession(PartLibrary.Default);

        /// <summary>State and time of the world simulation (for the navigation rail).</summary>
        public (Simulation.SimState State, double Time) SimulationState => (_session.State, _session.Time);
        private WorldDocument? _shown;
        private long _serialShown;
        private DateTime _lastPanels = DateTime.MinValue;

        public WorldPage(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            Scene.Session = _session;
            _session.CablePins = Scene.CablePinsOf;   // [phys] cables become ropes in the running world
            _session.CableSeed = Scene.CableSeedOf;   // [phys]
            Scene.MassOf = o => W.ActiveWorld is { } w ? EstimateMass(w, o) / 1000.0 : 0.01;
            Scene.Grid = AppSettings.Current.WorldGridMm / 1000.0;
            Scene.CableSelectionChanged += _ =>
            {
                BuildInspector();
                if (DockCables.IsChecked == true) BuildCableTable();
            };
            Scene.SelectionChanged += _ =>
            {
                BuildInspector();
                if (TabScene.IsChecked == true) BuildPalette();
            };
            Scene.BeginEdit += () => W.BeginWorldChange();
            Scene.EditCommitted += msg => Commit(msg, rebuild: false);
            Scene.CableRequested += AddCable;
            Scene.JointTargetPicked += OnJointTarget;
            LogList.ItemsSource = _session.Log;
            _session.Updated += (_, _) => OnSimUpdated();
            _session.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateRunButton));
            _session.Failure += (obj, msg) => Dispatcher.BeginInvoke(new Action(() =>
            {
                Scene.Burst(obj);
                W.Status = $"Arıza: {W.ActiveWorld?.Find(obj)?.Name ?? obj} — {msg}";
            }));
            W.ActiveDocumentChanged += (_, _) =>
            {
                if (IsVisible) OnShown();
            };
            W.WorldChanged += (_, _) =>
            {
                if (W.ActiveWorld != null && W.ActiveWorld == _shown && !_terrainQuiet) Refresh(); // [terrain] strokes do not rebuild the scene
            };
            AppSettings.Changed += (_, _) => Scene.Grid = AppSettings.Current.WorldGridMm / 1000.0;
            PreviewKeyDown += OnKeys;
            BuildPalette();
            UpdateRunButton();
            InitRadio();   // [radio] Pages/WorldPage.Radio.cs
            InitReady();   // [objs] Pages/WorldPage.Ready.cs
            InitSense();   // [sense] Pages/WorldPage.Sense.cs
            InitSdk();   // [sdk] Pages/WorldPage.Sdk.cs
            InitTerrain(); // [terrain] Pages/WorldPage.Terrain.cs
        }

        private static Workspace W => Workspace.Current;
        private static PartLibrary Lib => PartLibrary.Default;
        private WorldDocument? World => W.ActiveWorld;

        // ================================================================ document

        public void OnShown()
        {
            var world = World;
            NoWorld.Visibility = world == null ? Visibility.Visible : Visibility.Collapsed;
            if (world == _shown) return;
            StopSimulation();
            _shown = world;
            Scene.Load(world);
            BuildPalette();
            BuildInspector();
            UpdateRunButton();
            RefreshCollisionView();   // [hull]
        }

        /// <summary>After any change of the world document.</summary>
        private void Refresh()
        {
            Scene.Rebuild();
            RefreshCollisionView();   // [hull] the parts moved or changed: rebuild their bodies
            if (TabScene.IsChecked == true || TabBoards.IsChecked == true || TabEnv.IsChecked == true) BuildPalette();
            else if (TabSdk.IsChecked == true) BuildPalette();   // [sdk]
            BuildInspector();
        }

        private void Commit(string status, bool rebuild = true)
        {
            W.CommitWorldChange(status);   // raises WorldChanged → Refresh()
            if (!rebuild) BuildInspector();
        }

        private void Edit(string status, Action change)
        {
            if (World == null) return;
            W.BeginWorldChange();
            change();
            Commit(status);
        }

        public void StopSimulation()
        {
            _session.Stop();
            Scene.ClearEffects();
            UpdateRunButton();
            RefreshCollisionView();   // [hull] back to the bodies a run would build
        }

        private void OnNewWorld(object sender, RoutedEventArgs e) => _main.NewWorld();

        private void OnOpenWorld(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "RobotWin dünyası (*.rwworld)|*.rwworld", Title = "Dünya aç" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) _main.OpenFile(dlg.FileName);
        }

        /// <summary>Puts the current circuit project into a new world as its first board.</summary>
        private void OnWorldFromDesign(object sender, RoutedEventArgs e)
        {
            W.FlushActiveDesign();
            var pkg = W.Project;
            var world = new WorldDocument { Title = (string.IsNullOrWhiteSpace(pkg.Schematic.Title) ? "Devre" : pkg.Schematic.Title) + " dünyası" };
            world.AddBoard(world.AddDesign(pkg, W.FilePath), new WorldPose());
            _main.NewWorld(world);
        }

        // ================================================================ palette

        private void OnPaletteTab(object sender, RoutedEventArgs e) => BuildPalette();

        private void OnSearch(object sender, TextChangedEventArgs e) => BuildPalette();

        private void BuildPalette()
        {
            Palette.Children.Clear();
            SearchRow.Visibility = TabParts.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (TerrainPalette()) return; // [terrain] Arazi tab
            if (TabScene.IsChecked == true) BuildScenePalette();
            else if (TabBoards.IsChecked == true) BuildBoardsPalette();
            else if (TabParts.IsChecked == true) BuildPartsPalette();
            else if (TabMech.IsChecked == true) BuildMechPalette();
            else if (TabSdk.IsChecked == true) BuildSdkPalette();   // [sdk]
            else BuildEnvironmentPalette();
        }

        private TextBlock Caption(string text, double top = 12) => new TextBlock { Text = text, Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, top, 0, 6) };

        private TextBlock Note(string text) => new TextBlock { Text = text, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };

        private Button Action(string text, Action click, string? tip = null, bool primary = false)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 10, 5), ToolTip = tip };
            if (primary) b.Style = (Style)FindResource("Btn.Primary");
            b.Click += (_, _) => click();
            return b;
        }

        private FrameworkElement Row(string title, string? subtitle, Action click, string? tip = null)
        {
            var border = new Border { Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = tip };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(subtitle)) sp.Children.Add(new TextBlock { Text = subtitle, Style = (Style)FindResource("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis });
            border.Child = sp;
            border.MouseEnter += (_, _) => border.Background = (Brush)FindResource("B.Hover");
            border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonUp += (_, _) => click();
            return border;
        }

        private void BuildBoardsPalette()
        {
            var world = World;
            Palette.Children.Add(Note("Kendi tasarladığınız kartlar (devre + PCB + kod) bu dünyaya ayrı nesneler olarak girer. Aynı kartı istediğiniz kadar yerleştirebilirsiniz."));
            if (world != null && world.Designs.Count > 0)
            {
                Palette.Children.Add(Caption("Bu dünyadaki kartlar"));
                foreach (var d in world.Designs)
                {
                    int count = world.Objects.Count(o => o.DesignId == d.Id);
                    var sch = d.Package.Schematic;
                    var design = d;
                    Palette.Children.Add(Row(d.Name, $"{sch.Parts.Count} parça · {count} adet yerleşik · tıklayın: bir tane daha koy", () => PlaceDesign(design),
                        "Sağdaki panelden 'Kartı düzenle' ile devresini, PCB'sini ve kodunu açabilirsiniz."));
                }
            }

            Palette.Children.Add(Caption("Kart ekle"));
            var open = W.Documents.Where(d => d.Kind == DocumentKind.Design && !d.IsEmbedded).ToList();
            foreach (var doc in open)
            {
                var d = doc;
                if (d == W.DesignDocument) W.FlushActiveDesign();
                Palette.Children.Add(Row("Açık sekmeden: " + d.BaseTitle, $"{d.Package.Schematic.Parts.Count} parça", () => AddDesignFrom(d.Package, d.FilePath)));
            }

            Palette.Children.Add(Action("Proje dosyasından kart ekle (.rwproj)…", AddDesignFromFile));
            Palette.Children.Add(Action("Yeni boş kart tasarla", NewEmptyBoard, "Boş bir kart oluşturur ve devre sekmesinde açar; çizdikçe dünyadaki kart güncellenir."));
            Palette.Children.Add(Caption("Hazır modül kartları"));
            foreach (var id in new[] { "arduino-uno", "arduino-nano", "esp32-devkit-v1", "esp32-devkitc", "fc-brushed" })
            {
                var preset = Lib.FindPreset(id);
                if (preset == null) continue;
                Palette.Children.Add(Row(preset.Name, preset.Description, () => AddComponent(id)));
            }

            RadioPalette();   // [radio]
        }

        private void BuildPartsPalette()
        {
            string q = SearchBox.Text.Trim();
            if (q.Length == 0) ReadyLightsPalette();   // [objs] lights and room light
            var order = new[] { "Geliştirme", "Elektromekanik", "Güç", "Sensör", "Ekran", "Anahtarlama" };
            int Rank(string cat)
            {
                for (int i = 0; i < order.Length; i++)
                {
                    if (cat.StartsWith(order[i], StringComparison.OrdinalIgnoreCase)) return i;
                }

                return order.Length;
            }

            var groups = Lib.Presets
                .Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => p.Category)
                .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key, StringComparer.CurrentCulture);
            int shown = 0;
            foreach (var g in groups)
            {
                Palette.Children.Add(Caption(g.Key, shown == 0 ? 0 : 12));
                foreach (var p in g.Take(q.Length == 0 ? 40 : 200))
                {
                    var id = p.Id;
                    Palette.Children.Add(Row(p.Name, p.Description, () => AddComponent(id)));
                    shown++;
                }
            }

            if (shown == 0) Palette.Children.Add(Note("Eşleşen parça yok."));
        }

        private void BuildMechPalette()
        {
            Palette.Children.Add(Note("Gövde, şase, kol, tekerlek ve platform parçaları. Malzeme ve dolulukla kütlesi hesaplanır; servo koluna, motor miline veya birbirine bağlanabilir."));
            Palette.Children.Add(Caption("Temel şekiller"));
            Palette.Children.Add(Row("Kutu", "100 × 60 × 40 mm", () => AddBody("box", 0.1, 0.06, 0.04)));
            Palette.Children.Add(Row("Plaka / şase", "200 × 150 × 3 mm", () => AddBody("plate", 0.2, 0.15, 0.003)));
            Palette.Children.Add(Row("Kol / çubuk", "120 × 18 × 3 mm", () => AddBody("box", 0.12, 0.018, 0.003)));
            Palette.Children.Add(Row("Silindir", "Ø 40 × 60 mm", () => AddBody("cylinder", 0.04, 0.04, 0.06)));
            Palette.Children.Add(Row("Tekerlek", "Ø 65 × 26 mm kauçuk lastik", () => AddBody("wheel", 0.065, 0.026, 0.026)));
            Palette.Children.Add(Row("Küre / bilye", "Ø 40 mm", () => AddBody("sphere", 0.04, 0.04, 0.04)));
            ShellPalette();   // [shell] hollow bodies with lids (Pages/WorldPage.Shells.cs)
            GasPalette();   // [gas] pressure vessels, motors, fins (Pages/WorldPage.Gas.cs)
            Palette.Children.Add(Caption("Uçuş yüzeyleri (gerçek kaldırma, sürükleme, moment)"));
            Palette.Children.Add(Row("Kanat", "1000 mm açıklık, 150 mm veter, Clark Y", () => AddBody("wing", 0.15, 1.0, 0.012, o => { o.Material = "AHŞAP"; o.MassKg = 0.08; o.Name = World!.UniqueName("Kanat"); o.Parameters["airfoil"] = "clarky"; o.Parameters["incidence"] = "2"; o.Parameters["control"] = "aileron"; })));
            Palette.Children.Add(Row("Yatay kuyruk + irtifa dümeni", "300 × 80 mm, NACA 0012", () => AddBody("wing", 0.08, 0.30, 0.006, o => { o.Material = "AHŞAP"; o.MassKg = 0.015; o.Name = World!.UniqueName("Yatay kuyruk"); o.Parameters["airfoil"] = "naca0012"; o.Parameters["incidence"] = "-2"; o.Parameters["control"] = "elevator"; o.Parameters["control.chord"] = "0.35"; })));
            Palette.Children.Add(Row("Dikey kuyruk + yön dümeni", "100 × 80 mm, NACA 0012", () => AddBody("wing", 0.08, 0.10, 0.006, o => { o.Material = "AHŞAP"; o.MassKg = 0.008; o.Name = World!.UniqueName("Dikey kuyruk"); o.Parameters["airfoil"] = "naca0012"; o.Parameters["symmetric"] = "false"; o.Parameters["control"] = "rudder"; })));
            Palette.Children.Add(Row("Masa / sabit zemin", "600 × 400 × 20 mm, sabit", () =>
            {
                AddBody("box", 0.6, 0.4, 0.02, o => { o.IsStatic = true; o.Material = "AHŞAP"; o.Name = World!.UniqueName("Masa"); });
            }));
            ReadyMechPalette();   // [objs] ready wheels, casters, propellers
            Palette.Children.Add(Caption("Dışarıdan model"));
            Palette.Children.Add(Action("3D model içe aktar (STL, OBJ, PLY)…", ImportModel, "Her şekil kullanılabilir; kütle, ağırlık merkezi ve atalet malzemeden hesaplanır."));
            CellPalette();   // [arm] robot cell parts (Pages/WorldPage.Cell.cs)
        }

        private void BuildEnvironmentPalette()
        {
            var world = World;
            if (world == null) return;
            var env = world.Environment;
            Palette.Children.Add(Caption("Dünya", 0));
            var title = new TextBox { Text = world.Title, Margin = new Thickness(0, 0, 0, 8) };
            title.LostFocus += (_, _) =>
            {
                if (title.Text != world.Title) Edit("Dünya adı değişti", () => world.Title = title.Text);
            };
            Palette.Children.Add(title);

            Palette.Children.Add(Caption("Yerçekimi"));
            var g = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (name, value) in new[] { ("Dünya · 9.81 m/s²", 9.81), ("Ay · 1.62 m/s²", 1.62), ("Mars · 3.71 m/s²", 3.71), ("Jüpiter · 24.8 m/s²", 24.79), ("Sıfır yerçekimi", 0.0) })
            {
                var item = new ComboBoxItem { Content = name, Tag = value };
                if (Math.Abs(env.Gravity - value) < 1e-3) item.IsSelected = true;
                g.Items.Add(item);
            }

            g.SelectionChanged += (_, _) =>
            {
                if (g.SelectedItem is ComboBoxItem it && Math.Abs((double)it.Tag - env.Gravity) > 1e-6) Edit("Yerçekimi değişti", () => env.Gravity = (double)it.Tag);
            };
            Palette.Children.Add(g);

            Palette.Children.Add(NumberField("Ortam sıcaklığı (°C)", env.AmbientC, v => Edit("Ortam sıcaklığı değişti", () => env.AmbientC = v), -40, 125));
            Palette.Children.Add(NumberField("Zemin genişliği (m)", env.Width, v => Edit("Zemin boyutu değişti", () => env.Width = v), 0.3, 50));
            Palette.Children.Add(NumberField("Zemin derinliği (m)", env.Depth, v => Edit("Zemin boyutu değişti", () => env.Depth = v), 0.3, 50));
            Palette.Children.Add(NumberField("Zemin sürtünmesi (μ)", env.Friction, v => Edit("Zemin sürtünmesi değişti", () => env.Friction = v), 0.02, 2));
            var walls = new CheckBox { Content = "Çevre duvarları", IsChecked = env.Walls, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 6, 0, 8) };
            walls.Click += (_, _) => Edit("Duvarlar değişti", () => env.Walls = walls.IsChecked == true);
            Palette.Children.Add(walls);
            Palette.Children.Add(Action("Engel ekle (200 × 200 × 150 mm)", () => Edit("Engel eklendi", () =>
            {
                var p = Scene.DropPoint();
                env.Obstacles.Add(new WorldObstacle { X = p.X + 0.3, Y = p.Y });
            })));
            if (env.Obstacles.Count > 0) Palette.Children.Add(Action($"Engelleri temizle ({env.Obstacles.Count})", () => Edit("Engeller silindi", () => env.Obstacles.Clear())));
            SenseEnvironmentPalette(world);   // [sense] light, floor, tapes, GPS origin
            SdkEnvironmentPalette(world);   // [sdk] atmosphere, wind, aerodynamics, force fields
        }

        private FrameworkElement NumberField(string label, double value, Action<double> set, double min = double.MinValue, double max = double.MaxValue, string format = "0.###")
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary"), TextWrapping = TextWrapping.Wrap });
            var box = new TextBox { Text = value.ToString(format, CultureInfo.InvariantCulture), HorizontalContentAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            void Apply()
            {
                if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    v = Math.Max(min, Math.Min(max, v));
                    if (Math.Abs(v - value) > 1e-12)
                    {
                        value = v;
                        set(v);
                    }
                }
                else
                {
                    box.Text = value.ToString(format, CultureInfo.InvariantCulture);
                }
            }

            box.LostFocus += (_, _) => Apply();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Apply();
            };
            return grid;
        }

        // ================================================================ adding things

        private WorldPose NewPose()
        {
            var p = Scene.DropPoint();
            // Keep new objects from landing exactly on top of each other.
            double x = p.X, y = p.Y;
            var world = World!;
            for (int i = 0; i < 20 && world.Objects.Any(o => Math.Abs(o.Pose.X - x) < 0.03 && Math.Abs(o.Pose.Y - y) < 0.03 && o.Pose.Z < 0.01); i++) x += 0.06;
            return new WorldPose { X = x, Y = y, Z = Scene.GroundZAt(x, y) }; // [terrain] on the terrain
        }

        private void EnsureWorld()
        {
            if (World == null) _main.NewWorld();
        }

        public void AddComponent(string presetId)
        {
            EnsureWorld();
            WorldObject? added = null;
            Edit("Eklendi", () => added = World!.AddComponent(Lib, presetId, NewPose()));
            if (added != null)
            {
                Scene.Select(added.Id);
                W.Status = $"{added.Name} dünyaya eklendi · pinlerini Kablo aracıyla bağlayın";
            }
        }

        private void AddBody(string shape, double sx, double sy, double sz, Action<WorldObject>? tweak = null)
        {
            EnsureWorld();
            WorldObject? added = null;
            Edit("Eklendi", () =>
            {
                added = World!.AddBody(shape, sx, sy, sz, NewPose());
                tweak?.Invoke(added);
            });
            if (added != null) Scene.Select(added.Id);
        }

        private void PlaceDesign(WorldDesign design)
        {
            WorldObject? added = null;
            Edit("Kart yerleştirildi", () => added = World!.AddBoard(design, NewPose()));
            if (added != null) Scene.Select(added.Id);
        }

        private void AddDesignFrom(ProjectPackage package, string? path)
        {
            EnsureWorld();
            WorldObject? added = null;
            Edit("Kart eklendi", () =>
            {
                var d = World!.AddDesign(package, path);
                added = World.AddBoard(d, NewPose());
            });
            if (added != null)
            {
                Scene.Select(added.Id);
                W.Status = $"'{added.Name}' kartı dünyaya kopyalandı (dünya kendi kopyasını tutar; 'Kartı düzenle' ile değiştirin)";
            }
        }

        private void AddDesignFromFile()
        {
            var dlg = new OpenFileDialog { Filter = "RobotWin projesi (*.rwproj)|*.rwproj", Title = "Dünyaya kart ekle", Multiselect = true };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            foreach (var f in dlg.FileNames)
            {
                try
                {
                    AddDesignFrom(ProjectPackage.Load(f), f);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
                {
                    Dialog.Show(Window.GetWindow(this), "Kart eklenemedi", ex.Message, "Tamam");
                }
            }
        }

        private void NewEmptyBoard()
        {
            EnsureWorld();
            var pkg = new ProjectPackage { Schematic = new Schematic { Title = World!.UniqueName("Yeni kart") } };
            var s = pkg.Schematic;
            s.AddPart(Lib, "hdr-1x4", new GridPoint(0, 0));
            WorldObject? added = null;
            Edit("Yeni kart", () =>
            {
                var d = World.AddDesign(pkg);
                added = World.AddBoard(d, NewPose());
            });
            if (added != null) EditBoard(added);
        }

        private void ImportModel()
        {
            var dlg = new OpenFileDialog { Filter = "3D model (*.stl;*.obj;*.ply)|*.stl;*.obj;*.ply", Title = "3D model içe aktar" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var mesh = MeshModel.Load(dlg.FileName);
                EnsureWorld();
                WorldObject? added = null;
                Edit("Model eklendi", () => added = World!.AddModel(mesh, Path.GetFileName(dlg.FileName), NewPose()));
                if (added != null)
                {
                    Scene.Select(added.Id);
                    var b = mesh.Bounds;
                    W.Status = $"{Path.GetFileName(dlg.FileName)}: {mesh.TriangleCount} üçgen, {(b.Max.X - b.Min.X) * 1000:F0} × {(b.Max.Y - b.Min.Y) * 1000:F0} × {(b.Max.Z - b.Min.Z) * 1000:F0} mm";
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is NotSupportedException)
            {
                Dialog.Show(Window.GetWindow(this), "Model okunamadı", ex.Message, "Tamam");
            }
        }

        // ================================================================ cables and joints

        private static string CableColor(string a, string b)
        {
            string s = (a + " " + b).ToUpperInvariant();
            if (s.Contains("GND") || s.EndsWith(" -", StringComparison.Ordinal) || s.StartsWith("- ", StringComparison.Ordinal) || s.Contains(".-") || s.Contains("V-")) return "#1F1F23";
            if (s.Contains("5V") || s.Contains("VCC") || s.Contains("V+") || s.Contains("VIN") || s.Contains("+") || s.Contains("3V3") || s.Contains("VBAT")) return "#E5484D";
            if (s.Contains("SDA")) return "#3E7CC8";
            if (s.Contains("SCL")) return "#F2D04B";
            return "#F2A33B";
        }

        private void AddCable(CableEnd a, CableEnd b)
        {
            var world = World;
            if (world == null) return;
            if (world.Cables.Any(c => (c.A.ToString() == a.ToString() && c.B.ToString() == b.ToString()) || (c.A.ToString() == b.ToString() && c.B.ToString() == a.ToString())))
            {
                W.Status = "Bu iki pin zaten kabloyla bağlı";
                return;
            }

            var oa = world.Find(a.ObjectId);
            var ob = world.Find(b.ObjectId);
            bool power = oa?.PartId == "dc-motor" || ob?.PartId == "dc-motor" || oa?.PartId == "battery" || ob?.PartId == "battery";
            Edit($"Kablo: {oa?.Name} {a.Pin} → {ob?.Name} {b.Pin}", () => world.Connect(a, b, power ? 22 : 24, CableColor(a.Pin, b.Pin)));
        }

        private void OnJointTarget(string targetId)
        {
            var world = World;
            if (world == null) return;
            var source = Scene.Selected;
            if (source == null)
            {
                Scene.Select(targetId);
                W.Status = "Takılacak nesne seçildi; şimdi takılacağı yeri tıklayın";
                return;
            }

            if (source == targetId)
            {
                W.Status = "Bir nesne kendine bağlanamaz";
                return;
            }

            var target = world.Find(targetId)!;
            var menu = new ContextMenu();
            var port = Scene.PortOf(targetId);
            if (port is { } pf)
            {
                var portItem = new MenuItem { Header = $"{target.Name} · {pf.Label}ne sabitle (birlikte döner)" };
                portItem.Click += (_, _) => MakeJoint(source, targetId, WorldJointKind.Fixed, pf.Name);
                menu.Items.Add(portItem);
            }

            var fixedItem = new MenuItem { Header = $"{target.Name} gövdesine sabitle (vida / yapıştırıcı)" };
            fixedItem.Click += (_, _) => MakeJoint(source, targetId, WorldJointKind.Fixed, string.Empty);
            menu.Items.Add(fixedItem);
            var hinge = new MenuItem { Header = "Menteşe ile bağla (serbest döner)" };
            hinge.Click += (_, _) => MakeJoint(source, targetId, WorldJointKind.Hinge, string.Empty);
            menu.Items.Add(hinge);
            var slider = new MenuItem { Header = "Kızakla bağla (doğrusal kayar)" };
            slider.Click += (_, _) => MakeJoint(source, targetId, WorldJointKind.Slider, string.Empty);
            menu.Items.Add(slider);
            menu.IsOpen = true;
        }

        private void MakeJoint(string childId, string parentId, WorldJointKind kind, string port)
        {
            if (TryAttachReady(childId, parentId, kind, port)) return;   // [objs] snaps a ready wheel / propeller onto the shaft
            var world = World!;
            var child = world.Find(childId)!;
            var parent = world.Find(parentId)!;
            Edit($"{child.Name} → {parent.Name} bağlandı", () =>
            {
                world.Joints.RemoveAll(j => j.ObjectA == childId && j.Kind == WorldJointKind.Fixed && kind == WorldJointKind.Fixed);
                var pf = Scene.PortOf(parentId);
                world.Joints.Add(new WorldJoint
                {
                    Id = world.NextId("J"),
                    Kind = kind,
                    ObjectA = childId,
                    ObjectB = parentId,
                    PortB = port,
                    AnchorX = child.Pose.X,
                    AnchorY = child.Pose.Y,
                    AnchorZ = child.Pose.Z,
                    AxisX = pf?.Axis.X ?? 0,
                    AxisY = pf?.Axis.Y ?? 0,
                    AxisZ = pf?.Axis.Z ?? 1
                });
            });
            SetTool(WorldTool.Select);
            Scene.Select(childId);
        }

        // ================================================================ inspector

        private void BuildInspector()
        {
            Inspector.Children.Clear();
            var world = World;
            if (world == null) return;
            _diagValues = null;
            if (Scene.SelectedCable is { } cableId && world.Cables.FirstOrDefault(x => x.Id == cableId) is { } cable)
            {
                BuildCableDiagnostics(world, cable);
                return;
            }

            var o = world.Find(Scene.Selected);
            if (o == null)
            {
                BuildWorldSummary(world);
                return;
            }

            var name = new TextBox { Text = o.Name, FontSize = 15, Margin = new Thickness(0, 0, 0, 2) };
            name.LostFocus += (_, _) =>
            {
                if (name.Text.Trim().Length > 0 && name.Text != o.Name) Edit("Ad değişti", () => o.Name = name.Text.Trim());
            };
            Inspector.Children.Add(name);
            Inspector.Children.Add(Note(KindText(o)));
            CellSection(world, o);   // [arm] teach pendant / tool / conveyor / light curtain (Pages/WorldPage.Cell.cs)
            WingSection(world, o);   // [linkage] servo, horn, control horn, pushrod (Pages/WorldPage.Wings.cs)
            ShellSection(world, o);   // [shell] fit check first, then wall, cable holes, lid, X-ray / exploded (Pages/WorldPage.Shells.cs)
            GasSection(world, o);   // [gas] contents, nozzle, valve, grain, burst pressure, live readout (Pages/WorldPage.Gas.cs)
            LaunchSection(world, o);   // [launch] switches on a countdown or by hand, parachutes (Pages/WorldPage.Launch.cs)

            if (o.Kind == WorldObjectKind.Board) BoardSection(world, o);

            Inspector.Children.Add(Caption("Konum ve yön"));
            Inspector.Children.Add(Vector3Row("Konum (mm)", o.Pose.X * 1000, o.Pose.Y * 1000, o.Pose.Z * 1000, (x, y, z) =>
                Edit("Konum değişti", () => world.MoveAssembly(o, x / 1000 - o.Pose.X, y / 1000 - o.Pose.Y, Math.Max(0, z / 1000) - o.Pose.Z))));
            Inspector.Children.Add(Vector3Row("Dönme (°) X · Y · Z", o.Pose.Roll, o.Pose.Pitch, o.Pose.Yaw, (r, p, yw) =>
                Edit("Yön değişti", () =>
                {
                    o.Pose.Roll = r;
                    o.Pose.Pitch = p;
                    world.RotateAssembly(o, yw - o.Pose.Yaw);
                })));

            Inspector.Children.Add(Caption("Fizik"));
            var st = new CheckBox { Content = "Sabit (zemine/masaya sabitlenmiş)", IsChecked = o.IsStatic, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6) };
            st.Click += (_, _) => Edit("Sabitlik değişti", () => o.IsStatic = st.IsChecked == true);
            Inspector.Children.Add(st);
            if (o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model)
            {
                var mat = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
                foreach (var m in Materials) mat.Items.Add(new ComboBoxItem { Content = m, IsSelected = string.Equals(m, o.Material, StringComparison.OrdinalIgnoreCase) });
                mat.SelectionChanged += (_, _) =>
                {
                    if (mat.SelectedItem is ComboBoxItem it && (string)it.Content != o.Material) Edit("Malzeme değişti", () => o.Material = (string)it.Content);
                };
                Inspector.Children.Add(Labeled("Malzeme", mat));
                Inspector.Children.Add(NumberField("Doluluk (%)", o.Infill * 100, v => Edit("Doluluk değişti", () => o.Infill = v / 100), 5, 100, "0"));
            }

            Inspector.Children.Add(NumberField("Kütle (g, 0 = otomatik)", (o.MassKg ?? 0) * 1000, v => Edit("Kütle değişti", () => o.MassKg = v <= 0 ? null : v / 1000), 0, 1e6, "0.#"));
            Inspector.Children.Add(Note("Otomatik kütle: " + EstimateMass(world, o).ToString("0.#", CultureInfo.InvariantCulture) + " g"));

            if (o.Kind == WorldObjectKind.Body)
            {
                Inspector.Children.Add(Caption("Boyut"));
                Inspector.Children.Add(Vector3Row(o.Shape == "wheel" || o.Shape == "cylinder" ? "Çap · — · uzunluk (mm)" : "X · Y · Z (mm)", o.SizeX * 1000, o.SizeY * 1000, o.SizeZ * 1000, (x, y, z) =>
                    Edit("Boyut değişti", () =>
                    {
                        o.SizeX = Math.Max(0.001, x / 1000);
                        o.SizeY = Math.Max(0.001, y / 1000);
                        o.SizeZ = Math.Max(0.0005, z / 1000);
                    })));
                var color = new TextBox { Text = o.Color ?? string.Empty, Tag = "#RRGGBB (boş = malzeme rengi)" };
                color.LostFocus += (_, _) =>
                {
                    string c = color.Text.Trim();
                    if (c != (o.Color ?? string.Empty)) Edit("Renk değişti", () => o.Color = c.Length == 0 ? null : c);
                };
                Inspector.Children.Add(Labeled("Renk", color));
            }

            if (o.Kind == WorldObjectKind.Component) ComponentSection(o);
            ReadySection(world, o);   // [objs]
            RadioSection(world, o);   // [radio] controller pairing, USB plug, IR keys, ESP32 radio
            PortSection(world, o);
            CablesSection(world, o);
            JointsSection(world, o);
            SdkSection(world, o);   // [sdk] aerodynamics, behaviours, expressions
            LiveSection(o);

            EditorSection(world, o); // [terrain] hide / lock / group / prop size
            Inspector.Children.Add(Caption("İşlemler"));
            Inspector.Children.Add(Action("Çoğalt (Ctrl+D)", Duplicate));
            Inspector.Children.Add(Action("Sil (Del)", Delete));
        }

        private void BuildWorldSummary(WorldDocument world)
        {
            Inspector.Children.Add(new TextBlock { Text = world.Title, FontSize = 16, Margin = new Thickness(0, 0, 0, 4) });
            Inspector.Children.Add(Note($"{world.Objects.Count} nesne · {world.Designs.Count} kart tasarımı · {world.Cables.Count} kablo · {world.Joints.Count} mekanik bağlantı"));
            Inspector.Children.Add(Caption("Nasıl kullanılır"));
            Inspector.Children.Add(Note("1. Soldaki paletten kartlarınızı, parçaları (servo, motor, pil, sensör, ekran…), mekanik gövdeleri veya kendi 3D modelinizi ekleyin."));
            Inspector.Children.Add(Note("2. Nesneleri sürükleyerek yerleştirin; Shift ile yükseltin, R ile döndürün."));
            Inspector.Children.Add(Note("3. Kablo aracı (K) ile iki pine tıklayın: kablolar gerçek bakır direnci taşır, aşırı akımda ısınıp yanar."));
            Inspector.Children.Add(Note("4. Bağla aracı (B) ile bir nesneyi servo koluna, motor miline veya başka bir gövdeye takın."));
            Inspector.Children.Add(Note("5. 'Dünyayı çalıştır': bütün kartların yazılımları, tüm devre ve hareketli parçalar birlikte çalışır."));
            Inspector.Children.Add(Caption("Nesneler"));
            foreach (var o in world.Objects)
            {
                var id = o.Id;
                Inspector.Children.Add(Row(o.Name, KindText(o), () =>
                {
                    Scene.Select(id);
                    Scene.FrameSelected();
                }));
            }
        }

        private string KindText(WorldObject o)
        {
            switch (o.Kind)
            {
                case WorldObjectKind.Board:
                    var d = World?.FindDesign(o.DesignId);
                    return "Kart · " + (d?.Name ?? "?") + (d != null ? $" · {d.Package.Schematic.Parts.Count} parça" : string.Empty);
                case WorldObjectKind.Component:
                    var preset = o.PresetId != null ? Lib.FindPreset(o.PresetId) : null;
                    var def = o.PartId != null ? Lib.Find(o.PartId) : null;
                    return "Parça · " + (preset?.Name ?? def?.Name ?? o.PartId) + (def != null ? " · " + def.Category : string.Empty);
                case WorldObjectKind.Model:
                    return "3D model · " + (o.MeshName ?? "?") + " · " + o.Material;
                default:
                    return ReadyKind(o) ?? "Mekanik · " + o.Shape + " · " + o.Material;   // [objs]
            }
        }

        private FrameworkElement Labeled(string label, FrameworkElement control)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            // Labels wrap instead of being cut off ("Kilitlenme akımı (A)" no longer loses its unit).
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 6, 0), ToolTip = label });
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        private FrameworkElement Vector3Row(string label, double a, double b, double c, Action<double, double, double> set)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 3) });
            var grid = new UniformGrid { Columns = 3 };
            var boxes = new[] { a, b, c }.Select(v => new TextBox { Text = v.ToString("0.##", CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 4, 0), HorizontalContentAlignment = HorizontalAlignment.Right }).ToArray();
            void Apply()
            {
                var vals = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    if (!double.TryParse(boxes[i].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i])) return;
                }

                if (Math.Abs(vals[0] - a) > 1e-9 || Math.Abs(vals[1] - b) > 1e-9 || Math.Abs(vals[2] - c) > 1e-9) set(vals[0], vals[1], vals[2]);
            }

            foreach (var box in boxes)
            {
                box.LostFocus += (_, _) => Apply();
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) Apply();
                };
                grid.Children.Add(box);
            }

            sp.Children.Add(grid);
            return sp;
        }

        private void BoardSection(WorldDocument world, WorldObject o)
        {
            var d = world.FindDesign(o.DesignId);
            if (d == null) return;
            Inspector.Children.Add(Action("Kartı düzenle (devre · PCB · kod)", () => EditBoard(o), "Kart kendi sekmesinde açılır; yaptığınız her değişiklik bu dünyadaki tüm kopyalarına işlenir.", primary: true));
            Inspector.Children.Add(Action("Kartın kodunu yaz", () =>
            {
                EditBoard(o);
                _main.Navigate("code");
            }));
            var fw = d.Package.Firmware;
            if (fw.Count > 0) Inspector.Children.Add(Note("Yazılım: " + string.Join(", ", fw.Select(f => f.BoardRefDes + " · " + f.FileName + (f.IsBuildCurrent ? " (derli)" : " (derlenmemiş)")))));
            int copies = world.Objects.Count(x => x.DesignId == d.Id);
            if (copies > 1) Inspector.Children.Add(Note($"Bu tasarımdan dünyada {copies} kart var; hepsi aynı devreyi paylaşır."));
            Inspector.Children.Add(Action("Ayrı proje dosyası olarak dışa aktar…", () =>
            {
                var dlg = new SaveFileDialog { Filter = "RobotWin projesi (*.rwproj)|*.rwproj", FileName = d.Name, DefaultExt = ".rwproj" };
                if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
                d.Package.Save(dlg.FileName);
                W.Status = "Kart dışa aktarıldı: " + Path.GetFileName(dlg.FileName);
            }));
        }

        private void EditBoard(WorldObject o)
        {
            var worldDoc = W.ActiveDocument;
            var d = World?.FindDesign(o.DesignId);
            if (d == null || worldDoc.Kind != DocumentKind.World) return;
            W.EditWorldDesign(worldDoc, d);
            _main.Navigate("schematic");
        }

        private void ComponentSection(WorldObject o)
        {
            var def = o.PartId != null ? Lib.Find(o.PartId) : null;
            if (def == null) return;
            var preset = o.PresetId != null ? Lib.FindPreset(o.PresetId) : null;
            var resolved = Lib.ResolveParameters(def, preset, o.Parameters);
            Inspector.Children.Add(Caption("Parametreler"));
            foreach (var p in def.Parameters)
            {
                string current = resolved.TryGetValue(p.Key, out var v) ? v : p.DefaultValue;
                string key = p.Key;
                FrameworkElement editor;
                if (p.Choices != null && p.Choices.Length > 0)
                {
                    var cb = new ComboBox();
                    foreach (var c in p.Choices) cb.Items.Add(new ComboBoxItem { Content = c, IsSelected = c == current });
                    cb.SelectionChanged += (_, _) =>
                    {
                        if (cb.SelectedItem is ComboBoxItem it && (string)it.Content != current) Edit(p.Label + " değişti", () => o.Parameters[key] = (string)it.Content);
                    };
                    editor = cb;
                }
                else
                {
                    var tb = new TextBox { Text = current, ToolTip = string.IsNullOrEmpty(p.Unit) ? null : p.Unit };
                    tb.LostFocus += (_, _) =>
                    {
                        if (tb.Text != current) Edit(p.Label + " değişti", () => o.Parameters[key] = tb.Text);
                    };
                    editor = tb;
                }

                Inspector.Children.Add(Labeled(p.Label + (string.IsNullOrEmpty(p.Unit) ? string.Empty : $" ({p.Unit})"), editor));
            }

            if (def.Symbol == SymbolKind.Board)
            {
                Inspector.Children.Add(Note("Bu kart tek parça olarak duruyor. Kodunu yazmak ve üstüne devre eklemek için kart tasarımına dönüştürün."));
                Inspector.Children.Add(Action("Kart tasarımına dönüştür ve kodunu aç", () => ConvertToBoard(o), primary: true));
            }
        }

        /// <summary>A free-standing Arduino/ESP32 becomes a one-part design that can carry code and extra circuitry.</summary>
        private void ConvertToBoard(WorldObject o)
        {
            var world = World!;
            var def = Lib.Find(o.PartId!)!;
            var pkg = new ProjectPackage { Schematic = new Schematic { Title = o.Name } };
            var part = pkg.Schematic.AddPart(Lib, o.PresetId ?? o.PartId!, new GridPoint(0, 0));
            foreach (var kv in o.Parameters) part.Parameters[kv.Key] = kv.Value;
            Edit($"{o.Name} kart tasarımına dönüştü", () =>
            {
                var d = world.AddDesign(pkg);
                // Cables keep working: "D9" on the component becomes "U1.D9" on the board.
                foreach (var c in world.Cables)
                {
                    if (c.A.ObjectId == o.Id) c.A.Pin = part.RefDes + "." + c.A.Pin;
                    if (c.B.ObjectId == o.Id) c.B.Pin = part.RefDes + "." + c.B.Pin;
                }

                o.Kind = WorldObjectKind.Board;
                o.DesignId = d.Id;
                o.PartId = null;
                o.PresetId = null;
                o.Material = "FR4";
            });
            EditBoard(o);
            _main.Navigate("code");
        }

        private void PortSection(WorldDocument world, WorldObject o)
        {
            if (Scene.PortOf(o.Id) is not { } pf) return;
            var attached = world.Joints.Where(j => j.ObjectB == o.Id && j.PortB == pf.Name).Select(j => world.Find(j.ObjectA)?.Name).Where(n => n != null).ToList();
            Inspector.Children.Add(Caption(pf.Label));
            Inspector.Children.Add(Note(attached.Count == 0
                ? $"{pf.Label} boşta. Bir kolu, tekerleği veya başka bir parçayı buraya takmak için o parçayı seçin, Bağla aracıyla bu nesneyi tıklayın."
                : $"{pf.Label}ne takılı: {string.Join(", ", attached)} — simülasyonda birlikte dönerler."));
        }

        private void CablesSection(WorldDocument world, WorldObject o)
        {
            var cables = world.Cables.Where(c => c.A.ObjectId == o.Id || c.B.ObjectId == o.Id).ToList();
            var pins = Scene.PinsOf(o.Id);
            Inspector.Children.Add(Caption($"Kablolar ({cables.Count}) · {pins.Count} bağlanabilir pin"));
            if (cables.Count == 0)
            {
                Inspector.Children.Add(Note(pins.Count == 0 ? "Bu nesnede elektrik bağlantısı yok." : "Pinleri bağlamak için Kablo aracını (K) seçin, sarı noktalara tıklayın."));
                return;
            }

            foreach (var c in cables)
            {
                bool isA = c.A.ObjectId == o.Id;
                var mine = isA ? c.A : c.B;
                var other = isA ? c.B : c.A;
                var otherObj = world.Find(other.ObjectId);
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sp = new StackPanel();
                var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
                try
                {
                    dot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Color));
                }
                catch (FormatException)
                {
                    dot.Fill = Brushes.Orange;
                }

                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(dot);
                line.Children.Add(new TextBlock { Text = $"{mine.Pin} → {otherObj?.Name} {other.Pin}", TextTrimming = TextTrimming.CharacterEllipsis });
                sp.Children.Add(line);
                var (span, len, verdict, bad) = CableFit(c);
                double drop = CableDrop(c);
                sp.Children.Add(new TextBlock
                {
                    Text = $"AWG {c.Awg:0} · {len * 100:0} cm (uçlar arası {span * 100:0} cm, {verdict}) · {WorldCable.Resistance(c.Awg, len) * 1000:0.#} mΩ"
                        + (drop > 0 ? $" · düşüm {SiValue.Format(drop, "V")}" : string.Empty) + $" · en çok {WorldCable.Ampacity(c.Awg):0.#} A",
                    Style = (Style)FindResource("Text.Caption"),
                    TextWrapping = TextWrapping.Wrap
                });
                if (bad) ((TextBlock)sp.Children[sp.Children.Count - 1]).Foreground = Brushes.OrangeRed;
                grid.Children.Add(sp);
                var awg = new ComboBox { Width = 64, Margin = new Thickness(4, 0, 4, 0), ToolTip = "Kablo kesiti (AWG)" };
                foreach (var g in new[] { 30, 28, 26, 24, 22, 20, 18, 16, 14, 12 }) awg.Items.Add(new ComboBoxItem { Content = g.ToString(CultureInfo.InvariantCulture), Tag = (double)g, IsSelected = Math.Abs(g - c.Awg) < 0.5 });
                var cable = c;
                awg.SelectionChanged += (_, _) =>
                {
                    if (awg.SelectedItem is ComboBoxItem it && Math.Abs((double)it.Tag - cable.Awg) > 0.1) Edit("Kablo kesiti değişti", () => cable.Awg = (double)it.Tag);
                };
                var sizes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sizes.Children.Add(awg);
                sizes.Children.Add(JumperLengthBox(cable));
                Grid.SetColumn(sizes, 1);
                grid.Children.Add(sizes);
                var del = new Button { Style = (Style)FindResource("Btn.Icon"), Width = 26, Height = 26, ToolTip = "Kabloyu sök", Content = new Theme.Icon { Data = (Geometry)FindResource("I.Delete"), Size = 13 } };
                del.Click += (_, _) => Edit("Kablo söküldü", () => world.Cables.Remove(cable));
                Grid.SetColumn(del, 2);
                grid.Children.Add(del);
                Inspector.Children.Add(grid);
            }
        }

        private void JointsSection(WorldDocument world, WorldObject o)
        {
            var joints = world.Joints.Where(j => j.ObjectA == o.Id || j.ObjectB == o.Id).ToList();
            Inspector.Children.Add(Caption($"Mekanik bağlantılar ({joints.Count})"));
            foreach (var j in joints)
            {
                var other = world.Find(j.ObjectA == o.Id ? j.ObjectB : j.ObjectA);
                string kind = j.Kind switch { WorldJointKind.Hinge => "menteşe", WorldJointKind.Slider => "kızak", _ => "sabit" };
                string port = j.ObjectA == o.Id && !string.IsNullOrEmpty(j.PortB) ? (Scene.PortOf(j.ObjectB!)?.Label ?? j.PortB) : string.Empty;
                var grid = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var joint = j;
                var del = new Button { Style = (Style)FindResource("Btn.Icon"), Width = 26, Height = 26, ToolTip = "Bağlantıyı çöz", Content = new Theme.Icon { Data = (Geometry)FindResource("I.Delete"), Size = 13 } };
                del.Click += (_, _) => Edit("Bağlantı çözüldü", () => world.Joints.Remove(joint));
                DockPanel.SetDock(del, Dock.Right);
                grid.Children.Add(del);
                grid.Children.Add(new TextBlock
                {
                    Text = (j.ObjectA == o.Id ? "→ " : "← ") + (other?.Name ?? "zemin") + (port.Length > 0 ? " · " + port : string.Empty) + " (" + kind + ")",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Inspector.Children.Add(grid);
            }

            Inspector.Children.Add(Action("Başka bir nesneye bağla (B)", () => SetTool(WorldTool.Joint)));
        }

        private TextBlock? _live;

        private void LiveSection(WorldObject o)
        {
            RealValuesSection(o);
            Inspector.Children.Add(Caption("Canlı ölçüm"));
            _live = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary") };
            Inspector.Children.Add(_live);
            UpdateLive();
        }

        private void UpdateLive()
        {
            if (_live == null) return;
            var world = World;
            var o = world?.Find(Scene.Selected);
            if (o == null || _session.State == SimState.Stopped || _session.Circuit == null)
            {
                _live.Text = "Simülasyon çalışırken burada sıcaklık, akım, açı ve gerilimler görünür.";
                return;
            }

            var lines = new List<string>();
            double t = _session.Temperature(o);
            if (!double.IsNaN(t)) lines.Add($"En sıcak parça : {t:0.0} °C");
            if (_session.Physics is { } ph && ph.MaterialOf(o.Id) is { } mat)   // [thermal]
            {
                double tb = ph.TemperatureOf(o.Id);
                string tg = double.IsNaN(mat.GlassTransition) ? string.Empty : $", camsı geçiş {mat.GlassTransition:0} °C";
                lines.Add($"Gövde sıcaklığı: {tb:0.0} °C · {mat.Name}: {ph.MaterialStateOf(o.Id)} (sertlik %{mat.ModulusFactor(tb) * 100:0}{tg})");
                double q = ph.HeatOf(o.Id);
                if (q > 0.001) lines.Add($"Üretilen ısı   : {SiValue.Format(q, "W")}");
            }
            if (_session.IsFailed(o.Id)) lines.Add("DURUM          : ARIZALI (yandı / bozuldu)");
            if (o.Kind == WorldObjectKind.Component)
            {
                var r = _session.Circuit.ComponentRef(o.Id);
                foreach (var d in r != null ? _session.Circuit.Compiled.DevicesOf(r) : Array.Empty<RobotTwin.CoreSim.Analog.AnalogDevice>())
                {
                    switch (d)
                    {
                        case Servo s:
                            lines.Add($"Servo açısı    : {s.AngleDeg:0.0}° (hedef {(double.IsNaN(s.TargetAngleDeg) ? "—" : s.TargetAngleDeg.ToString("0.0", CultureInfo.InvariantCulture) + "°")})");
                            lines.Add($"Darbe          : {s.PulseWidth * 1e6:0} µs · akım {s.SupplyCurrent * 1000:0} mA");
                            break;
                        case DcMotor m:
                            lines.Add($"Motor          : {m.OutputRpm:0} rpm · {m.Current * 1000:0} mA · {m.Voltage:0.00} V");
                            lines.Add($"Tork (çıkış)   : {m.OutputTorque * 100:0.00} N·cm · sargı {m.Thermal.TemperatureC:0} °C");
                            break;
                    }
                }
            }

            ReadyLive(o, lines);   // [objs] propeller thrust, roller load, light flux
            WingLive(o, lines);   // [linkage] deflection, lift, pushrod and servo load (Pages/WorldPage.Wings.cs)
            foreach (var pin in Scene.PinsOf(o.Id).Take(16))
            {
                double v = _session.Voltage(new CableEnd { ObjectId = o.Id, Pin = pin.Pin });
                if (!double.IsNaN(v)) lines.Add($"{pin.Pin,-14} : {SiValue.Format(v, "V")}");
            }

            _live.Text = string.Join("\n", lines);
        }

        private double EstimateMass(WorldDocument world, WorldObject o)
        {
            if (o.MassKg is { } m) return m * 1000;
            double density = RobotTwin.CoreSim.Mechanics.MechatronicSystem.DensityOf(o.Material == "KAUÇUK" ? "ABS" : o.Material) * Math.Max(0.05, o.Infill);
            switch (o.Kind)
            {
                case WorldObjectKind.Body:
                    double v = o.Shape switch
                    {
                        "cylinder" => Math.PI * o.SizeX * o.SizeX / 4 * o.SizeZ,
                        "wheel" => Math.PI * o.SizeX * o.SizeX / 4 * o.SizeZ * 0.55,
                        "sphere" => Math.PI / 6 * o.SizeX * o.SizeX * o.SizeX,
                        _ => RobotTwin.CoreSim.Sandbox.WorldPhysics.MaterialVolume(o)   // [shell] hollow bodies weigh their walls
                    };
                    return v * density * 1000;
                case WorldObjectKind.Model when o.Mesh != null:
                    try
                    {
                        var mesh = MeshModel.FromEmbedded(o.Mesh);
                        return Math.Abs(RobotTwin.CoreSim.Mechanics.MassProperties.FromMesh(mesh, density).Mass) * 1000;
                    }
                    catch (FormatException)
                    {
                        return 0;
                    }

                case WorldObjectKind.Component:
                    return AssemblyEstimates.ComponentMass(o) * 1000;
                case WorldObjectKind.Board:
                    var b = world.FindDesign(o.DesignId)?.Package.Schematic.Board;
                    double area = b != null ? b.Width * b.Height * 1e-6 : 0.003;
                    return (area * 0.0016 * 1850 + (world.FindDesign(o.DesignId)?.Package.Schematic.Parts.Count ?? 0) * 0.0008) * 1000;
                default:
                    return 0;
            }
        }

        // ================================================================ toolbar

        private void OnTool(object sender, RoutedEventArgs e)
        {
            var tool = (string)((FrameworkElement)sender).Tag switch { "Cable" => WorldTool.Cable, "Joint" => WorldTool.Joint, _ => WorldTool.Select };
            SetTool(tool);
        }

        private void SetTool(WorldTool tool)
        {
            Scene.Tool = tool;
            ToolSelect.IsChecked = tool == WorldTool.Select;
            ToolCable.IsChecked = tool == WorldTool.Cable;
            ToolJoint.IsChecked = tool == WorldTool.Joint;
            if (tool == WorldTool.Joint && Scene.Selected == null) W.Status = "Önce takılacak nesneyi seçin, sonra takılacağı yeri tıklayın";
        }

        private void OnRotate(object sender, RoutedEventArgs e) => Rotate(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -15 : 15);

        private void Rotate(double degrees)
        {
            var o = World?.Find(Scene.Selected);
            if (o == null) return;
            int mounted = World!.MountedOn(o.Id).Count;
            Edit(mounted > 0 ? $"{o.Name} ve üstündeki {mounted} parça döndürüldü" : $"{o.Name} döndürüldü", () => World.RotateAssembly(o, degrees));
        }

        private void OnDuplicate(object sender, RoutedEventArgs e) => Duplicate();

        private void Duplicate()
        {
            var world = World;
            var o = world?.Find(Scene.Selected);
            if (world == null || o == null) return;
            WorldObject? copy = null;
            Edit($"{o.Name} çoğaltıldı", () =>
            {
                copy = o.Clone();
                copy.Id = world.NextId("O");
                copy.Name = world.UniqueName(o.Name);
                copy.Pose.X += 0.05;
                world.Objects.Add(copy);
            });
            if (copy != null) Scene.Select(copy.Id);
        }

        private void OnDelete(object sender, RoutedEventArgs e) => Delete();

        private void Delete()
        {
            var world = World;
            var o = world?.Find(Scene.Selected);
            if (world == null || o == null || DeleteBlocked(o)) return; // [terrain] locked
            Edit($"{o.Name} silindi", () => world.Remove(o));
            Scene.Select(null);
        }

        private void OnUndo(object sender, RoutedEventArgs e)
        {
            if (!TerrainUndoRedo(false)) W.Undo(); // [terrain] terrain strokes in the Arazi tab
        }

        private void OnRedo(object sender, RoutedEventArgs e)
        {
            if (!TerrainUndoRedo(true)) W.Redo(); // [terrain]
        }

        private void OnTop(object sender, RoutedEventArgs e) => Scene.TopView();

        private void OnFollow(object sender, RoutedEventArgs e)
        {
            Scene.Follow = FollowToggle.IsChecked == true;
            W.Status = Scene.Follow ? "Kamera seçili nesneyi takip ediyor" : "Kamera takibi kapalı";
        }

        private void OnFit(object sender, RoutedEventArgs e) => Scene.FrameAll();

        private void OnKeys(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (TerrainKeys(e)) // [terrain] Arazi tab keys
            {
                e.Handled = true;
                return;
            }

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            switch (e.Key)
            {
                case Key.Delete:
                    Delete();
                    break;
                case Key.R when !ctrl:
                    Rotate(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -15 : 15);
                    break;
                case Key.D when ctrl:
                    Duplicate();
                    break;
                case Key.Z when ctrl:
                    W.Undo();
                    break;
                case Key.Y when ctrl:
                    W.Redo();
                    break;
                case Key.T:
                    FollowToggle.IsChecked = FollowToggle.IsChecked != true;
                    OnFollow(this, new RoutedEventArgs());
                    break;
                case Key.K:
                    SetTool(WorldTool.Cable);
                    break;
                case Key.B:
                    SetTool(WorldTool.Joint);
                    break;
                case Key.Escape:
                    Scene.CancelTool();
                    SetTool(WorldTool.Select);
                    break;
                case Key.F when !ctrl:
                    if (Scene.Selected != null) Scene.FrameSelected();
                    else Scene.FrameAll();
                    break;
                case Key.R when ctrl:
                    OnRecord(this, new RoutedEventArgs());
                    break;
                case Key.F5:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) StopSimulation();
                    else if (ctrl) OpenAnalysis();
                    else Run();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        // ================================================================ simulation

        private void OnRun(object sender, RoutedEventArgs e) => Run();

        /// <summary>Parts of the active world that start inside each other (for --overlap-report and checks).</summary>
        public System.Collections.Generic.IReadOnlyList<string> OverlapReport()
        {
            var world = World;
            if (world == null) return System.Array.Empty<string>();
            var physics = RobotTwin.CoreSim.Sandbox.WorldPhysics.Build(world, Scene.GeometryOf, null);
            var lines = physics.FindInterpenetrations(0.001).Select(o => $"{world.Find(o.A)?.Name ?? o.A} ↔ {world.Find(o.B)?.Name ?? o.B}: {o.Depth * 1000:F1} mm").ToList();
            // [shell] fit problems, and where each part of a shelled world really is (pose, collision box)
            lines.AddRange(physics.ShellFit(world).Select(i => "sığma: " + i.Message));
            if (world.Objects.Any(RobotTwin.CoreSim.Sandbox.WorldPhysics.IsShell))
                foreach (var o in world.Objects)
                {
                    var g = Scene.GeometryOf(o);
                    var at = physics.PoseOf(o.Id)?.Position;
                    lines.Add(FormattableString.Invariant($"geo {o.Name}: poz ({o.Pose.X * 1000:F1}, {o.Pose.Y * 1000:F1}, {o.Pose.Z * 1000:F1}) dön ({o.Pose.Roll:F0}, {o.Pose.Pitch:F0}, {o.Pose.Yaw:F0}) kutu [{g.Min.X * 1000:F1}..{g.Max.X * 1000:F1}, {g.Min.Y * 1000:F1}..{g.Max.Y * 1000:F1}, {g.Min.Z * 1000:F1}..{g.Max.Z * 1000:F1}] gövde {(at is { } a ? FormattableString.Invariant($"({a.X * 1000:F1}, {a.Y * 1000:F1}, {a.Z * 1000:F1})") : "-")}"));
                }

            return lines;
        }

        /// <summary>Run / pause / resume the world simulation (menu bar).</summary>
        public void ToggleRun() => Run();

        private void Run()
        {
            var world = World;
            if (world == null) return;
            switch (_session.State)
            {
                case SimState.Running:
                    _session.Pause();
                    break;
                case SimState.Paused:
                    _session.Resume();
                    break;
                default:
                    W.FlushActiveDesign();
                    Scene.ClearEffects();
                    if (!_session.Start(world, Scene.CableLength, Scene.GeometryOf, out var message))
                    {
                        W.Status = message;
                        return;
                    }

                    _serialShown = 0;
                    SerialView.Clear();
                    W.Status = $"Dünya çalışıyor: {world.Objects.Count} nesne, {world.Cables.Count} kablo, {_session.Circuit?.Programmables.Count ?? 0} programlı kart";
                    break;
            }

            UpdateRunButton();
        }

        private void OnStep(object sender, RoutedEventArgs e)
        {
            if (_session.State == SimState.Stopped) Run();
            if (_session.State == SimState.Running) _session.Pause();
            _session.StepOnce(0.01);
            UpdateRunButton();
        }

        private void OnStop(object sender, RoutedEventArgs e) => StopSimulation();

        private void OnSpeed(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedBox?.SelectedItem is ComboBoxItem item) _session.Speed = double.Parse((string)item.Tag, CultureInfo.InvariantCulture);
        }

        private void UpdateRunButton()
        {
            bool running = _session.State == SimState.Running;
            RunIcon.Data = (Geometry)FindResource(running ? "I.Pause" : "I.Play");
            RunText.Text = running ? "Duraklat" : _session.State == SimState.Paused ? "Devam" : "Dünyayı çalıştır";
            SimStatus.Text = _session.State == SimState.Stopped ? string.Empty : $"t = {SiValue.Format(_session.Time, "s")}";
        }

        private void OnSimUpdated()
        {
            var now = DateTime.Now;
            if ((now - _lastPanels).TotalMilliseconds < 120) return;
            _lastPanels = now;
            SimStatus.Text = $"t = {SiValue.Format(_session.Time, "s")}" + (_session.RealtimeFactor < 0.9 * _session.Speed ? $" · gerçek zamanın {_session.RealtimeFactor:P0}'i" : string.Empty);
            UpdateSerial();
            UpdateLive();
            UpdateRadioLive();   // [radio]
            UpdateGasLive();   // [gas]
            UpdateCellLive();   // [arm]
            UpdateSdkLive();   // [sdk]
            RecordCableHistory();
            UpdateCableDiagnostics();
            UpdateThermalView();   // [thermal] Pages/WorldPage.Thermal.cs
            UpdateCollisionView();   // [hull] Pages/WorldPage.Collision.cs
            if (DockCables.IsChecked == true) BuildCableTable();
        }

        private void UpdateSerial()
        {
            long total = _session.SerialTotal;
            if (total < _serialShown) _serialShown = 0;
            if (total == _serialShown) return;
            string text = _session.SerialText;
            long fresh = total - _serialShown;
            if (_serialShown == 0 || fresh >= text.Length) SerialView.Text = text;
            else SerialView.AppendText(text.Substring(text.Length - (int)fresh));
            _serialShown = total;
            if (SerialView.Text.Length > 60000) SerialView.Text = SerialView.Text.Substring(SerialView.Text.Length - 40000);
            SerialView.ScrollToEnd();
        }

        private void OnSerialSend(object sender, RoutedEventArgs e)
        {
            if (SerialInput.Text.Length == 0) return;
            _session.SendSerial(SerialInput.Text + "\n");
            SerialInput.Clear();
        }

        private void OnSerialKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnSerialSend(sender, e);
        }

        private void OnDockTab(object sender, RoutedEventArgs e)
        {
            LogList.Visibility = DockLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SerialPanel.Visibility = DockSerial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CablePanel.Visibility = DockCables.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (DockCables.IsChecked == true) BuildCableTable();
            ShowRfPanel(DockRf.IsChecked == true);   // [radio]
        }


        // ================================================================ snapshot hook

        /// <summary>--snapshot --page world --fault &lt;template id&gt; [--simulate s]: loads a world template and runs it.</summary>
        public void PrepareSnapshot(string? template, string? simulate)
        {
            template = SenseSnapshotTemplate(template);   // [sense] "id:studio:sensors…" view modes
            template = ParseTerrainSnapshot(template); // [terrain] "+arazi", "+boya", "+yakın"
            var t = WorldTemplates.Find(template ?? "servo-arm") ?? WorldTemplates.All[1];
            if (World == null || World.Title != t.Build().Title) _main.NewWorld(t.Build());
            OnShown();
            Scene.FrameAll();
            if (simulate != null && double.TryParse(simulate, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
            {
                Run();
                _session.Pause();
                for (int i = 0; i < Math.Max(1, (int)(secs / 0.01)); i++)
                {
                    _session.StepOnce(0.01);
                    if (secs - i * 0.01 < 1.5) Scene.PlumeTick();   // [gas] the last second and a half of exhaust
                }
                UpdateRunButton();
                OnSimUpdated();
                DockSerial.IsChecked = true;
                OnDockTab(this, new RoutedEventArgs());
                Scene.FrameCurrent();
            }

            if (Environment.GetCommandLineArgs().Contains("--dock-cables"))
            {
                DockCables.IsChecked = true;
                OnDockTab(this, new RoutedEventArgs());
            }

            if (Environment.GetCommandLineArgs().Contains("--select-cable") && World is { } cw && cw.Cables.Count > 0)
            {
                // The cable carrying the most current (or the first one when stopped).
                var busiest = cw.Cables.OrderByDescending(c => Math.Abs(_session.CableState(c).Current)).First();
                RecordCableHistory();
                Scene.SelectCable(busiest.Id);
                return;
            }

            var first = World?.Objects.FirstOrDefault(o => o.Kind == WorldObjectKind.Component && o.PartId == "servo") ?? World?.Objects.FirstOrDefault();
            if (first != null) Scene.Select(first.Id);
            if (Environment.GetCommandLineArgs().Contains("--collision")) SetCollisionView(true);   // [hull]
            LaunchSnapshot();   // [launch] --frame <name>, --thermal (Pages/WorldPage.Launch.cs)
            ShellSnapshot();   // [shell] --select-shell, --xray, --exploded (Pages/WorldPage.Shells.cs)
            SenseSnapshotFinish();   // [sense]
            SdkSnapshot();   // [sdk]
            AnalysisSnapshot();   // [analysis] Pages/WorldPage.Analysis.cs
            FinishTerrainSnapshot(simulate != null); // [terrain]
        }
    }

    /// <summary>Rough masses of free-standing catalogue parts.</summary>
    internal static class AssemblyEstimates
    {
        public static double ComponentMass(WorldObject o)
        {
            if (RobotTwin.CoreSim.Sandbox.CellGeometry.MassOf(o) is double cell) return cell;   // [arm]
            string id = (o.PresetId ?? o.PartId ?? string.Empty).ToLowerInvariant();
            if (id.Contains("sg90") || id.Contains("mg90")) return 0.009;
            if (id.Contains("996")) return 0.055;
            if (id.Contains("ds3218")) return 0.06;
            if (id.Contains("motor-tt")) return 0.03;
            if (id.Contains("25ga") || id.Contains("jga")) return 0.09;
            if (id.Contains("8520")) return 0.0045;
            if (id.Contains("0720")) return 0.002;
            if (id.Contains("bat-9v")) return 0.045;
            if (id.Contains("2s-usbc")) return 0.1;   // [usbc]
            if (id.Contains("18650")) return 0.046;
            if (id.Contains("aa-usbc") && !id.Contains("4aa")) return 0.022;
            if (id.Contains("4aa")) return 0.11;
            if (id.Contains("2aa")) return 0.055;
            if (id.Contains("lipo")) return id.Contains("300") ? 0.009 : id.Contains("3s") ? 0.19 : 0.12;
            if (id.Contains("arduino-uno")) return 0.025;
            if (id.Contains("nano")) return 0.007;
            if (id.Contains("esp32")) return 0.01;
            return 0.005;
        }
    }
}

