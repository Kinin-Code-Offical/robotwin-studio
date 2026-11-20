using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    // [terrain] "Arazi" palette tab: create/resize terrain, sculpt and paint tools, surface layers,
    // prefab props; editor flags (hide, lock, group) and prop sizes in the inspector.
    public partial class WorldPage
    {
        private bool _terrainQuiet;
        private TerrainMap? _removedTerrain;
        private string? _snapshotTerrainMode;

        private void InitTerrain()
        {
            Scene.TerrainEdited += name =>
            {
                // Terrain strokes have their own undo; mark the world dirty without rebuilding the scene.
                _terrainQuiet = true;
                try
                {
                    W.CommitWorldChange("Arazi: " + name);
                }
                finally
                {
                    _terrainQuiet = false;
                }

                if (TabTerrain.IsChecked == true) BuildPalette();
            };
            Scene.PropPlaceRequested += (id, x, y) =>
            {
                var world = World;
                if (world == null) return;
                WorldObject? added = null;
                Edit($"{TerrainProps.Find(id)?.Name ?? id} yerleştirildi", () => added = WorldBuilding.AddProp(world, id, x, y, 0, Scene.AlignToNormal));
                if (added != null) Scene.Select(added.Id);
            };
            Scene.TerrainHeightPicked += _ =>
            {
                if (TabTerrain.IsChecked == true) BuildPalette();
            };
        }

        /// <summary>Palette hook: true when the Arazi tab is shown (and builds it).</summary>
        private bool TerrainPalette()
        {
            bool on = TabTerrain.IsChecked == true;
            Scene.TerrainEditing = on && World?.Terrain != null;
            if (!on)
            {
                Scene.PendingProp = null;
                return false;
            }

            BuildTerrainPalette();
            return true;
        }

        private static readonly (TerrainTool Tool, string Key)[] ToolKeys =
        {
            (TerrainTool.Raise, "1"), (TerrainTool.Lower, "2"), (TerrainTool.Smooth, "3"), (TerrainTool.Flatten, "4"), (TerrainTool.SetHeight, "5"),
            (TerrainTool.Noise, "6"), (TerrainTool.Erode, "7"), (TerrainTool.Ramp, "8"), (TerrainTool.Stamp, "9"), (TerrainTool.Paint, "0"), (TerrainTool.Road, "Y")
        };

        private void BuildTerrainPalette()
        {
            var world = World;
            if (world == null)
            {
                Palette.Children.Add(Note("Önce bir dünya açın veya oluşturun."));
                return;
            }

            var map = world.Terrain;
            if (map == null)
            {
                BuildTerrainCreate(world);
                return;
            }

            Palette.Children.Add(Caption("Arazi", 0));
            Palette.Children.Add(Note($"{map.Width:0.##} × {map.Depth:0.##} m · {map.CellSize * 100:0.#} cm hücre · {map.Columns} × {map.Rows} nokta · {Scene.TerrainRenderer?.ChunkCount ?? 0} parça (LOD)"));

            // Tools.
            Palette.Children.Add(Caption("Araç"));
            var tools = new WrapPanel();
            foreach (var (tool, key) in ToolKeys)
            {
                var t = tool;
                var rb = new RadioButton { Content = WorldScene.ToolName(tool), Style = (Style)FindResource("Chip"), GroupName = "terrainTool", IsChecked = Scene.TerrainTool == tool, Margin = new Thickness(0, 0, 4, 4), ToolTip = "Kısayol: " + key };
                rb.Click += (_, _) =>
                {
                    Scene.TerrainTool = t;
                    Scene.CancelRoad();
                    BuildPalette();
                };
                tools.Children.Add(rb);
            }

            Palette.Children.Add(tools);
            var brush = Scene.Brush;
            var tool0 = Scene.TerrainTool;

            // Brush.
            if (tool0 != TerrainTool.Stamp && tool0 != TerrainTool.Road)
            {
                Palette.Children.Add(Caption("Fırça"));
                Palette.Children.Add(NumberField("Yarıçap (cm)  [ ]", brush.Radius * 100, v => { brush.Radius = v / 100; }, 1, 2500, "0.#"));
                if (tool0 != TerrainTool.Ramp) Palette.Children.Add(NumberField("Güç (%)", brush.Strength * 100, v => brush.Strength = v / 100, 1, 100, "0"));
                Palette.Children.Add(NumberField("Sertlik (%)", brush.Hardness * 100, v => brush.Hardness = v / 100, 0, 100, "0"));
            }

            switch (tool0)
            {
                case TerrainTool.SetHeight:
                case TerrainTool.Flatten:
                    Palette.Children.Add(NumberField("Hedef yükseklik (mm)", brush.TargetHeight * 1000, v => brush.TargetHeight = v / 1000, -10000, 10000, "0.#"));
                    Palette.Children.Add(Note(tool0 == TerrainTool.Flatten
                        ? "Düzleştir: fırçanın ilk değdiği yüksekliğe çeker. Ctrl+tık hedef yüksekliği zeminden alır."
                        : "Fırça altını hedef yüksekliğe çeker. Ctrl+tık hedefi zeminden alır."));
                    break;
                case TerrainTool.Noise:
                    Palette.Children.Add(NumberField("Pürüz boyu (cm)", brush.NoiseScale * 100, v => brush.NoiseScale = v / 100, 1, 500, "0.#"));
                    Palette.Children.Add(Note("Engebe ekler (fraktal gürültü); Shift ile ters yönde. Küçük pürüz boyu: taşlı/bozuk zemin, büyük: dalgalı arazi."));
                    break;
                case TerrainTool.Erode:
                    Palette.Children.Add(Note("Termal erozyon: 30°'den dik yamaçlardan malzeme aşağı kayar; sırtlar yuvarlanır, eteklerde birikir."));
                    break;
                case TerrainTool.Ramp:
                    Palette.Children.Add(NumberField("Bitiş yüksekliği (Ctrl, mm)", brush.TargetHeight * 1000, v => brush.TargetHeight = v / 1000, -10000, 10000, "0.#"));
                    Palette.Children.Add(Note("Rampanın başından sonuna sürükleyin: iki uçtaki zemin yüksekliği doğrusal olarak birleştirilir, genişlik = fırça çapı. Ctrl ile bitiş yüksekliği yukarıdaki değer olur."));
                    break;
                case TerrainTool.Stamp:
                    BuildStampOptions(brush);
                    break;
                case TerrainTool.Paint:
                    BuildLayerList(brush, map);
                    Palette.Children.Add(Action("Tüm araziyi bu katmanla kapla", () =>
                    {
                        map.History.BeginStroke("Kapla");
                        map.BeginModify(0, 0, map.Columns - 1, map.Rows - 1);
                        map.FillLayer(brush.Layer);
                        if (map.History.EndStroke(map)) Scene_TerrainCommitted("Kapla");
                    }));
                    break;
                case TerrainTool.Road:
                    Palette.Children.Add(Caption("Yol"));
                    Palette.Children.Add(NumberField("Genişlik (cm)", brush.RoadWidth * 100, v => brush.RoadWidth = v / 100, 5, 2000, "0.#"));
                    var flat = new CheckBox { Content = "Enine düzleştir (yol yatay, boyuna eğimi izler)", IsChecked = brush.RoadFlatten, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 4, 0, 6) };
                    flat.Click += (_, _) => brush.RoadFlatten = flat.IsChecked == true;
                    Palette.Children.Add(flat);
                    Palette.Children.Add(Note("Araziye noktalar tıklayın; eğri (spline) bu noktalardan geçer. Enter veya çift tık yolu döşer, Geri tuşu son noktayı siler."));
                    Palette.Children.Add(Action($"Yolu döşe (Enter) · {Scene.RoadPoints} nokta", () => Scene.FinishRoad(), primary: true));
                    BuildLayerList(brush, map);
                    break;
            }

            // Undo of strokes.
            Palette.Children.Add(Caption("Geçmiş"));
            var hist = new WrapPanel();
            var undo = new Button { Content = "Geri al (Ctrl+Z)", Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4), IsEnabled = map.History.CanUndo, ToolTip = map.History.UndoName };
            undo.Click += (_, _) => TerrainUndoRedo(false);
            var redo = new Button { Content = "Yinele (Ctrl+Y)", Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4), IsEnabled = map.History.CanRedo, ToolTip = map.History.RedoName };
            redo.Click += (_, _) => TerrainUndoRedo(true);
            hist.Children.Add(undo);
            hist.Children.Add(redo);
            Palette.Children.Add(hist);

            // Props.
            Palette.Children.Add(Caption("Nesneler (tıklayın, sonra araziye yerleştirin)"));
            foreach (var def in TerrainProps.All)
            {
                var id = def.Id;
                Palette.Children.Add(Row(def.Name, $"{def.Description} · {def.SizeX * 1000:0} × {def.SizeY * 1000:0} × {def.SizeZ * 1000:0} mm{(def.Dynamic ? " · hareketli" : " · sabit")}", () => Scene.PendingProp = id));
            }

            var snap = new CheckBox { Content = "Yüzeye yapıştır (sürüklerken araziyi izle)", IsChecked = Scene.SurfaceSnap, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 6, 0, 4) };
            snap.Click += (_, _) => Scene.SurfaceSnap = snap.IsChecked == true;
            Palette.Children.Add(snap);
            var align = new CheckBox { Content = "Eğime hizala", IsChecked = Scene.AlignToNormal, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 8) };
            align.Click += (_, _) => Scene.AlignToNormal = align.IsChecked == true;
            Palette.Children.Add(align);

            // Size.
            Palette.Children.Add(Caption("Boyut ve çözünürlük"));
            double nw = map.Width, nd = map.Depth, nc = map.CellSize * 100;
            Palette.Children.Add(NumberField("Genişlik (m)", nw, v => nw = v, 1, 50));
            Palette.Children.Add(NumberField("Derinlik (m)", nd, v => nd = v, 1, 50));
            Palette.Children.Add(NumberField("Hücre (cm)", nc, v => nc = v, 0.5, 100, "0.#"));
            Palette.Children.Add(Action("Yeniden örnekle", () =>
            {
                var next = map.Resampled(nw, nd, nc / 100);
                Edit($"Arazi {next.Width:0.##} × {next.Depth:0.##} m, {next.CellSize * 100:0.#} cm", () =>
                {
                    world.Terrain = next;
                    world.Environment.Width = next.Width;
                    world.Environment.Depth = next.Depth;
                });
                BuildPalette();
            }, "Yükseklikler ve boyalar yeni ızgaraya aktarılır (en fazla 1025 × 1025 nokta)."));
            Palette.Children.Add(Action("Araziyi kaldır", () =>
            {
                _removedTerrain = world.Terrain;
                Edit("Arazi kaldırıldı", () => world.Terrain = null);
                BuildPalette();
            }));
        }

        private void BuildTerrainCreate(WorldDocument world)
        {
            var env = world.Environment;
            Palette.Children.Add(Caption("Arazi oluştur", 0));
            Palette.Children.Add(Note("Yükseklik haritası tabanlı, şekillendirilebilir ve boyanabilir zemin (Unity Terrain gibi). Fiziği gerçektir: tekerler eğimde, tümseklerde ve gevşek zeminde (kum, çakıl, çamur) batarak ve kayarak hareket eder."));
            double w = Math.Max(2, env.Width), d = Math.Max(2, env.Depth);
            double cell = Math.Max(2, Math.Round(Math.Max(w, d) / 300 * 100));
            int layer = TerrainLayers.IndexOf("grass");
            Palette.Children.Add(NumberField("Genişlik (m)", w, v => w = v, 1, 50));
            Palette.Children.Add(NumberField("Derinlik (m)", d, v => d = v, 1, 50));
            Palette.Children.Add(NumberField("Hücre (cm)", cell, v => cell = v, 0.5, 100, "0.#"));
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var l in TerrainLayers.All) combo.Items.Add(new ComboBoxItem { Content = l.Name, Tag = l.Index, IsSelected = l.Index == layer });
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem it) layer = (int)it.Tag;
            };
            Palette.Children.Add(Labeled("Zemin", combo));
            Palette.Children.Add(Action("Düz arazi oluştur", () => CreateTerrain(world, w, d, cell / 100, layer, false), primary: true));
            Palette.Children.Add(Action("Engebeli arazi oluştur", () => CreateTerrain(world, w, d, cell / 100, layer, true), "Tepeler ve küçük tümseklerle hazır engebe; sonra fırçalarla düzenleyin."));
            if (_removedTerrain != null) Palette.Children.Add(Action("Kaldırılan araziyi geri getir", () =>
            {
                var back = _removedTerrain;
                _removedTerrain = null;
                Edit("Arazi geri getirildi", () => world.Terrain = back);
                BuildPalette();
            }));
            Palette.Children.Add(Note("Hazır şablonlar: Dosya → Yeni dünya → 'Arazi parkuru' veya 'Kapalı atölye'."));
        }

        private void CreateTerrain(WorldDocument world, double w, double d, double cell, int layer, bool bumpy)
        {
            var map = new TerrainMap(w, d, cell, 0, 0, TerrainLayers.Get(layer).Id);
            if (bumpy)
            {
                double s = Math.Max(map.Width, map.Depth);
                map.Fill((x, y) =>
                {
                    double big = 0.06 * s / 4 * TerrainSculpt.Fbm(x / s * 3 + 5.3, y / s * 3 - 2.1, 3);
                    double small = 0.012 * TerrainSculpt.Fbm(x * 4 + 1.7, y * 4 + 9.2, 3);
                    return Math.Max(0, big + 0.03 * s / 4) + small;
                });
            }

            Edit(bumpy ? "Engebeli arazi oluşturuldu" : "Arazi oluşturuldu", () =>
            {
                world.Terrain = map;
                world.Environment.Width = map.Width;
                world.Environment.Depth = map.Depth;
                world.Environment.Walls = false;
                foreach (var o in world.Objects.Where(o => o.Pose.Z < 0.002)) o.Pose.Z = map.Height(o.Pose.X, o.Pose.Y);
            });
            Scene.FrameTerrain();
            BuildPalette();
        }

        private void BuildStampOptions(TerrainBrush brush)
        {
            Palette.Children.Add(Caption("Damga"));
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (TerrainStamp s in Enum.GetValues(typeof(TerrainStamp))) combo.Items.Add(new ComboBoxItem { Content = WorldScene.StampName(s), Tag = s, IsSelected = s == brush.Stamp });
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem it || (TerrainStamp)it.Tag == brush.Stamp) return;
                brush.Stamp = (TerrainStamp)it.Tag;
                // Typical sizes for robot-scale test courses.
                (brush.StampHeight, brush.StampLength, brush.StampWidth) = brush.Stamp switch
                {
                    TerrainStamp.Bump => (0.03, 0.3, 0.3),
                    TerrainStamp.Pothole => (0.04, 0.3, 0.3),
                    TerrainStamp.SpeedBump => (0.015, 0.12, 0.8),
                    TerrainStamp.Kerb => (0.04, 0.1, 1.0),
                    _ => (0.03, 0.4, 0.6)
                };
                BuildPalette();
            };
            Palette.Children.Add(Labeled("Şekil", combo));
            bool round = brush.Stamp == TerrainStamp.Bump || brush.Stamp == TerrainStamp.Pothole;
            Palette.Children.Add(NumberField(brush.Stamp == TerrainStamp.Pothole ? "Derinlik (mm)" : "Yükseklik (mm)", brush.StampHeight * 1000, v => brush.StampHeight = v / 1000, 0.5, 5000, "0.#"));
            Palette.Children.Add(NumberField(round ? "Çap (mm)" : "Boy, ilerleme yönünde (mm)", brush.StampLength * 1000, v => brush.StampLength = v / 1000, 5, 20000, "0"));
            if (!round)
            {
                Palette.Children.Add(NumberField("Genişlik (mm)", brush.StampWidth * 1000, v => brush.StampWidth = v / 1000, 5, 20000, "0"));
                Palette.Children.Add(NumberField("Yön (°)  Q / E", brush.Angle, v => brush.Angle = v, -360, 360, "0"));
            }

            Palette.Children.Add(Note("Tıklayın: şekil zemine eklenir (Shift: ters, ör. tümsek yerine çukur). Not: dikey yüzler bir hücre genişliğinde eğime dönüşür; keskin bordür/basamak için Nesneler'den katı parça koyun."));
        }

        private void BuildLayerList(TerrainBrush brush, TerrainMap map)
        {
            Palette.Children.Add(Caption("Yüzey katmanı"));
            foreach (var l in TerrainLayers.All)
            {
                var layer = l;
                bool on = brush.Layer == l.Index;
                var row = new Border { Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 2), Background = on ? (Brush)FindResource("B.Hover") : Brushes.Transparent };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var c = l.ColorArgb;
                var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)), BorderBrush = on ? (Brush)FindResource("B.Accent") : (Brush)FindResource("B.Border"), BorderThickness = new Thickness(on ? 2 : 1) };
                grid.Children.Add(swatch);
                var sp = new StackPanel();
                Grid.SetColumn(sp, 1);
                bool used = map.ActiveLayers.Contains(l.Index);
                sp.Children.Add(new TextBlock { Text = l.Name + (used ? "  ·  kullanımda" : string.Empty) });
                var soil = l.Soil is { } s ? $" · gevşek: c {s.Cohesion / 1000:0.##} kPa, φ {s.FrictionAngle:0}°, n {s.N:0.#}" : string.Empty;
                sp.Children.Add(new TextBlock { Text = $"μ kauçuk {l.Rubber.Static:0.00}/{l.Rubber.Kinetic:0.00} · plastik {l.Plastic.Static:0.00} · çelik {l.Steel.Static:0.00} · Crr {l.Rubber.Rolling:0.000}{soil}", Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap });
                if (l.Note.Length > 0) sp.Children.Add(new TextBlock { Text = l.Note, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap });
                grid.Children.Add(sp);
                row.Child = grid;
                row.MouseLeftButtonUp += (_, _) =>
                {
                    brush.Layer = layer.Index;
                    BuildPalette();
                };
                Palette.Children.Add(row);
            }
        }

        private void Scene_TerrainCommitted(string name)
        {
            _terrainQuiet = true;
            try
            {
                W.CommitWorldChange("Arazi: " + name);
            }
            finally
            {
                _terrainQuiet = false;
            }

            BuildPalette();
        }

        // ================================================================ undo, keys

        private bool TerrainUndoActive => TabTerrain.IsChecked == true && World?.Terrain != null;

        /// <summary>Toolbar/keyboard undo hook: in the Arazi tab, undo terrain strokes.</summary>
        private bool TerrainUndoRedo(bool redo)
        {
            if (!TerrainUndoActive) return false;
            var name = Scene.TerrainUndo(redo);
            W.Status = name == null ? (redo ? "Yinelenecek arazi adımı yok" : "Geri alınacak arazi adımı yok") : (redo ? "Yinelendi: " : "Geri alındı: ") + name;
            return true;
        }

        /// <summary>Keyboard hook (before the page's own keys).</summary>
        private bool TerrainKeys(KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return false;
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (e.Key == Key.Escape && Scene.PendingProp != null)
            {
                Scene.PendingProp = null;
                return true;
            }

            if (!TerrainUndoActive) return false;
            var b = Scene.Brush;
            switch (e.Key)
            {
                case Key.Z when ctrl:
                    return TerrainUndoRedo(false);
                case Key.Y when ctrl:
                    return TerrainUndoRedo(true);
                case Key.Enter:
                    Scene.FinishRoad();
                    BuildPalette();
                    return true;
                case Key.Back:
                    Scene.RemoveLastRoadPoint();
                    return true;
                case Key.Escape:
                    Scene.CancelRoad();
                    return true;
                case Key.OemOpenBrackets:
                    b.Radius = Math.Max(0.01, b.Radius / 1.25);
                    BuildPalette();
                    return true;
                case Key.OemCloseBrackets:
                    b.Radius = Math.Min(25, b.Radius * 1.25);
                    BuildPalette();
                    return true;
                case Key.Q when !ctrl:
                case Key.E when !ctrl:
                    b.Angle = (b.Angle + (e.Key == Key.Q ? -15 : 15) + 360) % 360;
                    BuildPalette();
                    return true;
            }

            if (ctrl) return false;
            string k = e.Key switch
            {
                >= Key.D0 and <= Key.D9 => ((int)(e.Key - Key.D0)).ToString(CultureInfo.InvariantCulture),
                >= Key.NumPad0 and <= Key.NumPad9 => ((int)(e.Key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture),
                Key.Y => "Y",
                _ => string.Empty
            };
            foreach (var (tool, key) in ToolKeys)
            {
                if (key != k) continue;
                Scene.TerrainTool = tool;
                Scene.CancelRoad();
                BuildPalette();
                return true;
            }

            return false;
        }

        // ================================================================ inspector: editor flags, prop size

        private void EditorSection(WorldDocument world, WorldObject o)
        {
            Inspector.Children.Add(Caption("Düzenleyici"));
            if (WorldBuilding.PropOf(o) is { } def)
            {
                var size = TerrainProps.SizeOf(o.Parameters, def);
                Inspector.Children.Add(Vector3Row("Boyut X · Y · Z (mm)", size.X * 1000, size.Y * 1000, size.Z * 1000, (x, y, z) =>
                    Edit($"{o.Name} boyutu değişti", () => WorldBuilding.ResizeProp(o, new Vec3(x / 1000, y / 1000, z / 1000)))));
            }

            var hidden = new CheckBox { Content = "Gizle (görünmez, simülasyonda var)", IsChecked = WorldBuilding.IsHidden(o), Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6) };
            hidden.Click += (_, _) => Edit(hidden.IsChecked == true ? $"{o.Name} gizlendi" : $"{o.Name} gösterildi", () => WorldBuilding.SetHidden(o, hidden.IsChecked == true));
            Inspector.Children.Add(hidden);
            var locked = new CheckBox { Content = "Kilitle (taşınamaz, silinemez)", IsChecked = WorldBuilding.IsLocked(o), Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6) };
            locked.Click += (_, _) => Edit(locked.IsChecked == true ? $"{o.Name} kilitlendi" : $"{o.Name} kilidi açıldı", () => WorldBuilding.SetLocked(o, locked.IsChecked == true));
            Inspector.Children.Add(locked);

            var group = new TextBox { Text = WorldBuilding.GroupOf(o) ?? string.Empty, Tag = "grup adı (aynı adlı nesneler birlikte taşınır)" };
            group.LostFocus += (_, _) =>
            {
                string g = group.Text.Trim();
                if (g != (WorldBuilding.GroupOf(o) ?? string.Empty)) Edit(g.Length == 0 ? $"{o.Name} gruptan çıktı" : $"{o.Name} → {g}", () => WorldBuilding.SetGroup(o, g));
            };
            Inspector.Children.Add(Labeled("Grup", group));
            int mates = WorldBuilding.GroupMates(world, o).Count;
            if (mates > 0) Inspector.Children.Add(Note($"Grupta {mates} nesne daha var; sürüklerken birlikte taşınırlar."));
            else Inspector.Children.Add(Action("Yeni grup başlat", () => Edit("Grup oluşturuldu", () => WorldBuilding.SetGroup(o, WorldBuilding.NewGroupName(world)))));

            if (world.Terrain != null)
            {
                Inspector.Children.Add(Action("Zemine oturt", () => Edit($"{o.Name} zemine oturtuldu", () =>
                {
                    double z = world.Terrain.Height(o.Pose.X, o.Pose.Y);
                    world.MoveAssembly(o, 0, 0, z - o.Pose.Z);
                })));
                if (world.MountedOn(o.Id).Count == 0)
                    Inspector.Children.Add(Action("Eğime hizala", () => Edit($"{o.Name} eğime hizalandı", () => WorldBuilding.PlaceOnGround(world, o, true))));
            }

            Inspector.Children.Add(Note("Seçili nesnede: renkli oklar taşır (Ctrl+ok: o eksende ölçekler), sarı halka döndürür."));
        }

        /// <summary>Delete hook: locked objects are kept.</summary>
        private bool DeleteBlocked(WorldObject o)
        {
            if (!WorldBuilding.IsLocked(o)) return false;
            W.Status = $"{o.Name} kilitli; önce kilidini açın";
            return true;
        }

        // ================================================================ snapshot hooks

        /// <summary>"--fault arazi-parkuru+arazi" shows the sculpt mode, "+boya" the paint mode, "+yakın" a close camera.</summary>
        private string? ParseTerrainSnapshot(string? template)
        {
            _snapshotTerrainMode = null;
            if (template == null) return null;
            int plus = template.IndexOf('+');
            if (plus < 0) return template;
            _snapshotTerrainMode = template.Substring(plus + 1);
            return template.Substring(0, plus);
        }

        private void FinishTerrainSnapshot(bool simulated)
        {
            var world = World;
            if (world?.Terrain is not { } map) return;
            string mode = _snapshotTerrainMode ?? string.Empty;
            if (mode == "arazi" || mode == "boya")
            {
                TabTerrain.IsChecked = true;
                Scene.TerrainTool = mode == "boya" ? TerrainTool.Paint : TerrainTool.Raise;
                Scene.Brush.Layer = TerrainLayers.IndexOf("mud");
                Scene.Brush.Radius = 0.45;
                BuildPalette();
                Scene.LookAt(new System.Windows.Media.Media3D.Point3D(1.2, 0.6, 0.1), 4.2, 0.62, 1.05);
                Scene.ShowBrushAt(1.6, 1.0);
                return;
            }

            if (simulated || mode == "yakın")
            {
                // Follow the car: the first moving object.
                var first = world.Objects.FirstOrDefault(o => !o.IsStatic && !WorldBuilding.IsProp(o));
                var pose = first != null ? _session.Physics?.PoseOf(first.Id) : null;
                double x = pose?.Position.X ?? first?.Pose.X ?? 0, y = pose?.Position.Y ?? first?.Pose.Y ?? 0;
                Scene.LookAt(new System.Windows.Media.Media3D.Point3D(x + 0.5, y, map.Height(x, y)), 2.2, 0.5, 1.25);
                return;
            }

            Scene.FrameTerrain();
        }
    }
}
