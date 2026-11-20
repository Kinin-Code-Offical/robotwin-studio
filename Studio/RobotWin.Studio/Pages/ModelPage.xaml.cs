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
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Modeling;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// "Modelle" page: a Blender-style 3D modelling workspace for robot parts (chassis,
    /// brackets, arms, enclosures, wheels) that can be sent to the world as model objects.
    /// </summary>
    public partial class ModelPage : UserControl
    {
        private readonly MainWindow? _main;
        private readonly ModelEditor _ed = new ModelEditor();
        private readonly ModelViewport _vp;

        public ModelPage()
        {
            InitializeComponent();
            _vp = new ModelViewport(_ed);
            ViewHost.Children.Add(_vp);
            _vp.ContextMenuRequested += p => ShowContextMenu();
            _vp.ViewChanged += () => { };
            _ed.Changed += OnEditorChanged;
            _ed.StatusChanged += s => StatusText.Text = s;
            PreviewKeyDown += OnKeys;
            BuildLibrary();
            OnEditorChanged(ModelChange.All);
            StatusText.Text = "Shift+A ekle · G/R/S taşı-döndür-ölçekle (sonra X/Y/Z ve sayı) · Tab düzenleme modu · orta tuş döndür, Shift+orta kaydır, tekerlek yakınlaştır";
            Loaded += (_, _) =>
            {
                if (_ed.Doc.Objects.Count == 0 && !_loadedOnce)
                {
                    _loadedOnce = true;
                    _vp.FrameAll();
                }
            };
        }

        public ModelPage(MainWindow main) : this()
        {
            _main = main;
        }

        private bool _loadedOnce;

        /// <summary>The editor behind the page (document, selection, undo).</summary>
        public ModelEditor Editor => _ed;

        private Window? Owner => Window.GetWindow(this);

        // ================================================================ editor → UI

        private void OnEditorChanged(ModelChange change)
        {
            ModeObject.IsChecked = _ed.Mode == EditorMode.Object;
            ModeEdit.IsChecked = _ed.Mode == EditorMode.Edit;
            ElementBar.Visibility = _ed.Mode == EditorMode.Edit ? Visibility.Visible : Visibility.Collapsed;
            ElVertex.IsChecked = _ed.Element == ElementMode.Vertex;
            ElEdge.IsChecked = _ed.Element == ElementMode.Edge;
            ElFace.IsChecked = _ed.Element == ElementMode.Face;
            ShadeSolid.IsChecked = _ed.Shading == ShadingMode.Solid;
            ShadeWire.IsChecked = _ed.Shading == ShadingMode.Wireframe;
            ShadeXray.IsChecked = _ed.Shading == ShadingMode.XRay;
            WireToggle.IsChecked = _ed.WireOverlay;
            SnapToggle.IsChecked = _ed.SnapOn;
            UndoButton.IsEnabled = _ed.CanUndo;
            RedoButton.IsEnabled = _ed.CanRedo;
            if (!_scrubbing)
            {
                BuildOutliner();
                BuildProperties();
            }

            UpdateHint();
        }

        private void UpdateHint()
        {
            int tris = 0;
            foreach (var o in _ed.Doc.Objects.Where(o => o.Visible))
            {
                foreach (var f in o.LocalMesh().Faces) tris += f.Length - 2;
            }

            string sel = _ed.Mode == EditorMode.Edit && _ed.EditMesh != null
                ? $"Nokta {_ed.SelectedVertexSet().Count}/{_ed.EditMesh.Vertices.Count} · Yüz {_ed.SelectedFaceList().Count}/{_ed.EditMesh.Faces.Count}"
                : $"Nesne {_ed.Selected.Count}/{_ed.Doc.Objects.Count}";
            string file = _ed.FilePath != null ? Path.GetFileName(_ed.FilePath) : _ed.Doc.Title;
            HintText.Text = $"{sel}   ·   Üçgen {tris:N0}   ·   {file}{(_ed.IsDirty ? " •" : string.Empty)}";
        }

        // ================================================================ keyboard

        private void OnKeys(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (_vp.HandleModalKey(e))
            {
                e.Handled = true;
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift), alt = mods.HasFlag(ModifierKeys.Alt);
            bool edit = _ed.Mode == EditorMode.Edit;
            bool handled = true;
            switch (key)
            {
                case Key.Z when ctrl && shift:
                case Key.Y when ctrl:
                    _ed.Redo();
                    break;
                case Key.Z when ctrl:
                    _ed.Undo();
                    break;
                case Key.S when ctrl:
                    Save(shift);
                    break;
                case Key.O when ctrl:
                    OnOpen(this, new RoutedEventArgs());
                    break;
                case Key.N when ctrl && !shift:
                    OnNew(this, new RoutedEventArgs());
                    break;
                case Key.Z when alt:
                    SetShading(_ed.Shading == ShadingMode.XRay ? ShadingMode.Solid : ShadingMode.XRay);
                    break;
                case Key.Z when shift:
                    SetShading(_ed.Shading == ShadingMode.Wireframe ? ShadingMode.Solid : ShadingMode.Wireframe);
                    break;
                case Key.Z:
                    ShowShadingMenu();
                    break;
                case Key.Tab when !ctrl:
                    _ed.ToggleEditMode();
                    break;
                case Key.A when shift:
                    ShowAddMenu();
                    break;
                case Key.A when ctrl:
                    if (!edit) _ed.ApplyTransform();
                    break;
                case Key.A when alt:
                    _ed.DeselectAll();
                    break;
                case Key.A:
                    _ed.SelectAll();
                    break;
                case Key.I when ctrl:
                    _ed.InvertSelection();
                    break;
                case Key.G:
                    _vp.StartModal(ModalKind.Move);
                    break;
                case Key.R:
                    _vp.StartModal(ModalKind.Rotate);
                    break;
                case Key.S:
                    _vp.StartModal(ModalKind.Scale);
                    break;
                case Key.E when edit:
                    _vp.StartModal(ModalKind.Extrude);
                    break;
                case Key.I when edit:
                    _vp.StartModal(ModalKind.Inset);
                    break;
                case Key.D when shift && !edit:
                    if (_ed.DuplicateSelected()) _vp.StartModalAfterDuplicate();
                    break;
                case Key.J when ctrl:
                    _ed.Join();
                    break;
                case Key.P when ctrl:
                    _ed.ParentToActive();
                    break;
                case Key.P when alt:
                    _ed.ClearParent();
                    break;
                case Key.P when edit:
                    _ed.Separate();
                    break;
                case Key.X:
                case Key.Delete:
                    if (edit) _ed.DeleteElements();
                    else _ed.DeleteSelected();
                    break;
                case Key.H when alt:
                    _ed.UnhideAll();
                    break;
                case Key.H when shift:
                    _ed.HideSelected(true);
                    break;
                case Key.H:
                    _ed.HideSelected();
                    break;
                case Key.M when edit:
                    _ed.MergeAtCenter();
                    break;
                case Key.F when edit:
                    _ed.Fill();
                    break;
                case Key.N when shift && edit:
                    _ed.RecalculateNormals();
                    break;
                case Key.F2:
                    StartRename(_ed.ActiveId);
                    break;
                case Key.W:
                    SetTool(ModelTool.Select);
                    break;
                case Key.Escape:
                    if (_ed.Tool == ModelTool.Measure)
                    {
                        _vp.ClearMeasure();
                        SetTool(ModelTool.Select);
                    }
                    else handled = false;

                    break;
                case Key.Home:
                    _vp.FrameAll();
                    break;
                case Key.Decimal:
                case Key.OemPeriod when !shift:
                    _vp.FrameSelected();
                    break;
                case Key.NumPad1:
                    _vp.ViewFront(ctrl);
                    break;
                case Key.NumPad3:
                    _vp.ViewRight(ctrl);
                    break;
                case Key.NumPad7:
                    _vp.ViewTop(ctrl);
                    break;
                case Key.NumPad5:
                    _vp.ToggleOrtho();
                    break;
                case Key.D1 when edit:
                    _ed.SetElementMode(ElementMode.Vertex);
                    break;
                case Key.D2 when edit:
                    _ed.SetElementMode(ElementMode.Edge);
                    break;
                case Key.D3 when edit:
                    _ed.SetElementMode(ElementMode.Face);
                    break;
                case Key.D1:
                    _vp.ViewFront(ctrl);
                    break;
                case Key.D3:
                    _vp.ViewRight(ctrl);
                    break;
                case Key.D7:
                    _vp.ViewTop(ctrl);
                    break;
                case Key.D5:
                    _vp.ToggleOrtho();
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled) e.Handled = true;
        }

        // ================================================================ toolbar

        private void OnUndo(object sender, RoutedEventArgs e) => _ed.Undo();
        private void OnRedo(object sender, RoutedEventArgs e) => _ed.Redo();

        private void OnModeObject(object sender, RoutedEventArgs e)
        {
            if (_ed.Mode == EditorMode.Edit) _ed.ToggleEditMode();
        }

        private void OnModeEdit(object sender, RoutedEventArgs e)
        {
            if (_ed.Mode == EditorMode.Object) _ed.ToggleEditMode();
            if (_ed.Mode == EditorMode.Object) ModeObject.IsChecked = true;
        }

        private void OnElement(object sender, RoutedEventArgs e) =>
            _ed.SetElementMode((ElementMode)int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture));

        private void OnShading(object sender, RoutedEventArgs e) =>
            SetShading((ShadingMode)int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture));

        private void SetShading(ShadingMode mode)
        {
            _ed.Shading = mode;
            _ed.Notify(ModelChange.Geometry | ModelChange.View);
        }

        private void OnWireOverlay(object sender, RoutedEventArgs e)
        {
            _ed.WireOverlay = WireToggle.IsChecked == true;
            _ed.Notify(ModelChange.View);
        }

        private void OnSnapToggle(object sender, RoutedEventArgs e)
        {
            _ed.SnapOn = SnapToggle.IsChecked == true;
            _ed.Status(_ed.SnapOn ? $"Izgaraya yapışma açık ({_ed.Doc.GridMm:0.###} mm, Ctrl geçici kapatır)" : "Izgaraya yapışma kapalı (Ctrl basılıyken adım adım)");
        }

        private void OnGridSize(object sender, SelectionChangedEventArgs e)
        {
            if (GridBox.SelectedItem is ComboBoxItem it && double.TryParse((string)it.Tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double g))
            {
                _ed.Doc.GridMm = g;
            }
        }

        private void OnTool(object sender, RoutedEventArgs e)
        {
            var tool = (string)((FrameworkElement)sender).Tag switch
            {
                "Move" => ModelTool.Move,
                "Rotate" => ModelTool.Rotate,
                "Scale" => ModelTool.Scale,
                "Measure" => ModelTool.Measure,
                _ => ModelTool.Select
            };
            SetTool(tool);
        }

        private void SetTool(ModelTool tool)
        {
            _ed.Tool = tool;
            ToolSelect.IsChecked = tool == ModelTool.Select;
            ToolMove.IsChecked = tool == ModelTool.Move;
            ToolRotate.IsChecked = tool == ModelTool.Rotate;
            ToolScale.IsChecked = tool == ModelTool.Scale;
            ToolMeasure.IsChecked = tool == ModelTool.Measure;
            if (tool != ModelTool.Measure) _vp.ClearMeasure();
            else _ed.Status("Ölç: ilk noktaya, sonra ikinci noktaya tıklayın (köşelere yapışır) · Esc bitir");
            _vp.RefreshOverlays();
        }

        private void OnUnion(object sender, RoutedEventArgs e) => _ed.Boolean(BooleanOp.Union);
        private void OnDifference(object sender, RoutedEventArgs e) => _ed.Boolean(BooleanOp.Difference);
        private void OnIntersect(object sender, RoutedEventArgs e) => _ed.Boolean(BooleanOp.Intersection);

        private void OnAddMenu(object sender, RoutedEventArgs e) => ShowAddMenu(sender as UIElement);

        // ================================================================ menus

        private static MenuItem Item(string header, string? gesture, Action action, bool enabled = true)
        {
            var m = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty, IsEnabled = enabled };
            m.Click += (_, _) => action();
            return m;
        }

        private static TextBlock MenuTitle(string text) => new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("B.TextMuted"),
            Margin = new Thickness(10, 6, 10, 4),
            IsHitTestVisible = false
        };

        private void Open(ContextMenu menu, UIElement? target = null)
        {
            menu.PlacementTarget = target ?? _vp;
            menu.Placement = target != null ? PlacementMode.Bottom : PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        /// <summary>Where new objects appear: on the floor below the view centre, snapped to the grid.</summary>
        private Vec3 DropPoint()
        {
            double g = Math.Max(1, _ed.Doc.GridMm);
            var t = _vp.Cam.Target;
            return new Vec3(Math.Round(t.X / g) * g, Math.Round(t.Y / g) * g, 0);
        }

        private void ShowAddMenu(UIElement? target = null)
        {
            var menu = new ContextMenu();
            menu.Items.Add(MenuTitle("Ekle · temel şekiller"));
            foreach (var kind in new[] { PrimitiveKind.Box, PrimitiveKind.Cylinder, PrimitiveKind.Cone, PrimitiveKind.Sphere, PrimitiveKind.Torus, PrimitiveKind.Tube, PrimitiveKind.Wedge, PrimitiveKind.Plate, PrimitiveKind.Extrude, PrimitiveKind.Revolve })
            {
                var k = kind;
                menu.Items.Add(Item(PrimitiveSchema.Name(k), null, () => AddPrimitive(k)));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuTitle("Robot parçaları"));
            foreach (var t in RobotParts.All.Where(t => t.Id != "enclosure-lid"))
            {
                var id = t.Id;
                menu.Items.Add(Item(t.Id == "enclosure" ? "Muhafaza kutusu + kapak" : t.Name, null, () => AddPart(id)));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(Item("STL / OBJ / PLY içe aktar…", null, () => OnImport(this, new RoutedEventArgs())));
            Open(menu, target);
        }

        private void AddPrimitive(PrimitiveKind kind)
        {
            _ed.AddPrimitive(kind, DropPoint());
            SideTabs.SelectedIndex = 0;
        }

        private void AddPart(string id)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var added = _ed.AddPart(id, DropPoint());
            if (added.Count > 0) _ed.Status($"{added[0].Name} eklendi ({sw.ElapsedMilliseconds} ms) · ölçüleri Özellikler panelinden değiştirin");
            SideTabs.SelectedIndex = 0;
        }

        private void ShowShadingMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(MenuTitle("Görünüm"));
            menu.Items.Add(Item("Katı", "Z", () => SetShading(ShadingMode.Solid)));
            menu.Items.Add(Item("Tel kafes", "Shift+Z", () => SetShading(ShadingMode.Wireframe)));
            menu.Items.Add(Item("X-ray (saydam)", "Alt+Z", () => SetShading(ShadingMode.XRay)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(_ed.WireOverlay ? "Kenar katmanını gizle" : "Kenar katmanını göster", null, () =>
            {
                _ed.WireOverlay = !_ed.WireOverlay;
                _ed.Notify(ModelChange.View);
            }));
            menu.Items.Add(Item(_vp.Cam.Ortho ? "Perspektif" : "Ortografik", "Numpad 5", _vp.ToggleOrtho));
            Open(menu);
        }

        private void ShowContextMenu()
        {
            var menu = new ContextMenu();
            if (_ed.Mode == EditorMode.Edit)
            {
                menu.Items.Add(MenuTitle("Ağ düzenleme"));
                menu.Items.Add(Item("Çıkar (extrude)", "E", () => _vp.StartModal(ModalKind.Extrude)));
                menu.Items.Add(Item("İçe kaydır (inset)", "I", () => _vp.StartModal(ModalKind.Inset)));
                menu.Items.Add(Item("Alt böl", null, _ed.Subdivide));
                menu.Items.Add(Item("Yumuşat (Catmull–Clark)", null, _ed.SmoothSubdivide));
                menu.Items.Add(Item("Yüz oluştur", "F", _ed.Fill));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Merkezde birleştir", "M", _ed.MergeAtCenter));
                menu.Items.Add(Item("Mesafeye göre birleştir…", null, () =>
                {
                    var r = ParamDialog.Show(Owner, "Mesafeye göre birleştir", "Bu mesafeden yakın noktalar tek noktada birleşir.", ("Mesafe (mm)", 0.01));
                    if (r != null) _ed.MergeByDistance(Math.Max(0, r[0]));
                }));
                menu.Items.Add(Item("Üçgenlere böl", null, _ed.TriangulateFaces));
                menu.Items.Add(Item("Normalleri dışa çevir", "Shift+N", _ed.RecalculateNormals));
                menu.Items.Add(Item("Normalleri ters çevir", null, _ed.FlipNormals));
                menu.Items.Add(Item("Seçimi ayrı nesne yap", "P", _ed.Separate));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Sil", "X", _ed.DeleteElements));
                menu.Items.Add(Item("Nesne moduna dön", "Tab", _ed.ToggleEditMode));
                Open(menu);
                return;
            }

            bool any = _ed.Selected.Count > 0;
            bool two = _ed.Selected.Count >= 2;
            menu.Items.Add(MenuTitle(any ? (_ed.Active?.Name ?? "Nesne") : "Nesne"));
            menu.Items.Add(Item("Birleşim (∪)", null, () => _ed.Boolean(BooleanOp.Union), two));
            menu.Items.Add(Item("Fark (−) · etkin nesneden çıkar", null, () => _ed.Boolean(BooleanOp.Difference), two));
            menu.Items.Add(Item("Kesişim (∩)", null, () => _ed.Boolean(BooleanOp.Intersection), two));
            menu.Items.Add(Item("Tek ağda birleştir", "Ctrl+J", _ed.Join, two));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Çoğalt", "Shift+D", () =>
            {
                if (_ed.DuplicateSelected()) _vp.StartModalAfterDuplicate();
            }, any));
            menu.Items.Add(Item("Ayna kopyası · X (sağ/sol)", null, () => _ed.MirrorCopy(Axis.X), any));
            menu.Items.Add(Item("Ayna kopyası · Y (ön/arka)", null, () => _ed.MirrorCopy(Axis.Y), any));
            menu.Items.Add(Item("Ayna kopyası · Z (alt/üst)", null, () => _ed.MirrorCopy(Axis.Z), any));
            menu.Items.Add(Item("Yerinde aynala · X", null, () => _ed.MirrorInPlace(Axis.X), any));
            menu.Items.Add(Item("Doğrusal dizi…", null, LinearArrayDialog, any));
            menu.Items.Add(Item("Dairesel dizi…", null, CircularArrayDialog, any));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Düzenleme modu", "Tab", _ed.ToggleEditMode, _ed.Active != null));
            menu.Items.Add(Item("Ağa dönüştür", null, _ed.ConvertToMesh, _ed.SelectedObjects.Any(o => o.Primitive != null)));
            menu.Items.Add(Item("Döndürme ve ölçeği uygula", "Ctrl+A", _ed.ApplyTransform, any));
            menu.Items.Add(Item("Orijini geometriye taşı", null, _ed.OriginToGeometry, any));
            menu.Items.Add(Item("Yumuşak gölgelendir", null, () => _ed.SetSmooth(true), any));
            menu.Items.Add(Item("Düz gölgelendir", null, () => _ed.SetSmooth(false), any));
            menu.Items.Add(Item("Etkin nesneye bağla (ebeveyn)", "Ctrl+P", _ed.ParentToActive, two));
            menu.Items.Add(Item("Ebeveyn bağını kaldır", "Alt+P", _ed.ClearParent, _ed.SelectedObjects.Any(o => o.ParentId != null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Gizle", "H", () => _ed.HideSelected(), any));
            menu.Items.Add(Item("Hepsini göster", "Alt+H", _ed.UnhideAll, _ed.Doc.Objects.Any(o => !o.Visible)));
            menu.Items.Add(Item("Seçimi çerçevele", "Numpad .", _vp.FrameSelected, any));
            menu.Items.Add(Item("Dünyaya gönder", null, () => SendToWorld(), _ed.Doc.Objects.Count > 0));
            menu.Items.Add(Item("Sil", "X", _ed.DeleteSelected, any));
            Open(menu);
        }

        private void LinearArrayDialog()
        {
            var b = ModelOps.WorldBounds(_ed.Doc, _ed.SelectedRoots());
            var r = ParamDialog.Show(Owner, "Doğrusal dizi", "Seçimin kopyaları eşit aralıklarla dizilir (aralık mm).",
                ("Adet (toplam)", 4), ("Aralık X", Math.Round(b.Size.X + 5, 2)), ("Aralık Y", 0), ("Aralık Z", 0));
            if (r == null) return;
            _ed.ArrayLinear((int)Math.Round(r[0]), new Vec3(r[1], r[2], r[3]));
        }

        private void CircularArrayDialog()
        {
            var r = ParamDialog.Show(Owner, "Dairesel dizi", "Kopyalar Z ekseni etrafında, merkez noktası çevresinde dağıtılır (360° = tam daire).",
                ("Adet (toplam)", 6), ("Toplam açı (°)", 360), ("Merkez X (mm)", 0), ("Merkez Y (mm)", 0));
            if (r == null) return;
            _ed.ArrayCircular((int)Math.Round(r[0]), r[1], Axis.Z, new Vec3(r[2], r[3], 0));
        }

        // ================================================================ files

        /// <summary>Asked by the main window before the app closes: save, discard or cancel unsaved model work.</summary>
        public bool ConfirmCloseApp(Window owner)
        {
            if (!_ed.IsDirty || _ed.Doc.Objects.Count == 0) return true;
            int r = Dialog.Show(owner, "Kaydedilmemiş model", $"'{_ed.Doc.Title}' modelindeki değişiklikler kaydedilsin mi?", "Kaydet", "Kaydetme", "İptal");
            if (r == 0) return Save(false);
            return r == 1;
        }

        private bool ConfirmDiscard()
        {
            if (!_ed.IsDirty || _ed.Doc.Objects.Count == 0) return true;
            int r = Dialog.Show(Owner!, "Kaydedilmemiş model", $"'{_ed.Doc.Title}' modelindeki değişiklikler kaydedilsin mi?", "Kaydet", "Kaydetme", "İptal");
            if (r == 0) return Save(false);
            return r == 1;
        }

        private void OnNew(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscard()) return;
            _ed.Load(new ModelDocument(), null);
            _vp.FrameAll();
            _ed.Status("Yeni model · Shift+A ile şekil ekleyin");
        }

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "RobotWin modeli veya 3B ağ|*.rwmodel;*.stl;*.obj;*.ply|RobotWin modeli (*.rwmodel)|*.rwmodel|3B ağ (*.stl;*.obj;*.ply)|*.stl;*.obj;*.ply|Tüm dosyalar|*.*"
            };
            if (dlg.ShowDialog(Owner) != true) return;
            OpenFile(dlg.FileName);
        }

        /// <summary>Opens a .rwmodel (replacing the model) or imports a mesh file into it.</summary>
        public void OpenFile(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ModelDocument.Extension, StringComparison.OrdinalIgnoreCase))
            {
                ImportMesh(path);
                return;
            }

            if (!ConfirmDiscard()) return;
            try
            {
                var doc = ModelDocument.Load(path);
                _ed.Load(doc, path);
                _vp.FrameAll();
                _ed.Status($"Açıldı: {Path.GetFileName(path)} · {doc.Objects.Count} nesne");
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Owner!, "Model açılamadı", ex.Message, "Tamam");
            }
        }

        private void OnSave(object sender, RoutedEventArgs e) => Save(false);

        private bool Save(bool saveAs)
        {
            _ed.FlushEditMesh();
            string? path = _ed.FilePath;
            if (saveAs || path == null)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "RobotWin modeli (*.rwmodel)|*.rwmodel",
                    FileName = SafeName(_ed.Doc.Title) + ModelDocument.Extension
                };
                if (dlg.ShowDialog(Owner) != true) return false;
                path = dlg.FileName;
                _ed.Doc.Title = Path.GetFileNameWithoutExtension(path);
            }

            try
            {
                _ed.Doc.Preview = RenderPreview(320, 200);
                _ed.Doc.Save(path);
                _ed.MarkSaved(path);
                _ed.Status("Kaydedildi: " + Path.GetFileName(path));
                UpdateHint();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Owner!, "Kaydedilemedi", ex.Message, "Tamam");
                return false;
            }
        }

        private static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "model" : s;
        }

        private byte[]? RenderPreview(int w, int h)
        {
            if (_vp.ActualWidth < 10 || _vp.ActualHeight < 10) return null;
            var dpi = VisualTreeHelper.GetDpi(_vp);
            var bmp = new RenderTargetBitmap((int)(_vp.ActualWidth * dpi.DpiScaleX), (int)(_vp.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bmp.Render(_vp);
            double scale = Math.Min((double)w / bmp.PixelWidth, (double)h / bmp.PixelHeight);
            var scaled = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(scaled));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "3B modeller (*.stl;*.obj;*.ply)|*.stl;*.obj;*.ply|Tüm dosyalar|*.*", Multiselect = true };
            if (dlg.ShowDialog(Owner) != true) return;
            foreach (var f in dlg.FileNames) ImportMesh(f);
        }

        private void ImportMesh(string path)
        {
            try
            {
                var mesh = MeshModel.Load(path);
                if (mesh.TriangleCount == 0) throw new InvalidDataException("Dosyada üçgen yok.");
                var poly = ModelOps.FromImported(mesh);
                _ed.AddMeshObject(poly, Path.GetFileNameWithoutExtension(path));
                var b = poly.Bounds;
                _ed.Status($"{Path.GetFileName(path)}: {mesh.TriangleCount:N0} üçgen → {poly.Faces.Count:N0} yüz, {b.Size.X:0.#} × {b.Size.Y:0.#} × {b.Size.Z:0.#} mm (STL mm, OBJ/PLY metre kabul edilir)");
                _vp.FrameSelected();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Owner!, "Model okunamadı", ex.Message, "Tamam");
            }
        }

        private List<ModelObject> ExportSet()
        {
            _ed.FlushEditMesh();
            var sel = _ed.SelectedObjects.ToList();
            var set = sel.Count > 0 ? _ed.Doc.WithDescendants(sel) : _ed.Doc.Objects.Where(o => o.Visible).ToList();
            return set.Where(o => o.Visible || sel.Contains(o)).ToList();
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            var set = ExportSet();
            if (set.Count == 0)
            {
                _ed.Status("Dışa aktarılacak nesne yok");
                return;
            }

            string name = set.Count == 1 ? set[0].Name : _ed.Doc.Title;
            var dlg = new SaveFileDialog
            {
                Filter = "STL, milimetre (*.stl)|*.stl|OBJ, metre (*.obj)|*.obj|PLY, metre (*.ply)|*.ply",
                FileName = SafeName(name) + ".stl"
            };
            if (dlg.ShowDialog(Owner) != true) return;
            try
            {
                var mesh = ModelOps.ToMeshModel(_ed.Doc, set, name);
                mesh.Save(dlg.FileName);
                var open = set.Where(o => !_ed.Doc.WorldMesh(o).IsClosed).Select(o => o.Name).ToList();
                _ed.Status($"Dışa aktarıldı: {Path.GetFileName(dlg.FileName)} · {mesh.TriangleCount:N0} üçgen" + (open.Count > 0 ? $" · uyarı: kapalı olmayan ağ ({string.Join(", ", open)})" : string.Empty));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                Dialog.Show(Owner!, "Dışa aktarılamadı", ex.Message, "Tamam");
            }
        }

        // ================================================================ world

        private void OnSendToWorld(object sender, RoutedEventArgs e) => SendToWorld();

        /// <summary>
        /// Adds the selection (or the whole visible model) to the active world as one model
        /// object with its computed mass. Opens or creates a world when none is active.
        /// </summary>
        public WorldObject? SendToWorld()
        {
            var set = ExportSet();
            if (set.Count == 0)
            {
                _ed.Status("Dünyaya gönderilecek nesne yok · Shift+A ile şekil ekleyin");
                return null;
            }

            string name = set.Count == 1 ? set[0].Name : _ed.Doc.Title;
            var mesh = ModelOps.ToMeshModel(_ed.Doc, set, name);
            var b = mesh.Bounds;
            double cx = (b.Min.X + b.Max.X) / 2, cy = (b.Min.Y + b.Max.Y) / 2;
            mesh.Translate(new Vec3(-cx, -cy, -b.Min.Z));
            var stats = ModelMeasure.Total(_ed.Doc, set);
            var main = set.OrderByDescending(o => ModelMeasure.Of(_ed.Doc, o).VolumeMm3).First();

            var w = Workspace.Current;
            if (w.ActiveWorld == null)
            {
                var existing = w.Documents.FirstOrDefault(d => d.Kind == DocumentKind.World && d.World != null);
                if (existing != null) w.Activate(existing);
                else w.NewWorld();
            }

            var world = w.ActiveWorld;
            if (world == null) return null;
            w.BeginWorldChange();
            var wo = world.AddModel(mesh, name, new WorldPose { X = cx, Y = cy, Z = Math.Max(0, b.Min.Z) }, main.Material);
            wo.MassKg = Math.Round(stats.MassG / 1000, 6);
            wo.Infill = main.Infill;
            wo.Color = main.Color;
            w.CommitWorldChange($"'{name}' dünyaya eklendi ({stats.MassG:0.#} g)");
            _ed.Status($"'{wo.Name}' dünyaya eklendi · {stats.Size.X:0.#}×{stats.Size.Y:0.#}×{stats.Size.Z:0.#} mm · {stats.MassG:0.#} g ({main.Material}) · Dünya sayfasında görün");
            return wo;
        }
    }
}
