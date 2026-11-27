using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;
using Geometry = RobotTwin.CoreSim.Pcb.Geometry;

namespace RobotWin.Studio.Pcb
{
    /// <summary>Editing tools of the PCB canvas.</summary>
    public enum PcbTool
    {
        Select,
        Route,
        Zone,
        Measure,
        Text,
        Dimension,
        Outline,
        Hole,

        // [graphics] KiCad's drawing tools and the standalone via (Pcb/BoardCanvas.Graphics.cs)
        Line,
        Rectangle,
        Circle,
        Arc,
        Polygon,

        /// <summary>[bezier] A cubic Bézier curve: start, end, then its two control points.</summary>
        Bezier,

        /// <summary>[image] A reference picture picked from a file, placed where clicked.</summary>
        Image,

        /// <summary>[table] A table of cells, placed by its top-left corner, then filled in.</summary>
        Table,
        Via,

        /// <summary>[tuning] KiCad's length tuner: meanders on the clicked segment up to its pair's or group's length.</summary>
        Tune,

        /// <summary>[keepout] A rule area: drawn as a polygon like a zone.</summary>
        Keepout,

        /// <summary>[diffroute] KiCad's differential pair router: both nets of a pair laid together (Pcb/BoardCanvas.DiffPair.cs).</summary>
        DiffPair,

        /// <summary>[highlight] KiCad's Highlight Net tool: a click lights up the net under it; the same net or empty space clears it.</summary>
        Highlight,

        /// <summary>[eraser] Altium's and Eagle's delete tool: each click deletes what is under it (as the selection filter allows).</summary>
        Erase
    }

    /// <summary>
    /// Interactive 2D PCB editor: every copper layer of the stackup (active layer on top,
    /// others dimmed), zone fills, pads, vias, silkscreen, drawings, ratsnest and DRC
    /// markers; selection and dragging (parts, track segments with rubber-banding, vias,
    /// texts, holes), the DRC-aware interactive router, zone/outline polygon drawing,
    /// measuring, texts, dimensions and mounting holes.
    /// </summary>
    public sealed partial class BoardCanvas : FrameworkElement
    {
        // [theme] Around the board, the board and its ink follow the theme (Shell.CanvasColors); copper keeps its colours.
        private static Color Back => Shell.CanvasColors.PcbBack;
        private static Color Substrate => Shell.CanvasColors.PcbSubstrate;
        private static Color SubstrateEdge => Shell.CanvasColors.PcbEdge;
        private static readonly Color PadGold = Color.FromRgb(0xB8, 0xB8, 0xBE);
        private static Color Silk => Shell.CanvasColors.PcbSilk;
        private static Color Rats => Shell.CanvasColors.PcbRats;
        private static Color Accent => Shell.CanvasColors.Accent;
        private static readonly Color Danger = Color.FromRgb(0xE5, 0x48, 0x4D);
        private static readonly Color Warning = Color.FromRgb(0xD4, 0xA8, 0x4B);
        private static Color Drawing => Shell.CanvasColors.PcbDrawing;

        // Inner layers cycle through these (In1 green, In2 ochre, In3 violet, In4 teal …), so neighbours differ.
        private static readonly Color[] InnerColors =
        {
            Color.FromRgb(0x5E, 0x9A, 0x6E), Color.FromRgb(0xB0, 0x92, 0x4A), Color.FromRgb(0x92, 0x6E, 0xB2), Color.FromRgb(0x4E, 0x9C, 0xA2),
            Color.FromRgb(0xB0, 0x6A, 0x86), Color.FromRgb(0x8A, 0xA2, 0x4E), Color.FromRgb(0x6A, 0x7E, 0xC0), Color.FromRgb(0xC0, 0x7A, 0x4E)
        };

        /// <summary>Display colour of a copper layer.</summary>
        /// <summary>[net-colors] A net's own colour (Board.NetColors), or null.</summary>
        private static Color? NetColor(BoardDocument board, string? net)
        {
            if (board.NetColorOf(net) is not { } hex) return null;
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>[net-colors] The colour a net's copper is drawn in on a layer: its own when it has one, else the layer's.</summary>
        private static Color CopperColor(BoardDocument board, CopperLayer layer, string? net) => NetColor(board, net) ?? LayerColor(layer);

        public static Color LayerColor(CopperLayer layer) => layer switch
        {
            CopperLayer.Top => Color.FromRgb(0xB0, 0x82, 0x6A),
            CopperLayer.Bottom => Color.FromRgb(0x5A, 0x72, 0xB0),
            _ => InnerColors[((int)layer - (int)CopperLayer.In1) % InnerColors.Length]
        };

        private double _scale = 8;
        private Vector _offset = new Vector(40, 40);
        private PcbTool _tool = PcbTool.Select;
        private CopperLayer _activeLayer = CopperLayer.Top;
        private readonly List<object> _sel = new List<object>();

        /// <summary>The primary selected item (first of the selection).</summary>
        private object? _selected
        {
            get => _sel.Count > 0 ? _sel[0] : null;
            set
            {
                _sel.Clear();
                if (value != null) _sel.Add(value);
            }
        }
        private BoardComponent? _hover;
        private (double X, double Y) _cursor;
        private bool _panning;
        private Point _panStartPoint;
        private Vector _panStart;
        private readonly Dictionary<Zone, (object Key, StreamGeometry Geo)> _zoneGeo = new Dictionary<Zone, (object, StreamGeometry)>();

        public BoardCanvas()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            Shell.Themes.Changed += InvalidateVisual;   // [theme] the board and its ink
        }

        // ================================================================ public state

        private BoardDocument? _board;

        public BoardDocument? Board
        {
            get => _board;
            set
            {
                if (!ReferenceEquals(_board, value))
                {
                    _sel.Clear();
                    CancelInteractions();
                }

                _board = value;
                if (_board != null && !_board.HasLayer(_activeLayer)) ActiveLayer = CopperLayer.Top;
                Refresh();
            }
        }

        public IReadOnlyList<DrcViolation> Violations { get; set; } = Array.Empty<DrcViolation>();

        /// <summary>The violation picked in the DRC list (drawn larger).</summary>
        public DrcViolation? FocusedViolation { get; set; }

        public PcbTool Tool => _tool;

        public CopperLayer ActiveLayer
        {
            get => _activeLayer;
            set
            {
                if (_activeLayer == value) return;
                _activeLayer = value;
                HiddenLayers.Remove(value);
                ActiveLayerChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
        }

        public HashSet<CopperLayer> HiddenLayers { get; } = new HashSet<CopperLayer>();
        public bool ShowSilk { get; set; } = true;
        public bool ShowRatsnest { get; set; } = true;
        public bool ShowZones { get; set; } = true;

        /// <summary>[zone-look] KiCad's zone display: pours drawn by their outline only (the copper under them shows).</summary>
        public bool ZoneOutlines { get; set; }

        /// <summary>[opacity] KiCad's Objects tab: how opaque tracks (with their teardrops), vias, pads and pours are drawn, 0–1.</summary>
        public double TrackOpacity { get; set; } = 1;
        public double ViaOpacity { get; set; } = 1;
        public double PadOpacity { get; set; } = 1;
        public double ZoneOpacity { get; set; } = 1;
        public bool ShowDrawings { get; set; } = true;
        public bool ShowPadNames { get; set; } = true;

        /// <summary>Draws the layers other than the active one faded (KiCad's high-contrast mode).</summary>
        public bool HighContrast { get; set; } = true;

        /// <summary>Snap grid in mm.</summary>
        public double GridSize { get; set; } = 0.635;

        /// <summary>Show lengths in mil instead of mm.</summary>
        public bool UseMil { get; set; }

        /// <summary>Net drawn highlighted (others faded); null for none.</summary>
        public string? HighlightNet { get; private set; }

        /// <summary>Corner radius applied to every vertex of a newly drawn board outline.</summary>
        public double OutlineCornerRadius { get; set; } = 1.0;

        public double HoleDrill { get; set; } = 3.2;
        public double HolePad { get; set; }

        public object? Selected => _selected;
        public BoardComponent? SelectedComponent => _selected as BoardComponent;
        public bool Routing => _tool == PcbTool.Route;
        public bool RouteInProgress => _routePts.Count > 0;

        // ================================================================ events

        public event EventHandler? SelectionChanged;

        /// <summary>Raised before the board is modified (undo snapshot point).</summary>
        public event EventHandler? BeforeEdit;

        /// <summary>Raised when a started edit was abandoned (nothing changed).</summary>
        public event EventHandler? EditCancelled;

        /// <summary>Raised after the board was modified.</summary>
        public event EventHandler<string>? BoardEdited;

        public event EventHandler? ToolChanged;

        /// <summary>[routebar] The route being laid changed (started, moved, a via, finished): the options bar's live part follows.</summary>
        public event EventHandler? RouteStatusChanged;

        /// <summary>[routebar] What the route being laid is now: its net and class, clearance, width, lengths and whether it gets through.</summary>
        public readonly record struct RouteStatus(string Net, string ClassName, double Clearance, double Width, double RouteLength, double NetLength, bool Clear, CopperLayer Layer, int Vias);

        /// <summary>[routebar] The route being laid, or null when none is.</summary>
        public RouteStatus? CurrentRoute
        {
            get
            {
                if (_router == null || _routePts.Count == 0 || Board == null) return null;
                var (route, net) = RouteLengths();
                return new RouteStatus(_routeNet, Board.ClassNameFor(_routeNet), _router.Class.Clearance, _router.Width, route, net, _preview.Count == 0 || _previewReached, _routeLayer, _routeVias.Count);
            }
        }
        public event EventHandler? ActiveLayerChanged;
        public event EventHandler<string>? Hint;
        public event EventHandler? CursorMoved;
        public event EventHandler<List<PcbPoint>>? ZoneDrawn;
        public event EventHandler<List<PcbPoint>>? OutlineDrawn;
        public event EventHandler<PcbPoint>? TextRequested;
        public event EventHandler<object>? EditRequested;
        public event EventHandler? UndoRequested;
        public event EventHandler? RedoRequested;
        public event EventHandler<string?>? HighlightChanged;

        public (double X, double Y) CursorPosition => _cursor;

        private void Edited(string message)
        {
            BoardEdited?.Invoke(this, message);
            InvalidateVisual();
        }

        private void Say(string text) => Hint?.Invoke(this, text);

        /// <summary>Drops cached geometry (call after the board changed outside the canvas).</summary>
        public void Refresh()
        {
            _zoneGeo.Clear();
            if (_board != null) _sel.RemoveAll(o => !Exists(o));
            InvalidateVisual();
        }

        public void Select(object? item)
        {
            _selected = item;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        /// <summary>Replaces the selection with several items (the first one is the primary).</summary>
        public void SetSelection(IEnumerable<object> items)
        {
            _sel.Clear();
            foreach (var i in items)
            {
                if (!_sel.Contains(i)) _sel.Add(i);
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public IReadOnlyList<object> SelectionItems => _sel;

        private bool Exists(object o)
        {
            var b = _board;
            if (b == null) return false;
            return o switch
            {
                Track t => b.Tracks.Contains(t),
                Via v => b.Vias.Contains(v),
                Zone z => b.Zones.Contains(z),
                BoardComponent c => b.Components.Contains(c),
                MountingHole h => b.MountingHoles.Contains(h),
                BoardText x => b.Texts.Contains(x),
                BoardDimension d => b.Dimensions.Contains(d),
                OutlineSelection => true,
                _ => false
            };
        }

        public void SetHighlight(string? net)
        {
            HighlightNet = string.IsNullOrEmpty(net) ? null : net;
            HighlightChanged?.Invoke(this, HighlightNet);
            InvalidateVisual();
        }

        public void SetTool(PcbTool tool)
        {
            if (tool == PcbTool.Image && _pendingImage == null)
            {
                // [image] the picture first; none picked: the tool stays as it was
                if (RobotWin.Studio.Theme.Pictures.Pick(Window.GetWindow(this)) is not { } data) return;
                var (w, h) = RobotTwin.CoreSim.Design.EmbeddedImage.SizeMm(data);
                if (w <= 0 || h <= 0) return;
                _pendingImage = (data, w, h);
            }
            else if (tool != PcbTool.Image) _pendingImage = null;

            CancelInteractions();
            _tool = tool;
            Cursor = tool == PcbTool.Select ? null : Cursors.Cross;
            ToolChanged?.Invoke(this, EventArgs.Empty);
            Say(ToolHint());
            InvalidateVisual();
        }

        public string ToolHint() => _tool switch
        {
            PcbTool.Route => "Yol: bir pede/yola/viaya tıklayın · tıklayarak ilerleyin (engellerin etrafından dolaşır) · V: via + katman · PgUp/PgDn: katman · W: genişlik · /: açı yönü · Boşluk: 45°/90° · Geri: son parça · Enter: bitir · Esc: iptal · F1: kısayollar",
            PcbTool.Zone => "Dolgu alanı: köşelere tıklayın · çift tık veya Enter: kapat · Geri: son köşeyi sil · Esc: iptal",
            PcbTool.Measure => "Ölç (Ctrl+Shift+M): iki noktaya tıklayın (pedlere ve vialara yapışır) · Esc: çık",
            PcbTool.Text => "Yazı: serigrafiye yazı koymak için tıklayın",
            PcbTool.Line => "Çizgi: başlangıca ve bitişe tıklayın; her bitiş yeni çizginin başı olur · Esc: bitir",
            PcbTool.Rectangle => "Dikdörtgen: iki karşı köşeye tıklayın · Esc: iptal",
            PcbTool.Circle => "Daire: merkeze, sonra çevresinden bir noktaya tıklayın · Esc: iptal",
            PcbTool.Arc => "Yay: merkeze, başlangıca ve bitişe tıklayın (saat yönünün tersine) · Esc: iptal",
            PcbTool.Bezier => "Bezier eğrisi: başlangıca, bitişe, sonra iki denetim noktasına tıklayın · Esc: iptal",
            PcbTool.Table => "Tablo: sol üst köşesine tıklayın (3 × 3); sonra hücreler yazılır · Esc: iptal",
            PcbTool.Image => "Resim: koymak istediğiniz yere tıklayın (seçenek çubuğundaki katmana; üretime girmez, yalnız yol gösterir) · Esc: iptal",
            PcbTool.Polygon => "Çokgen: köşelere tıklayın · çift tık, ilk köşeye tıklamak ya da Enter: kapat · Esc: iptal",
            PcbTool.Via => "Via: yere tıklayın (altındaki yolun, pedin ya da dolgunun hattını alır) · Esc: çık",
            PcbTool.Keepout => "Yasak bölge: köşelere tıklayın · çift tık veya Enter: kapat · seçenek çubuğundan katman ve neyin yasak olduğu · Esc: iptal",
            PcbTool.DiffPair => "Fark çifti: bir çiftin (D+/D−, CAN H/L, _P/_N …) pedine tıklayın · tıklayarak ilerleyin, iki yol çiftin aralığıyla birlikte gider · karşı uçtaki çift pedine tıklayınca biter · Enter: burada bitir · Geri: son parça · Esc: iptal",
            PcbTool.Tune => "Uzunluk ayarı: kısa kalan hattın uzun, düz bir parçasına tıklayın; kıvrımlar hattı çiftinin ya da grubunun uzunluğuna getirir · Esc: çık",
            PcbTool.Erase => "Silici: tıklanan öğe silinir (seçim süzgecinin izin verdikleri) · Ctrl+Z geri alır · Esc: çık",
            PcbTool.Dimension => DimKind switch
            {
                DimensionKind.Orthogonal => "Dik ölçü: iki noktaya tıklayın, sonra üstüne/altına (yatay) ya da yanına (dikey) çekip tıklayın",
                DimensionKind.Radial => "Yarıçap: merkeze, sonra çemberin bir noktasına tıklayın, sonra dışarı çekip tıklayın",
                DimensionKind.Leader => "Kılavuz: okun ucuna, sonra yazının yerine tıklayın; not sorulur",
                DimensionKind.Center => "Merkez işareti: merkeze, sonra işaretin ucuna tıklayın",
                _ => "Ölçü çizgisi: başlangıç ve bitişe tıklayın, sonra kaydırıp tıklayın"
            },
            PcbTool.Outline => "Kart kenarı: köşelere tıklayın · çift tık veya Enter: kapat (köşe yarıçapı panelden) · Esc: iptal",
            PcbTool.Hole => "Montaj deliği: yerleştirmek için tıklayın",
            PcbTool.Highlight => "Hattı vurgula: bir yola, pede, via'ya ya da dolguya tıklayın · aynı hatta ya da boşa tıklayınca kalkar · Esc: çık",
            _ => "Sağ tık: menü · Sürükle: kutu seçimi · M: taşı · G: yollarla sürükle · D: yol sürükle · R: döndür · F: çevir · Del: sil · U: tüm yol · W: genişlik · `: hat vurgula · B: doldur · X: yol · Ctrl+Z: geri al · F1 / ?: kısayollar"
        };

        private void CancelInteractions()
        {
            if (_moving) CancelMove();
            _keyboardSegmentDrag = false;
            bool routing = _router != null;
            _routePts.Clear();
            _routeVias.Clear();
            _preview.Clear();
            _router = null;
            if (routing) RouteStatusChanged?.Invoke(this, EventArgs.Empty);   // [routebar] the route ended
            _routeStartPad = null;
            _polyPts.Clear();
            _gfx.Clear();   // [graphics]
            DiffPairCancel();   // [diffroute]
            _measureA = _measureB = null;
            _dimA = _dimB = null;
            _drag = DragKind.None;
            InvalidateVisual();
        }

        // ================================================================ coordinates

        private Point S(double x, double y) => new Point(x * _scale + _offset.X, y * _scale + _offset.Y);
        private (double X, double Y) ToBoard(Point p) => ((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);
        private double Snap(double v) => GridSize > 0 ? Math.Round(Math.Round(v / GridSize) * GridSize, 6) : v;

        // [origins] a point snaps to the grid as it runs through the board's grid origin (a step, dx or dy, keeps Snap)
        private double SnapX(double v) => SnapFrom(v, Board?.GridOrigin?.X ?? 0);

        private double SnapY(double v) => SnapFrom(v, Board?.GridOrigin?.Y ?? 0);

        private double SnapFrom(double v, double origin) => GridSize > 0 ? Math.Round(origin + Math.Round((v - origin) / GridSize) * GridSize, 6) : v;

        public string Length(double mm) => UseMil
            ? (mm / 0.0254).ToString("0.#", CultureInfo.InvariantCulture) + " mil"
            : mm.ToString("0.###", CultureInfo.InvariantCulture) + " mm";

        public void Fit()
        {
            if (Board == null || ActualWidth < 10) return;
            _scale = Math.Min((ActualWidth - 80) / Board.Width, (ActualHeight - 80) / Board.Height);
            _scale = Math.Max(1, _scale);
            _offset = new Vector((ActualWidth - Board.Width * _scale) / 2, (ActualHeight - Board.Height * _scale) / 2);
            InvalidateVisual();
        }

        /// <summary>[viewports] The view as it is now: its middle on the board and the width it shows (mm).</summary>
        public BoardViewport CurrentView(string name)
        {
            var (x, y) = ToBoard(new Point(ActualWidth / 2, ActualHeight / 2));
            return new BoardViewport { Name = name, X = Math.Round(x, 3), Y = Math.Round(y, 3), Width = Math.Round(ActualWidth / _scale, 3) };
        }

        /// <summary>[viewports] Goes back to a saved view: its middle in the middle, its width across the window.</summary>
        public void ShowView(BoardViewport v)
        {
            if (ActualWidth < 10 || v.Width <= 0) return;
            _scale = Math.Max(0.5, ActualWidth / v.Width);
            _offset = new Vector(ActualWidth / 2 - v.X * _scale, ActualHeight / 2 - v.Y * _scale);
            InvalidateVisual();
        }

        /// <summary>[crossprobe] Pans so a board point (mm) is in view when it is not (the zoom stays).</summary>
        public void EnsureVisible(double x, double y)
        {
            if (ActualWidth < 10) return;
            double sx = _offset.X + x * _scale, sy = _offset.Y + y * _scale;
            if (sx >= 40 && sy >= 40 && sx <= ActualWidth - 40 && sy <= ActualHeight - 40) return;
            _offset = new Vector(ActualWidth / 2 - x * _scale, ActualHeight / 2 - y * _scale);
            InvalidateVisual();
        }

        public void ZoomTo(double x, double y)
        {
            _scale = Math.Max(_scale, 14);
            _offset = new Vector(ActualWidth / 2 - x * _scale, ActualHeight / 2 - y * _scale);
            InvalidateVisual();
        }

        private static Pen P(Color c, double w, bool round = true)
        {
            var pen = new Pen(SymbolRenderer.Brush(c), w);
            if (round)
            {
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
                pen.LineJoin = PenLineJoin.Round;
            }

            pen.Freeze();
            return pen;
        }

        private static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        private static Color A(Color c, double alpha) => Color.FromArgb((byte)Math.Max(0, Math.Min(255, alpha * 255)), c.R, c.G, c.B);

        private double Dip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        private FormattedText Text(string s, double size, Color c, bool mono = false) =>
            new FormattedText(RobotWin.Studio.Shell.Lang.T(s), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono ? Typefaces.Mono : Typefaces.Ui, size, SymbolRenderer.Brush(c), Dip);

        private bool Visible(CopperLayer l) => !HiddenLayers.Contains(l);

        /// <summary>Opacity multiplier for an item of a net on a layer (highlight and high-contrast).</summary>
        private double Fade(string? net, CopperLayer? layer)
        {
            double f = 1;
            if (layer.HasValue && HighContrast && layer.Value != _activeLayer) f *= 0.45;
            if (HighlightNet != null)
            {
                bool hit = string.Equals(net, HighlightNet, StringComparison.OrdinalIgnoreCase);
                f = hit ? 1 : f * 0.3;
            }

            return f;
        }

        // ================================================================ rendering

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(SymbolRenderer.Brush(Back), null, new Rect(0, 0, ActualWidth, ActualHeight));
            var board = Board;
            if (board == null) return;

            // Substrate in the real outline.
            var outline = board.OutlinePolygon(0.02);
            dc.DrawGeometry(SymbolRenderer.Brush(Substrate), P(SubstrateEdge, 1), Poly(outline));
            if (ShowGrid) DrawGrid(dc, board);   // [grid] KiCad's View → Show Grid

            // Copper layers: bottom of the stack first, the active layer last.
            var stack = board.CopperLayers().Reverse().Where(Visible).ToList();
            if (stack.Remove(_activeLayer)) stack.Add(_activeLayer);
            var pads = board.Pads().ToList();
            foreach (var layer in stack) DrawLayer(dc, board, layer, pads);

            // Through-hole pads (all layers) and holes.
            foreach (var pad in pads.Where(p => p.Pad.IsThroughHole)) DrawPad(dc, pad, pad.Pad.NonPlated ? Back : PadGold, Fade(pad.Net, null));
            foreach (var via in board.Vias) DrawVia(dc, board, via);
            foreach (var h in board.MountingHoles)
            {
                var c = S(h.X, h.Y);
                if (h.Plated) dc.DrawEllipse(SymbolRenderer.Brush(A(PadGold, Fade(h.Net, null))), null, c, h.PadDiameter / 2 * _scale, h.PadDiameter / 2 * _scale);
                dc.DrawEllipse(SymbolRenderer.Brush(Back), P(A(SubstrateEdge, (byte)0xC0), 1), c, h.Drill / 2 * _scale, h.Drill / 2 * _scale);
            }

            if (ShowPadNames && _scale > 16) DrawPadNames(dc, pads);
            if (ShowSilk) DrawSilk(dc, board);
            if (ShowDrawings) DrawDrawings(dc, board);
            DrawGraphics(dc, board);   // [graphics] each by its layer's switch
            DrawRuleAreas(dc, board);   // [keepout]
            if (ShowRatsnest || LocalRatsnest.Count > 0 || _sel.OfType<BoardComponent>().Any()) DrawRatsnest(dc, board);   // [local-rats]
            DrawSelection(dc, board);
            DrawGraphicPreview(dc);   // [graphics]
            DrawDiffPreview(dc);   // [diffroute]

            // DRC markers.
            foreach (var v in Violations)
            {
                var c = S(v.X, v.Y);
                bool focus = ReferenceEquals(v, FocusedViolation);
                var color = v.Severity == DrcSeverity.Error ? Danger : Warning;
                double r = focus ? 14 : 9;
                dc.DrawEllipse(SymbolRenderer.Brush(A(color, focus ? (byte)0x70 : (byte)0x40)), P(color, focus ? 2.4 : 1.6), c, r, r);
                var ft = Text("!", 12, color);
                dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
            }

            // Scale bar.
            double mm = _scale >= 20 ? 5 : (_scale >= 6 ? 10 : 50);
            var bar = new Point(16, ActualHeight - 22);
            dc.DrawLine(P(Rats, 2), bar, new Point(bar.X + mm * _scale, bar.Y));
            dc.DrawText(Text(Length(mm), 11, Rats, true), new Point(bar.X, bar.Y - 16));

            DrawRoutePreview(dc, board);
            DrawPolygonPreview(dc);
            DrawMeasure(dc);
            DrawDimensionPreview(dc);
            DrawMagnetMark(dc);   // [magnet]
            DrawOrigins(dc, board);   // [origins]
            if (FullCrosshair && IsMouseOver)
            {
                // [crosshair] KiCad's full-window crosshair: two thin lines through the pointer, edge to edge
                var c = S(_cursor.X, _cursor.Y);
                var pen = P(A(Accent, (byte)0x90), 1, false);
                dc.DrawLine(pen, new Point(0, c.Y), new Point(ActualWidth, c.Y));
                dc.DrawLine(pen, new Point(c.X, 0), new Point(c.X, ActualHeight));
            }
        }

        private StreamGeometry Poly(IReadOnlyList<PcbPoint> pts)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                if (pts.Count > 0)
                {
                    c.BeginFigure(S(pts[0].X, pts[0].Y), true, true);
                    c.PolyLineTo(pts.Skip(1).Select(p => S(p.X, p.Y)).ToList(), true, true);
                }
            }

            g.Freeze();
            return g;
        }

        /// <summary>[grid] Whether the editing grid is drawn (snapping is unchanged either way).</summary>
        public bool ShowGrid
        {
            get => _showGrid;
            set
            {
                _showGrid = value;
                InvalidateVisual();
            }
        }

        private bool _showGrid = true;

        private void DrawGrid(DrawingContext dc, BoardDocument board)
        {
            double g = GridSize;
            if (g <= 0) return;
            while (g * _scale < 10) g *= 5;
            var pen = P(Shell.CanvasColors.PcbGrid, 1, false);
            var (x0, y0) = ToBoard(new Point(0, 0));
            var (x1, y1) = ToBoard(new Point(ActualWidth, ActualHeight));
            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            x1 = Math.Min(board.Width, x1);
            y1 = Math.Min(board.Height, y1);
            double ox = board.GridOrigin?.X ?? 0, oy = board.GridOrigin?.Y ?? 0;   // [origins] the grid runs through its origin
            for (double x = ox + Math.Ceiling((x0 - ox) / g) * g; x <= x1; x += g) dc.DrawLine(pen, S(x, y0), S(x, y1));
            for (double y = oy + Math.Ceiling((y0 - oy) / g) * g; y <= y1; y += g) dc.DrawLine(pen, S(x0, y), S(x1, y));
        }

        private StreamGeometry ZoneGeometry(Zone z)
        {
            var key = z.FilledRegions;
            if (_zoneGeo.TryGetValue(z, out var cached) && ReferenceEquals(cached.Key, key) && Math.Abs(cached.Geo.Bounds.Width - 0) >= 0 && _geoScale == _scale && _geoOffset == _offset)
            {
                return cached.Geo;
            }

            var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var c = g.Open())
            {
                foreach (var region in key)
                {
                    foreach (var contour in region.Contours())
                    {
                        if (contour.Count < 3) continue;
                        c.BeginFigure(S(contour[0].X, contour[0].Y), true, true);
                        c.PolyLineTo(contour.Skip(1).Select(p => S(p.X, p.Y)).ToList(), true, false);
                    }
                }
            }

            g.Freeze();
            _zoneGeo[z] = (key, g);
            return g;
        }

        private double _geoScale;
        private Vector _geoOffset;

        private void DrawLayer(DrawingContext dc, BoardDocument board, CopperLayer layer, List<PlacedPad> pads)
        {
            var color = LayerColor(layer);
            bool active = layer == _activeLayer;
            if (_geoScale != _scale || _geoOffset != _offset)
            {
                _zoneGeo.Clear();
                _geoScale = _scale;
                _geoOffset = _offset;
            }

            if (ShowZones)
            {
                foreach (var z in board.Zones.Where(z => z.Layer == layer))
                {
                    double f = Fade(z.Net, layer);
                    if (z.IsFilled)
                    {
                        var zc = CopperColor(board, layer, z.Net);
                        if (ZoneOutlines) dc.DrawGeometry(null, P(A(zc, (active ? 0.95 : 0.7) * f * ZoneOpacity), 1.2, false), ZoneGeometry(z));   // [zone-look] the fill's edge only
                        else dc.DrawGeometry(SymbolRenderer.Brush(A(zc, (active ? 0.42 : 0.3) * f * ZoneOpacity)), null, ZoneGeometry(z));
                    }
                    if (!z.IsWholeBoard)
                    {
                        var pen = new Pen(SymbolRenderer.Brush(A(color, 0.8 * f)), 1) { DashStyle = DashStyles.Dash };
                        pen.Freeze();
                        dc.DrawGeometry(null, pen, Poly(z.Outline));
                    }
                }
            }

            foreach (var td in TeardropsNow(board).Where(d => d.Layer == layer))   // [teardrop] copper like the track's (Pcb/BoardCanvas.Teardrops.cs)
            {
                double f = Fade(td.Net, layer);
                dc.DrawGeometry(SymbolRenderer.Brush(A(CopperColor(board, layer, td.Net), (active ? 0.92 : 0.8) * f * TrackOpacity)), null, Poly(td.Outline));
            }

            foreach (var t in board.Tracks.Where(t => t.Layer == layer))
            {
                double f = Fade(t.Net, layer);
                dc.DrawLine(P(A(CopperColor(board, layer, t.Net), (active ? 0.92 : 0.8) * f * TrackOpacity), Math.Max(1, t.Width * _scale)), S(t.X1, t.Y1), S(t.X2, t.Y2));
            }

            foreach (var pad in pads.Where(p => !p.Pad.IsThroughHole && p.SmdLayer == layer))
            {
                DrawPad(dc, pad, Blend(color, PadGold, 0.45), Fade(pad.Net, layer));
            }
        }

        private static Color Blend(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private void DrawPad(DrawingContext dc, PlacedPad pad, Color color, double fade = 1)
        {
            var c = S(pad.X, pad.Y);
            double w = pad.Width * _scale, h = pad.Height * _scale;
            var brush = SymbolRenderer.Brush(A(color, fade * PadOpacity));   // [opacity]
            if (!(pad.Pad.IsThroughHole && pad.Pad.NonPlated))
            {
                switch (pad.Pad.Shape)
                {
                    case PadShape.Circle:
                        dc.DrawEllipse(brush, null, c, Math.Max(w, h) / 2, Math.Max(w, h) / 2);
                        break;
                    case PadShape.Rect:
                        dc.DrawRectangle(brush, null, new Rect(c.X - w / 2, c.Y - h / 2, w, h));
                        break;
                    default:
                        double r = pad.CornerRadius * _scale;
                        dc.DrawRoundedRectangle(brush, null, new Rect(c.X - w / 2, c.Y - h / 2, w, h), r, r);
                        break;
                }
            }

            if (pad.Pad.IsThroughHole)
            {
                dc.DrawEllipse(SymbolRenderer.Brush(Back), null, c, pad.Pad.Drill / 2 * _scale, pad.Pad.Drill / 2 * _scale);
            }
        }

        private void DrawVia(DrawingContext dc, BoardDocument board, Via via)
        {
            var c = S(via.X, via.Y);
            double f = Fade(via.Net, null) * ViaOpacity;   // [opacity]
            var ring = NetColor(board, via.Net) is { } own ? Blend(own, PadGold, 0.25) : PadGold;   // [net-colors]
            dc.DrawEllipse(SymbolRenderer.Brush(A(ring, f)), null, c, via.Diameter / 2 * _scale, via.Diameter / 2 * _scale);
            if (!via.IsThrough(board.LayerCount))
            {
                // Blind/buried: a ring in the colours of the two end layers.
                var (from, to) = via.Span(board.LayerCount);
                double r = (via.Diameter / 2 + via.Drill / 2) / 2 * _scale;
                var g = new StreamGeometry();
                using (var sg = g.Open())
                {
                    sg.BeginFigure(new Point(c.X - r, c.Y), false, false);
                    sg.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }

                var g2 = new StreamGeometry();
                using (var sg = g2.Open())
                {
                    sg.BeginFigure(new Point(c.X + r, c.Y), false, false);
                    sg.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }

                double w = Math.Max(1, (via.Diameter - via.Drill) / 2 * _scale * 0.7);
                dc.DrawGeometry(null, P(LayerColor(from), w, false), g);
                dc.DrawGeometry(null, P(LayerColor(to), w, false), g2);
            }

            dc.DrawEllipse(SymbolRenderer.Brush(Back), null, c, via.Drill / 2 * _scale, via.Drill / 2 * _scale);
        }

        private void DrawPadNames(DrawingContext dc, List<PlacedPad> pads)
        {
            foreach (var p in pads)
            {
                if (!p.Pad.IsThroughHole && !Visible(p.SmdLayer)) continue;
                double size = Math.Min(p.Width, p.Height) * _scale * 0.42;
                if (size < 6) continue;
                size = Math.Min(size, 13);
                var name = Text(p.Pad.Name, size, Color.FromRgb(0x10, 0x10, 0x12));
                var c = S(p.X, p.Y);
                double dy = string.IsNullOrEmpty(p.Net) || size < 8 ? 0 : -name.Height * 0.45;
                dc.DrawText(name, new Point(c.X - name.Width / 2, c.Y - name.Height / 2 + dy));
                if (dy != 0)
                {
                    var net = Text(p.Net.Length > 8 ? p.Net.Substring(0, 8) : p.Net, size * 0.75, Color.FromRgb(0x20, 0x20, 0x24));
                    if (net.Width < p.Width * _scale * 1.1) dc.DrawText(net, new Point(c.X - net.Width / 2, c.Y + name.Height * 0.05));
                }
            }
        }

        private void DrawSilk(DrawingContext dc, BoardDocument board)
        {
            foreach (var comp in board.Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                bool bottom = comp.Side == CopperLayer.Bottom;
                if (bottom && HiddenLayers.Contains(CopperLayer.Bottom) || !bottom && HiddenLayers.Contains(CopperLayer.Top)) continue;
                var pen = P(A(Silk, bottom ? (byte)0x50 : (byte)0x90), Math.Max(1, board.Rules.SilkWidth * _scale));
                foreach (var s in RobotTwin.CoreSim.Pcb.Silk.FootprintStrokes(comp, fp)) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
                if (_scale > 3)
                {
                    var box = comp.Courtyard(fp);
                    double size = Math.Max(9, Math.Min(15, 1.1 * _scale));
                    var ft = Text(comp.RefDes, size, A(Silk, bottom ? (byte)0x70 : (byte)0xB0));
                    var at = S(box.MinX, box.MinY);
                    dc.DrawText(ft, new Point(at.X, at.Y - ft.Height));
                }
            }

            foreach (var t in board.Texts.Where(t => t.Layer == GraphicLayer.SilkTop || t.Layer == GraphicLayer.SilkBottom))
            {
                var pen = P(A(Silk, t.Layer == GraphicLayer.SilkBottom ? (byte)0x60 : (byte)0xC0), Math.Max(1, t.Thickness * _scale));
                foreach (var s in RobotTwin.CoreSim.Pcb.Silk.TextStrokes(t)) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
            }
        }

        private void DrawDrawings(DrawingContext dc, BoardDocument board)
        {
            var pen = P(A(Drawing, (byte)0xC0), 1);
            foreach (var d in board.Dimensions)
            {
                foreach (var s in RobotTwin.CoreSim.Pcb.Silk.DimensionStrokes(d, UseMil)) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
            }

            foreach (var t in board.Texts.Where(t => t.Layer != GraphicLayer.SilkTop && t.Layer != GraphicLayer.SilkBottom))
            {
                var p = P(A(Drawing, (byte)0xC0), Math.Max(1, t.Thickness * _scale));
                foreach (var s in RobotTwin.CoreSim.Pcb.Silk.TextStrokes(t)) dc.DrawLine(p, S(s.X1, s.Y1), S(s.X2, s.Y2));
            }
        }

        private void DrawRatsnest(DrawingContext dc, BoardDocument board)
        {
            var pen = P(A(Rats, (byte)0xB0), 1, true);
            // [local-rats] with the board's ratsnest off, KiCad's local ratsnest: the lines of the parts it was switched on for, and of the selected ones
            var own = ShowRatsnest ? null : new HashSet<BoardComponent>(board.Components.Where(c => LocalRatsnest.Contains(c.RefDes)).Concat(_sel.OfType<BoardComponent>()));
            if (own is { Count: 0 }) return;
            foreach (var (net, a, b) in board.Ratsnest())
            {
                if (HighlightNet != null && !string.Equals(net, HighlightNet, StringComparison.OrdinalIgnoreCase)) continue;
                if (own != null && !own.Contains(a.Component) && !own.Contains(b.Component)) continue;
                if (!CurvedRatsnest)
                {
                    dc.DrawLine(pen, S(a.X, a.Y), S(b.X, b.Y));
                    continue;
                }

                // [rats-curve] KiCad's curved ratsnest: a gentle bow (a tenth of its length) so lines on one row stay apart
                var p0 = S(a.X, a.Y);
                var p1 = S(b.X, b.Y);
                var mid = new Point((p0.X + p1.X) / 2 - (p1.Y - p0.Y) * 0.1, (p0.Y + p1.Y) / 2 + (p1.X - p0.X) * 0.1);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(p0, false, false);
                    ctx.QuadraticBezierTo(mid, p1, true, false);
                }

                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        }

        /// <summary>[crosshair] A crosshair across the whole board view through the pointer (KiCad's full-window cursor).</summary>
        public bool FullCrosshair { get; set; }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (FullCrosshair) InvalidateVisual();   // [crosshair] gone with the pointer
        }

        /// <summary>[local-rats] The parts (by reference) whose ratsnest is drawn while the board's is switched off.</summary>
        public HashSet<string> LocalRatsnest { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>[locks] KiCad's Override locks: locked parts move, turn and flip like the rest (the lock stays on them).</summary>
        public bool OverrideLocks { get; set; }

        /// <summary>Whether a part's lock holds it now.</summary>
        private bool Held(BoardComponent c) => c.Locked && !OverrideLocks;

        /// <summary>[rats-curve] Draws the ratsnest as gentle curves (KiCad's option): overlapping straight lines on one row are told apart.</summary>
        public bool CurvedRatsnest { get; set; }

        private void DrawSelection(DrawingContext dc, BoardDocument board)
        {
            foreach (var comp in new[] { _hover }.Concat(_sel.OfType<BoardComponent>()))
            {
                if (comp == null) continue;
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                var box = comp.Courtyard(fp);
                var r = new Rect(S(box.MinX, box.MinY), S(box.MaxX, box.MaxY));
                bool sel = _sel.Contains(comp);
                dc.DrawRoundedRectangle(null, P(A(Accent, sel ? (byte)0xD0 : (byte)0x60), 1, false), r, 3, 3);
            }

            var glow = A(Accent, (byte)0x90);
            foreach (var t in _sel.OfType<Track>()) dc.DrawLine(P(glow, t.Width * _scale + 6), S(t.X1, t.Y1), S(t.X2, t.Y2));
            foreach (var v in _sel.OfType<Via>())
            {
                dc.DrawEllipse(null, P(glow, 3), S(v.X, v.Y), v.Diameter / 2 * _scale + 3, v.Diameter / 2 * _scale + 3);
            }

            foreach (var item in _sel)
            {
                DrawSelected(dc, board, item);
            }

            DrawBox(dc);
        }

        private void DrawSelected(DrawingContext dc, BoardDocument board, object item)
        {
            switch (item)
            {
                case Zone z:
                {
                    var pts = z.IsWholeBoard ? board.OutlinePolygon(0.05) : z.Outline;
                    var pen = new Pen(SymbolRenderer.Brush(Accent), 1.6) { DashStyle = DashStyles.Dash };
                    pen.Freeze();
                    dc.DrawGeometry(null, pen, Poly(pts));
                    if (!z.IsWholeBoard)
                    {
                        foreach (var p in z.Outline) dc.DrawRectangle(SymbolRenderer.Brush(Accent), null, new Rect(S(p.X, p.Y) - new Vector(3, 3), new Size(6, 6)));
                    }

                    break;
                }

                case MountingHole h:
                    dc.DrawEllipse(null, P(Accent, 1.6), S(h.X, h.Y), Math.Max(h.Drill, h.PadDiameter) / 2 * _scale + 3, Math.Max(h.Drill, h.PadDiameter) / 2 * _scale + 3);
                    break;
                case BoardGraphic g:   // [graphics]
                    DrawSelectedGraphic(dc, g);
                    break;
                case RuleArea a:   // [keepout]
                    foreach (var p in a.Outline) dc.DrawRectangle(SymbolRenderer.Brush(Accent), null, new Rect(S(p.X, p.Y) - new Vector(3, 3), new Size(6, 6)));
                    dc.DrawGeometry(null, P(Accent, 2), Poly(a.Outline));
                    break;
                case BoardText t:
                {
                    var b = Bounds(RobotTwin.CoreSim.Pcb.Silk.TextStrokes(t));
                    dc.DrawRectangle(null, P(Accent, 1, false), new Rect(S(b.MinX - 0.3, b.MinY - 0.3), S(b.MaxX + 0.3, b.MaxY + 0.3)));
                    break;
                }

                case BoardDimension d:
                    foreach (var s in RobotTwin.CoreSim.Pcb.Silk.DimensionStrokes(d, UseMil)) dc.DrawLine(P(A(Accent, (byte)0xC0), 2), S(s.X1, s.Y1), S(s.X2, s.Y2));
                    break;
            }

            if (item is OutlineSelection && board.Outline.Count >= 3)
            {
                foreach (var p in board.Outline) dc.DrawRectangle(SymbolRenderer.Brush(Accent), null, new Rect(S(p.X, p.Y) - new Vector(3.5, 3.5), new Size(7, 7)));
            }
        }

        private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IEnumerable<Segment2> segs)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var s in segs)
            {
                minX = Math.Min(minX, Math.Min(s.X1, s.X2));
                minY = Math.Min(minY, Math.Min(s.Y1, s.Y2));
                maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
                maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
            }

            return double.IsInfinity(minX) ? (0, 0, 0, 0) : (minX, minY, maxX, maxY);
        }

        /// <summary>[select-filter] Which kinds of items can be picked (KiCad's Selection Filter).</summary>
        public PcbSelectionFilter SelectionFilter { get; } = new PcbSelectionFilter();

        /// <summary>Marker object selected when the board outline is picked.</summary>
        public sealed class OutlineSelection
        {
        }

        private static readonly OutlineSelection TheOutline = new OutlineSelection();

        // ================================================================ hit testing

        private BoardComponent? HitComponent(double x, double y)
        {
            if (Board == null) return null;
            foreach (var comp in Board.Components.AsEnumerable().Reverse())
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                if (comp.Side == CopperLayer.Bottom && HiddenLayers.Contains(CopperLayer.Bottom) || comp.Side == CopperLayer.Top && HiddenLayers.Contains(CopperLayer.Top)) continue;
                var b = comp.Courtyard(fp);
                if (x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY) return comp;
            }

            return null;
        }

        private Track? HitTrack(double x, double y)
        {
            if (Board == null) return null;
            Track? best = null;
            double bestD = double.PositiveInfinity;
            foreach (var t in Board.Tracks)
            {
                if (!Visible(t.Layer)) continue;
                double d = Geometry.PointSegmentDistance(x, y, t.X1, t.Y1, t.X2, t.Y2);
                double tol = Math.Max(t.Width / 2, 4 / _scale);
                if (d > tol) continue;
                if (t.Layer == _activeLayer) d -= 1000;   // prefer the active layer
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }

            return best;
        }

        private Via? HitVia(double x, double y) =>
            Board?.Vias.FirstOrDefault(v => Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y)) <= Math.Max(v.Diameter / 2, 4 / _scale));

        private PlacedPad? HitPad(double x, double y)
        {
            if (Board == null) return null;
            PlacedPad? best = null;
            foreach (var p in Board.Pads())
            {
                if (!p.Pad.IsThroughHole && !Visible(p.SmdLayer)) continue;
                if (Math.Abs(x - p.X) <= p.Width / 2 + 0.1 && Math.Abs(y - p.Y) <= p.Height / 2 + 0.1)
                {
                    if (best == null || p.Pad.IsThroughHole || p.SmdLayer == _activeLayer) best = p;
                }
            }

            return best;
        }

        private MountingHole? HitHole(double x, double y) =>
            Board?.MountingHoles.FirstOrDefault(h => Math.Sqrt((h.X - x) * (h.X - x) + (h.Y - y) * (h.Y - y)) <= Math.Max(h.Drill, h.PadDiameter) / 2 + 0.2);

        private BoardText? HitText(double x, double y)
        {
            if (Board == null) return null;
            foreach (var t in Board.Texts.AsEnumerable().Reverse())
            {
                var b = Bounds(RobotTwin.CoreSim.Pcb.Silk.TextStrokes(t));
                if (x >= b.MinX - 0.3 && x <= b.MaxX + 0.3 && y >= b.MinY - 0.3 && y <= b.MaxY + 0.3) return t;
            }

            return null;
        }

        private BoardDimension? HitDimension(double x, double y)
        {
            if (Board == null || !ShowDrawings) return null;
            foreach (var d in Board.Dimensions)
            {
                var geo = d.Geometry();
                if (geo.Lines.Any(l => Geometry.PointSegmentDistance(x, y, l.X1, l.Y1, l.X2, l.Y2) <= 5 / _scale)) return d;   // [dims] any of its lines
                if (Math.Abs(x - geo.TextX) < 2 && Math.Abs(y - geo.TextY) < d.TextSize) return d;
            }

            return null;
        }

        private Zone? HitZoneEdge(double x, double y)
        {
            if (Board == null) return null;
            double tol = 5 / _scale;
            foreach (var z in Board.Zones.OrderByDescending(z => z.Layer == _activeLayer))
            {
                if (!Visible(z.Layer)) continue;
                var pts = z.IsWholeBoard ? null : z.Outline;
                if (pts == null) continue;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    if (Geometry.PointSegmentDistance(x, y, a.X, a.Y, b.X, b.Y) <= tol) return z;
                }
            }

            return null;
        }

        private bool HitOutlineEdge(double x, double y)
        {
            if (Board == null) return false;
            var poly = Board.OutlinePolygon(0.05);
            double tol = 5 / _scale;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                if (Geometry.PointSegmentDistance(x, y, a.X, a.Y, b.X, b.Y) <= tol) return true;
            }

            return false;
        }

        /// <summary>Net of the copper under the point (pad, via, track, zone fill), for highlighting.</summary>
        public string? NetAt(double x, double y)
        {
            if (Board == null) return null;
            var pad = HitPad(x, y);
            if (pad != null && pad.Value.Net.Length > 0) return pad.Value.Net;
            var via = HitVia(x, y);
            if (via != null) return via.Net;
            var t = HitTrack(x, y);
            if (t != null) return t.Net;
            foreach (var z in Board.Zones.Where(z => z.Layer == _activeLayer && z.IsFilled))
            {
                if (Polygon.AnyContains(z.FilledRegions, x, y)) return z.Net;
            }

            return null;
        }

        // ================================================================ interactive routing

        /// <summary>A finished manual route.</summary>
        public sealed class RouteResult
        {
            /// <summary>Pad the route started on (null when it started on a track or via).</summary>
            public PlacedPad? StartPad { get; set; }

            /// <summary>Pad the route ended on (null for a free end or an end on copper of the net).</summary>
            public PlacedPad? EndPad { get; set; }

            /// <summary>Net of the start copper (empty for an unconnected pad).</summary>
            public string Net { get; set; } = string.Empty;

            public double Width { get; set; }
            public List<(double X1, double Y1, double X2, double Y2, CopperLayer Layer)> Segments { get; } = new List<(double, double, double, double, CopperLayer)>();
            public List<(double X, double Y, CopperLayer From, CopperLayer To)> Vias { get; } = new List<(double, double, CopperLayer, CopperLayer)>();
        }

        public event EventHandler<RouteResult>? RouteFinished;

        private readonly List<(PcbPoint P, CopperLayer Layer)> _routePts = new List<(PcbPoint, CopperLayer)>();
        private readonly List<(double X, double Y, CopperLayer From, CopperLayer To)> _routeVias = new List<(double, double, CopperLayer, CopperLayer)>();
        private List<PcbPoint> _preview = new List<PcbPoint>();
        private bool _previewReached;
        private InteractiveRouter? _router;
        private PlacedPad? _routeStartPad;
        private string _routeNet = string.Empty;
        private CopperLayer _routeLayer = CopperLayer.Top;
        private bool _diagonalFirst = true;
        private bool _only90;

        /// <summary>Enters or leaves the routing tool (X key or the tool strip).</summary>
        public void ToggleRouting() => SetTool(_tool == PcbTool.Route ? PcbTool.Select : PcbTool.Route);

        private (PcbPoint P, PlacedPad? Pad, Track? Track, Via? Via) RouteTarget(double x, double y)
        {
            var pad = HitPad(x, y);
            if (pad != null) return (new PcbPoint(pad.Value.X, pad.Value.Y), pad, null, null);
            var via = HitVia(x, y);
            if (via != null) return (new PcbPoint(via.X, via.Y), null, null, via);
            return (new PcbPoint(SnapX(x), SnapY(y)), null, null, null);
        }

        private void RouteClick(double x, double y, bool doubleClick)
        {
            var board = Board!;
            if (_routePts.Count == 0)
            {
                // Start on a pad, a via or a track.
                var pad = HitPad(x, y);
                var via = pad == null ? HitVia(x, y) : null;
                var track = pad == null && via == null ? HitTrack(x, y) : null;
                PcbPoint start;
                CopperLayer layer = _activeLayer;
                if (pad != null)
                {
                    start = new PcbPoint(pad.Value.X, pad.Value.Y);
                    _routeNet = pad.Value.Net;
                    _routeStartPad = pad;
                    if (!pad.Value.OnLayer(layer)) layer = pad.Value.SmdLayer;
                }
                else if (via != null)
                {
                    start = new PcbPoint(via.X, via.Y);
                    _routeNet = via.Net;
                    if (!via.Spans(layer, board.LayerCount)) layer = via.Span(board.LayerCount).From;
                }
                else if (track != null)
                {
                    // Nearest point on the segment, snapped to an end when close.
                    double dx = track.X2 - track.X1, dy = track.Y2 - track.Y1, len2 = dx * dx + dy * dy;
                    double tt = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - track.X1) * dx + (y - track.Y1) * dy) / len2)) : 0;
                    start = new PcbPoint(track.X1 + dx * tt, track.Y1 + dy * tt);
                    if (Math.Abs(start.X - track.X1) + Math.Abs(start.Y - track.Y1) < 0.3) start = new PcbPoint(track.X1, track.Y1);
                    else if (Math.Abs(start.X - track.X2) + Math.Abs(start.Y - track.Y2) < 0.3) start = new PcbPoint(track.X2, track.Y2);
                    _routeNet = track.Net;
                    layer = track.Layer;
                }
                else
                {
                    if (StartFromRatsnest(x, y)) return;   // clicking a ratsnest line routes that connection
                    Say("Yol çizmeye başlamak için bir pede, yola, viaya veya bir ratsnest çizgisine tıklayın.");
                    return;
                }

                _routeLayer = layer;
                if (layer != _activeLayer) ActiveLayer = layer;
                _router = new InteractiveRouter(board, _routeNet, RouteWidth);   // [flyout] the options bar's width, or the net class's
                _routePts.Add((start, layer));
                _preview.Clear();
                Say($"{(_routeNet.Length > 0 ? _routeNet : "yeni hat")} · {_router.Width:0.###} mm ({board.ClassNameFor(_routeNet)}) · {PcbLayers.DisplayName(layer)}");
                InvalidateVisual();
                return;
            }

            UpdateRoutePreview();
            var target = RouteTarget(x, y);
            bool reached = _previewReached;
            CommitPreview();
            if (!reached)
            {
                Say("Hedefe kuralları bozmadan ulaşılamıyor; yol engelin önünde durdu. Başka bir noktaya tıklayın veya via ile katman değiştirin.");
                InvalidateVisual();
                return;
            }

            // Finish on a pad, or on copper of the same net, or by double click.
            bool sameNet(string? n) => _routeNet.Length > 0 && string.Equals(n, _routeNet, StringComparison.OrdinalIgnoreCase);
            var endTrack = target.Pad == null && target.Via == null ? HitTrack(x, y) : null;
            bool startPad = _routeStartPad != null && target.Pad != null && target.Pad.Value.Component == _routeStartPad.Value.Component && target.Pad.Value.Pad == _routeStartPad.Value.Pad && _routePts.Count <= 2;
            if (target.Pad != null && !startPad && (target.Pad.Value.OnLayer(_routeLayer) || true))
            {
                if (!target.Pad.Value.OnLayer(_routeLayer) && !target.Pad.Value.Pad.IsThroughHole)
                {
                    // SMD pad on the other side: a via first.
                    if (!AddVia(target.Pad.Value.SmdLayer)) return;
                }

                FinishRoute(target.Pad);
                return;
            }

            if (target.Via != null && sameNet(target.Via.Net) || endTrack != null && sameNet(endTrack.Net) || doubleClick)
            {
                if (_routeNet.Length == 0)
                {
                    Say("Hattı olmayan bir pedden başlayan yol bir pedde bitmeli.");
                    return;
                }

                FinishRoute(null);
                return;
            }

            InvalidateVisual();
        }

        private void UpdateRoutePreview()
        {
            if (_routePts.Count == 0 || _router == null || Board == null)
            {
                _preview.Clear();
                return;
            }

            var last = _routePts[_routePts.Count - 1].P;
            var target = RouteTarget(_cursor.X, _cursor.Y).P;
            _preview = _router.Walkaround(_routeLayer, last, target, _diagonalFirst, _only90);
            var end = _preview[_preview.Count - 1];
            _previewReached = Math.Abs(end.X - target.X) < 1e-6 && Math.Abs(end.Y - target.Y) < 1e-6;
            RouteStatusChanged?.Invoke(this, EventArgs.Empty);   // [routebar]
        }

        private void CommitPreview()
        {
            for (int i = 1; i < _preview.Count; i++) _routePts.Add((_preview[i], _routeLayer));
            _preview.Clear();
        }

        private bool AddVia(CopperLayer newLayer)
        {
            var board = Board!;
            if (newLayer == _routeLayer || _router == null) return false;
            var at = _routePts[_routePts.Count - 1].P;
            bool blind = board.Stackup.AllowBlindBuriedVias && board.LayerCount >= 4;
            var from = blind ? _routeLayer : CopperLayer.Top;
            var to = blind ? newLayer : CopperLayer.Bottom;
            if (!_router.ViaClear(at.X, at.Y, from, to))
            {
                Say("Via buraya sığmıyor (aralık veya delik kuralı). Biraz ilerleyip tekrar deneyin.");
                return false;
            }

            _routeVias.Add((at.X, at.Y, from, to));
            _routeLayer = newLayer;
            _routePts.Add((at, newLayer));
            ActiveLayer = newLayer;
            UpdateRoutePreview();
            Say($"Via ({(blind ? "kör/gömülü" : "geçişli")}) · {PcbLayers.DisplayName(newLayer)}");
            InvalidateVisual();
            return true;
        }

        private bool RouteKey(Key key, bool shift)
        {
            if (_routePts.Count == 0) return false;
            var board = Board!;
            switch (key)
            {
                case Key.V:
                {
                    UpdateRoutePreview();
                    if (_previewReached) CommitPreview();
                    var target = _activeLayer != _routeLayer ? _activeLayer : PairedWith(_routeLayer);   // [layer-pair]
                    AddVia(target);
                    return true;
                }

                case Key.PageUp:
                case Key.PageDown:
                {
                    UpdateRoutePreview();
                    if (_previewReached) CommitPreview();
                    var stack = board.CopperLayers().ToList();
                    int i = stack.IndexOf(_routeLayer) + (key == Key.PageDown ? 1 : -1);
                    if (i >= 0 && i < stack.Count) AddVia(stack[i]);
                    return true;
                }

                case Key.F:
                    FinishToTarget();
                    return true;
                case Key.W:
                    CycleTrackWidth(shift ? -1 : 1);
                    return true;
                case Key.Space:
                    _only90 = !_only90;
                    UpdateRoutePreview();
                    Say(_only90 ? "90° mod: yalnızca yatay/dikey" : "45° mod");
                    InvalidateVisual();
                    return true;
                case Key.Back:
                    if (_routePts.Count > 1)
                    {
                        var removed = _routePts[_routePts.Count - 1];
                        _routePts.RemoveAt(_routePts.Count - 1);
                        if (_routeVias.Count > 0 && removed.Layer != _routePts[_routePts.Count - 1].Layer)
                        {
                            _routeVias.RemoveAt(_routeVias.Count - 1);
                            _routeLayer = _routePts[_routePts.Count - 1].Layer;
                            ActiveLayer = _routeLayer;
                        }
                    }
                    else
                    {
                        CancelInteractions();
                    }

                    UpdateRoutePreview();
                    InvalidateVisual();
                    return true;
                case Key.Enter:
                    UpdateRoutePreview();
                    if (_previewReached) CommitPreview();
                    if (_routeNet.Length == 0)
                    {
                        Say("Hattı olmayan bir pedden başlayan yol bir pedde bitmeli.");
                        return true;
                    }

                    FinishRoute(null);
                    return true;
                case Key.Escape:
                    CancelInteractions();
                    Say(ToolHint());
                    return true;
            }

            return false;
        }

        private void FinishRoute(PlacedPad? endPad)
        {
            var result = new RouteResult
            {
                StartPad = _routeStartPad,
                EndPad = endPad,
                Net = _routeNet,
                Width = _router?.Width ?? Board!.TrackWidthFor(_routeNet)
            };
            for (int i = 1; i < _routePts.Count; i++)
            {
                var a = _routePts[i - 1];
                var b = _routePts[i];
                if (a.Layer != b.Layer) continue;   // via
                if (Math.Abs(a.P.X - b.P.X) < 1e-9 && Math.Abs(a.P.Y - b.P.Y) < 1e-9) continue;
                result.Segments.Add((a.P.X, a.P.Y, b.P.X, b.P.Y, b.Layer));
            }

            result.Vias.AddRange(_routeVias);
            CancelInteractions();
            if (result.Segments.Count == 0 && result.Vias.Count == 0) return;
            RouteFinished?.Invoke(this, result);
            InvalidateVisual();
        }

        private void DrawRoutePreview(DrawingContext dc, BoardDocument board)
        {
            if (_tool != PcbTool.Route) return;
            var cross = P(A(Accent, (byte)0xA0), 1, false);
            var c = S(_cursor.X, _cursor.Y);
            dc.DrawLine(cross, new Point(c.X - 8, c.Y), new Point(c.X + 8, c.Y));
            dc.DrawLine(cross, new Point(c.X, c.Y - 8), new Point(c.X, c.Y + 8));
            if (_routePts.Count == 0 || _router == null) return;

            double width = Math.Max(1, _router.Width * _scale);
            for (int i = 1; i < _routePts.Count; i++)
            {
                var a = _routePts[i - 1];
                var b = _routePts[i];
                if (a.Layer != b.Layer) continue;
                dc.DrawLine(P(LayerColor(b.Layer), width), S(a.P.X, a.P.Y), S(b.P.X, b.P.Y));
            }

            var ghost = P(A(LayerColor(_routeLayer), (byte)0xA0), width);
            for (int i = 1; i < _preview.Count; i++) dc.DrawLine(ghost, S(_preview[i - 1].X, _preview[i - 1].Y), S(_preview[i].X, _preview[i].Y));
            if (_preview.Count > 0)
            {
                // Clearance outline around the head of the route.
                var head = _preview[_preview.Count - 1];
                double halo = (_router.Width / 2 + _router.Class.Clearance) * _scale;
                var pen = new Pen(SymbolRenderer.Brush(A(_previewReached ? Accent : Danger, (byte)0x90)), 1) { DashStyle = DashStyles.Dot };
                pen.Freeze();
                dc.DrawEllipse(null, pen, S(head.X, head.Y), halo, halo);
                if (!_previewReached)
                {
                    var target = RouteTarget(_cursor.X, _cursor.Y).P;
                    var tp = S(target.X, target.Y);
                    var red = P(Danger, 2);
                    dc.DrawLine(red, new Point(tp.X - 5, tp.Y - 5), new Point(tp.X + 5, tp.Y + 5));
                    dc.DrawLine(red, new Point(tp.X - 5, tp.Y + 5), new Point(tp.X + 5, tp.Y - 5));
                }
            }

            var cls = _router.Class;
            foreach (var v in _routeVias)
            {
                dc.DrawEllipse(SymbolRenderer.Brush(PadGold), null, S(v.X, v.Y), cls.ViaDiameter / 2 * _scale, cls.ViaDiameter / 2 * _scale);
                dc.DrawEllipse(SymbolRenderer.Brush(Back), null, S(v.X, v.Y), cls.ViaDrill / 2 * _scale, cls.ViaDrill / 2 * _scale);
            }

            string mode = (_only90 ? "90°" : "45°") + (_diagonalFirst ? " · önce çapraz" : " · önce düz");
            var (routeLen, netLen) = RouteLengths();
            var label = Text($"{PcbLayers.DisplayName(_routeLayer)} · {(_routeNet.Length > 0 ? _routeNet : "yeni hat")} · {Length(_router.Width)} · {mode}\nbu yol {Length(routeLen)}" + (netLen > 0 ? $" · hattın çizili toplamı {Length(netLen)}" : string.Empty) + " · F: hedefe bitir", 12, Silk);
            dc.DrawText(label, new Point(c.X + 14, c.Y + 10));
            DrawRouteTargets(dc);
        }

        /// <summary>For snapshots: start a route at (x0, y0) and show the walkaround preview towards (x1, y1).</summary>
        public void SimulateRouteForSnapshot(double x0, double y0, double x1, double y1)
        {
            RouteClick(x0, y0, false);
            _cursor = (x1, y1);
            UpdateRoutePreview();
            InvalidateVisual();
        }

        /// <summary>For snapshots: a click of the route tool (finishes on a pad).</summary>
        public void RouteClickForSnapshot(double x, double y) => RouteClick(x, y, false);

        /// <summary>For snapshots: a measurement from (x0, y0) to the cursor at (x1, y1).</summary>
        public void SimulateMeasureForSnapshot(double x0, double y0, double x1, double y1)
        {
            MeasureClick(x0, y0);
            _cursor = (x1, y1);
            InvalidateVisual();
        }

        // ================================================================ polygons: zones and board outline

        private readonly List<PcbPoint> _polyPts = new List<PcbPoint>();

        private void PolygonClick(double x, double y, bool doubleClick)
        {
            var p = Magnet(x, y);   // [magnet] a zone's or the edge's corner on a pad, a track or a drawing
            if (_polyPts.Count > 0) p = Angle45(_polyPts[^1], p);   // [angle45]
            if (_tool == PcbTool.Outline) p = new PcbPoint(Math.Max(0, p.X), Math.Max(0, p.Y));
            if (doubleClick || _polyPts.Count >= 3 && Math.Abs(_polyPts[0].X - p.X) * _scale < 6 && Math.Abs(_polyPts[0].Y - p.Y) * _scale < 6)
            {
                ClosePolygon();
                return;
            }

            if (_polyPts.Count == 0 || !_polyPts[_polyPts.Count - 1].Equals(p)) _polyPts.Add(p);
            InvalidateVisual();
        }

        private bool PolygonKey(Key key)
        {
            switch (key)
            {
                case Key.Enter:
                    ClosePolygon();
                    return true;
                case Key.Back:
                    if (_polyPts.Count > 0) _polyPts.RemoveAt(_polyPts.Count - 1);
                    InvalidateVisual();
                    return true;
                case Key.Escape when _polyPts.Count > 0:
                    _polyPts.Clear();
                    InvalidateVisual();
                    return true;
            }

            return false;
        }

        private void ClosePolygon()
        {
            if (_polyPts.Count < 3)
            {
                Say("En az üç köşe gerekli.");
                return;
            }

            var pts = _polyPts.ToList();
            _polyPts.Clear();
            if (Math.Abs(Polygon.Area(pts)) < 1e-3)
            {
                Say("Alan sıfır; çokgen iptal edildi.");
                return;
            }

            if (_tool == PcbTool.Zone) ZoneDrawn?.Invoke(this, pts);
            else if (_tool == PcbTool.Keepout) KeepoutDrawn?.Invoke(this, pts);   // [keepout]
            else OutlineDrawn?.Invoke(this, pts);
            InvalidateVisual();
        }

        private void DrawPolygonPreview(DrawingContext dc)
        {
            if (_tool != PcbTool.Zone && _tool != PcbTool.Outline && _tool != PcbTool.Keepout || _polyPts.Count == 0) return;
            var color = _tool == PcbTool.Zone ? LayerColor(_activeLayer) : _tool == PcbTool.Keepout ? Danger : SubstrateEdge;
            var pen = P(color, 1.6);
            var cur = Angle45(_polyPts[^1], Magnet(_cursor.X, _cursor.Y));   // [magnet] [angle45]
            var pts = _polyPts.Concat(new[] { cur }).ToList();
            if (pts.Count >= 3) dc.DrawGeometry(SymbolRenderer.Brush(A(color, (byte)0x30)), null, Poly(pts));
            for (int i = 1; i < pts.Count; i++) dc.DrawLine(pen, S(pts[i - 1].X, pts[i - 1].Y), S(pts[i].X, pts[i].Y));
            var dash = new Pen(SymbolRenderer.Brush(A(color, (byte)0x80)), 1) { DashStyle = DashStyles.Dash };
            dash.Freeze();
            dc.DrawLine(dash, S(cur.X, cur.Y), S(pts[0].X, pts[0].Y));
            foreach (var p in _polyPts) dc.DrawRectangle(SymbolRenderer.Brush(color), null, new Rect(S(p.X, p.Y) - new Vector(3, 3), new Size(6, 6)));
        }

        // ================================================================ measure and dimensions

        private PcbPoint? _measureA, _measureB;
        private PcbPoint? _dimA, _dimB;

        private void MeasureClick(double x, double y)
        {
            var p = Magnet(x, y);
            if (_measureA == null || _measureB != null)
            {
                _measureA = p;
                _measureB = null;
            }
            else
            {
                _measureB = p;
                double dx = p.X - _measureA.Value.X, dy = p.Y - _measureA.Value.Y;
                Say($"Ölçüm: {Length(Math.Sqrt(dx * dx + dy * dy))} · Δx {Length(Math.Abs(dx))} · Δy {Length(Math.Abs(dy))} · açı {Math.Atan2(-dy, dx) * 180 / Math.PI:0.#}°");
            }

            InvalidateVisual();
        }

        private void DrawMeasure(DrawingContext dc)
        {
            if (_tool != PcbTool.Measure || _measureA == null) return;
            var a = _measureA.Value;
            var b = _measureB ?? Magnet(_cursor.X, _cursor.Y);
            double dx = b.X - a.X, dy = b.Y - a.Y, d = Math.Sqrt(dx * dx + dy * dy);
            var pen = P(Accent, 1.4);
            var dash = new Pen(SymbolRenderer.Brush(A(Accent, (byte)0x70)), 1) { DashStyle = DashStyles.Dash };
            dash.Freeze();
            dc.DrawLine(dash, S(a.X, a.Y), S(b.X, a.Y));
            dc.DrawLine(dash, S(b.X, a.Y), S(b.X, b.Y));
            dc.DrawLine(pen, S(a.X, a.Y), S(b.X, b.Y));
            foreach (var p in new[] { a, b }) dc.DrawEllipse(null, pen, S(p.X, p.Y), 4, 4);
            string text = $"{Length(d)}\nΔx {Length(Math.Abs(dx))}  Δy {Length(Math.Abs(dy))}\n∠ {Math.Atan2(-dy, dx) * 180 / Math.PI:0.#}°";
            var ft = Text(text, 12, Silk, true);
            var at = S((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var box = new Rect(at.X + 10, at.Y + 6, ft.Width + 12, ft.Height + 8);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(A(Color.FromRgb(0x20, 0x20, 0x23), (byte)0xE8)), P(A(Accent, (byte)0x60), 1, false), box, 4, 4);
            dc.DrawText(ft, new Point(box.X + 6, box.Y + 4));
        }

        /// <summary>[dims] The kind the dimension tool places (KiCad's aligned, orthogonal, radial, leader, centre mark).</summary>
        public DimensionKind DimKind { get; set; } = DimensionKind.Aligned;

        private void DimensionClick(double x, double y)
        {
            var p = Magnet(x, y);
            if (_dimA == null)
            {
                _dimA = p;
            }
            else if (_dimB == null)
            {
                if (Math.Abs(p.X - _dimA.Value.X) + Math.Abs(p.Y - _dimA.Value.Y) < 1e-6) return;
                _dimB = p;
                if (DimKind is DimensionKind.Leader or DimensionKind.Center) PlaceDimension();   // two clicks are all they need
                else InvalidateVisual();
                return;
            }
            else
            {
                PlaceDimension();
                return;
            }

            InvalidateVisual();
        }

        private void PlaceDimension()
        {
            var dim = PreviewDimension()!;
            if (dim.Kind == DimensionKind.Leader)
            {
                // KiCad asks for the leader's note as it is placed
                string? note = Window.GetWindow(this) is { } owner ? RobotWin.Studio.Shell.Dialog.Prompt(owner, "Kılavuz", "Kılavuzun notu:", "Not") : "Not";
                if (note == null)
                {
                    _dimA = _dimB = null;
                    InvalidateVisual();
                    return;
                }

                dim.Text = note;
            }

            BeforeEdit?.Invoke(this, EventArgs.Empty);
            Board!.Dimensions.Add(dim);
            _dimA = _dimB = null;
            Edited(dim.Kind switch
            {
                DimensionKind.Leader => $"Kılavuz eklendi: {dim.Label(UseMil)}",
                DimensionKind.Center => "Merkez işareti eklendi",
                _ => $"Ölçü eklendi: {dim.Label(UseMil)}"
            });
        }

        private BoardDimension? PreviewDimension()
        {
            if (_dimA == null) return null;
            var a = _dimA.Value;
            var b = _dimB ?? Magnet(_cursor.X, _cursor.Y);
            var dim = new BoardDimension { Kind = DimKind, X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Offset = DimKind == DimensionKind.Radial ? 3 : 0 };
            if (_dimB == null || dim.Span < 1e-9) return dim;
            switch (DimKind)
            {
                case DimensionKind.Orthogonal:
                {
                    // KiCad: dragged above or below it measures across, to the side it measures up and down
                    double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                    dim.Vertical = Math.Abs(_cursor.X - mx) > Math.Abs(_cursor.Y - my);
                    dim.Offset = Math.Round((dim.Vertical ? _cursor.X - a.X : _cursor.Y - a.Y) * 4) / 4;
                    break;
                }

                case DimensionKind.Radial:
                {
                    var (ux, uy) = dim.OffsetDirection();
                    dim.Offset = Math.Max(1, Math.Round(((_cursor.X - b.X) * ux + (_cursor.Y - b.Y) * uy) * 4) / 4);
                    break;
                }

                case DimensionKind.Aligned:
                {
                    var (nx, ny) = dim.OffsetDirection();
                    dim.Offset = Math.Round(((_cursor.X - a.X) * nx + (_cursor.Y - a.Y) * ny) * 4) / 4;
                    break;
                }
            }

            return dim;
        }

        private void DrawDimensionPreview(DrawingContext dc)
        {
            if (_tool != PcbTool.Dimension) return;
            var dim = PreviewDimension();
            if (dim == null || dim.Span < 1e-9) return;
            var pen = P(A(Accent, (byte)0xC0), 1);
            foreach (var s in RobotTwin.CoreSim.Pcb.Silk.DimensionStrokes(dim, UseMil)) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
        }
    }
}
