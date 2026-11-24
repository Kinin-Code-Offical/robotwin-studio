using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Modeling;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Pages
{
    public partial class ModelPage
    {
        private bool _scrubbing;
        private bool _rebuildQueued;
        private readonly Dictionary<string, (Border Row, TextBlock Name)> _rows = new Dictionary<string, (Border, TextBlock)>();

        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        private Geometry Icon(string key) => (Geometry)FindResource(key);

        private Geometry IconFor(ModelObject o)
        {
            if (o.Primitive == null) return Icon("M.Edit");
            return o.Primitive.Kind switch
            {
                PrimitiveKind.Box => Icon("M.Box"),
                PrimitiveKind.Cylinder => Icon("M.Cylinder"),
                PrimitiveKind.Cone => Icon("M.Cone"),
                PrimitiveKind.Sphere => Icon("M.Sphere"),
                PrimitiveKind.Torus => Icon("M.Torus"),
                PrimitiveKind.Tube => Icon("M.Tube"),
                PrimitiveKind.Wedge => Icon("M.Wedge"),
                PrimitiveKind.Plate => Icon("M.Plate"),
                PrimitiveKind.Extrude => Icon("M.Extrude"),
                PrimitiveKind.Revolve => Icon("M.Revolve"),
                _ => Icon("I.Part")
            };
        }

        // ================================================================ outliner

        private void BuildOutliner()
        {
            Outliner.Children.Clear();
            _rows.Clear();
            var doc = _ed.Doc;
            int parts = doc.Objects.Count(o => o.ParentId == null);   // [assembly] every root is a part of its own
            OutlinerCount.Text = doc.Objects.Count == 0 ? string.Empty : parts == doc.Objects.Count ? $"{parts} parça" : $"{parts} parça · {doc.Objects.Count} nesne";
            if (doc.Objects.Count == 0)
            {
                Outliner.Children.Add(new TextBlock
                {
                    Text = "Henüz parça yok. Shift+A ile şekil ya da Kütüphane sekmesinden hazır parça ekleyin. Bir dosyada istediğiniz kadar parça olur: en üstteki her nesne ayrı bir parçadır, altına bağlanan (Ctrl+P) nesneler onunla tek parça olur. Bitince 'Montaja gönder'.",
                    Style = (Style)FindResource("Text.Caption"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 4, 8, 0)
                });
                return;
            }

            var ids = new HashSet<string>(doc.Objects.Select(o => o.Id));
            void Add(ModelObject o, int depth, HashSet<string> seen)
            {
                if (!seen.Add(o.Id)) return;
                Outliner.Children.Add(OutlinerRow(o, depth));
                foreach (var c in doc.Children(o.Id)) Add(c, depth + 1, seen);
            }

            var visited = new HashSet<string>();
            foreach (var o in doc.Objects.Where(o => o.ParentId == null || !ids.Contains(o.ParentId))) Add(o, 0, visited);
            foreach (var o in doc.Objects.Where(o => !visited.Contains(o.Id))) Add(o, 0, visited);
        }

        private Border OutlinerRow(ModelObject o, int depth)
        {
            bool selected = _ed.Selected.Contains(o.Id);
            bool active = o.Id == _ed.ActiveId;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(depth * 14 + 4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Icon { Data = IconFor(o), Size = 14, Foreground = active ? Res("B.Accent") : selected ? Res("B.Text") : Res("B.TextMuted"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);
            var name = new TextBlock
            {
                Text = o.Name,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = !o.Visible ? Res("B.TextDisabled") : active ? Res("B.Text") : selected ? Res("B.Text") : Res("B.TextSecondary"),
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
            };
            Grid.SetColumn(name, 2);
            grid.Children.Add(name);
            var eye = new Button
            {
                Style = (Style)FindResource("Btn.Icon"),
                Width = 24,
                Height = 22,
                ToolTip = o.Visible ? "Gizle (H)" : "Göster",
                Content = new Icon { Data = Icon("I.Eye"), Size = 14, Opacity = o.Visible ? 0.9 : 0.3 }
            };
            eye.Click += (_, e) =>
            {
                _ed.SetVisible(o, !o.Visible);
                e.Handled = true;
            };
            Grid.SetColumn(eye, 3);
            grid.Children.Add(eye);
            var row = new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 1, 2, 1),
                Margin = new Thickness(0, 0, 0, 1),
                Background = active ? Res("B.Pressed") : selected ? Res("B.Hover") : Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    StartRename(o.Id);
                    e.Handled = true;
                    return;
                }

                if (_ed.Mode == EditorMode.Edit) _ed.ToggleEditMode();
                bool extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                _ed.Select(o.Id, extend);
                e.Handled = true;
            };
            row.MouseRightButtonUp += (_, e) =>
            {
                if (_ed.Mode == EditorMode.Edit) return;
                if (!_ed.Selected.Contains(o.Id)) _ed.Select(o.Id);
                ShowContextMenu();
                e.Handled = true;
            };
            row.MouseEnter += (_, _) =>
            {
                if (!selected) row.Background = Res("B.Surface");
            };
            row.MouseLeave += (_, _) =>
            {
                if (!selected) row.Background = Brushes.Transparent;
            };
            _rows[o.Id] = (row, name);
            return row;
        }

        private void StartRename(string? id)
        {
            if (id == null || !_rows.TryGetValue(id, out var r)) return;
            var o = _ed.Doc.Find(id);
            if (o == null) return;
            var grid = (Grid)r.Row.Child;
            var box = new TextBox { Text = o.Name, FontSize = 12.5, Padding = new Thickness(4, 1, 4, 1) };
            Grid.SetColumn(box, 2);
            grid.Children.Remove(r.Name);
            grid.Children.Add(box);
            bool done = false;
            void Finish(bool commit)
            {
                if (done) return;
                done = true;
                string text = box.Text.Trim();
                if (commit && text.Length > 0 && text != o.Name)
                {
                    _ed.Do("Yeniden adlandırıldı: " + text, () => o.Name = text, ModelChange.Selection);
                }
                else BuildOutliner();
            }

            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Finish(true);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Finish(false);
                    e.Handled = true;
                }
            };
            box.LostKeyboardFocus += (_, _) => Finish(true);
            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
        }

        // ================================================================ properties helpers

        private void Section(string title, string? sub = null)
        {
            var g = new Grid { Margin = new Thickness(0, Props.Children.Count == 0 ? 2 : 16, 0, 8) };
            g.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("B.Text") });
            if (sub != null) g.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Res("B.TextMuted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            Props.Children.Add(g);
        }

        private void Line(string label, string value, Brush? color = null)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Res("B.TextMuted") });
            var v = new TextBlock { Text = value, FontSize = 12, FontFamily = (FontFamily)FindResource("F.Mono"), Foreground = color ?? Res("B.TextSecondary"), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            Props.Children.Add(g);
        }

        private void Caption(string text) =>
            Props.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });

        /// <summary>A number field bound to an apply action, with undo and live scrubbing.</summary>
        private NumberField Num(Panel host, string label, double value, string unit, Action<double> apply, string undo, bool geometry,
            double step = 1, int decimals = 2, double min = double.NegativeInfinity, double max = double.PositiveInfinity, bool integer = false)
        {
            var f = new NumberField(label, value, unit, step, decimals) { Min = min, Max = max, Integer = integer };
            f.Committed += v => _ed.Do(undo, () => apply(v));
            f.ScrubStarted += () =>
            {
                _scrubbing = true;
                _ed.BeginChange();
            };
            f.Scrubbed += v =>
            {
                apply(v);
                if (!geometry)
                {
                    _vp.RefreshTransforms();
                    return;
                }

                // Parametric parts can take a few hundred ms (booleans): coalesce rebuilds while dragging.
                if (_rebuildQueued) return;
                _rebuildQueued = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _rebuildQueued = false;
                    _vp.Rebuild();
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            f.ScrubEnded += v =>
            {
                _scrubbing = false;
                _ed.CommitChange(undo);
            };
            host.Children.Add(f);
            return f;
        }

        private Button SmallButton(string text, Action click, string? tip = null)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 0, 10, 0), MinHeight = 26, FontSize = 12, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
            b.Click += (_, _) => click();
            return b;
        }

        // ================================================================ properties

        private void BuildProperties()
        {
            Props.Children.Clear();
            if (_ed.Mode == EditorMode.Edit)
            {
                EditProperties();
                return;
            }

            var objs = _ed.SelectedObjects.ToList();
            if (objs.Count == 0)
            {
                DocumentProperties();
                return;
            }

            var o = _ed.Active ?? objs.Last();
            ObjectHeader(o, objs.Count);
            TransformSection(o);
            ShapeOpsSection(objs);
            if (o.Primitive != null) PrimitiveSection(o);
            MaterialSection(o);
            ShadingSection(o);
            StatsSection(o, objs);
        }

        /// <summary>Boolean and slicing in plain sight (they were only in the right-click menu).</summary>
        private void ShapeOpsSection(List<ModelObject> objs)
        {
            Section("Kes, kırp, birleştir", objs.Count > 1 ? $"{objs.Count} nesne seçili" : null);
            var wrap = new WrapPanel();
            wrap.Children.Add(SmallButton("Dilimle  K", () => _vp.StartModal(ModalKind.Knife), "Ekrana bir çizgi çizin: etkin nesne o çizgi boyunca iki kapalı parçaya ayrılır; parçaları ayrı ayrı taşıyıp döndürebilirsiniz"));
            wrap.Children.Add(SmallButton("Fark (kırp)", () => _ed.Boolean(BooleanOp.Difference), "Hedeften, ona geçen araç nesnelerinin hacmini oyar (iç içe geçen kısım kırpılır)"));
            wrap.Children.Add(SmallButton("Birleşim", () => _ed.Boolean(BooleanOp.Union), "İç içe geçen nesneleri tek kapalı gövdede birleştirir"));
            wrap.Children.Add(SmallButton("Kesişim", () => _ed.Boolean(BooleanOp.Intersection), "Yalnız nesnelerin ortak hacmi kalır"));
            Props.Children.Add(wrap);
            Caption(objs.Count > 1
                ? "Boolean hedefi en son seçilen (etkin) nesnedir; diğerleri araçtır ve işlemden sonra kaldırılır."
                : "Boolean için önce araç nesnesini, sonra Shift+tık ile hedefi seçin. Ayrıntılı nokta/kenar/yüz düzenleme: Tab.");
        }

        private void ObjectHeader(ModelObject o, int count)
        {
            var nameBox = new TextBox { Text = o.Name, FontSize = 14, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 2, 0, 4) };
            nameBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Keyboard.ClearFocus();
            };
            nameBox.LostKeyboardFocus += (_, _) =>
            {
                string t = nameBox.Text.Trim();
                if (t.Length > 0 && t != o.Name) _ed.Do("Yeniden adlandırıldı: " + t, () => o.Name = t, ModelChange.Selection);
            };
            Props.Children.Add(nameBox);
            string kind = o.Primitive != null
                ? (o.Primitive.Kind == PrimitiveKind.Part ? RobotParts.Find(o.Primitive.Template)?.Name ?? "Parça" : PrimitiveSchema.Name(o.Primitive.Kind)) + " · parametrik"
                : $"Düzenlenebilir ağ · {o.LocalMesh().Vertices.Count} nokta, {o.LocalMesh().Faces.Count} yüz";
            if (count > 1) kind += $" · {count} nesne seçili (etkin nesne gösteriliyor)";
            Caption(kind);
        }

        private void TransformSection(ModelObject o)
        {
            var t = o.Transform;
            Section("Dönüşüm", "mm · derece");
            var host = new StackPanel();
            Props.Children.Add(host);
            Num(host, "Konum X", t.X, "mm", v => t.X = v, "Konum", false, 1, 3);
            Num(host, "Konum Y", t.Y, "mm", v => t.Y = v, "Konum", false, 1, 3);
            Num(host, "Konum Z", t.Z, "mm", v => t.Z = v, "Konum", false, 1, 3);
            Num(host, "Döndürme X", t.RX, "°", v => t.RX = v, "Döndürme", false, 5, 2);
            Num(host, "Döndürme Y", t.RY, "°", v => t.RY = v, "Döndürme", false, 5, 2);
            Num(host, "Döndürme Z", t.RZ, "°", v => t.RZ = v, "Döndürme", false, 5, 2);
            Num(host, "Ölçek X", t.SX, "", v => t.SX = v == 0 ? 1e-4 : v, "Ölçek", false, 0.05, 4);
            Num(host, "Ölçek Y", t.SY, "", v => t.SY = v == 0 ? 1e-4 : v, "Ölçek", false, 0.05, 4);
            Num(host, "Ölçek Z", t.SZ, "", v => t.SZ = v == 0 ? 1e-4 : v, "Ölçek", false, 0.05, 4);
            var size = o.LocalMesh().Bounds.Size;
            Section("Boyutlar", "nesne eksenlerinde");
            var dims = new StackPanel();
            Props.Children.Add(dims);
            void Dim(string label, double local, Func<double> getScale, Action<double> setScale)
            {
                if (local <= 1e-9) return;
                Num(dims, label, Math.Abs(local * getScale()), "mm", v =>
                {
                    double s = getScale();
                    setScale((s < 0 ? -1 : 1) * Math.Max(1e-4, v) / local);
                }, "Boyut", false, 1, 3, 0.001);
            }

            Dim("Boyut X", size.X, () => t.SX, v => t.SX = v);
            Dim("Boyut Y", size.Y, () => t.SY, v => t.SY = v);
            Dim("Boyut Z", size.Z, () => t.SZ, v => t.SZ = v);
            if (o.ParentId != null && _ed.Doc.Find(o.ParentId) is { } parent) Caption($"Ebeveyn: {parent.Name} (değerler ebeveyne göredir)");
        }

        private void PrimitiveSection(ModelObject o)
        {
            var spec = o.Primitive!;
            string title = spec.Kind == PrimitiveKind.Part ? RobotParts.Find(spec.Template)?.Name ?? "Parça" : PrimitiveSchema.Name(spec.Kind);
            Section(title + " ölçüleri", "canlı");
            var host = new StackPanel();
            Props.Children.Add(host);
            foreach (var p in PrimitiveSchema.For(spec))
            {
                var ps = p;
                double step = ps.Integer ? 1 : ps.Unit == "°" ? 5 : 0.5;
                Num(host, ps.Label, spec.Get(ps.Key), ps.Unit, v => spec.Set(ps.Key, ps.Clamp(v)), ps.Label, true, step, 3, ps.Min, ps.Max, ps.Integer);
            }

            if (spec.Kind == PrimitiveKind.Part && RobotParts.Find(spec.Template) is { } tpl) Caption(tpl.Description);
            if (spec.Kind == PrimitiveKind.Plate) PlateHoles(spec);
            if (spec.Kind == PrimitiveKind.Extrude) SketchSection(spec);
            if (spec.Kind == PrimitiveKind.Revolve) RevolveSection(spec);
            var conv = SmallButton("Düzenlenebilir ağa dönüştür", _ed.ConvertToMesh, "Parametreler kaybolur; nokta/kenar/yüz düzenlemesi açılır (Tab da dönüştürür)");
            conv.Margin = new Thickness(0, 6, 0, 0);
            conv.HorizontalAlignment = HorizontalAlignment.Left;
            Props.Children.Add(conv);
        }

        private void PlateHoles(PrimitiveSpec spec)
        {
            spec.Holes ??= new List<HoleSpec>();
            Section("Ek delikler", $"{spec.Holes.Count} adet");
            int i = 0;
            foreach (var h in spec.Holes.ToList())
            {
                int index = ++i;
                var hole = h;
                var head = new Grid { Margin = new Thickness(0, 4, 0, 2) };
                head.Children.Add(new TextBlock { Text = $"Delik {index}", FontSize = 12, Foreground = Res("B.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
                var rm = new Button { Style = (Style)FindResource("Btn.Icon"), Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Deliği kaldır", Content = new Icon { Data = Icon("I.Close"), Size = 12 } };
                rm.Click += (_, _) => _ed.Do("Delik kaldırıldı", () => spec.Holes.Remove(hole));
                head.Children.Add(rm);
                Props.Children.Add(head);
                var host = new StackPanel();
                Props.Children.Add(host);
                Num(host, "Merkez X", hole.X, "mm", v => hole.X = v, "Delik", true, 0.5, 3);
                Num(host, "Merkez Y", hole.Y, "mm", v => hole.Y = v, "Delik", true, 0.5, 3);
                Num(host, "Çap", hole.D, "mm", v => hole.D = Math.Max(0.1, v), "Delik", true, 0.1, 3, 0.1);
            }

            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(SmallButton("+ M3 delik", () => _ed.Do("Delik eklendi", () => spec.Holes.Add(new HoleSpec { X = 0, Y = 0, D = 3.2 }))));
            row.Children.Add(SmallButton("+ M2 delik", () => _ed.Do("Delik eklendi", () => spec.Holes.Add(new HoleSpec { X = 0, Y = 0, D = 2.2 }))));
            Props.Children.Add(row);
        }

        private void SketchSection(PrimitiveSpec spec)
        {
            spec.Sketch ??= new Sketch2D();
            var sk = spec.Sketch;
            Section("Profil (dış şekil)");
            ShapeEditor(sk.Outer, false);
            Section("Delikler", $"{sk.Holes.Count} adet");
            int i = 0;
            foreach (var h in sk.Holes.ToList())
            {
                int index = ++i;
                var hole = h;
                var head = new Grid { Margin = new Thickness(0, 4, 0, 2) };
                head.Children.Add(new TextBlock { Text = $"Delik {index}", FontSize = 12, Foreground = Res("B.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
                var rm = new Button { Style = (Style)FindResource("Btn.Icon"), Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Deliği kaldır", Content = new Icon { Data = Icon("I.Close"), Size = 12 } };
                rm.Click += (_, _) => _ed.Do("Delik kaldırıldı", () => sk.Holes.Remove(hole));
                head.Children.Add(rm);
                Props.Children.Add(head);
                ShapeEditor(hole, true);
            }

            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(SmallButton("+ Daire", () => _ed.Do("Delik eklendi", () => sk.Holes.Add(SketchShape.Circle(5)))));
            row.Children.Add(SmallButton("+ Dikdörtgen", () => _ed.Do("Delik eklendi", () => sk.Holes.Add(SketchShape.Rect(10, 6)))));
            row.Children.Add(SmallButton("+ Oluk", () => _ed.Do("Delik eklendi", () => sk.Holes.Add(SketchShape.Slot(14, 4)))));
            row.Children.Add(SmallButton("+ Altıgen", () => _ed.Do("Delik eklendi", () => sk.Holes.Add(SketchShape.Ngon(6, 3.2)))));
            Props.Children.Add(row);
        }

        private void ShapeEditor(SketchShape s, bool hole)
        {
            var kinds = new[] { (SketchShapeKind.Rectangle, "Dikdörtgen"), (SketchShapeKind.Circle, "Daire"), (SketchShapeKind.Polygon, "Düzgün çokgen"), (SketchShapeKind.Slot, "Oluk") };
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (k, n) in kinds) combo.Items.Add(new ComboBoxItem { Content = n, Tag = k });
            if (s.Kind == SketchShapeKind.Custom) combo.Items.Add(new ComboBoxItem { Content = "Serbest çokgen", Tag = SketchShapeKind.Custom });
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (SketchShapeKind)i.Tag == s.Kind);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem it && (SketchShapeKind)it.Tag != s.Kind) _ed.Do("Şekil türü", () => s.Kind = (SketchShapeKind)it.Tag);
            };
            Props.Children.Add(combo);
            var host = new StackPanel();
            Props.Children.Add(host);
            Num(host, "Merkez X", s.X, "mm", v => s.X = v, "Profil", true, 0.5, 3);
            Num(host, "Merkez Y", s.Y, "mm", v => s.Y = v, "Profil", true, 0.5, 3);
            switch (s.Kind)
            {
                case SketchShapeKind.Circle:
                    Num(host, "Çap", s.Radius * 2, "mm", v => s.Radius = Math.Max(0.05, v / 2), "Profil", true, 0.5, 3, 0.1);
                    break;
                case SketchShapeKind.Polygon:
                    Num(host, "Kenar sayısı", s.Sides, "", v => s.Sides = (int)Math.Max(3, v), "Profil", true, 1, 0, 3, 64, true);
                    Num(host, "Çevre yarıçapı", s.Radius, "mm", v => s.Radius = Math.Max(0.05, v), "Profil", true, 0.5, 3, 0.05);
                    Num(host, "Döndürme", s.Rotation, "°", v => s.Rotation = v, "Profil", true, 5, 2);
                    break;
                case SketchShapeKind.Slot:
                    Num(host, "Uzunluk", s.Width, "mm", v => s.Width = Math.Max(0.1, v), "Profil", true, 0.5, 3, 0.1);
                    Num(host, "Genişlik", s.Height, "mm", v => s.Height = Math.Max(0.1, v), "Profil", true, 0.5, 3, 0.1);
                    Num(host, "Döndürme", s.Rotation, "°", v => s.Rotation = v, "Profil", true, 5, 2);
                    break;
                case SketchShapeKind.Custom:
                    Caption("Serbest çokgen noktaları değiştirilemez; türünü değiştirebilirsiniz.");
                    break;
                default:
                    Num(host, "Genişlik", s.Width, "mm", v => s.Width = Math.Max(0.1, v), "Profil", true, 0.5, 3, 0.1);
                    Num(host, "Yükseklik", s.Height, "mm", v => s.Height = Math.Max(0.1, v), "Profil", true, 0.5, 3, 0.1);
                    Num(host, "Köşe yarıçapı", s.CornerRadius, "mm", v => s.CornerRadius = Math.Max(0, v), "Profil", true, 0.5, 3, 0);
                    Num(host, "Döndürme", s.Rotation, "°", v => s.Rotation = v, "Profil", true, 5, 2);
                    break;
            }
        }

        private void RevolveSection(PrimitiveSpec spec)
        {
            spec.Sketch ??= new Sketch2D { Outer = new SketchShape { Kind = SketchShapeKind.Custom, Points = new List<double> { 0, 0, 10, 0, 10, 10, 0, 10 } } };
            var outer = spec.Sketch.Outer;
            Section("Profil noktaları", "yarıçap, yükseklik");
            Caption("Z ekseni etrafında döndürülen kesit: \"r,z; r,z; …\" (mm). Örnek kasnak: 3,0; 15,0; 15,2; 12,4; 12,8; 15,10; 15,12; 3,12");
            string text = outer.Kind == SketchShapeKind.Custom && outer.Points != null
                ? string.Join("; ", Enumerable.Range(0, outer.Points.Count / 2).Select(i => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###}", outer.Points[2 * i], outer.Points[2 * i + 1])))
                : string.Empty;
            var box = new TextBox { Text = text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, Padding = new Thickness(6, 4, 6, 4), MinHeight = 48 };
            void Commit()
            {
                var pts = new List<double>();
                foreach (var pair in box.Text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var xy = pair.Split(',');
                    if (xy.Length != 2 || !double.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ||
                        !double.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                    {
                        _ed.Status("Profil okunamadı: her nokta \"r,z\" biçiminde, noktalar ';' ile ayrılmalı");
                        return;
                    }

                    pts.Add(Math.Max(0, r));
                    pts.Add(z);
                }

                if (pts.Count < 6)
                {
                    _ed.Status("Profil en az üç nokta olmalı");
                    return;
                }

                if (outer.Points != null && outer.Points.SequenceEqual(pts)) return;
                _ed.Do("Döndürme profili", () =>
                {
                    outer.Kind = SketchShapeKind.Custom;
                    outer.X = 0;
                    outer.Y = 0;
                    outer.Rotation = 0;
                    outer.Points = pts;
                });
            }

            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                    e.Handled = true;
                }
            };
            box.LostKeyboardFocus += (_, _) => Commit();
            Props.Children.Add(box);
        }

        private static readonly string[] Swatches =
        {
            "#B9BFC9", "#8E98A8", "#5A6273", "#2A2D34", "#E8E4D8", "#E87A2C", "#E0A030", "#D9483B", "#C2457E", "#8E6CC8", "#3B82C4", "#2E9E9E", "#2E9E6E", "#7DB343"
        };

        private void MaterialSection(ModelObject o)
        {
            Section("Malzeme ve baskı");
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var m in ModelMaterials.All) combo.Items.Add(new ComboBoxItem { Content = $"{m.Name}  ·  {m.Density:0} kg/m³", Tag = m.Name });
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, ModelMaterials.Get(o.Material).Name, StringComparison.OrdinalIgnoreCase));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem it && (string)it.Tag != o.Material)
                {
                    var objs = _ed.SelectedObjects.ToList();
                    _ed.Do("Malzeme: " + it.Tag, () =>
                    {
                        foreach (var x in objs) x.Material = (string)it.Tag;
                    }, ModelChange.Selection);
                }
            };
            Props.Children.Add(combo);
            var mat = ModelMaterials.Get(o.Material);
            if (mat.Printed)
            {
                var host = new StackPanel();
                Props.Children.Add(host);
                Num(host, "Doluluk (infill)", o.Infill * 100, "%", v =>
                {
                    foreach (var x in _ed.SelectedObjects) x.Infill = Math.Max(0, Math.Min(100, v)) / 100;
                }, "Doluluk", false, 5, 0, 0, 100);
            }

            var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var hex in Swatches.Concat(new[] { mat.Color }).Distinct())
            {
                var c = ModelGeometry.ParseColor(hex, Colors.Gray);
                bool current = string.Equals(hex, o.Color, StringComparison.OrdinalIgnoreCase);
                var sw = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(c),
                    BorderBrush = current ? Res("B.Text") : Res("B.BorderStrong"),
                    BorderThickness = new Thickness(current ? 2 : 1),
                    Margin = new Thickness(0, 0, 5, 5),
                    Cursor = Cursors.Hand,
                    ToolTip = hex == mat.Color ? hex + " (malzeme rengi)" : hex
                };
                string h = hex;
                sw.MouseLeftButtonUp += (_, _) =>
                {
                    var objs = _ed.SelectedObjects.ToList();
                    _ed.Do("Renk " + h, () =>
                    {
                        foreach (var x in objs) x.Color = h;
                    });
                };
                wrap.Children.Add(sw);
            }

            Props.Children.Add(wrap);
            var hexBox = new TextBox { Text = o.Color, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, Padding = new Thickness(6, 2, 6, 2), Width = 110, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Renk #RRGGBB" };
            hexBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                string t = hexBox.Text.Trim();
                if (!t.StartsWith("#", StringComparison.Ordinal)) t = "#" + t;
                if (t.Length == 7 && int.TryParse(t.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int _))
                {
                    var objs = _ed.SelectedObjects.ToList();
                    _ed.Do("Renk " + t, () =>
                    {
                        foreach (var x in objs) x.Color = t.ToUpperInvariant();
                    });
                }
            };
            Props.Children.Add(hexBox);
        }

        private void ShadingSection(ModelObject o)
        {
            Section("Gölgelendirme");
            var sw = new CheckBox { Content = "Yumuşak gölgelendirme (otomatik)", IsChecked = o.SmoothShading, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6) };
            sw.Click += (_, _) => _ed.SetSmooth(sw.IsChecked == true);
            Props.Children.Add(sw);
            if (o.SmoothShading)
            {
                var host = new StackPanel();
                Props.Children.Add(host);
                Num(host, "Keskin kenar açısı", o.AutoSmoothAngle, "°", v => o.AutoSmoothAngle = Math.Max(0, Math.Min(180, v)), "Yumuşatma açısı", true, 5, 0, 0, 180);
            }
        }

        private void StatsSection(ModelObject o, List<ModelObject> objs)
        {
            var s = ModelMeasure.Of(_ed.Doc, o);
            var mat = ModelMaterials.Get(o.Material);
            Section("Ölçüm", "dünya koordinatlarında");
            Line("Boyutlar", $"{s.Size.X:0.##} × {s.Size.Y:0.##} × {s.Size.Z:0.##} mm");
            Line("Hacim", $"{s.VolumeMm3 / 1000:0.###} cm³");
            Line("Yüzey alanı", $"{s.AreaMm2 / 100:0.##} cm²");
            Line("Kütle", $"{s.MassG:0.##} g" + (mat.Printed ? $"  ({mat.Name}, %{o.Infill * 100:0} doluluk)" : $"  ({mat.Name})"), Res("B.Text"));
            Line("Ağırlık merkezi", $"{s.CenterOfMass.X:0.#}, {s.CenterOfMass.Y:0.#}, {s.CenterOfMass.Z:0.#} mm");
            Line("Ağ", $"{s.Faces} yüz · {s.Triangles} üçgen");
            Line("Durum", s.Closed ? "kapalı, baskıya uygun" : "açık ağ (delik/çakışma var)", s.Closed ? Res("B.Success") : Res("B.Warning"));
            if (objs.Count > 1)
            {
                var t = ModelMeasure.Total(_ed.Doc, objs);
                Section("Seçim toplamı", $"{objs.Count} nesne");
                Line("Boyutlar", $"{t.Size.X:0.##} × {t.Size.Y:0.##} × {t.Size.Z:0.##} mm");
                Line("Kütle", $"{t.MassG:0.##} g", Res("B.Text"));
                Line("Hacim", $"{t.VolumeMm3 / 1000:0.###} cm³");
            }
        }

        private void DocumentProperties()
        {
            var doc = _ed.Doc;
            Section("Model");
            var title = new TextBox { Text = doc.Title, FontSize = 14, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 0, 6) };
            title.LostKeyboardFocus += (_, _) =>
            {
                string t = title.Text.Trim();
                if (t.Length > 0 && t != doc.Title) _ed.Do("Model adı", () => doc.Title = t, ModelChange.Selection);
            };
            title.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Keyboard.ClearFocus();
            };
            Props.Children.Add(title);
            var visible = doc.Objects.Where(o => o.Visible).ToList();
            if (visible.Count > 0)
            {
                var t = ModelMeasure.Total(doc, visible);
                Line("Nesneler", $"{doc.Objects.Count} ({visible.Count} görünür)");
                Line("Boyutlar", $"{t.Size.X:0.##} × {t.Size.Y:0.##} × {t.Size.Z:0.##} mm");
                Line("Toplam kütle", $"{t.MassG:0.##} g", Res("B.Text"));
                Line("Toplam hacim", $"{t.VolumeMm3 / 1000:0.###} cm³");
            }

            Section("Kısayollar", "Blender uyumlu");
            foreach (var (k, v) in new[]
                     {
                         ("Shift+A", "şekil / parça ekle"), ("G · R · S", "taşı · döndür · ölçekle"), ("  ardından X/Y/Z", "eksen, Shift+X düzlem"), ("  sayı + Enter", "kesin değer (mm, °)"),
                         ("Tab", "düzenleme modu"), ("1 · 2 · 3", "nokta · kenar · yüz"), ("E · I", "çıkar · içe kaydır"), ("M · F · X", "birleştir · yüz · sil"),
                         ("K", "bıçak / dilimle"), ("Ctrl+R", "halka kesim"), ("Ctrl+B", "pah kır (bevel)"), ("L · Alt+tık", "bağlı parça · halka seçimi"), ("Ctrl+J", "birleştir (join)"), ("Sağ tık", "Boolean ve diğer işlemler"), ("Orta tuş", "döndür, Shift ile kaydır"), ("Numpad 1/3/7", "ön / sağ / üst görünüm"),
                         ("Numpad . · Home", "seçimi / hepsini sığdır"), ("Z · Alt+Z", "görünüm · X-ray"), ("Ctrl (sürüklerken)", "ızgara adımı"), ("Ctrl+Z", "geri al")
                     }) Line(k, v);
        }

        private void EditProperties()
        {
            var o = _ed.Active;
            var mesh = _ed.EditMesh;
            if (o == null || mesh == null) return;
            Section("Düzenleme: " + o.Name);
            var vs = _ed.SelectedVertexSet();
            Line("Seçim", $"{vs.Count} n · {_ed.SelectedEdges().Count} k · {_ed.SelectedFaceList().Count} y");
            Line("Ağ", $"{mesh.Vertices.Count} nokta · {mesh.Faces.Count} yüz");
            var topo = mesh.Topology();
            Line("Durum", topo.IsClosed ? "kapalı" : $"açık: {topo.BoundaryEdges} sınır, {topo.NonManifoldEdges} çoklu kenar", topo.IsClosed ? Res("B.Success") : Res("B.Warning"));
            if (vs.Count > 0)
            {
                var w = _ed.Doc.WorldMatrix(o);
                var c = Vec3.Zero;
                foreach (int v in vs) c += w.Apply(mesh.Vertices[v]);
                c /= vs.Count;
                Section(vs.Count == 1 ? "Nokta konumu" : "Seçim merkezi", "dünya, mm");
                var host = new StackPanel();
                Props.Children.Add(host);
                var inv = w.Inverse();
                var orig = vs.ToDictionary(v => v, v => mesh.Vertices[v]);
                void MoveTo(int axis, double value)
                {
                    double cur = axis == 0 ? c.X : axis == 1 ? c.Y : c.Z;
                    var d = new Vec3(axis == 0 ? value - cur : 0, axis == 1 ? value - cur : 0, axis == 2 ? value - cur : 0);
                    foreach (var kv in orig) mesh.Vertices[kv.Key] = inv.Apply(w.Apply(kv.Value) + d);
                }

                Num(host, "X", c.X, "mm", v => MoveTo(0, v), "Nokta taşındı", true, 0.5, 3).Scrubbed += _ => _vp.RefreshEditMesh();
                Num(host, "Y", c.Y, "mm", v => MoveTo(1, v), "Nokta taşındı", true, 0.5, 3).Scrubbed += _ => _vp.RefreshEditMesh();
                Num(host, "Z", c.Z, "mm", v => MoveTo(2, v), "Nokta taşındı", true, 0.5, 3).Scrubbed += _ => _vp.RefreshEditMesh();
            }

            Section("İşlemler");
            var wrap = new WrapPanel();
            wrap.Children.Add(SmallButton("Çıkar  E", () => _vp.StartModal(ModalKind.Extrude), "Seçili yüzleri normal yönünde çıkarır; nokta/kenar modunda seçili kenarlardan yeni yüz çeker"));
            wrap.Children.Add(SmallButton("İçe kaydır  I", () => _vp.StartModal(ModalKind.Inset)));
            wrap.Children.Add(SmallButton("Bıçak  K", () => _vp.StartModal(ModalKind.Knife), "Ekrana çizdiğiniz çizgi boyunca keser (seçim varsa yalnız seçili yüzleri). C ile kip: kes / sağ tarafı sil / sol tarafı sil; silinen taraf kapatılır"));
            wrap.Children.Add(SmallButton("Halka kes  Ctrl+R", () => _vp.StartModal(ModalKind.LoopCut), "Fareyle bir kenarın üzerine gelin: dörtgen halkası boyunca yeni kenar halkaları ekler; tekerlek kesim sayısını değiştirir"));
            wrap.Children.Add(SmallButton("Pah kır  Ctrl+B", () => _vp.StartModal(ModalKind.Bevel), "Seçili kenarların yerine düz pah yüzü koyar; fareyle genişliği ayarlayın veya mm yazın"));
            wrap.Children.Add(SmallButton("Kalınlık ver", () => _vp.StartModal(ModalKind.Solidify), "Yüzeye içe doğru et kalınlığı verir: açık yüzey kalın bir duvar, kapalı gövde içi boş kabuk olur"));
            wrap.Children.Add(SmallButton("Alt böl", _ed.Subdivide));
            wrap.Children.Add(SmallButton("Yumuşat", _ed.SmoothSubdivide, "Catmull–Clark: tüm ağ"));
            wrap.Children.Add(SmallButton("Birleştir  M", _ed.MergeAtCenter));
            wrap.Children.Add(SmallButton("Yüz yap  F", _ed.Fill));
            wrap.Children.Add(SmallButton("Normaller", _ed.RecalculateNormals, "Normalleri dışa çevir (Shift+N)"));
            wrap.Children.Add(SmallButton("Ayır  P", _ed.Separate));
            wrap.Children.Add(SmallButton("Sil  X", _ed.DeleteElements));
            Props.Children.Add(wrap);
            Section("Seçim");
            var selWrap = new WrapPanel();
            selWrap.Children.Add(SmallButton("Bağlıları seç  L", () => { _ed.SelectLinked(); _vp.RefreshEditMesh(); }, "Seçimle bağlantılı bütün parçayı seçer (dilimlenmiş ya da birleştirilmiş parçaları ayırmak için)"));
            selWrap.Children.Add(SmallButton("Büyüt  Ctrl+Num+", () => { _ed.GrowSelection(true); _vp.RefreshEditMesh(); }));
            selWrap.Children.Add(SmallButton("Küçült  Ctrl+Num−", () => { _ed.GrowSelection(false); _vp.RefreshEditMesh(); }));
            Props.Children.Add(selWrap);
            Caption("Alt+tık (sürüklemeden): kenar halkasını seçer, yüz modunda yüz halkasını · Shift+Alt+tık ekler");
            Caption("G/R/S seçimi taşır/döndürür/ölçekler · K bıçak · Ctrl+R halka kesim · Ctrl+B pah · Shift+tık seçime ekler · sürükleyerek kutu seçimi · X-ray (Alt+Z) arkadakileri de seçer");
        }

        // ================================================================ library

        private void BuildLibrary()
        {
            Library.Children.Clear();
            Library.Children.Add(new TextBlock { Text = "Temel şekiller", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("B.Text"), Margin = new Thickness(0, 2, 0, 8) });
            var grid = new UniformGrid { Columns = 3 };
            foreach (var (kind, icon) in new[]
                     {
                         (PrimitiveKind.Box, "M.Box"), (PrimitiveKind.Cylinder, "M.Cylinder"), (PrimitiveKind.Cone, "M.Cone"), (PrimitiveKind.Sphere, "M.Sphere"),
                         (PrimitiveKind.Torus, "M.Torus"), (PrimitiveKind.Tube, "M.Tube"), (PrimitiveKind.Wedge, "M.Wedge"), (PrimitiveKind.Plate, "M.Plate"),
                         (PrimitiveKind.Extrude, "M.Extrude"), (PrimitiveKind.Revolve, "M.Revolve")
                     })
            {
                var k = kind;
                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(new Icon { Data = Icon(icon), Size = 22, HorizontalAlignment = HorizontalAlignment.Center });
                content.Children.Add(new TextBlock { Text = PrimitiveSchema.Name(k), FontSize = 12, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var b = new Button { Content = content, Height = 62, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(2), ToolTip = PrimitiveSchema.Name(k) + " ekle" };
                b.Click += (_, _) => AddPrimitive(k);
                grid.Children.Add(b);
            }

            Library.Children.Add(grid);
            Library.Children.Add(new TextBlock { Text = "Robot parçaları", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("B.Text"), Margin = new Thickness(0, 14, 0, 4) });
            Library.Children.Add(new TextBlock
            {
                Text = "Parametrik: ekledikten sonra ölçülerini Özellikler sekmesinden değiştirin. Tab ile düzenlenebilir ağa dönüşür.",
                Style = (Style)FindResource("Text.Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            foreach (var t in RobotParts.All.Where(t => t.Id != "enclosure-lid"))
            {
                string id = t.Id;
                var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(ModelGeometry.ParseColor(t.Color, Colors.Gray)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                g.Children.Add(swatch);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = id == "enclosure" ? "Muhafaza kutusu + kapak" : t.Name, FontSize = 12.5, Foreground = Res("B.Text") });
                text.Children.Add(new TextBlock { Text = t.Description, FontSize = 12, Foreground = Res("B.TextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                text.Children.Add(new TextBlock { Text = $"{t.Material} · {t.Params.Count} parametre", FontSize = 12, Foreground = Res("B.TextDisabled"), Margin = new Thickness(0, 4, 0, 0) });
                Grid.SetColumn(text, 1);
                g.Children.Add(text);
                card.Child = g;
                card.MouseEnter += (_, _) => card.Background = Res("B.Surface");
                card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
                card.MouseLeftButtonUp += (_, _) => AddPart(id);
                Library.Children.Add(card);
            }
        }
    }
}
