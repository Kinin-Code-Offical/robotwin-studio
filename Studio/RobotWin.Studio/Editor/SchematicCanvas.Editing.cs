using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Editor
{
    public enum GridStyle
    {
        Dots,
        Lines,
        None
    }

    /// <summary>Editor commands that need page-level UI (pickers, dialogs, panels, files).</summary>
    public enum SchematicCommand
    {
        AddPart,
        AddPower,
        EditProperties,
        EditValue,
        ChooseFootprint,
        Datasheet,
        ShowInPcb,
        Find,
        Replace,
        Help,
        Erc,
        Annotate,
        ExportBom,
        ExportNetlist,
        ExportSpice,
        ExportImage,
        Print,

        /// <summary>[notes] A new text where the text tool was clicked (target: the grid point), or an existing one edited (target: the note).</summary>
        EditNote,

        /// <summary>[fields] KiCad's Symbol Fields Table.</summary>
        FieldsTable,

        /// <summary>[table] A table's cells and sizes (target: the table).</summary>
        EditTable,

        /// <summary>[rule-area] A rule area's net class (target: the area).</summary>
        EditRuleArea,

        /// <summary>[sheets] A sheet symbol's name (target: the sheet).</summary>
        EditSheet,

        /// <summary>[sheets] A sheet pin's name and shape (target: (sheet, pin)).</summary>
        EditSheetPin
    }

    public sealed class SchematicCommandEventArgs : EventArgs
    {
        public SchematicCommandEventArgs(SchematicCommand command, object? target)
        {
            Command = command;
            Target = target;
        }

        public SchematicCommand Command { get; }

        /// <summary>The part or label the command is for (null = the selection / the sheet).</summary>
        public object? Target { get; }
    }

    /// <summary>Selection model, editing operations, clipboard and editor drawing of the schematic canvas.</summary>
    public sealed partial class SchematicCanvas
    {
        public const string ClipboardFormat = "RobotWin.SchematicFragment";

        private static readonly Color HighlightColor = Color.FromRgb(0xFF, 0xC8, 0x57);
        private static readonly Color CrossingColor = Color.FromRgb(0x5E, 0xD3, 0x9A);
        private static readonly Color ErrorColor = Color.FromRgb(0xE5, 0x48, 0x4D);
        private static readonly Color WarningColor = Color.FromRgb(0xF0, 0xA0, 0x30);

        private GridStyle _gridStyle = GridStyle.Dots;
        private bool _crosshair;
        private WireMode _wireMode = WireMode.Orthogonal;
        private bool _alternatePosture;
        private string? _netHighlight;
        private GridPoint? _relOrigin;

        // Mouse drag of the selection (rubber-band) or of one wire segment.
        private SchematicDrag? _moveOp;
        private SegmentDrag? _segmentOp;
        private SchematicWire? _pressedWire;
        private int _pressedSegment = -1;
        private bool _boxToggle, _boxAdd;

        // Keyboard move (M) / drag (G): the selection follows the cursor until a click.
        private bool _keyMove;
        private bool _moved;

        // Paste / duplicate: a fragment that follows the cursor until a click.
        private SchematicFragment? _floating;
        private string _floatingVerb = "Yapıştırıldı";

        private bool _exporting;
        private bool _clearBackground;
        private bool _spaceDown, _spacePanned;
        private readonly Dictionary<SchematicPart, (Rect Ref, Rect? Value)> _labelRects = new Dictionary<SchematicPart, (Rect, Rect?)>();
        private List<(SchematicWire Wire, GridPoint End)>? _dangling;
        private Netlist? _danglingFor;
        private IReadOnlyList<ErcViolation>? _ercMarkers;

        public event EventHandler<SchematicCommandEventArgs>? CommandRequested;
        public event EventHandler<GridPoint>? MouseGridChanged;

        public GridStyle GridStyle
        {
            get => _gridStyle;
            set
            {
                _gridStyle = value;
                InvalidateVisual();
            }
        }

        /// <summary>Full-window crosshair cursor (KiCad Ctrl+Shift+X).</summary>
        public bool Crosshair
        {
            get => _crosshair;
            set
            {
                _crosshair = value;
                InvalidateVisual();
            }
        }

        public WireMode WireMode
        {
            get => _wireMode;
            set
            {
                _wireMode = value;
                InvalidateVisual();
            }
        }

        /// <summary>The net drawn highlighted (all its wires, pins and labels glow). Also cross-probed.</summary>
        public string? HighlightedNet
        {
            get => _netHighlight;
            set
            {
                _netHighlight = string.IsNullOrWhiteSpace(value) ? null : value;
                CrossProbe.HighlightNet(this, _netHighlight);
                InvalidateVisual();
            }
        }

        /// <summary>ERC violations drawn as markers on the sheet (null = none).</summary>
        public IReadOnlyList<ErcViolation>? ErcMarkers
        {
            get => _ercMarkers;
            set
            {
                _ercMarkers = value;
                InvalidateVisual();
            }
        }

        public GridPoint MouseGrid => _mouseGrid;

        public bool IsFloating => _floating != null;

        /// <summary>A one-line description of the current mode for the HUD.</summary>
        private string ModeText()
        {
            if (_floating != null) return $"{_floating.Count} öğe imleci izliyor · tık: bırak · R/X/Y: döndür/aynala · Esc: iptal";
            if (_keyMove) return "Taşı · tık: bırak · Esc: iptal";
            if (_wirePoints != null) return "Kablo · " + WireModeText + " · tık: köşe · çift tık / Esc: bitir · Backspace: son köşeyi sil";
            return _tool switch
            {
                EditorTool.Wire => "Kablo · " + WireModeText + " · / köşe yönü · Shift+Boşluk mod",
                EditorTool.Label => "Etiket · tıklayın",
                EditorTool.Text => "Metin · yazının sol üst köşesine tıklayın",
                EditorTool.GlobalLabel => "Global etiket · tıklayın",
                EditorTool.NoConnect => "NC işareti · pin uçlarına tıklayın",
                EditorTool.Junction => "Bağlantı noktası · tellerin kesiştiği yere tıklayın: birleşirler · elle konmuş noktaya tıklayınca ayrılırlar",
                EditorTool.Erase => "Silici · tıklanan öğe silinir (tel, parça, etiket, not, işaret) · Esc seçime döner",   // [erase]
                EditorTool.Highlight => "Hattı vurgula · pine, tele ya da etikete tıklayın · aynı hatta ya da boşa tıklayınca kalkar",
                EditorTool.Line => "Çizgi · köşelere tıklayın · çift tık, Enter ya da Esc: bitir",
                EditorTool.Rectangle => "Dikdörtgen · iki köşeye tıklayın",
                EditorTool.Circle => "Daire · merkeze, sonra çemberin bir noktasına tıklayın",
                EditorTool.Arc => "Yay · başlangıca, yayın bir noktasına, sonra bitişe tıklayın",
                EditorTool.Bezier => "Bezier eğrisi · başlangıca, bitişe, sonra iki denetim noktasına tıklayın",
                EditorTool.Image => "Resim · koymak istediğiniz yere tıklayın · Esc vazgeçer",
                EditorTool.Table => "Tablo · sol üst köşesine tıklayın (3 × 3); sonra hücreler yazılır",
                EditorTool.RuleArea => "Kural alanı · iki köşeye tıklayın; içindeki hatlar alanın net sınıfını alır",
                EditorTool.Place => "Yerleştir · R döndür · X/Y aynala · Esc bitir",
                _ => string.Empty
            };
        }

        private string WireModeText => _wireMode switch
        {
            WireMode.Diagonal45 => "45°",
            WireMode.Free => "serbest",
            _ => _alternatePosture ? "90° (önce dikey)" : "90° (önce yatay)"
        };

        public void RequestCommand(SchematicCommand command, object? target = null) =>
            CommandRequested?.Invoke(this, new SchematicCommandEventArgs(command, target));

        private void ResetEditorState()
        {
            _floating = null;
            _keyMove = false;
            _moveOp = null;
            _segmentOp = null;
            _netHighlight = null;
            _ercMarkers = null;
            _relOrigin = null;
            _labelRects.Clear();
        }

        // ================================================================ selection

        private void NotifySelection()
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void ClearSelection()
        {
            if (_selection.Count == 0) return;
            _selection.Clear();
            NotifySelection();
        }

        public void SelectItems(IEnumerable<object> items, bool add = false)
        {
            if (!add) _selection.Clear();
            foreach (var i in items) _selection.Add(i);
            NotifySelection();
        }

        public void SelectAll() => SelectItems(SchematicEditing.AllItems(Doc, CurrentSheet));   // [sheets] the shown sheet's

        /// <summary>The selection, or the item under the cursor when nothing is selected (KiCad hotkey behaviour).</summary>
        private List<object> Targets()
        {
            if (_selection.Count > 0) return _selection.ToList();
            var hit = HitObject(_mouseGrid);
            if (hit == null && HitPin(_mouseGrid) is { } pin) hit = pin.Part;
            if (hit == null) return new List<object>();
            _selection.Add(hit);
            NotifySelection();
            return new List<object> { hit };
        }

        private bool GuardEditing()
        {
            if (!Simulating) return true;
            W.Status = "Düzenlemek için önce simülasyonu durdurun (Shift+F5).";
            return false;
        }

        /// <summary>Selects every wire and label of a net (KiCad "select connection").</summary>
        public void SelectConnection(string net)
        {
            var nl = Netlist;
            var items = new List<object>();
            for (int i = 0; i < Doc.Wires.Count && i < nl.WireNets.Count; i++) if (Here(Doc.Wires[i]) && string.Equals(nl.WireNets[i], net, StringComparison.OrdinalIgnoreCase)) items.Add(Doc.Wires[i]);
            for (int i = 0; i < Doc.Labels.Count && i < nl.LabelNets.Count; i++) if (Here(Doc.Labels[i]) && string.Equals(nl.LabelNets[i], net, StringComparison.OrdinalIgnoreCase)) items.Add(Doc.Labels[i]);
            SelectItems(items);
            W.Status = $"{net}: {items.Count} kablo/etiket seçildi";
        }

        /// <summary>Net under the cursor (pin, wire or label), or null.</summary>
        public string? NetUnderCursor() => NetAt(_mouseGrid).Net;

        public void ToggleHighlightUnderCursor()
        {
            var net = NetUnderCursor();
            HighlightedNet = net == null || string.Equals(net, _netHighlight, StringComparison.OrdinalIgnoreCase) ? null : net;
            W.Status = _netHighlight != null ? $"Hat vurgulandı: {_netHighlight} (` ile kaldır)" : "Hat vurgusu kaldırıldı";
        }

        /// <summary>Selects the next/previous part (by designator) and brings it into view (Tab / Shift+Tab).</summary>
        public void CyclePart(int dir)
        {
            var parts = Doc.Parts.Where(p => !Annotator.IsPowerSymbol(p.RefDes)).OrderBy(p => p.RefDes, Annotator.NaturalComparer).ToList();
            if (parts.Count == 0) return;
            var current = _selection.OfType<SchematicPart>().FirstOrDefault();
            int i = current == null ? (dir > 0 ? 0 : parts.Count - 1) : (parts.IndexOf(current) + dir + parts.Count) % parts.Count;
            SelectItems(new object[] { parts[i] });
            EnsureVisible(parts[i].Position);
            W.Status = parts[i].RefDes + " · " + (Lib.Find(parts[i].PartId)?.Name ?? string.Empty);
        }

        // ================================================================ view

        public Point GridToScreen(GridPoint g) => ToScreen(g);

        /// <summary>Centres the view on a point, zooming in to at least <paramref name="minScale"/>.</summary>
        public void ZoomToPoint(GridPoint p, double minScale = 18)
        {
            if (ActualWidth < 10) return;
            _scale = Math.Max(_scale, Math.Min(MaxScale, minScale));
            _offset = new Vector(ActualWidth / 2 - p.X * _scale, ActualHeight / 2 - p.Y * _scale);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void EnsureVisible(GridPoint p)
        {
            var s = ToScreen(p);
            if (s.X < 40 || s.Y < 40 || s.X > ActualWidth - 40 || s.Y > ActualHeight - 40) ZoomToPoint(p, _scale);
        }

        public void ZoomBy(double factor, Point? about = null)
        {
            var p = about ?? new Point(ActualWidth / 2, ActualHeight / 2);
            var g = ToGrid(p);
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
            _offset = new Vector(p.X - g.X * _scale, p.Y - g.Y * _scale);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        /// <summary>Screen rectangle of a part's value text (null if it has none or is not drawn).</summary>
        public Rect? ValueTextRect(SchematicPart part) => _labelRects.TryGetValue(part, out var r) ? r.Value : null;

        public Rect ScreenBounds(SchematicPart part)
        {
            var b = PartBounds(part);
            return new Rect(ToScreen(b.Left, b.Top), ToScreen(b.Right, b.Bottom));
        }

        public Rect LabelScreenRect(NetLabel label) => LabelRect(label);

        private void RememberLabelRects(SchematicPart part, Rect refRect, Rect? valueRect) => _labelRects[part] = (refRect, valueRect);

        private SchematicPart? ValueTextAt(Point screen)
        {
            foreach (var kv in _labelRects)
            {
                if (!Doc.Parts.Contains(kv.Key) || kv.Value.Value == null) continue;
                var r = kv.Value.Value.Value;
                r.Inflate(3, 2);
                if (r.Contains(screen)) return kv.Key;
            }

            return null;
        }

        // ================================================================ editing operations

        public void DeleteSelection()
        {
            if (!GuardEditing()) return;
            var items = Targets();
            if (items.Count == 0) return;
            W.BeginChange();
            int n = SchematicEditing.Delete(Doc, items);
            _selection.Clear();
            W.CommitChange($"{n} öğe silindi");
            NotifySelection();
        }

        public void RotateSelection() => RotateSelection(false);

        public void RotateSelection(bool clockwise)
        {
            if (_floating != null)
            {
                _floating.Rotate(clockwise);
                InvalidateVisual();
                return;
            }

            if (_tool == EditorTool.Place && _ghost != null)
            {
                _ghost.Rotation = ((_ghost.Rotation + (clockwise ? 270 : 90)) % 360 + 360) % 360;
                InvalidateVisual();
                return;
            }

            if (_keyMove || _moveOp != null || !GuardEditing()) return;
            var items = Targets();
            if (items.Count == 0) return;
            W.BeginChange();
            SchematicEditing.Rotate(items, SchematicEditing.PivotOf(items, Lib), clockwise);
            W.CommitChange(items.Count == 1 ? "Döndürüldü" : $"{items.Count} öğe döndürüldü");
        }

        public void MirrorSelection() => MirrorSelection(false);

        /// <summary>Mirror left↔right (X) or top↔bottom (Y).</summary>
        public void MirrorSelection(bool vertical)
        {
            if (_floating != null)
            {
                _floating.Mirror(vertical);
                InvalidateVisual();
                return;
            }

            if (_tool == EditorTool.Place && _ghost != null)
            {
                SchematicEditing.Mirror(new object[] { _ghost }, _ghost.Position, vertical);
                InvalidateVisual();
                return;
            }

            if (_keyMove || _moveOp != null || !GuardEditing()) return;
            var items = Targets();
            if (items.Count == 0) return;
            W.BeginChange();
            SchematicEditing.Mirror(items, SchematicEditing.PivotOf(items, Lib), vertical);
            W.CommitChange(vertical ? "Dikey aynalandı" : "Yatay aynalandı");
        }

        /// <summary>Arrow keys: moves the selection by whole grid steps, wires stretch along.</summary>
        public void Nudge(double dx, double dy)
        {
            if (!GuardEditing() || _selection.Count == 0) return;
            W.BeginChange();
            new SchematicDrag(Doc, Lib, _selection, rubberBand: true).Apply(new GridPoint(dx, dy));
            W.CommitChange($"Kaydırıldı ({dx:0}, {dy:0})");
        }

        /// <summary>M (rubberBand false) / G (true): the selection follows the cursor until clicked.</summary>
        public void BeginKeyMove(bool rubberBand)
        {
            if (!GuardEditing() || _keyMove || _floating != null) return;
            var items = Targets();
            if (items.Count == 0) return;
            W.BeginChange();
            _moveOp = new SchematicDrag(Doc, Lib, items, rubberBand);
            _dragStartGrid = Snap(_mouseGrid);
            _keyMove = true;
            _moved = false;
            Focus();
            W.Status = rubberBand ? "Sürükle: bağlı kablolar uzar · tık bırak · Esc iptal" : "Taşı: kablolar yerinde kalır · tık bırak · Esc iptal";
            InvalidateVisual();
        }

        private void UpdateKeyMove()
        {
            if (_moveOp == null) return;
            var d = Snap(_mouseGrid) - _dragStartGrid;
            if (d.X != 0 || d.Y != 0) _moved = true;
            _moveOp.Apply(d);
            _netlist = null;
            InvalidateVisual();
        }

        private void CommitKeyMove()
        {
            _keyMove = false;
            var op = _moveOp;
            _moveOp = null;
            if (op == null) return;
            if (_moved) W.CommitChange("Taşındı");
            else W.CancelChange();
            InvalidateVisual();
        }

        private void CancelKeyMove()
        {
            _keyMove = false;
            _moveOp?.Cancel();
            _moveOp = null;
            W.CancelChange();
            Refresh();
        }

        // ------------------------------------------------------------- mouse drag

        private void UpdateMouseDrag()
        {
            var d = Snap(_mouseGrid) - _dragStartGrid;
            if (_moveOp == null && _segmentOp == null)
            {
                if ((_mouse - _dragStart).Length < 4 || (d.X == 0 && d.Y == 0)) return;
                if (!GuardEditing()) return;
                W.BeginChange();
                if (_pressedWire != null && _pressedSegment >= 0 && _selection.Count == 1 && _selection.Contains(_pressedWire) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    _segmentOp = new SegmentDrag(Doc, Lib, _pressedWire, _pressedSegment);
                }
                else
                {
                    _moveOp = new SchematicDrag(Doc, Lib, _selection, rubberBand: !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
                }
            }

            _moved = true;
            if (_segmentOp != null) _segmentOp.Apply(d);
            else _moveOp?.Apply(d);
            _netlist = null;
            InvalidateVisual();
        }

        private void EndMouseDrag()
        {
            bool started = _moveOp != null || _segmentOp != null;
            _moveOp = null;
            _segmentOp = null;
            _pressedWire = null;
            _pressedSegment = -1;
            if (started) W.CommitChange("Taşındı");
        }

        private void ApplyBoxSelection()
        {
            var a = ToGrid(_dragStart);
            bool crossing = _mouse.X < _dragStart.X;
            var rect = GridRect.FromCorners(a, _mouseGrid);
            var items = SchematicEditing.ItemsInRect(Doc, Lib, rect, crossing, CurrentSheet);
            foreach (var item in items)
            {
                if (_boxToggle)
                {
                    if (!_selection.Remove(item)) _selection.Add(item);
                }
                else
                {
                    _selection.Add(item);
                }
            }

            W.Status = $"{_selection.Count} öğe seçili" + (crossing ? " (kesişen seçim)" : string.Empty);
            NotifySelection();
        }

        // ------------------------------------------------------------- clipboard

        public SchematicFragment? SelectionFragment() =>
            _selection.Count == 0 ? null : SchematicFragment.FromItems(_selection, Lib, Doc.Title);

        public void CopySelection()
        {
            var f = SelectionFragment();
            if (f == null) return;
            string json = f.ToJson();
            try
            {
                var data = new DataObject();
                data.SetData(ClipboardFormat, json);
                data.SetText(json);
                Clipboard.SetDataObject(data, true);
                W.Status = $"{f.Count} öğe panoya kopyalandı (başka sekmeye veya pencereye yapıştırılabilir)";
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is System.Threading.ThreadStateException)
            {
                W.Status = "Pano kullanılamıyor: " + ex.Message;
            }
        }

        public void CutSelection()
        {
            if (!GuardEditing() || _selection.Count == 0) return;
            CopySelection();
            W.BeginChange();
            int n = SchematicEditing.Delete(Doc, _selection);
            _selection.Clear();
            W.CommitChange($"{n} öğe kesildi");
            NotifySelection();
        }

        public static SchematicFragment? ReadClipboard()
        {
            try
            {
                if (Clipboard.ContainsData(ClipboardFormat) && Clipboard.GetData(ClipboardFormat) is string json) return SchematicFragment.FromJson(json);
                if (Clipboard.ContainsText()) return SchematicFragment.FromJson(Clipboard.GetText());
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is System.Threading.ThreadStateException)
            {
            }

            return null;
        }

        public void Paste()
        {
            if (!GuardEditing()) return;
            var f = ReadClipboard();
            if (f == null || f.IsEmpty)
            {
                W.Status = "Panoda RobotWin devre parçası yok";
                return;
            }

            BeginFloating(f, "Yapıştırıldı");
        }

        /// <summary>Ctrl+D / C: copies of the selection follow the cursor (KiCad duplicate).</summary>
        public void DuplicateSelection()
        {
            if (!GuardEditing()) return;
            if (_selection.Count == 0) Targets();
            var f = SelectionFragment();
            if (f == null) return;
            BeginFloating(f, "Çoğaltıldı");
        }

        /// <summary>Lets a fragment follow the cursor; a click adds it as one undo step.</summary>
        public void BeginFloating(SchematicFragment fragment, string verb)
        {
            if (_tool != EditorTool.Select) Tool = EditorTool.Select;
            if (_keyMove) CommitKeyMove();
            _floating = fragment;
            _floatingVerb = verb;
            var at = IsMouseOver ? Snap(_mouseGrid) : Snap(ToGrid(new Point(ActualWidth / 2, ActualHeight / 2)));
            if (verb == "Çoğaltıldı" && !IsMouseOver) at = fragment.Anchor + new GridPoint(4, 4);
            _floating.MoveAnchorTo(at);
            Focus();
            W.Status = $"{fragment.Count} öğe: yerleştirmek için tıklayın · R döndür · X/Y aynala · Esc iptal";
            InvalidateVisual();
        }

        private void CommitFloating()
        {
            var f = _floating;
            _floating = null;
            if (f == null) return;
            W.BeginChange();
            var result = f.PasteInto(Doc, Lib, f.Anchor);
            foreach (var item in result.Items) Schematic.SetSheet(item, CurrentSheet);   // [sheets] onto the shown sheet
            string extra = result.Renamed.Count > 0 ? $" · {result.Renamed.Count} parça yeniden numaralandı" : string.Empty;
            if (result.SkippedParts.Count > 0) extra += $" · {result.SkippedParts.Count} bilinmeyen parça atlandı";
            W.CommitChange($"{_floatingVerb}: {result.Items.Count} öğe" + extra);
            SelectItems(result.Items);
        }

        public void CancelFloating()
        {
            if (_floating == null) return;
            _floating = null;
            W.Status = "İptal edildi";
            InvalidateVisual();
        }

        // ------------------------------------------------------------- wires, labels, flags

        /// <summary>Snap target while wiring: pin tip, wire vertex, a point on a wire, a label, else the grid.</summary>
        private GridPoint WireSnap(GridPoint g)
        {
            var pin = HitPin(g);
            if (pin != null) return pin.Value.Part.Transform(pin.Value.Pin.X, pin.Value.Pin.Y);
            double tol = Math.Max(0.4, 7 / _scale);
            if (HitSheetPin(g) is { } sp) return sp.Pin.Position;   // [sheets]
            foreach (var l in SheetLabels) if (Math.Abs(l.Position.X - g.X) <= tol && Math.Abs(l.Position.Y - g.Y) <= tol) return l.Position;
            foreach (var w in SheetWires)
            {
                foreach (var p in w.Points) if (Math.Abs(p.X - g.X) <= tol && Math.Abs(p.Y - g.Y) <= tol) return p;
            }

            foreach (var w in SheetWires)
            {
                for (int i = 0; i + 1 < w.Points.Count; i++)
                {
                    var a = w.Points[i];
                    var b = w.Points[i + 1];
                    if (SchematicEditing.DistanceToSegment(g, a, b, out var q) > tol) continue;
                    if (Math.Abs(a.X - b.X) < 1e-9) return new GridPoint(a.X, Math.Round(q.Y));
                    if (Math.Abs(a.Y - b.Y) < 1e-9) return new GridPoint(Math.Round(q.X), a.Y);
                }
            }

            return Snap(g);
        }

        private bool IsConnectionPoint(GridPoint p) => SchematicEditing.IsConnectionPoint(Doc, Lib, p, null, CurrentSheet);

        private List<GridPoint> WirePreview()
        {
            var pts = new List<GridPoint>(_wirePoints!);
            pts.AddRange(SchematicEditing.Route(_wirePoints![^1], WireSnap(_mouseGrid), _wireMode, _alternatePosture));
            return pts;
        }

        private void WireClick(bool doubleClick)
        {
            var p = WireSnap(_mouseGrid);
            if (_wirePoints == null)
            {
                _wirePoints = new List<GridPoint> { p };
                InvalidateVisual();
                return;
            }

            foreach (var q in SchematicEditing.Route(_wirePoints[^1], p, _wireMode, _alternatePosture))
            {
                if (!q.Equals(_wirePoints[^1])) _wirePoints.Add(q);
            }

            bool endsOnSomething = !p.Equals(_wirePoints[0]) && IsConnectionPoint(p);
            if (doubleClick || endsOnSomething) FinishWire();
            else InvalidateVisual();
        }

        private void FinishWire()
        {
            var pts = _wirePoints;
            _wirePoints = null;
            if (pts != null)
            {
                var clean = SchematicEditing.CleanPolyline(pts);
                if (clean.Count >= 2)
                {
                    W.BeginChange();
                    Doc.Wires.Add(new SchematicWire { Points = clean, Sheet = CurrentSheet });
                    W.CommitChange("Kablo eklendi");
                }
            }

            InvalidateVisual();
        }

        public void BeginWireAtCursor()
        {
            if (!GuardEditing()) return;
            Tool = EditorTool.Wire;
            if (IsMouseOver) WireClick(false);
        }

        private void PlaceGhost()
        {
            if (_ghost == null || _placePreset == null || Simulating) return;
            W.BeginChange();
            var part = Doc.AddPart(Lib, _placePreset, Snap(_mouseGrid), _ghost.Rotation);
            part.Mirror = _ghost.Mirror;
            part.Sheet = CurrentSheet;   // [sheets]
            W.CommitChange($"{part.RefDes} yerleştirildi");
            SelectItems(new object[] { part });
        }

        /// <summary>Adds a net label (or global label) at a point and opens its name for editing.</summary>
        public NetLabel? AddLabelAt(GridPoint at, bool global)
        {
            if (!GuardEditing()) return null;
            W.BeginChange();
            int n = 1;
            while (Doc.Labels.Any(l => l.Name == "NET" + n.ToString(CultureInfo.InvariantCulture))) n++;
            var label = new NetLabel { Name = "NET" + n.ToString(CultureInfo.InvariantCulture), Position = at, Global = global, Sheet = CurrentSheet };
            Doc.Labels.Add(label);
            W.CommitChange(global ? "Global etiket eklendi" : "Hat etiketi eklendi");
            SelectItems(new object[] { label });
            RequestCommand(SchematicCommand.EditValue, label);
            return label;
        }

        /// <summary>[directive] A directive label (KiCad's netclass flag) at a point, its class opened for typing.</summary>
        public NetLabel? AddDirectiveAt(GridPoint at)
        {
            if (!GuardEditing()) return null;
            W.BeginChange();
            var label = new NetLabel { Name = string.Empty, NetClass = "Power", Position = at, Sheet = CurrentSheet };
            Doc.Labels.Add(label);
            W.CommitChange(Shell.Lang.T("Net sınıfı yönergesi eklendi"));
            SelectItems(new object[] { label });
            RequestCommand(SchematicCommand.EditValue, label);
            return label;
        }

        private void PlaceLabel()
        {
            AddLabelAt(WireSnap(_mouseGrid), _tool == EditorTool.GlobalLabel);
            Tool = EditorTool.Select;
        }

        /// <summary>Q: puts (or removes) a no-connect flag on the pin under the cursor.</summary>
        public void ToggleNoConnectAt(GridPoint g)
        {
            if (!GuardEditing()) return;
            var pin = HitPin(g);
            var at = pin != null ? pin.Value.Part.Transform(pin.Value.Pin.X, pin.Value.Pin.Y) : Snap(g);
            W.BeginChange();
            var existing = SheetNoConnects.FirstOrDefault(n => n.Position.Equals(at));
            if (existing != null)
            {
                Doc.NoConnects.Remove(existing);
                W.CommitChange("NC işareti kaldırıldı");
            }
            else
            {
                Doc.NoConnects.Add(new NoConnectFlag { Position = at, Sheet = CurrentSheet });
                W.CommitChange(pin != null ? $"{pin.Value.Part.RefDes}.{pin.Value.Pin.Name}: bağlantı yok (NC)" : "NC işareti eklendi");
            }
        }

        /// <summary>[junction] KiCad's Add Junction at a grid point; on a junction placed by hand, it comes away.</summary>
        public void ToggleJunctionAt(GridPoint g)
        {
            if (!GuardEditing()) return;
            var at = Snap(g);
            if (SchematicEditing.HasRemovableJunction(Doc, at, CurrentSheet))
            {
                W.BeginChange();
                SchematicEditing.RemoveJunction(Doc, at, CurrentSheet);
                W.CommitChange("Bağlantı noktası kaldırıldı: teller yalnız kesişiyor");
                return;
            }

            int through = SchematicEditing.WiresThrough(Doc, at, CurrentSheet).Count;
            if (through < 2)
            {
                W.Status = through == 0 ? "Burada tel yok" : "Burada kesişen ikinci bir tel yok";
                return;
            }

            W.BeginChange();
            SchematicEditing.PlaceJunction(Doc, at, CurrentSheet);
            W.CommitChange($"Bağlantı noktası: {through} tel birleşti");
        }

        public void BreakWireAt(SchematicWire wire, GridPoint at)
        {
            if (!GuardEditing()) return;
            int seg = SchematicEditing.HitSegment(wire, at, 1.0);
            if (seg < 0) return;
            SchematicEditing.DistanceToSegment(at, wire.Points[seg], wire.Points[seg + 1], out var q);
            var a = wire.Points[seg];
            var b = wire.Points[seg + 1];
            q = Math.Abs(a.X - b.X) < 1e-9 ? new GridPoint(a.X, Math.Round(q.Y)) : Math.Abs(a.Y - b.Y) < 1e-9 ? new GridPoint(Math.Round(q.X), a.Y) : q;
            W.BeginChange();
            if (SchematicEditing.BreakWire(Doc, wire, q) != null) W.CommitChange("Kablo bölündü");
            else W.CancelChange();
        }

        public void DeleteSegmentAt(SchematicWire wire, GridPoint at)
        {
            if (!GuardEditing()) return;
            int seg = SchematicEditing.HitSegment(wire, at, 1.0);
            if (seg < 0) return;
            W.BeginChange();
            SchematicEditing.DeleteSegment(Doc, wire, seg);
            _selection.Remove(wire);
            W.CommitChange("Kablo segmenti silindi");
            NotifySelection();
        }

        // ================================================================ editor drawing

        private void DrawGridLines(DrawingContext dc, GridPoint g0, GridPoint g1, int step)
        {
            var minor = MakePen(Color.FromRgb(0x18, 0x18, 0x1C), 1, false);
            var major = MakePen(Color.FromRgb(0x24, 0x24, 0x2A), 1, false);
            for (double x = Math.Floor(g0.X / step) * step; x <= g1.X; x += step)
            {
                double sx = Math.Round(ToScreen(x, 0).X) + 0.5;
                dc.DrawLine(Math.Abs(x % 10) < 1e-9 ? major : minor, new Point(sx, 0), new Point(sx, ActualHeight));
            }

            for (double y = Math.Floor(g0.Y / step) * step; y <= g1.Y; y += step)
            {
                double sy = Math.Round(ToScreen(0, y).Y) + 0.5;
                dc.DrawLine(Math.Abs(y % 10) < 1e-9 ? major : minor, new Point(0, sy), new Point(ActualWidth, sy));
            }
        }

        private void DrawNetHighlightUnderlay(DrawingContext dc, Netlist netlist)
        {
            if (_netHighlight == null || Simulating) return;
            var glow = MakePen(Color.FromArgb(0x50, HighlightColor.R, HighlightColor.G, HighlightColor.B), Math.Max(6, _scale * 0.5));
            for (int i = 0; i < Doc.Wires.Count && i < netlist.WireNets.Count; i++)
            {
                if (Doc.Wires[i].Points.Count < 2 || !Here(Doc.Wires[i]) || !string.Equals(netlist.WireNets[i], _netHighlight, StringComparison.OrdinalIgnoreCase)) continue;
                dc.DrawGeometry(null, glow, Polyline(Doc.Wires[i].Points));
            }

            var net = netlist.Nets.FirstOrDefault(n => string.Equals(n.Name, _netHighlight, StringComparison.OrdinalIgnoreCase));
            if (net == null) return;
            var ring = MakePen(HighlightColor, 1.6);
            foreach (var c in net.Connections)
            {
                var part = Doc.FindPart(c.RefDes);
                var pin = part != null ? Lib.Find(part.PartId)?.FindPin(c.Pin) : null;
                if (part == null || pin == null) continue;
                var tip = ToScreen(part.Transform(pin.X, pin.Y));
                dc.DrawEllipse(SymbolRenderer.Brush(Color.FromArgb(0x40, HighlightColor.R, HighlightColor.G, HighlightColor.B)), ring, tip, 5, 5);
            }
        }

        private void DrawNoConnect(DrawingContext dc, GridPoint at, Color color)
        {
            var c = ToScreen(at);
            double r = Math.Max(3.5, _scale * 0.35);
            var pen = MakePen(color, Math.Max(1.3, _scale * 0.09));
            dc.DrawLine(pen, new Point(c.X - r, c.Y - r), new Point(c.X + r, c.Y + r));
            dc.DrawLine(pen, new Point(c.X - r, c.Y + r), new Point(c.X + r, c.Y - r));
        }

        private void DrawNoConnects(DrawingContext dc)
        {
            foreach (var nc in SheetNoConnects) DrawNoConnect(dc, nc.Position, _selection.Contains(nc) ? Accent : Color.FromRgb(0x8C, 0x8C, 0xD8));

            // Dangling wire ends get a small square (KiCad style) so open wires stand out.
            if (Simulating || _exporting) return;
            var nl = Netlist;
            if (!ReferenceEquals(_danglingFor, nl))
            {
                _dangling = SchematicEditing.DanglingEnds(Doc, Lib, CurrentSheet);
                _danglingFor = nl;
            }

            if (_dangling == null) return;
            var pen = MakePen(WarningColor, 1, false);
            foreach (var (_, end) in _dangling)
            {
                var c = ToScreen(end);
                dc.DrawRectangle(null, pen, new Rect(c.X - 3, c.Y - 3, 6, 6));
            }
        }

        private void DrawEditorOverlay(DrawingContext dc)
        {
            if (_exporting) return;

            // Wire in progress
            if (_wirePoints != null && _wirePoints.Count > 0)
            {
                var pts = WirePreview();
                dc.DrawGeometry(null, MakePen(Accent, Math.Max(1.3, _scale * 0.1)), Polyline(pts));
                foreach (var p in _wirePoints) dc.DrawEllipse(SymbolRenderer.Brush(Accent), null, ToScreen(p), 2.5, 2.5);
                var end = pts[^1];
                if (IsConnectionPoint(end)) dc.DrawEllipse(null, MakePen(Accent, 1.4), ToScreen(end), 6, 6);
            }

            // Ghost part
            if (_tool == EditorTool.Place && _ghost != null) DrawPart(dc, _ghost, true);

            // Floating paste / duplicate
            if (_floating != null)
            {
                var pen = MakePen(Color.FromArgb(0xC0, Accent.R, Accent.G, Accent.B), Math.Max(1.3, _scale * 0.1));
                foreach (var w in _floating.Wires) if (w.Points.Count >= 2) dc.DrawGeometry(null, pen, Polyline(w.Points));
                foreach (var p in _floating.Parts) if (Lib.Find(p.PartId) != null) DrawPart(dc, p, true);
                foreach (var l in _floating.Labels)
                {
                    var ft = Text(l.Name, 11.5, Accent, false);
                    var at = ToScreen(l.Position);
                    dc.DrawRoundedRectangle(null, MakePen(Accent, 1, false), new Rect(at.X, at.Y - ft.Height / 2 - 3, ft.Width + 18, ft.Height + 6), 3, 3);
                    dc.DrawText(ft, new Point(at.X + 12, at.Y - ft.Height / 2));
                }

                foreach (var nc in _floating.NoConnects) DrawNoConnect(dc, nc.Position, Accent);
                var anchor = ToScreen(_floating.Anchor);
                dc.DrawLine(MakePen(Accent, 1, false), new Point(anchor.X - 6, anchor.Y), new Point(anchor.X + 6, anchor.Y));
                dc.DrawLine(MakePen(Accent, 1, false), new Point(anchor.X, anchor.Y - 6), new Point(anchor.X, anchor.Y + 6));
            }

            // Hover pin
            if (_hoverPin is { } hp && !Simulating && _floating == null && !_keyMove)
            {
                var tip = ToScreen(hp.Part.Transform(hp.Pin.X, hp.Pin.Y));
                dc.DrawEllipse(null, MakePen(Accent, 1.4), tip, 5, 5);
                var ft = Text(hp.Part.RefDes + "." + hp.Pin.Name, 11, TextColor);
                DrawPill(dc, new Point(tip.X + 10, tip.Y - 22), ft, PanelColor);
            }

            // Box selection: window (left→right, solid) or crossing (right→left, dashed)
            if (_drag == DragMode.Box)
            {
                bool crossing = _mouse.X < _dragStart.X;
                var c = crossing ? CrossingColor : Accent;
                var r = new Rect(_dragStart, _mouse);
                var pen = new Pen(SymbolRenderer.Brush(Color.FromArgb(0xC0, c.R, c.G, c.B)), 1);
                if (crossing) pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
                pen.Freeze();
                dc.DrawRectangle(SymbolRenderer.Brush(Color.FromArgb(0x14, c.R, c.G, c.B)), pen, r);
            }

            // ERC markers
            if (_ercMarkers != null)
            {
                foreach (var v in _ercMarkers)
                {
                    if (v.Severity == ErcSeverity.Info || !Schematic.SameSheet(v.Sheet, CurrentSheet)) continue;   // [sheets] the shown sheet's
                    var c = ToScreen(v.Location);
                    var color = v.Severity == ErcSeverity.Error ? ErrorColor : WarningColor;
                    var g = new StreamGeometry();
                    using (var s = g.Open())
                    {
                        s.BeginFigure(new Point(c.X, c.Y - 13), true, true);
                        s.LineTo(new Point(c.X + 7, c.Y - 1), true, true);
                        s.LineTo(new Point(c.X - 7, c.Y - 1), true, true);
                    }

                    g.Freeze();
                    dc.DrawGeometry(SymbolRenderer.Brush(color), null, g);
                    var ex = Text("!", 9, CanvasColor, true);
                    dc.DrawText(ex, new Point(c.X - ex.Width / 2, c.Y - 12));
                }
            }

            // Crosshair
            if (_crosshair && IsMouseOver && !_exporting)
            {
                var p = ToScreen(Snap(_mouseGrid));
                var pen = MakePen(Color.FromArgb(0x60, Accent.R, Accent.G, Accent.B), 1, false);
                dc.DrawLine(pen, new Point(0, p.Y), new Point(ActualWidth, p.Y));
                dc.DrawLine(pen, new Point(p.X, 0), new Point(p.X, ActualHeight));
            }

            // Relative origin
            if (_relOrigin is { } o)
            {
                var p = ToScreen(o);
                var pen = MakePen(HighlightColor, 1, false);
                dc.DrawEllipse(null, pen, p, 4, 4);
                dc.DrawLine(pen, new Point(p.X - 7, p.Y), new Point(p.X + 7, p.Y));
                dc.DrawLine(pen, new Point(p.X, p.Y - 7), new Point(p.X, p.Y + 7));
            }
        }

        /// <summary>[units] The unit the corner's coordinates are in: the grid's steps, or mm, mil or inches (KiCad's choice).</summary>
        public string Units { get; set; } = Shell.AppSettings.Current.SchematicUnits is { Length: > 0 } u ? u : "grid";

        /// <summary>[units] A length in grid steps in the chosen unit, with its sign.</summary>
        private string InUnits(double steps)
        {
            double mm = steps * RobotTwin.CoreSim.Pcb.KiCadExporter.SchematicGridMm;
            return Units switch
            {
                "mm" => mm.ToString("0.00", CultureInfo.InvariantCulture),
                "mil" => (mm / 0.0254).ToString("0", CultureInfo.InvariantCulture),
                "in" => (mm / 25.4).ToString("0.000", CultureInfo.InvariantCulture),
                _ => steps.ToString("0", CultureInfo.InvariantCulture)
            };
        }

        private string UnitSuffix => Units switch { "mm" => " mm", "mil" => " mil", "in" => " in", _ => string.Empty };

        /// <summary>Status strip in the bottom-left corner: cursor coordinates, relative offset, zoom and mode.</summary>
        private void DrawHud(DrawingContext dc, Rect size)
        {
            if (size.Width < 200) return;
            var g = Snap(_mouseGrid);
            string text = $"X {InUnits(g.X),5}  Y {InUnits(g.Y),5}{UnitSuffix}";
            if (_relOrigin is { } o) text += $"   dX {InUnits(g.X - o.X)}  dY {InUnits(g.Y - o.Y)}{UnitSuffix}";
            text += string.Format(CultureInfo.InvariantCulture, "   {0:0}%", _scale / 14.0 * 100);
            if (_selection.Count > 0) text += $"   · {_selection.Count} seçili";
            string mode = ModeText();
            if (mode.Length > 0) text += "   · " + mode;
            if (_netHighlight != null) text += "   · vurgulu hat: " + _netHighlight;
            var ft = Text(text, 10.5, MutedText, false, true);
            var r = new Rect(8, size.Height - ft.Height - 12, ft.Width + 14, ft.Height + 6);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(Color.FromArgb(0xD0, PanelColor.R, PanelColor.G, PanelColor.B)), MakePen(PanelBorder, 1, false), r, 4, 4);
            dc.DrawText(ft, new Point(r.Left + 7, r.Top + 3));
        }

        // ================================================================ export

        /// <summary>Renders the whole sheet (no grid, hover or selection) for PNG/SVG/print export.</summary>
        public DrawingGroup RenderSheet(out Size size, double pixelsPerUnit = 20, bool clearBackground = false)
        {
            _clearBackground = clearBackground;   // [home] a preview: only the drawing, the start page's card is behind it
            var bounds = SchematicEditing.Bounds(SchematicEditing.AllItems(Doc, CurrentSheet), Lib) ?? new GridRect(-10, -10, 10, 10);
            bounds = bounds.Inflate(4);
            var saved = (_scale, _offset, _hover, _hoverPin, _netHighlight, _ercMarkers);
            var selection = _selection.ToList();
            _selection.Clear();
            _hover = null;
            _hoverPin = null;
            _ercMarkers = null;
            _netHighlight = null;
            _scale = pixelsPerUnit;
            _offset = new Vector(-bounds.MinX * _scale, -bounds.MinY * _scale);
            size = new Size(Math.Ceiling(bounds.Width * _scale), Math.Ceiling(bounds.Height * _scale));
            var visual = new DrawingVisual();
            _exporting = true;
            try
            {
                using (var dc = visual.RenderOpen()) RenderScene(dc, new Rect(size));
            }
            finally
            {
                _exporting = false;
                _clearBackground = false;
                (_scale, _offset, _hover, _hoverPin, _netHighlight, _ercMarkers) = saved;
                foreach (var s in selection) _selection.Add(s);
                _labelRects.Clear();
                InvalidateVisual();
            }

            return visual.Drawing;
        }
    }
}
