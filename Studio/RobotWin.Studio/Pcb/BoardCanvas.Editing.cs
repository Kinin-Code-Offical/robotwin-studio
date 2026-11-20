using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;
using Geometry = RobotTwin.CoreSim.Pcb.Geometry;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// Editing interaction of the PCB canvas (Pcbnew-like): click / Shift+click / box
    /// selection (left-to-right = inside, right-to-left = touching), mouse drag, M move,
    /// G drag with track ends, D drag track segment, arrow nudge, copy / cut / paste /
    /// duplicate, right-click context menus with shortcuts, and the keyboard map.
    /// Every modification is one undo step (BeforeEdit … BoardEdited).
    /// </summary>
    public sealed partial class BoardCanvas
    {
        // ================================================================ events used by the page

        public event EventHandler? RefillRequested;
        public event EventHandler? UnfillRequested;
        public event EventHandler? HelpRequested;
        public event EventHandler<string>? ShowInSchematicRequested;
        public event EventHandler<BoardComponent>? ChangeFootprintRequested;
        public event EventHandler<BoardComponent>? EditFootprintRequested;

        /// <summary>Deletes parts (they live in the schematic); returns false when the page refused.</summary>
        public event EventHandler<List<BoardComponent>>? DeleteComponentsRequested;

        /// <summary>Restores the state before the current edit (cancelled paste of parts).</summary>
        public event EventHandler? RevertRequested;

        /// <summary>Creates copies of parts (new schematic parts) and returns their board components.</summary>
        public Func<IReadOnlyList<BoardComponent>, IReadOnlyList<BoardComponent>>? DuplicateComponents { get; set; }

        /// <summary>Track widths offered by W / Shift+W and the context menu.</summary>
        public IReadOnlyList<double> TrackWidthSteps()
        {
            var b = Board;
            var set = new SortedSet<double> { 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.8, 1.0, 1.5, 2.0 };
            if (b != null)
            {
                foreach (var c in b.NetClasses) set.Add(Math.Round(c.TrackWidth, 4));
                set.RemoveWhere(w => w < b.Rules.MinTrackWidth - 1e-9);
            }

            return set.ToList();
        }

        // ================================================================ mouse

        private enum DragKind
        {
            None,
            Group,
            Segment,
            ZoneVertex,
            OutlineVertex,
            Dimension,
            Box,
            RightPan
        }

        private DragKind _drag;
        private bool _dragStarted;
        private Point _dragStartScreen;
        private (double X, double Y) _dragStartBoard;
        private int _vertexIndex;
        private double _dimOffsetStart;
        private (double X, double Y) _boxEnd;
        private bool _boxAdd;
        private List<(Track T, double X1, double Y1, double X2, double Y2)>? _trackSnapshot;
        private List<Track> _dragAdded = new List<Track>();

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Zoom(e.GetPosition(this), e.Delta > 0 ? 1.18 : 1 / 1.18);
            e.Handled = true;
        }

        private void Zoom(Point at, double factor)
        {
            var b = ToBoard(at);
            _scale = Math.Max(1, Math.Min(160, _scale * factor));
            _offset = new Vector(at.X - b.X * _scale, at.Y - b.Y * _scale);
            InvalidateVisual();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            CaptureMouse();
            if (e.ChangedButton == MouseButton.Middle)
            {
                _panning = true;
                _panStartPoint = p;
                _panStart = _offset;
                return;
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                // Pan when dragged, context menu when released in place.
                _drag = DragKind.RightPan;
                _panStartPoint = p;
                _panStart = _offset;
                _dragStartScreen = p;
                _dragStarted = false;
                return;
            }

            if (e.ChangedButton != MouseButton.Left || Board == null) return;
            var (x, y) = ToBoard(p);
            if (_moving)
            {
                CommitMove();
                return;
            }

            if (_keyboardSegmentDrag)
            {
                _keyboardSegmentDrag = false;
                EndDrag();
                _drag = DragKind.None;
                _dragStarted = false;
                return;
            }

            switch (_tool)
            {
                case PcbTool.Route:
                    RouteClick(x, y, e.ClickCount >= 2);
                    return;
                case PcbTool.Zone:
                case PcbTool.Outline:
                    PolygonClick(x, y, e.ClickCount >= 2);
                    return;
                case PcbTool.Measure:
                    MeasureClick(x, y);
                    return;
                case PcbTool.Dimension:
                    DimensionClick(x, y);
                    return;
                case PcbTool.Text:
                    TextRequested?.Invoke(this, new PcbPoint(Snap(x), Snap(y)));
                    return;
                case PcbTool.Hole:
                    BeforeEdit?.Invoke(this, EventArgs.Empty);
                    Board.MountingHoles.Add(new MountingHole { X = Snap(x), Y = Snap(y), Drill = HoleDrill, PadDiameter = HolePad });
                    Edited($"Montaj deliği eklendi (Ø{HoleDrill:0.##} mm)");
                    return;
            }

            if (e.ClickCount >= 2)
            {
                object? target = HitAny(x, y);
                if (target != null)
                {
                    Select(target);
                    EditRequested?.Invoke(this, target);
                    return;
                }
            }

            SelectAt(x, y, p, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        }

        /// <summary>The item under a point, smallest first (vias, tracks, texts, holes, parts, zone edges, dimensions, outline).</summary>
        private object? HitAny(double x, double y) =>
            HitVia(x, y) as object ?? HitTrack(x, y) as object ?? HitText(x, y) as object ?? HitHole(x, y) as object ??
            HitComponent(x, y) as object ?? HitZoneEdge(x, y) as object ?? HitDimension(x, y) as object ??
            (HitOutlineEdge(x, y) ? TheOutline : null);

        private void SelectAt(double x, double y, Point screen, bool add)
        {
            var board = Board!;
            _drag = DragKind.None;
            _dragStarted = false;
            _dragStartScreen = screen;
            _dragStartBoard = (x, y);

            // Vertex handles of the selected zone / outline first.
            if (_selected is Zone sz && !sz.IsWholeBoard && !add)
            {
                int k = NearVertex(sz.Outline.Select(v => (v.X, v.Y)).ToList(), x, y);
                if (k >= 0)
                {
                    _drag = DragKind.ZoneVertex;
                    _vertexIndex = k;
                    return;
                }
            }

            if (_selected is OutlineSelection && board.Outline.Count >= 3 && !add)
            {
                int k = NearVertex(board.Outline.Select(v => (v.X, v.Y)).ToList(), x, y);
                if (k >= 0)
                {
                    _drag = DragKind.OutlineVertex;
                    _vertexIndex = k;
                    return;
                }
            }

            var hit = HitAny(x, y);
            if (hit == null)
            {
                // Rubber-band selection.
                if (!add) _sel.Clear();
                _drag = DragKind.Box;
                _boxAdd = add;
                _boxEnd = (x, y);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                return;
            }

            if (add)
            {
                if (_sel.Contains(hit)) _sel.Remove(hit);
                else _sel.Add(hit);
            }
            else if (!_sel.Contains(hit))
            {
                _selected = hit;
            }
            else if (_sel.Count > 1)
            {
                // Clicking an item of a multi-selection keeps it (to drag the group) and makes it primary.
                _sel.Remove(hit);
                _sel.Insert(0, hit);
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            if (!add)
            {
                if (_sel.Count == 1 && hit is Track) _drag = DragKind.Segment;
                else if (_sel.Count == 1 && hit is BoardDimension d)
                {
                    _drag = DragKind.Dimension;
                    _dimOffsetStart = d.Offset;
                }
                else if (hit is not OutlineSelection && hit is not Zone) _drag = DragKind.Group;
            }

            InvalidateVisual();
        }

        private int NearVertex(List<(double X, double Y)> pts, double x, double y)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                if (Math.Abs(pts[i].X - x) * _scale <= 6 && Math.Abs(pts[i].Y - y) * _scale <= 6) return i;
            }

            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_panning)
            {
                _offset = _panStart + (p - _panStartPoint);
                InvalidateVisual();
                return;
            }

            if (_drag == DragKind.RightPan && e.RightButton == MouseButtonState.Pressed)
            {
                if (_dragStarted || (p - _dragStartScreen).Length > 4)
                {
                    _dragStarted = true;
                    _offset = _panStart + (p - _panStartPoint);
                    InvalidateVisual();
                }

                return;
            }

            var (x, y) = ToBoard(p);
            _cursor = (x, y);
            CursorMoved?.Invoke(this, EventArgs.Empty);

            if (_moving)
            {
                ApplyMove(Snap(x) - _moveOrigin.X, Snap(y) - _moveOrigin.Y);
                InvalidateVisual();
                return;
            }

            if (_drag != DragKind.None && (e.LeftButton == MouseButtonState.Pressed || _keyboardSegmentDrag) && Board != null)
            {
                if (_drag == DragKind.Box)
                {
                    _boxEnd = (x, y);
                    InvalidateVisual();
                    return;
                }

                if (!_dragStarted && (p - _dragStartScreen).Length < 3) return;
                if (!_dragStarted)
                {
                    _dragStarted = true;
                    BeginDrag();
                }

                Drag(x, y);
                InvalidateVisual();
                return;
            }

            switch (_tool)
            {
                case PcbTool.Route:
                    UpdateRoutePreview();
                    InvalidateVisual();
                    break;
                case PcbTool.Zone:
                case PcbTool.Outline:
                case PcbTool.Measure:
                case PcbTool.Dimension:
                    InvalidateVisual();
                    break;
            }

            var hover = _tool == PcbTool.Select ? HitComponent(x, y) : null;
            if (!ReferenceEquals(hover, _hover))
            {
                _hover = hover;
                ToolTip = hover == null ? null : $"{hover.RefDes} · {hover.Value} · {hover.FootprintId}";
                InvalidateVisual();
            }
        }

        private void BeginDrag()
        {
            var board = Board!;
            switch (_drag)
            {
                case DragKind.Group:
                    BeginMove(MoveKind.Drag, false, Snap(_dragStartBoard.X), Snap(_dragStartBoard.Y));
                    return;
                default:
                    BeforeEdit?.Invoke(this, EventArgs.Empty);
                    _trackSnapshot = board.Tracks.Select(t => (t, t.X1, t.Y1, t.X2, t.Y2)).ToList();
                    _dragAdded = new List<Track>();
                    return;
            }
        }

        private void RestoreTracks()
        {
            if (_trackSnapshot == null || Board == null) return;
            foreach (var t in _dragAdded) Board.Tracks.Remove(t);
            _dragAdded.Clear();
            foreach (var (t, x1, y1, x2, y2) in _trackSnapshot)
            {
                t.X1 = x1;
                t.Y1 = y1;
                t.X2 = x2;
                t.Y2 = y2;
            }
        }

        private void Drag(double x, double y)
        {
            var board = Board!;
            double dx = x - _dragStartBoard.X, dy = y - _dragStartBoard.Y;
            switch (_drag)
            {
                case DragKind.Group:
                    ApplyMove(Snap(x) - _moveOrigin.X, Snap(y) - _moveOrigin.Y);
                    break;
                case DragKind.Segment when _selected is Track track:
                    RestoreTracks();
                    _dragAdded = TrackEditing.DragSegment(board, track, Snap(dx), Snap(dy));
                    break;
                case DragKind.Dimension when _selected is BoardDimension dim:
                {
                    double len = Math.Max(1e-9, dim.Length);
                    double nx = -(dim.Y2 - dim.Y1) / len, ny = (dim.X2 - dim.X1) / len;
                    dim.Offset = Math.Round((_dimOffsetStart + dx * nx + dy * ny) * 4) / 4;
                    break;
                }

                case DragKind.ZoneVertex when _selected is Zone zone:
                    zone.Outline[_vertexIndex] = new PcbPoint(Snap(x), Snap(y));
                    _zoneGeo.Remove(zone);
                    break;
                case DragKind.OutlineVertex:
                    board.Outline[_vertexIndex].X = Math.Max(0, Snap(x));
                    board.Outline[_vertexIndex].Y = Math.Max(0, Snap(y));
                    break;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            ReleaseMouseCapture();
            _panning = false;
            if (e.ChangedButton == MouseButton.Right && _drag == DragKind.RightPan)
            {
                bool panned = _dragStarted;
                _drag = DragKind.None;
                _dragStarted = false;
                if (!panned) RightClick(ToBoard(e.GetPosition(this)));
                return;
            }

            if (_drag == DragKind.Box)
            {
                FinishBox();
            }
            else if (_drag != DragKind.None && _dragStarted && Board != null)
            {
                EndDrag();
            }

            _drag = DragKind.None;
            _dragStarted = false;
        }

        private void RightClick((double X, double Y) at)
        {
            if (_moving)
            {
                CancelMove();
                return;
            }

            if (_tool != PcbTool.Select && _tool != PcbTool.Route && (_polyPts.Count > 0 || _dimA != null || _measureA != null))
            {
                CancelInteractions();
                Say(ToolHint());
                return;
            }

            ShowContextMenu(at.X, at.Y);
        }

        private void EndDrag()
        {
            var board = Board!;
            switch (_drag)
            {
                case DragKind.Group:
                    CommitMove();
                    return;
                case DragKind.Segment when _selected is Track track:
                {
                    var changed = ChangedTracks();
                    if (changed.Count == 0)
                    {
                        EditCancelled?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    var router = new InteractiveRouter(board, track.Net, track.Width);
                    if (changed.Any(t => !router.SegmentClear(t.Layer, t.X1, t.Y1, t.X2, t.Y2)))
                    {
                        RestoreTracks();
                        EditCancelled?.Invoke(this, EventArgs.Empty);
                        Say("Sürükleme geri alındı: yeni konum aralık kuralını ihlal ediyor.");
                        InvalidateVisual();
                        return;
                    }

                    Edited($"{track.Net} yolu sürüklendi");
                    return;
                }

                case DragKind.Dimension:
                    Edited("Ölçü çizgisi kaydırıldı");
                    return;
                case DragKind.ZoneVertex when _selected is Zone z:
                    Edited($"{z.Net} dolgu sınırı değişti");
                    return;
                case DragKind.OutlineVertex:
                    board.FitSizeToOutline();
                    Edited("Kart kenarı değişti");
                    return;
            }

            EditCancelled?.Invoke(this, EventArgs.Empty);
        }

        private List<Track> ChangedTracks()
        {
            var list = new List<Track>(_dragAdded);
            if (_trackSnapshot == null) return list;
            foreach (var (t, x1, y1, x2, y2) in _trackSnapshot)
            {
                if (Board!.Tracks.Contains(t) && (t.X1 != x1 || t.Y1 != y1 || t.X2 != x2 || t.Y2 != y2)) list.Add(t);
            }

            return list;
        }

        // ================================================================ box selection

        private void DrawBox(DrawingContext dc)
        {
            if (_drag != DragKind.Box) return;
            var a = S(_dragStartBoard.X, _dragStartBoard.Y);
            var b = S(_boxEnd.X, _boxEnd.Y);
            bool window = _boxEnd.X >= _dragStartBoard.X;
            var pen = new Pen(SymbolRenderer.Brush(Accent), 1) { DashStyle = window ? DashStyles.Solid : DashStyles.Dash };
            pen.Freeze();
            dc.DrawRectangle(SymbolRenderer.Brush(Color.FromArgb(0x18, Accent.R, Accent.G, Accent.B)), pen, new Rect(a, b));
        }

        private void FinishBox()
        {
            var board = Board;
            if (board == null) return;
            double x0 = Math.Min(_dragStartBoard.X, _boxEnd.X), x1 = Math.Max(_dragStartBoard.X, _boxEnd.X);
            double y0 = Math.Min(_dragStartBoard.Y, _boxEnd.Y), y1 = Math.Max(_dragStartBoard.Y, _boxEnd.Y);
            if ((x1 - x0) * _scale < 3 && (y1 - y0) * _scale < 3)
            {
                InvalidateVisual();
                return;
            }

            bool window = _boxEnd.X >= _dragStartBoard.X;   // left→right: fully inside; right→left: touching
            bool In(double x, double y) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
            bool BoxHit(double a0, double b0, double a1, double b1) =>
                window ? a0 >= x0 && a1 <= x1 && b0 >= y0 && b1 <= y1 : a1 >= x0 && a0 <= x1 && b1 >= y0 && b0 <= y1;
            double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hx = (x1 - x0) / 2, hy = (y1 - y0) / 2;
            var found = new List<object>();
            foreach (var c in board.Components)
            {
                var fp = FootprintLibrary.Get(c.FootprintId);
                if (fp == null || HiddenLayers.Contains(c.Side)) continue;
                var b = c.Courtyard(fp);
                if (BoxHit(b.MinX, b.MinY, b.MaxX, b.MaxY)) found.Add(c);
            }

            foreach (var t in board.Tracks)
            {
                if (!Visible(t.Layer)) continue;
                if (window ? In(t.X1, t.Y1) && In(t.X2, t.Y2) : Geometry.SegmentRectDistance(t.X1, t.Y1, t.X2, t.Y2, cx, cy, hx, hy) <= t.Width / 2) found.Add(t);
            }

            foreach (var v in board.Vias)
            {
                if (In(v.X, v.Y)) found.Add(v);
            }

            foreach (var h in board.MountingHoles)
            {
                if (In(h.X, h.Y)) found.Add(h);
            }

            foreach (var t in board.Texts)
            {
                var b = Bounds(RobotTwin.CoreSim.Pcb.Silk.TextStrokes(t));
                if (BoxHit(b.MinX, b.MinY, b.MaxX, b.MaxY)) found.Add(t);
            }

            foreach (var d in board.Dimensions)
            {
                if (window ? In(d.X1, d.Y1) && In(d.X2, d.Y2) : In(d.X1, d.Y1) || In(d.X2, d.Y2)) found.Add(d);
            }

            foreach (var z in board.Zones)
            {
                if (z.IsWholeBoard || !Visible(z.Layer)) continue;
                if (window ? z.Outline.All(q => In(q.X, q.Y)) : z.Outline.Any(q => In(q.X, q.Y))) found.Add(z);
            }

            if (!_boxAdd) _sel.Clear();
            foreach (var f in found)
            {
                if (!_sel.Contains(f)) _sel.Add(f);
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Say(_sel.Count == 0 ? "Seçim boş" : $"{_sel.Count} öğe seçildi · M: taşı · G: yollarla sürükle · R: döndür · Del: sil · Ctrl+C/D: kopyala/çoğalt");
            InvalidateVisual();
        }

        public void SelectAll()
        {
            var board = Board;
            if (board == null) return;
            var all = new List<object>();
            all.AddRange(board.Components);
            all.AddRange(board.Tracks.Where(t => Visible(t.Layer)));
            all.AddRange(board.Vias);
            all.AddRange(board.Texts);
            all.AddRange(board.MountingHoles);
            all.AddRange(board.Dimensions);
            all.AddRange(board.Zones.Where(z => !z.IsWholeBoard));
            SetSelection(all);
            Say($"{all.Count} öğe seçildi");
        }

        // ================================================================ move (M), drag (G), paste

        private enum MoveKind
        {
            /// <summary>Everything selected translates; tracks of moved parts stay (KiCad M).</summary>
            Move,

            /// <summary>Track ends sitting on moved pads and vias follow (KiCad G).</summary>
            Drag
        }

        private bool _moving;
        private MoveKind _moveKind;
        private bool _movePaste;
        private bool _movePasteParts;
        private (double X, double Y) _moveOrigin;
        private readonly List<(BoardComponent C, double X, double Y, int Rotation)> _moveComps = new List<(BoardComponent, double, double, int)>();
        private readonly List<(Via V, double X, double Y)> _moveVias = new List<(Via, double, double)>();
        private readonly List<(BoardText T, double X, double Y)> _moveTexts = new List<(BoardText, double, double)>();
        private readonly List<(MountingHole H, double X, double Y)> _moveHoles = new List<(MountingHole, double, double)>();
        private readonly List<(BoardDimension D, double X1, double Y1, double X2, double Y2)> _moveDims = new List<(BoardDimension, double, double, double, double)>();
        private readonly List<(Zone Z, List<PcbPoint> Outline)> _moveZones = new List<(Zone, List<PcbPoint>)>();
        private readonly List<(Track T, bool Start, BoardComponent C, Pad Pad)> _attachedPads = new List<(Track, bool, BoardComponent, Pad)>();
        private readonly List<(Track T, bool Start, Via V)> _attachedVias = new List<(Track, bool, Via)>();
        private readonly HashSet<Track> _moveTracks = new HashSet<Track>();

        public bool Moving => _moving;

        /// <summary>M from the panel: move the selection with the mouse.</summary>
        public void BeginMoveSelection()
        {
            Focus();
            BeginMove(MoveKind.Move, false, Snap(_cursor.X), Snap(_cursor.Y));
        }

        /// <summary>Starts moving the selection with the mouse (click drops it, Esc / right click restores it).</summary>
        private bool BeginMove(MoveKind kind, bool paste, double originX, double originY, bool raiseBeforeEdit = true)
        {
            var board = Board;
            if (board == null) return false;
            var locked = _sel.OfType<BoardComponent>().Where(c => c.Locked).ToList();
            if (locked.Count > 0) _sel.RemoveAll(o => o is BoardComponent c && c.Locked);
            if (_sel.Count == 0 || _sel.All(o => o is OutlineSelection))
            {
                if (locked.Count > 0) Say($"{string.Join(", ", locked.Select(c => c.RefDes))} kilitli (L ile açın)");
                return false;
            }

            if (raiseBeforeEdit && !paste) BeforeEdit?.Invoke(this, EventArgs.Empty);
            _moving = true;
            _moveKind = kind;
            _movePaste = paste;
            _moveOrigin = (originX, originY);
            _trackSnapshot = board.Tracks.Select(t => (t, t.X1, t.Y1, t.X2, t.Y2)).ToList();
            _dragAdded = new List<Track>();
            _moveComps.Clear();
            _moveVias.Clear();
            _moveTexts.Clear();
            _moveHoles.Clear();
            _moveDims.Clear();
            _moveZones.Clear();
            _attachedPads.Clear();
            _attachedVias.Clear();
            _moveTracks.Clear();
            foreach (var o in _sel)
            {
                switch (o)
                {
                    case BoardComponent c: _moveComps.Add((c, c.X, c.Y, c.Rotation)); break;
                    case Via v: _moveVias.Add((v, v.X, v.Y)); break;
                    case BoardText t: _moveTexts.Add((t, t.X, t.Y)); break;
                    case MountingHole h: _moveHoles.Add((h, h.X, h.Y)); break;
                    case BoardDimension d: _moveDims.Add((d, d.X1, d.Y1, d.X2, d.Y2)); break;
                    case Zone z when !z.IsWholeBoard: _moveZones.Add((z, z.Outline.ToList())); break;
                    case Track t: _moveTracks.Add(t); break;
                }
            }

            if (kind == MoveKind.Drag)
            {
                foreach (var (c, _, _, _) in _moveComps)
                {
                    foreach (var pad in board.Pads().Where(pp => ReferenceEquals(pp.Component, c)))
                    {
                        foreach (var t in board.Tracks)
                        {
                            if (_moveTracks.Contains(t) || !string.Equals(t.Net, pad.Net, StringComparison.OrdinalIgnoreCase) || !pad.OnLayer(t.Layer)) continue;
                            if (Math.Abs(t.X1 - pad.X) < 1e-4 && Math.Abs(t.Y1 - pad.Y) < 1e-4) _attachedPads.Add((t, true, c, pad.Pad));
                            if (Math.Abs(t.X2 - pad.X) < 1e-4 && Math.Abs(t.Y2 - pad.Y) < 1e-4) _attachedPads.Add((t, false, c, pad.Pad));
                        }
                    }
                }

                foreach (var (v, _, _) in _moveVias)
                {
                    foreach (var t in board.Tracks)
                    {
                        if (_moveTracks.Contains(t) || !string.Equals(t.Net, v.Net, StringComparison.OrdinalIgnoreCase) || !v.Spans(t.Layer, board.LayerCount)) continue;
                        if (Math.Abs(t.X1 - v.X) < 1e-4 && Math.Abs(t.Y1 - v.Y) < 1e-4) _attachedVias.Add((t, true, v));
                        if (Math.Abs(t.X2 - v.X) < 1e-4 && Math.Abs(t.Y2 - v.Y) < 1e-4) _attachedVias.Add((t, false, v));
                    }
                }
            }

            Say(paste ? "Yapıştır: konuma tıklayın · Esc / sağ tık: vazgeç" : $"{(kind == MoveKind.Drag ? "Sürükle (yollar uçlarıyla gelir)" : "Taşı")}: bırakmak için tıklayın · R: döndür · Esc / sağ tık: vazgeç");
            return true;
        }

        private void ApplyMove(double dx, double dy)
        {
            if (!_moving || Board == null) return;
            RestoreTracks();
            foreach (var (c, x, y, _) in _moveComps)
            {
                c.X = Math.Round(x + dx, 6);
                c.Y = Math.Round(y + dy, 6);
            }

            foreach (var (v, x, y) in _moveVias)
            {
                v.X = x + dx;
                v.Y = y + dy;
            }

            foreach (var (t, x, y) in _moveTexts)
            {
                t.X = x + dx;
                t.Y = y + dy;
            }

            foreach (var (h, x, y) in _moveHoles)
            {
                h.X = x + dx;
                h.Y = y + dy;
            }

            foreach (var (d, x1, y1, x2, y2) in _moveDims)
            {
                d.X1 = x1 + dx;
                d.Y1 = y1 + dy;
                d.X2 = x2 + dx;
                d.Y2 = y2 + dy;
            }

            foreach (var (z, outline) in _moveZones)
            {
                z.Outline = outline.Select(q => new PcbPoint(q.X + dx, q.Y + dy)).ToList();
                _zoneGeo.Remove(z);
            }

            foreach (var t in _moveTracks)
            {
                t.X1 += dx;
                t.Y1 += dy;
                t.X2 += dx;
                t.Y2 += dy;
            }

            foreach (var (t, start, c, pad) in _attachedPads)
            {
                var at = c.ToBoard(pad.X, pad.Y);
                if (start)
                {
                    t.X1 = at.X;
                    t.Y1 = at.Y;
                }
                else
                {
                    t.X2 = at.X;
                    t.Y2 = at.Y;
                }
            }

            foreach (var (t, start, v) in _attachedVias)
            {
                if (start)
                {
                    t.X1 = v.X;
                    t.Y1 = v.Y;
                }
                else
                {
                    t.X2 = v.X;
                    t.Y2 = v.Y;
                }
            }
        }

        private void CommitMove()
        {
            if (!_moving) return;
            _moving = false;
            bool moved = _moveComps.Any(c => c.C.X != c.X || c.C.Y != c.Y || c.C.Rotation != c.Rotation) || _moveVias.Any(v => v.V.X != v.X || v.V.Y != v.Y) ||
                         _moveTexts.Any(t => t.T.X != t.X || t.T.Y != t.Y) || _moveHoles.Any(h => h.H.X != h.X || h.H.Y != h.Y) ||
                         _moveDims.Any(d => d.D.X1 != d.X1 || d.D.Y1 != d.Y1) || _moveZones.Any(z => !z.Z.Outline.SequenceEqual(z.Outline)) ||
                         ChangedTracks().Count > 0;
            if (!moved && !_movePaste)
            {
                EditCancelled?.Invoke(this, EventArgs.Empty);
                Say(ToolHint());
                return;
            }

            int n = _sel.Count;
            Edited(_movePaste ? $"{n} öğe yapıştırıldı" : _sel.Count == 1 && _selected is BoardComponent c ? $"{c.RefDes} taşındı ({Length(c.X)}, {Length(c.Y)})" : $"{n} öğe taşındı");
            Say(ToolHint());
        }

        private void CancelMove()
        {
            if (!_moving) return;
            ApplyMove(0, 0);
            foreach (var (c, _, _, rot) in _moveComps) c.Rotation = rot;
            _moving = false;
            if (_movePaste)
            {
                if (_movePasteParts)
                {
                    RevertRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    var b = Board!;
                    foreach (var o in _sel)
                    {
                        switch (o)
                        {
                            case Track t: b.Tracks.Remove(t); break;
                            case Via v: b.Vias.Remove(v); break;
                            case BoardText x: b.Texts.Remove(x); break;
                            case MountingHole h: b.MountingHoles.Remove(h); break;
                            case BoardDimension d: b.Dimensions.Remove(d); break;
                            case Zone z: b.Zones.Remove(z); break;
                        }
                    }

                    _sel.Clear();
                    EditCancelled?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                EditCancelled?.Invoke(this, EventArgs.Empty);
            }

            _movePasteParts = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Say("İptal edildi");
            InvalidateVisual();
        }

        /// <summary>D: drags the selected (or hovered) track segment with the mouse, neighbours keep their angle.</summary>
        private void BeginSegmentDrag()
        {
            var track = _selected as Track ?? HitTrack(_cursor.X, _cursor.Y);
            if (track == null)
            {
                Say("D: sürüklemek için bir yol parçası seçin veya üstüne gelin");
                return;
            }

            _selected = track;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            _drag = DragKind.Segment;
            _dragStartBoard = _cursor;
            _dragStarted = true;
            BeginDrag();
            _keyboardSegmentDrag = true;
            Say("Yol parçasını sürükleyin, tıklayınca bırakılır · Esc: vazgeç");
        }

        private bool _keyboardSegmentDrag;

        // ================================================================ clipboard

        private static readonly List<object> Clipboard = new List<object>();
        private static (double X, double Y) _clipOrigin;

        public bool CanPaste => Clipboard.Count > 0;

        /// <summary>Copies the selection (tracks, vias, zones, texts, holes, dimensions and parts).</summary>
        public void CopySelection()
        {
            if (_sel.Count == 0)
            {
                Say("Kopyalanacak seçim yok");
                return;
            }

            Clipboard.Clear();
            foreach (var o in _sel)
            {
                switch (o)
                {
                    case Track t: Clipboard.Add(new Track { Net = t.Net, Layer = t.Layer, X1 = t.X1, Y1 = t.Y1, X2 = t.X2, Y2 = t.Y2, Width = t.Width }); break;
                    case Via v: Clipboard.Add(new Via { Net = v.Net, X = v.X, Y = v.Y, Diameter = v.Diameter, Drill = v.Drill, StartLayer = v.StartLayer, EndLayer = v.EndLayer }); break;
                    case BoardText x: Clipboard.Add(new BoardText { Text = x.Text, X = x.X, Y = x.Y, Size = x.Size, Thickness = x.Thickness, Layer = x.Layer, Rotation = x.Rotation }); break;
                    case MountingHole h: Clipboard.Add(new MountingHole { X = h.X, Y = h.Y, Drill = h.Drill, PadDiameter = h.PadDiameter, Net = h.Net }); break;
                    case BoardDimension d: Clipboard.Add(new BoardDimension { X1 = d.X1, Y1 = d.Y1, X2 = d.X2, Y2 = d.Y2, Offset = d.Offset, TextSize = d.TextSize, Layer = d.Layer }); break;
                    case Zone z when !z.IsWholeBoard: Clipboard.Add(z.CloneSettings()); break;
                    case BoardComponent c: Clipboard.Add(c); break;
                }
            }

            _clipOrigin = (Snap(_cursor.X), Snap(_cursor.Y));
            Say($"{Clipboard.Count} öğe kopyalandı · Ctrl+V: yapıştır");
        }

        /// <summary>Pastes the clipboard attached to the cursor (parts become new schematic parts).</summary>
        public void Paste()
        {
            var board = Board;
            if (board == null || Clipboard.Count == 0) return;
            CancelInteractions();
            SetTool(PcbTool.Select);
            BeforeEdit?.Invoke(this, EventArgs.Empty);
            var pasted = new List<object>();
            foreach (var o in Clipboard)
            {
                switch (o)
                {
                    case Track t:
                    {
                        var c = new Track { Net = t.Net, Layer = board.HasLayer(t.Layer) ? t.Layer : _activeLayer, X1 = t.X1, Y1 = t.Y1, X2 = t.X2, Y2 = t.Y2, Width = t.Width };
                        board.Tracks.Add(c);
                        pasted.Add(c);
                        break;
                    }

                    case Via v:
                    {
                        var c = new Via { Net = v.Net, X = v.X, Y = v.Y, Diameter = v.Diameter, Drill = v.Drill, StartLayer = v.StartLayer, EndLayer = v.EndLayer };
                        board.Vias.Add(c);
                        pasted.Add(c);
                        break;
                    }

                    case BoardText x:
                    {
                        var c = new BoardText { Text = x.Text, X = x.X, Y = x.Y, Size = x.Size, Thickness = x.Thickness, Layer = x.Layer, Rotation = x.Rotation };
                        board.Texts.Add(c);
                        pasted.Add(c);
                        break;
                    }

                    case MountingHole h:
                    {
                        var c = new MountingHole { X = h.X, Y = h.Y, Drill = h.Drill, PadDiameter = h.PadDiameter, Net = h.Net };
                        board.MountingHoles.Add(c);
                        pasted.Add(c);
                        break;
                    }

                    case BoardDimension d:
                    {
                        var c = new BoardDimension { X1 = d.X1, Y1 = d.Y1, X2 = d.X2, Y2 = d.Y2, Offset = d.Offset, TextSize = d.TextSize, Layer = d.Layer };
                        board.Dimensions.Add(c);
                        pasted.Add(c);
                        break;
                    }

                    case Zone z:
                    {
                        var c = z.CloneSettings();
                        if (!board.HasLayer(c.Layer)) c.Layer = _activeLayer;
                        board.Zones.Add(c);
                        pasted.Add(c);
                        break;
                    }
                }
            }

            var parts = Clipboard.OfType<BoardComponent>().Where(c => board.Components.Contains(c)).ToList();
            _movePasteParts = false;
            if (parts.Count > 0 && DuplicateComponents != null)
            {
                var copies = DuplicateComponents(parts);
                for (int i = 0; i < copies.Count && i < parts.Count; i++)
                {
                    copies[i].X = parts[i].X;
                    copies[i].Y = parts[i].Y;
                    copies[i].Rotation = parts[i].Rotation;
                    copies[i].Side = parts[i].Side;
                    pasted.Add(copies[i]);
                }

                _movePasteParts = copies.Count > 0;
            }

            _sel.Clear();
            _sel.AddRange(pasted);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            if (pasted.Count == 0 || !BeginMove(MoveKind.Move, true, _clipOrigin.X, _clipOrigin.Y))
            {
                EditCancelled?.Invoke(this, EventArgs.Empty);
                return;
            }

            ApplyMove(Snap(_cursor.X) - _moveOrigin.X, Snap(_cursor.Y) - _moveOrigin.Y);
            _ratsCache = null;
            InvalidateVisual();
        }

        public void Duplicate()
        {
            CopySelection();
            if (Clipboard.Count > 0) Paste();
        }

        public void CutSelection()
        {
            CopySelection();
            DeleteSelection();
        }

        // ================================================================ keyboard

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            base.OnTextInput(e);
            if (Board == null || string.IsNullOrEmpty(e.Text)) return;
            switch (e.Text)
            {
                case "?":
                    HelpRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                case "`":
                    HighlightUnderCursor();
                    e.Handled = true;
                    return;
                case "/" when _tool == PcbTool.Route:
                    TogglePosture();
                    e.Handled = true;
                    return;
                case "+":
                    Zoom(S(_cursor.X, _cursor.Y), 1.25);
                    e.Handled = true;
                    return;
                case "-":
                    Zoom(S(_cursor.X, _cursor.Y), 1 / 1.25);
                    e.Handled = true;
                    return;
            }
        }

        private void HighlightUnderCursor()
        {
            string? net = NetAt(_cursor.X, _cursor.Y) ?? (_selected as Track)?.Net ?? (_selected as Via)?.Net;
            SetHighlight(net != null && string.Equals(net, HighlightNet, StringComparison.OrdinalIgnoreCase) ? null : net);
            Say(HighlightNet == null ? "Vurgulama kapatıldı" : $"{HighlightNet} hattı vurgulanıyor (` : kapat)");
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Board == null) return;
            var board = Board;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            e.Handled = true;

            if (e.Key == Key.F1)
            {
                HelpRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_moving)
            {
                switch (e.Key)
                {
                    case Key.Escape:
                        CancelMove();
                        return;
                    case Key.Enter:
                        CommitMove();
                        return;
                    case Key.R:
                        foreach (var (c, _, _, _) in _moveComps) c.Rotation = (c.Rotation + (shift ? 270 : 90)) % 360;
                        ApplyMove(Snap(_cursor.X) - _moveOrigin.X, Snap(_cursor.Y) - _moveOrigin.Y);
                        InvalidateVisual();
                        return;
                }

                return;
            }

            if (_keyboardSegmentDrag && e.Key == Key.Escape)
            {
                RestoreTracks();
                _keyboardSegmentDrag = false;
                _drag = DragKind.None;
                _dragStarted = false;
                EditCancelled?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                return;
            }

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.Z:
                        CancelInteractions();
                        UndoRequested?.Invoke(this, EventArgs.Empty);
                        return;
                    case Key.Y:
                        CancelInteractions();
                        RedoRequested?.Invoke(this, EventArgs.Empty);
                        return;
                    case Key.A:
                        SelectAll();
                        return;
                    case Key.C:
                        CopySelection();
                        return;
                    case Key.X:
                        CutSelection();
                        return;
                    case Key.V:
                        Paste();
                        return;
                    case Key.D:
                        Duplicate();
                        return;
                    case Key.B:
                        UnfillRequested?.Invoke(this, EventArgs.Empty);
                        return;
                    case Key.M when shift:
                        SetTool(PcbTool.Measure);
                        return;
                    case Key.H when shift:
                        SetTool(PcbTool.Dimension);
                        return;
                }

                e.Handled = false;
                return;
            }

            if (_tool == PcbTool.Route && RouteKey(e.Key, shift)) return;
            if ((_tool == PcbTool.Zone || _tool == PcbTool.Outline) && PolygonKey(e.Key)) return;

            switch (e.Key)
            {
                case Key.Escape:
                    if (_routePts.Count > 0 || _polyPts.Count > 0 || _measureA != null || _dimA != null) CancelInteractions();
                    else if (_tool != PcbTool.Select) SetTool(PcbTool.Select);
                    else if (HighlightNet != null) SetHighlight(null);
                    else Select(null);
                    return;
                case Key.X:
                    SetTool(_tool == PcbTool.Route ? PcbTool.Select : PcbTool.Route);
                    return;
                case Key.Z:
                    SetTool(PcbTool.Zone);
                    return;
                case Key.T:
                    SetTool(PcbTool.Text);
                    return;
                case Key.Home:
                    Fit();
                    return;
                case Key.H:
                {
                    // Show only the active copper layer / show all again.
                    var others = board.CopperLayers().Where(l => l != _activeLayer).ToList();
                    bool hide = others.Any(l => !HiddenLayers.Contains(l));
                    foreach (var l in others)
                    {
                        if (hide) HiddenLayers.Add(l);
                        else HiddenLayers.Remove(l);
                    }

                    Say(hide ? $"Yalnızca {PcbLayers.DisplayName(_activeLayer)} görünür (H: hepsini göster)" : "Tüm katmanlar görünür");
                    ActiveLayerChanged?.Invoke(this, EventArgs.Empty);
                    InvalidateVisual();
                    return;
                }

                case Key.PageUp:
                case Key.PageDown:
                {
                    var stack = board.CopperLayers();
                    int i = stack.ToList().IndexOf(_activeLayer);
                    i = Math.Max(0, Math.Min(stack.Count - 1, i + (e.Key == Key.PageDown ? 1 : -1)));
                    ActiveLayer = stack[i];
                    Say("Etkin katman: " + PcbLayers.DisplayName(_activeLayer));
                    return;
                }

                case Key.B:
                    RefillRequested?.Invoke(this, EventArgs.Empty);
                    return;
            }

            if (_tool != PcbTool.Select)
            {
                e.Handled = false;
                return;
            }

            switch (e.Key)
            {
                case Key.R:
                    RotateSelection(shift);
                    return;
                case Key.F:
                    if (_sel.OfType<BoardComponent>().Any()) FlipSelection();
                    else Fit();
                    return;
                case Key.L:
                    ToggleLock();
                    return;
                case Key.M:
                    if (_sel.Count == 0 && HitAny(_cursor.X, _cursor.Y) is { } m) Select(m);
                    BeginMove(MoveKind.Move, false, Snap(_cursor.X), Snap(_cursor.Y));
                    return;
                case Key.G:
                    if (_sel.Count == 0 && HitAny(_cursor.X, _cursor.Y) is { } g) Select(g);
                    if (_sel.Count == 1 && _selected is Track) BeginSegmentDrag();
                    else BeginMove(MoveKind.Drag, false, Snap(_cursor.X), Snap(_cursor.Y));
                    return;
                case Key.D:
                    BeginSegmentDrag();
                    return;
                case Key.U:
                    SelectWholeTrack();
                    return;
                case Key.W:
                    CycleTrackWidth(shift ? -1 : 1);
                    return;
                case Key.V:
                    ActiveLayer = PcbLayers.IsOuter(_activeLayer) ? PcbLayers.Opposite(_activeLayer) : CopperLayer.Top;
                    Say("Etkin katman: " + PcbLayers.DisplayName(_activeLayer));
                    return;
                case Key.E:
                    if (_selected != null) EditRequested?.Invoke(this, _selected);
                    return;
                case Key.Delete:
                case Key.Back:
                    DeleteSelection();
                    return;
                case Key.Left:
                case Key.Right:
                case Key.Up:
                case Key.Down:
                    Nudge(e.Key, shift);
                    return;
            }

            e.Handled = false;
        }

        private void Nudge(Key key, bool big)
        {
            if (_sel.Count == 0) return;
            double step = Math.Max(GridSize, 0.01) * (big ? 10 : 1);
            double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
            double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
            if (!BeginMove(MoveKind.Drag, false, 0, 0)) return;
            ApplyMove(dx, dy);
            CommitMove();
        }

        // ================================================================ selection operations

        public void RotateSelection(bool counterClockwise = false)
        {
            var parts = _sel.OfType<BoardComponent>().Where(c => !c.Locked).ToList();
            var texts = _sel.OfType<BoardText>().ToList();
            if (parts.Count == 0 && texts.Count == 0) return;
            TransformComponents(parts, () =>
            {
                foreach (var c in parts) c.Rotation = (c.Rotation + (counterClockwise ? 270 : 90)) % 360;
                foreach (var t in texts) t.Rotation = (t.Rotation + (counterClockwise ? 270 : 90)) % 360;
            }, parts.Count == 1 && texts.Count == 0 ? $"{parts[0].RefDes} döndürüldü" : $"{parts.Count + texts.Count} öğe döndürüldü", false);
        }

        public void FlipSelection()
        {
            var parts = _sel.OfType<BoardComponent>().Where(c => !c.Locked).ToList();
            if (parts.Count == 0) return;
            TransformComponents(parts, () =>
            {
                foreach (var c in parts) c.Side = c.Side == CopperLayer.Top ? CopperLayer.Bottom : CopperLayer.Top;
            }, parts.Count == 1 ? $"{parts[0].RefDes} {(parts[0].Side == CopperLayer.Top ? "alt" : "üst")} yüze alındı" : $"{parts.Count} parça ters çevrildi", true);
        }

        public void ToggleLock()
        {
            var parts = _sel.OfType<BoardComponent>().ToList();
            if (parts.Count == 0) return;
            BeforeEdit?.Invoke(this, EventArgs.Empty);
            bool lockIt = parts.Any(c => !c.Locked);
            foreach (var c in parts) c.Locked = lockIt;
            Edited(lockIt ? $"{string.Join(", ", parts.Select(c => c.RefDes))} kilitlendi" : $"{string.Join(", ", parts.Select(c => c.RefDes))} kilidi açıldı");
        }

        /// <summary>Rotate / flip parts in place: track ends on their pads follow (rotation) or are removed (flip).</summary>
        private void TransformComponents(List<BoardComponent> parts, Action change, string message, bool flip)
        {
            var board = Board!;
            BeforeEdit?.Invoke(this, EventArgs.Empty);
            var ends = new List<(Track T, bool Start, BoardComponent C, Pad Pad)>();
            foreach (var pad in board.Pads().Where(p => parts.Contains(p.Component)))
            {
                foreach (var t in board.Tracks)
                {
                    if (!string.Equals(t.Net, pad.Net, StringComparison.OrdinalIgnoreCase) || !pad.OnLayer(t.Layer)) continue;
                    if (Math.Abs(t.X1 - pad.X) < 1e-4 && Math.Abs(t.Y1 - pad.Y) < 1e-4) ends.Add((t, true, pad.Component, pad.Pad));
                    if (Math.Abs(t.X2 - pad.X) < 1e-4 && Math.Abs(t.Y2 - pad.Y) < 1e-4) ends.Add((t, false, pad.Component, pad.Pad));
                }
            }

            change();
            if (flip)
            {
                // Segments attached to a flipped part would be on the wrong side: remove them.
                foreach (var (t, _, _, _) in ends) board.Tracks.Remove(t);
            }
            else
            {
                foreach (var (t, start, c, pad) in ends)
                {
                    var at = c.ToBoard(pad.X, pad.Y);
                    if (start)
                    {
                        t.X1 = at.X;
                        t.Y1 = at.Y;
                    }
                    else
                    {
                        t.X2 = at.X;
                        t.Y2 = at.Y;
                    }
                }
            }

            Edited(message);
        }

        public void SelectWholeTrack()
        {
            var board = Board;
            if (board == null) return;
            var t = _selected as Track ?? HitTrack(_cursor.X, _cursor.Y);
            if (t == null) return;
            var (tracks, vias) = TrackEditing.ConnectedTrack(board, t);
            var items = new List<object> { t };
            items.AddRange(tracks.Where(x => !ReferenceEquals(x, t)));
            items.AddRange(vias);
            SetSelection(items);
            Say($"{t.Net}: {tracks.Count} yol parçası, {vias.Count} via seçildi · Del: hepsini sil");
        }

        /// <summary>W / Shift+W: the next/previous standard width for the selected tracks (or the route being drawn).</summary>
        public void CycleTrackWidth(int direction)
        {
            var steps = TrackWidthSteps();
            if (steps.Count == 0) return;
            double Next(double w)
            {
                if (direction > 0) return steps.FirstOrDefault(s => s > w + 1e-6) is double up && up > 0 ? up : steps[steps.Count - 1];
                var down = steps.Where(s => s < w - 1e-6).ToList();
                return down.Count > 0 ? down[down.Count - 1] : steps[0];
            }

            if (_tool == PcbTool.Route && _router != null)
            {
                _router.Width = Next(_router.Width);
                UpdateRoutePreview();
                Say($"Yol genişliği {Length(_router.Width)}");
                InvalidateVisual();
                return;
            }

            var tracks = _sel.OfType<Track>().ToList();
            if (tracks.Count == 0)
            {
                Say("W: genişliği değiştirmek için yol seçin (yol çizerken de çalışır)");
                return;
            }

            SetTrackWidth(tracks, Next(tracks[0].Width));
        }

        public void SetTrackWidth(List<Track> tracks, double width)
        {
            if (tracks.Count == 0) return;
            BeforeEdit?.Invoke(this, EventArgs.Empty);
            foreach (var t in tracks) t.Width = width;
            Edited($"{tracks.Count} yol parçası {Length(width)} genişliğe ayarlandı");
        }

        public void DeleteSelection()
        {
            var board = Board;
            if (board == null || _sel.Count == 0) return;
            var parts = _sel.OfType<BoardComponent>().ToList();
            var other = _sel.Where(o => o is not BoardComponent).ToList();
            if (other.Count > 0)
            {
                BeforeEdit?.Invoke(this, EventArgs.Empty);
                int tracks = 0;
                foreach (var o in other)
                {
                    switch (o)
                    {
                        case Track t: board.Tracks.Remove(t); tracks++; break;
                        case Via v: board.Vias.Remove(v); break;
                        case Zone z: board.Zones.Remove(z); break;
                        case MountingHole h: board.MountingHoles.Remove(h); break;
                        case BoardText x: board.Texts.Remove(x); break;
                        case BoardDimension d: board.Dimensions.Remove(d); break;
                        case OutlineSelection: board.Outline.Clear(); break;
                    }
                }

                _sel.RemoveAll(o => o is not BoardComponent);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Edited(other.Count == 1 ? other[0] switch
                {
                    Track t => $"{t.Net} yol parçası silindi",
                    Via v => $"{v.Net} viası silindi",
                    Zone z => $"{z.Net} dolgusu silindi",
                    OutlineSelection => "Çokgen kart kenarı kaldırıldı (dikdörtgen kart)",
                    _ => "Öğe silindi"
                } : $"{other.Count} öğe silindi ({tracks} yol parçası)");
            }

            if (parts.Count > 0) DeleteComponentsRequested?.Invoke(this, parts);
        }

        /// <summary>A free via at a point (net of the copper under it), checked against the rules.</summary>
        public void PlaceVia(double x, double y)
        {
            var board = Board;
            if (board == null) return;
            string net = NetAt(x, y) ?? string.Empty;
            var router = new InteractiveRouter(board, net);
            double vx = Snap(x), vy = Snap(y);
            if (!router.ViaClear(vx, vy, CopperLayer.Top, CopperLayer.Bottom))
            {
                Say("Via buraya sığmıyor (aralık / delik kuralı)");
                return;
            }

            BeforeEdit?.Invoke(this, EventArgs.Empty);
            var cls = board.ClassFor(net);
            var via = new Via { Net = net, X = vx, Y = vy, Diameter = cls.ViaDiameter, Drill = cls.ViaDrill };
            board.Vias.Add(via);
            Select(via);
            Edited($"Via eklendi{(net.Length > 0 ? " (" + net + ")" : string.Empty)}");
        }

        // ================================================================ context menu

        private static MenuItem Item(string header, string? gesture, Action action, bool enabled = true, bool isChecked = false)
        {
            var m = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty, IsEnabled = enabled, IsChecked = isChecked };
            m.Click += (_, _) => action();
            return m;
        }

        private static MenuItem Sub(string header, IEnumerable<MenuItem> items)
        {
            var m = new MenuItem { Header = header };
            foreach (var i in items) m.Items.Add(i);
            return m;
        }

        private void ShowContextMenu(double x, double y)
        {
            var board = Board;
            if (board == null) return;
            _cursor = (x, y);
            var menu = new ContextMenu();
            void Add(object? item)
            {
                if (item == null) menu.Items.Add(new Separator());
                else menu.Items.Add(item);
            }

            if (_tool == PcbTool.Route && _routePts.Count > 0)
            {
                Add(Item("Yolu bitir", "Enter", () => RouteKey(Key.Enter, false)));
                Add(Item("Via ekle, katman değiştir", "V", () => RouteKey(Key.V, false)));
                Add(Sub("Katmana geç (via ile)", board.CopperLayers().Where(l => l != _routeLayer).Select(l => Item(PcbLayers.DisplayName(l), null, () => RouteToLayer(l)))));
                Add(Item("Açı yönünü değiştir", "/", TogglePosture));
                Add(Item(_only90 ? "45° moda geç" : "90° moda geç", "Boşluk", () => RouteKey(Key.Space, false)));
                Add(Sub("Yol genişliği", TrackWidthSteps().Select(w => Item(Length(w), null, () =>
                {
                    if (_router != null) _router.Width = w;
                    UpdateRoutePreview();
                    InvalidateVisual();
                }, true, _router != null && Math.Abs(_router.Width - w) < 1e-6))));
                Add(Item("Son parçayı geri al", "Geri", () => RouteKey(Key.Back, false)));
                Add(null);
                Add(Item("Yolu iptal et", "Esc", () => RouteKey(Key.Escape, false)));
                menu.PlacementTarget = this;
                menu.IsOpen = true;
                return;
            }

            var hit = HitAny(x, y);
            if (hit != null && !_sel.Contains(hit)) Select(hit);
            if (hit == null && _sel.Count > 0 && !_sel.Any(o => o is BoardComponent c && Contains(c, x, y))) Select(null);

            if (_sel.Count > 1)
            {
                Add(new MenuItem { Header = $"{_sel.Count} öğe seçili", IsEnabled = false });
                Add(Item("Taşı", "M", () => BeginMove(MoveKind.Move, false, Snap(x), Snap(y))));
                Add(Item("Sürükle (yollarla)", "G", () => BeginMove(MoveKind.Drag, false, Snap(x), Snap(y))));
                if (_sel.OfType<BoardComponent>().Any())
                {
                    Add(Item("Döndür", "R", () => RotateSelection()));
                    Add(Item("Alt/üst yüze çevir", "F", FlipSelection));
                    Add(Item("Kilitle / kilidi aç", "L", ToggleLock));
                }

                var tracks = _sel.OfType<Track>().ToList();
                if (tracks.Count > 0) Add(Sub("Yol genişliği", TrackWidthSteps().Select(w => Item(Length(w), null, () => SetTrackWidth(tracks, w)))));
                Add(null);
                Add(Item("Kopyala", "Ctrl+C", CopySelection));
                Add(Item("Kes", "Ctrl+X", CutSelection));
                Add(Item("Çoğalt", "Ctrl+D", Duplicate));
                Add(Item("Sil", "Del", DeleteSelection));
            }
            else
            {
                switch (_selected)
                {
                    case BoardComponent c:
                        Add(new MenuItem { Header = $"{c.RefDes} · {c.Value}", IsEnabled = false });
                        Add(Item("Döndür", "R", () => RotateSelection(), !c.Locked));
                        Add(Item("Ters yöne döndür", "Shift+R", () => RotateSelection(true), !c.Locked));
                        Add(Item(c.Side == CopperLayer.Top ? "Alt yüze çevir" : "Üst yüze çevir", "F", FlipSelection, !c.Locked));
                        Add(Item(c.Locked ? "Kilidi aç" : "Kilitle", "L", ToggleLock));
                        Add(null);
                        Add(Item("Taşı", "M", () => BeginMove(MoveKind.Move, false, Snap(x), Snap(y)), !c.Locked));
                        Add(Item("Sürükle (yollarla)", "G", () => BeginMove(MoveKind.Drag, false, Snap(x), Snap(y)), !c.Locked));
                        Add(null);
                        Add(Item("Özellikler…", "E", () => EditRequested?.Invoke(this, c)));
                        Add(Item("Footprint değiştir…", null, () => ChangeFootprintRequested?.Invoke(this, c)));
                        Add(Item("Footprint'i düzenle…", null, () => EditFootprintRequested?.Invoke(this, c)));
                        Add(Item("Devrede göster", null, () => ShowInSchematicRequested?.Invoke(this, c.RefDes)));
                        Add(null);
                        Add(Item("Kopyala", "Ctrl+C", CopySelection));
                        Add(Item("Çoğalt", "Ctrl+D", Duplicate));
                        Add(Item("Sil (şemadan da)", "Del", DeleteSelection));
                        break;
                    case Track t:
                    {
                        var list = new List<Track> { t };
                        Add(new MenuItem { Header = $"Yol · {t.Net} · {Length(t.Width)}", IsEnabled = false });
                        Add(Item("Parçayı sil", "Del", DeleteSelection));
                        Add(Item("Tüm yolu seç", "U", SelectWholeTrack));
                        Add(Item("Tüm yolu sil", null, () =>
                        {
                            SelectWholeTrack();
                            DeleteSelection();
                        }));
                        Add(Sub("Genişliği değiştir", TrackWidthSteps().Select(w => Item(Length(w), null, () => SetTrackWidth(list, w), true, Math.Abs(t.Width - w) < 1e-6))));
                        Add(Sub("Katmana taşı", board.CopperLayers().Select(l => Item(PcbLayers.DisplayName(l), null, () =>
                        {
                            BeforeEdit?.Invoke(this, EventArgs.Empty);
                            t.Layer = l;
                            Edited($"{t.Net} yolu {PcbLayers.DisplayName(l)} katmanına");
                        }, true, t.Layer == l))));
                        Add(Item("Sürükle (açıyı koru)", "D", BeginSegmentDrag));
                        Add(Item("Taşı", "M", () => BeginMove(MoveKind.Move, false, Snap(x), Snap(y))));
                        Add(Item("Buradan yol çiz", "X", () =>
                        {
                            SetTool(PcbTool.Route);
                            RouteClick(x, y, false);
                        }));
                        Add(Item(string.Equals(HighlightNet, t.Net, StringComparison.OrdinalIgnoreCase) ? "Vurguyu kaldır" : "Hattı vurgula", "`", () => SetHighlight(string.Equals(HighlightNet, t.Net, StringComparison.OrdinalIgnoreCase) ? null : t.Net)));
                        Add(null);
                        Add(Item("Kopyala", "Ctrl+C", CopySelection));
                        Add(Item("Çoğalt", "Ctrl+D", Duplicate));
                        break;
                    }

                    case Via v:
                        Add(new MenuItem { Header = $"Via · {v.Net}", IsEnabled = false });
                        Add(Item("Sil", "Del", DeleteSelection));
                        Add(Item("Tüm yolu seç", "U", () =>
                        {
                            var t0 = board.Tracks.FirstOrDefault(t => string.Equals(t.Net, v.Net, StringComparison.OrdinalIgnoreCase) && (Math.Abs(t.X1 - v.X) + Math.Abs(t.Y1 - v.Y) < 1e-4 || Math.Abs(t.X2 - v.X) + Math.Abs(t.Y2 - v.Y) < 1e-4));
                            if (t0 != null)
                            {
                                Select(t0);
                                SelectWholeTrack();
                            }
                        }));
                        Add(Item("Sürükle (yollarla)", "G", () => BeginMove(MoveKind.Drag, false, Snap(x), Snap(y))));
                        Add(Item("Taşı", "M", () => BeginMove(MoveKind.Move, false, Snap(x), Snap(y))));
                        Add(Item("Buradan yol çiz", "X", () =>
                        {
                            SetTool(PcbTool.Route);
                            RouteClick(v.X, v.Y, false);
                        }));
                        Add(Item("Hattı vurgula", "`", () => SetHighlight(v.Net)));
                        Add(Item("Özellikler…", "E", () => EditRequested?.Invoke(this, v)));
                        break;
                    case Zone z:
                        Add(new MenuItem { Header = $"Dolgu · {z.Net} · {PcbLayers.DisplayName(z.Layer)}", IsEnabled = false });
                        Add(Item("Yeniden doldur", "B", () => RefillRequested?.Invoke(this, EventArgs.Empty)));
                        Add(Item("Düzenle…", "E", () => EditRequested?.Invoke(this, z)));
                        Add(Item("Dolguyu boşalt", null, () =>
                        {
                            BeforeEdit?.Invoke(this, EventArgs.Empty);
                            z.ClearFill();
                            _zoneGeo.Remove(z);
                            Edited($"{z.Net} dolgusu boşaltıldı");
                        }));
                        Add(Item("Hattı vurgula", "`", () => SetHighlight(z.Net)));
                        Add(null);
                        Add(Item("Kopyala", "Ctrl+C", CopySelection));
                        Add(Item("Çoğalt", "Ctrl+D", Duplicate));
                        Add(Item("Sil", "Del", DeleteSelection));
                        break;
                    case BoardText or MountingHole or BoardDimension:
                    {
                        var item = _selected!;
                        Add(Item("Özellikler…", "E", () => EditRequested?.Invoke(this, item)));
                        Add(Item("Taşı", "M", () => BeginMove(MoveKind.Move, false, Snap(x), Snap(y))));
                        if (item is BoardText) Add(Item("Döndür", "R", () => RotateSelection()));
                        Add(null);
                        Add(Item("Kopyala", "Ctrl+C", CopySelection));
                        Add(Item("Çoğalt", "Ctrl+D", Duplicate));
                        Add(Item("Sil", "Del", DeleteSelection));
                        break;
                    }

                    case OutlineSelection:
                        Add(Item("Dikdörtgene dön", null, DeleteSelection, board.Outline.Count >= 3));
                        Add(Item("Kenarı yeniden çiz", null, () => SetTool(PcbTool.Outline)));
                        break;
                    default:
                        Add(Item("Yapıştır", "Ctrl+V", Paste, CanPaste));
                        Add(null);
                        Add(Item("Yol çiz", "X", () => SetTool(PcbTool.Route)));
                        Add(Item("Buraya via koy", null, () => PlaceVia(x, y)));
                        Add(Item("Dolgu alanı çiz", "Z", () =>
                        {
                            SetTool(PcbTool.Zone);
                            PolygonClick(x, y, false);
                        }));
                        Add(Item("Ölç", "Ctrl+Shift+M", () =>
                        {
                            SetTool(PcbTool.Measure);
                            MeasureClick(x, y);
                        }));
                        Add(Item("Ölçü çizgisi", "Ctrl+Shift+H", () =>
                        {
                            SetTool(PcbTool.Dimension);
                            DimensionClick(x, y);
                        }));
                        Add(Item("Yazı ekle", "T", () => TextRequested?.Invoke(this, new PcbPoint(Snap(x), Snap(y)))));
                        Add(Item("Montaj deliği", null, () =>
                        {
                            BeforeEdit?.Invoke(this, EventArgs.Empty);
                            board.MountingHoles.Add(new MountingHole { X = Snap(x), Y = Snap(y), Drill = HoleDrill, PadDiameter = HolePad });
                            Edited("Montaj deliği eklendi");
                        }));
                        Add(null);
                        Add(Item("Tümünü seç", "Ctrl+A", SelectAll));
                        Add(Item("Dolguları doldur", "B", () => RefillRequested?.Invoke(this, EventArgs.Empty), board.Zones.Count > 0));
                        if (HighlightNet != null) Add(Item("Vurguyu kaldır", "`", () => SetHighlight(null)));
                        Add(null);
                        Add(Item("Sığdır", "Home", Fit));
                        Add(Sub("Izgara", new (double Mm, string Label)[]
                        {
                            (0.1, "0.1 mm"), (0.127, "5 mil"), (0.25, "0.25 mm"), (0.254, "10 mil"), (0.5, "0.5 mm"), (0.635, "25 mil"), (1.0, "1 mm"), (1.27, "50 mil"), (2.54, "100 mil")
                        }.Select(g => Item(g.Label, null, () =>
                        {
                            GridSize = g.Mm;
                            GridChanged?.Invoke(this, EventArgs.Empty);
                            InvalidateVisual();
                        }, true, Math.Abs(GridSize - g.Mm) < 1e-9))));
                        Add(Sub("Etkin katman", board.CopperLayers().Select(l => Item(PcbLayers.DisplayName(l), null, () => ActiveLayer = l, true, l == _activeLayer))));
                        Add(null);
                        Add(Item("Kısayollar", "F1", () => HelpRequested?.Invoke(this, EventArgs.Empty)));
                        break;
                }
            }

            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        public event EventHandler? GridChanged;

        private static bool Contains(BoardComponent c, double x, double y)
        {
            var fp = FootprintLibrary.Get(c.FootprintId);
            if (fp == null) return false;
            var b = c.Courtyard(fp);
            return x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY;
        }

        private void RouteToLayer(CopperLayer layer)
        {
            UpdateRoutePreview();
            if (_previewReached) CommitPreview();
            AddVia(layer);
        }

        private void TogglePosture()
        {
            _diagonalFirst = !_diagonalFirst;
            UpdateRoutePreview();
            Say(_diagonalFirst ? "Önce çapraz (45°) parça" : "Önce düz parça");
            InvalidateVisual();
        }
    }
}
