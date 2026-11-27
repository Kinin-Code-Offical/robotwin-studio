using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Instruments;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Editor
{
    public enum EditorTool
    {
        Select,
        Wire,
        Label,
        Place,

        /// <summary>Global label (KiCad Ctrl+L).</summary>
        GlobalLabel,

        /// <summary>No-connect flag on a pin (KiCad Q).</summary>
        NoConnect,

        /// <summary>[notes] Text on the sheet (KiCad T).</summary>
        Text,

        /// <summary>[highlight] KiCad's Highlight Net tool: a click lights up the net under it (and on the board); the same net or nothing clears it.</summary>
        Highlight,

        /// <summary>[junction] KiCad's Add Junction (J): wires crossing at the click join; on a junction placed by hand, they part again.</summary>
        Junction,

        /// <summary>[shapes] KiCad's schematic graphics: a polyline, a rectangle, a circle, an arc (Editor/SchematicCanvas.Shapes.cs).</summary>
        Line,
        Rectangle,
        Circle,
        Arc,

        /// <summary>[erase] KiCad's eraser: every click deletes what is under it (a wire, a part, a label, a note, a flag).</summary>
        Erase
    }

    /// <summary>
    /// The schematic editor surface: an infinite grid with parts, wires and net
    /// labels, editing tools, and the live simulation overlay.
    /// </summary>
    public sealed partial class SchematicCanvas : FrameworkElement
    {
        private const double MinScale = 4, MaxScale = 60;

        // [theme] The paper and its ink follow the theme: dark on dark themes, a printed sheet on light ones.
        private static Color CanvasColor => Shell.CanvasColors.Paper;
        private static Color GridDot => Shell.CanvasColors.GridDot;
        private static Color WireColor => Shell.CanvasColors.Wire;
        private static Color Accent => Shell.CanvasColors.Accent;
        private static Color TextColor => Shell.CanvasColors.Text;
        private static Color MutedText => Shell.CanvasColors.MutedText;
        private static Color LabelColor => Shell.CanvasColors.Label;
        private static Color PanelColor => Shell.CanvasColors.Panel;
        private static Color PanelBorder => Shell.CanvasColors.PanelBorder;

        private double _scale = 14;
        private Vector _offset = new Vector(320, 240);

        private EditorTool _tool = EditorTool.Select;
        private string? _placePreset;
        private SchematicPart? _ghost;

        private readonly HashSet<object> _selection = new HashSet<object>();
        private object? _hover;
        private (SchematicPart Part, PinDefinition Pin)? _hoverPin;
        private Point _mouse;
        private GridPoint _mouseGrid;

        private enum DragMode { None, Pan, Move, Box, Interact, Segment }
        private DragMode _drag;
        private Point _dragStart;
        private GridPoint _dragStartGrid;
        private Vector _panStartOffset;
        private SchematicPart? _pressedButton;

        private List<GridPoint>? _wirePoints;

        private Netlist? _netlist;
        private double _flowPhase;

        private readonly SchematicEffects _effects = new SchematicEffects();
        private double _lastEffectWall = double.NaN;

        /// <summary>Placing a multimeter test lead: true = red, false = black, null = not placing.</summary>
        private bool? _placingLead;

        private readonly HashSet<string> _highlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private double _highlightUntil;

        public SchematicCanvas()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            Cursor = Cursors.Arrow;
            W.DocumentChanged += (_, _) => Refresh();
            Shell.Themes.Changed += Refresh;   // [theme] the paper and its ink
            W.DocumentReplaced += (_, _) =>
            {
                _selection.Clear();
                ResetEditorState();
                // Probes and meter leads belong to the drawing they were put on: another document starts without them.
                W.Simulation.ClearProbes();
                W.Simulation.SetMeterLead(true, null, null);
                W.Simulation.SetMeterLead(false, null, null);
                Dispatcher.BeginInvoke(new Action(FitToContent), System.Windows.Threading.DispatcherPriority.Loaded);
            };
            W.Simulation.Updated += (_, _) =>
            {
                _flowPhase += 0.08;
                StepEffects();
                InvalidateVisual();
            };
            W.Simulation.Failure += (_, entry) => OnPartFailed(entry);
            W.Simulation.InstrumentsChanged += (_, _) => InvalidateVisual();
            W.Simulation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State) && W.Simulation.State == SimState.Stopped)
                {
                    _effects.Clear();
                    _lastEffectWall = double.NaN;
                }
            };
            Loaded += (_, _) => FitToContent();
        }

        private static Workspace W => Workspace.Current;
        private Schematic Doc => W.Schematic;
        private PartLibrary Lib => W.Library;
        private SimulationSession Sim => W.Simulation;
        private bool Simulating => Sim.IsActive;

        public EditorTool Tool
        {
            get => _tool;
            set
            {
                _tool = value;
                _wirePoints = null;
                _shapeDraft = null;   // [shapes]
                if (value != EditorTool.Place)
                {
                    _ghost = null;
                    _placePreset = null;
                }

                CancelFloating();
                Cursor = value == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
                ToolChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
        }

        public IReadOnlyCollection<object> Selection => _selection;

        public double Zoom => _scale / 14.0;

        public event EventHandler? SelectionChanged;

        /// <summary>Selects a part and centres the view on it (used by the PCB editor's "show in schematic").</summary>
        public bool RevealPart(string refDes)
        {
            var part = Doc.Parts.FirstOrDefault(p => string.Equals(p.RefDes, refDes, StringComparison.OrdinalIgnoreCase));
            if (part == null) return false;
            _selection.Clear();
            _selection.Add(part);
            _scale = Math.Max(_scale, 18);
            if (ActualWidth > 0) _offset = new Vector(ActualWidth / 2 - part.Position.X * _scale, ActualHeight / 2 - part.Position.Y * _scale);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return true;
        }

        public event EventHandler? ToolChanged;

        /// <summary>Starts placing a part from the library (follows the mouse until clicked).</summary>
        public void BeginPlace(string presetOrPartId)
        {
            Tool = EditorTool.Place;
            _placePreset = presetOrPartId;
            var preset = Lib.FindPreset(presetOrPartId);
            var def = preset != null ? Lib.Get(preset.PartId) : Lib.Get(presetOrPartId);
            _ghost = new SchematicPart { RefDes = def.RefPrefix + "?", PartId = def.Id, PresetId = preset?.Id, Position = _mouseGrid };
            Focus();
            InvalidateVisual();
        }

        /// <summary>Starts placing a multimeter test lead: the next click on a wire, pin or label clips it there.</summary>
        public void BeginLeadPlacement(bool red)
        {
            if (_tool != EditorTool.Select) Tool = EditorTool.Select;
            _placingLead = red;
            Cursor = Cursors.Cross;
            W.Status = red ? "Kırmızı ucu ölçülecek noktaya (kablo/pin) tıklayın · Esc iptal" : "Siyah (COM) ucu referans noktasına tıklayın (genellikle GND) · Esc iptal";
            Focus();
            InvalidateVisual();
        }

        public bool IsPlacingLead => _placingLead != null;

        /// <summary>Raised after a lead was clipped onto a net.</summary>
        public event EventHandler? LeadPlaced;

        /// <summary>A placed multimeter part was clicked during simulation.</summary>
        public event EventHandler<SchematicPart>? MeterPartClicked;

        /// <summary>Selects and briefly highlights parts (used by the fault diagnosis list).</summary>
        public void HighlightParts(IEnumerable<string> refDes)
        {
            _highlight.Clear();
            foreach (var r in refDes) _highlight.Add(r);
            _highlightUntil = W.Simulation.WallTime + 3.0;
            _selection.Clear();
            foreach (var part in Doc.Parts)
            {
                if (_highlight.Contains(part.RefDes)) _selection.Add(part);
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            var first = Doc.Parts.FirstOrDefault(p => _highlight.Contains(p.RefDes));
            if (first != null && ActualWidth > 0)
            {
                var b = PartBounds(first);
                var center = ToScreen((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2);
                if (center.X < 40 || center.Y < 40 || center.X > ActualWidth - 40 || center.Y > ActualHeight - 40)
                {
                    _offset += new Vector(ActualWidth / 2 - center.X, ActualHeight / 2 - center.Y);
                }
            }

            InvalidateVisual();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            timer.Tick += (_, _) =>
            {
                InvalidateVisual();
                if (W.Simulation.WallTime > _highlightUntil) timer.Stop();
            };
            timer.Start();
        }

        /// <summary>Runs the particle effects forward (used by --snapshot, where frames come too fast).</summary>
        public void AdvanceEffects(double seconds)
        {
            for (double t = 0; t < seconds; t += 0.04) StepEffects(0.04);
            InvalidateVisual();
        }

        public void Refresh()
        {
            _netlist = null;
            _selection.RemoveWhere(o => o is SchematicPart p && !Doc.Parts.Contains(p) ||
                                        o is SchematicWire w && !Doc.Wires.Contains(w) ||
                                        o is NetLabel l && !Doc.Labels.Contains(l) ||
                                        o is SchematicNote sn && !Doc.Notes.Contains(sn) ||
                                        o is SchematicShape sh && !Doc.Shapes.Contains(sh) ||
                                        o is NoConnectFlag nc && !Doc.NoConnects.Contains(nc));
            InvalidateVisual();
        }

        private Netlist Netlist => _netlist ??= Doc.BuildNetlist(Lib);

        // ================================================================ coordinates

        private Point ToScreen(GridPoint g) => new Point(g.X * _scale + _offset.X, g.Y * _scale + _offset.Y);
        private Point ToScreen(double x, double y) => new Point(x * _scale + _offset.X, y * _scale + _offset.Y);
        private GridPoint ToGrid(Point p) => new GridPoint((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);
        private static GridPoint Snap(GridPoint p) => new GridPoint(Math.Round(p.X), Math.Round(p.Y));

        public void FitToContent()
        {
            if (ActualWidth < 10 || ActualHeight < 10) return;
            if (Doc.Parts.Count == 0 && Doc.Wires.Count == 0)
            {
                _scale = 14;
                _offset = new Vector(ActualWidth / 2, ActualHeight / 2);
                InvalidateVisual();
                return;
            }

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            void Inc(GridPoint p)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            foreach (var part in Doc.Parts)
            {
                var b = PartBounds(part);
                Inc(new GridPoint(b.Left, b.Top));
                Inc(new GridPoint(b.Right, b.Bottom));
            }

            foreach (var w in Doc.Wires) foreach (var p in w.Points) Inc(p);
            foreach (var l in Doc.Labels) Inc(l.Position);
            // A net label reaches beyond its point by its name, and while simulating by its voltage badge too: room on
            // both sides so the outermost ones are not cut by the edge.
            double labelRoom = Doc.Labels.Count > 0 ? 12 : 0;
            double w2 = Math.Max(10, maxX - minX + 8 + labelRoom), h2 = Math.Max(8, maxY - minY + 8);
            _scale = Math.Max(MinScale, Math.Min(26, Math.Min(ActualWidth / w2, ActualHeight / h2)));
            _offset = new Vector(ActualWidth / 2 - (minX + maxX) / 2 * _scale, ActualHeight / 2 - (minY + maxY) / 2 * _scale);
            InvalidateVisual();
        }

        // ================================================================ geometry & hit testing

        private Rect PartBounds(SchematicPart part)
        {
            var def = Lib.Find(part.PartId);
            if (def == null) return new Rect(part.Position.X - 1, part.Position.Y - 1, 2, 2);
            double hw = def.HalfWidth, hh = def.HalfHeight;
            var a = part.Transform(-hw, -hh);
            var b = part.Transform(hw, hh);
            var r = new Rect(new Point(a.X, a.Y), new Point(b.X, b.Y));
            foreach (var pin in def.Pins)
            {
                var p = part.Transform(pin.X, pin.Y);
                r.Union(new Point(p.X, p.Y));
            }

            return r;
        }

        private (SchematicPart Part, PinDefinition Pin)? HitPin(GridPoint g)
        {
            double tol = Math.Max(0.45, 7 / _scale);
            foreach (var part in Doc.Parts.AsEnumerable().Reverse())
            {
                var def = Lib.Find(part.PartId);
                if (def == null) continue;
                foreach (var pin in def.Pins)
                {
                    var p = part.Transform(pin.X, pin.Y);
                    if (Math.Abs(p.X - g.X) <= tol && Math.Abs(p.Y - g.Y) <= tol) return (part, pin);
                }
            }

            return null;
        }

        private object? HitObject(GridPoint g)
        {
            double tol = Math.Max(0.35, 6 / _scale);
            if (HitNote(ToScreen(g)) is { } note) return note;   // [notes]
            foreach (var nc in Doc.NoConnects)
            {
                if (Math.Abs(nc.Position.X - g.X) <= tol && Math.Abs(nc.Position.Y - g.Y) <= tol) return nc;
            }

            foreach (var label in Doc.Labels)
            {
                var r = LabelRect(label);
                if (r.Contains(ToScreen(g))) return label;
            }

            foreach (var wire in Doc.Wires)
            {
                for (int i = 0; i + 1 < wire.Points.Count; i++)
                {
                    var a = wire.Points[i];
                    var b = wire.Points[i + 1];
                    if (RobotTwin.CoreSim.Pcb.Geometry.PointSegmentDistance(g.X, g.Y, a.X, a.Y, b.X, b.Y) <= tol) return wire;
                }
            }

            foreach (var part in Doc.Parts.AsEnumerable().Reverse())
            {
                var r = PartBounds(part);
                r.Inflate(0.2, 0.2);
                if (r.Contains(new Point(g.X, g.Y))) return part;
            }

            return HitShape(ToScreen(g));   // [shapes] last: a frame drawn round a block leaves its parts and wires pickable
        }

        private Rect LabelRect(NetLabel label)
        {
            var origin = ToScreen(label.Position);
            var ft = Text(label.Name, 11.5, LabelColor, true);
            double w = ft.Width + 18;
            return LabelFacesLeft(label)
                ? new Rect(origin.X - w, origin.Y - ft.Height / 2 - 3, w, ft.Height + 6)
                : new Rect(origin.X, origin.Y - ft.Height / 2 - 3, w, ft.Height + 6);
        }

        /// <summary>A label on a pin that points left (into the part body on its right) is drawn leftwards.</summary>
        private bool LabelFacesLeft(NetLabel label)
        {
            var probe = new Point(label.Position.X + 0.8, label.Position.Y);
            foreach (var part in Doc.Parts)
            {
                var r = PartBounds(part);
                if (r.Contains(probe)) return true;
            }

            return false;
        }

        // ================================================================ rendering

        private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        private FormattedText Text(string text, double size, Color color, bool semibold = false, bool mono = false) =>
            new FormattedText(RobotWin.Studio.Shell.Lang.T(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                mono ? Typefaces.Mono : semibold ? Typefaces.UiSemi : Typefaces.Ui, size, SymbolRenderer.Brush(color), Dpi);

        private static Pen MakePen(Color c, double w, bool round = true)
        {
            var p = new Pen(SymbolRenderer.Brush(c), w);
            if (round)
            {
                p.StartLineCap = PenLineCap.Round;
                p.EndLineCap = PenLineCap.Round;
                p.LineJoin = PenLineJoin.Round;
            }

            p.Freeze();
            return p;
        }

        protected override void OnRender(DrawingContext dc)
        {
            // [perf] Currents, voltages and burnt parts come from the circuit the simulation thread advances.
            using (Simulation.SimThread.Hold()) RenderScene(dc, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        private void RenderScene(DrawingContext dc, Rect size)
        {
            if (!(_exporting && _clearBackground)) dc.DrawRectangle(SymbolRenderer.Brush(CanvasColor), null, size);
            if (!_exporting) DrawGrid(dc, size);

            var netlist = Netlist;
            DrawNetHighlightUnderlay(dc, netlist);
            double vmax = 1.0;
            if (Simulating)
            {
                foreach (var net in netlist.Nets) vmax = Math.Max(vmax, Math.Abs(Sim.NetVoltage(net.Name)));
            }

            // Wires
            for (int i = 0; i < Doc.Wires.Count; i++)
            {
                var wire = Doc.Wires[i];
                if (wire.Points.Count < 2) continue;
                string? net = i < netlist.WireNets.Count ? netlist.WireNets[i] : null;
                Color color = WireColor;
                if (Simulating && net != null)
                {
                    double v = Sim.NetVoltage(net);
                    if (!double.IsNaN(v)) color = VoltageColor(v / vmax);
                }

                bool selected = _selection.Contains(wire);
                if (!Simulating && net != null && _netHighlight != null && string.Equals(net, _netHighlight, StringComparison.OrdinalIgnoreCase)) color = HighlightColor;
                bool hovered = ReferenceEquals(_hover, wire) || (Simulating && net != null && _hover is SchematicWire hw &&
                                                                  Doc.Wires.IndexOf(hw) is int hi && hi >= 0 && hi < netlist.WireNets.Count && netlist.WireNets[hi] == net);
                var geo = Polyline(wire.Points);
                if (selected || hovered) dc.DrawGeometry(null, MakePen(Color.FromArgb(selected ? (byte)0x40 : (byte)0x22, Accent.R, Accent.G, Accent.B), 5), geo);
                dc.DrawGeometry(null, MakePen(selected ? Accent : color, Math.Max(1.3, _scale * 0.1)), geo);
            }

            // Junctions
            foreach (var j in netlist.Junctions)
            {
                dc.DrawEllipse(SymbolRenderer.Brush(WireColor), null, ToScreen(j), Math.Max(2.5, _scale * 0.2), Math.Max(2.5, _scale * 0.2));
            }

            // Parts
            _variant = Variants.Find(Doc, Doc.ActiveVariant);   // [variants] parts as the active alternative has them
            foreach (var part in Doc.Parts) DrawPart(dc, part, false);

            // Net labels
            for (int i = 0; i < Doc.Labels.Count; i++) DrawLabel(dc, Doc.Labels[i], i < netlist.LabelNets.Count ? netlist.LabelNets[i] : null);

            DrawNoConnects(dc);
            DrawShapes(dc);   // [shapes] (Editor/SchematicCanvas.Shapes.cs)
            DrawNotes(dc);   // [notes]
            DrawEditorOverlay(dc);

            if (_exporting) return;
            if (Simulating) DrawSimulationOverlay(dc, netlist);
            _effects.Draw(dc, (x, y) => ToScreen(x, y), _scale);
            if (Doc.Parts.Count > 0)
            {
                DrawScopeProbes(dc, netlist);
                DrawMeterLeads(dc);
            }
            DrawHighlight(dc);
            DrawHud(dc, size);
        }

        private void DrawGrid(DrawingContext dc, Rect size)
        {
            var g0 = ToGrid(new Point(0, 0));
            var g1 = ToGrid(new Point(size.Width, size.Height));
            int step = _scale >= 9 ? 1 : (_scale >= 4.5 ? 2 : 5);
            if (_gridStyle == GridStyle.None) return;
            if (_gridStyle == GridStyle.Lines)
            {
                DrawGridLines(dc, g0, g1, step);
                return;
            }

            var dotGeo = new StreamGeometry();
            using (var s = dotGeo.Open())
            {
                double r = _scale >= 18 ? 0.9 : 0.7;
                for (double x = Math.Floor(g0.X / step) * step; x <= g1.X; x += step)
                {
                    for (double y = Math.Floor(g0.Y / step) * step; y <= g1.Y; y += step)
                    {
                        var p = ToScreen(x, y);
                        s.BeginFigure(new Point(p.X - r, p.Y), true, true);
                        s.LineTo(new Point(p.X, p.Y - r), false, false);
                        s.LineTo(new Point(p.X + r, p.Y), false, false);
                        s.LineTo(new Point(p.X, p.Y + r), false, false);
                    }
                }
            }

            dotGeo.Freeze();
            dc.DrawGeometry(SymbolRenderer.Brush(GridDot), null, dotGeo);
        }

        private StreamGeometry Polyline(IReadOnlyList<GridPoint> pts)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(ToScreen(pts[0]), false, false);
                for (int i = 1; i < pts.Count; i++) s.LineTo(ToScreen(pts[i]), true, true);
            }

            g.Freeze();
            return g;
        }

        private static Color VoltageColor(double ratio)
        {
            // One hue: an unpowered wire is neutral grey, a wire at full supply is the accent.
            double t = Math.Min(1.0, Math.Abs(ratio));
            return Lerp(Color.FromRgb(0x4A, 0x4A, 0x50), Accent, Math.Sqrt(t));
        }

        private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        /// <summary>[variants] The active alternative while the sheet is drawn (null: the design as drawn).</summary>
        private DesignVariant? _variant;

        private void DrawPart(DrawingContext dc, SchematicPart part, bool ghost)
        {
            var def = Lib.Find(part.PartId);
            if (def == null) return;
            var parameters = Doc.ResolveParameters(Lib, part);
            var over = ghost ? null : _variant?.For(part);   // [variants] its values, and whether it is fitted, in the active alternative
            if (over != null && over.Parameters.Count > 0)
            {
                var merged = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in over.Parameters) merged[kv.Key] = kv.Value;
                parameters = merged;
            }

            bool notFitted = over?.NotFitted == true;
            bool selected = _selection.Contains(part);
            bool hovered = ReferenceEquals(_hover, part);
            var visual = Simulating && !ghost ? VisualFor(part, def) : default;

            // Glow under light-emitting parts.
            if (visual.HasState && visual.Brightness > 0.02 && (def.Symbol == SymbolKind.Led || def.Symbol == SymbolKind.Lamp))
            {
                var color = def.Symbol == SymbolKind.Led
                    ? SymbolRenderer.LedColor(parameters.TryGetValue("color", out var c) ? c : null)
                    : Color.FromRgb(0xFF, 0xD5, 0x80);
                double b = Math.Min(1.5, visual.Brightness);
                var glow = new RadialGradientBrush(Color.FromArgb((byte)(90 * Math.Min(1, b)), color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
                glow.Freeze();
                double r = _scale * (1.8 + 1.4 * b);
                dc.DrawEllipse(glow, null, ToScreen(part.Position), r, r);
            }

            // Red-hot glow around parts approaching their temperature limit.
            if (visual.HasState && visual.Heat > 0.05 && !visual.Failed)
            {
                var hb = PartBounds(part);
                var hc = ToScreen((hb.Left + hb.Right) / 2, (hb.Top + hb.Bottom) / 2);
                double hr = Math.Max(hb.Width, hb.Height) * 0.5 * _scale * (1.1 + 0.5 * visual.Heat);
                var heat = SymbolRenderer.HeatColor(Math.Max(0.6, visual.Heat));
                var hg = new RadialGradientBrush(Color.FromArgb((byte)(150 * visual.Heat), heat.R, heat.G, heat.B), Color.FromArgb(0, heat.R, heat.G, 0));
                hg.Freeze();
                dc.DrawEllipse(hg, null, hc, hr, hr);
            }

            if (selected || hovered)
            {
                var b = PartBounds(part);
                var r = new Rect(ToScreen(b.Left, b.Top), ToScreen(b.Right, b.Bottom));
                r.Inflate(6, 6);
                dc.DrawRoundedRectangle(null, selected ? MakePen(Color.FromArgb(0xB0, Accent.R, Accent.G, Accent.B), 1, false) : MakePen(PanelBorder, 1, false), r, 4, 4);
            }

            if (ghost) dc.PushOpacity(0.55);
            if (notFitted) dc.PushOpacity(0.35);
            dc.PushTransform(new MatrixTransform(SymbolRenderer.PartMatrix(part, _scale, new Point(_offset.X, _offset.Y))));
            var ink = selected ? Accent : SymbolRenderer.SymbolColor;
            SymbolRenderer.Draw(dc, def, parameters, _scale, ink, visual);
            dc.Pop();

            // Pin tips: open pins get a ring so unconnected pins are visible.
            foreach (var pin in def.Pins)
            {
                var tip = part.Transform(pin.X, pin.Y);
                if (ghost || !IsConnectionPointCached(tip, part))
                {
                    dc.DrawEllipse(null, MakePen(SymbolRenderer.PinColor, 1), ToScreen(tip), Math.Max(2, _scale * 0.14), Math.Max(2, _scale * 0.14));
                }
            }

            if (ghost) dc.Pop();
            if (notFitted)
            {
                // [variants] left off in this alternative: faded, crossed out, and said so under it
                dc.Pop();
                var nb = PartBounds(part);
                var nr = new Rect(ToScreen(nb.Left, nb.Top), ToScreen(nb.Right, nb.Bottom));
                var cross = MakePen(Color.FromArgb(0xC0, Accent.R, Accent.G, Accent.B), 1.5, false);
                dc.DrawLine(cross, nr.TopLeft, nr.BottomRight);
                dc.DrawLine(cross, nr.BottomLeft, nr.TopRight);
                if (_scale >= 6)
                {
                    var dnp = Text(Shell.Lang.T("takılmaz"), Math.Max(9, Math.Min(12, _scale * 0.62)), Accent, true);
                    dc.DrawText(dnp, new Point((nr.Left + nr.Right - dnp.Width) / 2, nr.Bottom + 2));
                }
            }

            DrawPartLabels(dc, part, def, parameters, ghost, over != null && !notFitted);
        }

        private bool IsConnectionPointCached(GridPoint tip, SchematicPart owner)
        {
            foreach (var w in Doc.Wires)
            {
                foreach (var p in w.Points) if (p.Equals(tip)) return true;
            }

            foreach (var l in Doc.Labels) if (l.Position.Equals(tip)) return true;
            foreach (var other in Doc.Parts)
            {
                if (ReferenceEquals(other, owner)) continue;
                var def = Lib.Find(other.PartId);
                if (def == null) continue;
                foreach (var pin in def.Pins) if (other.Transform(pin.X, pin.Y).Equals(tip)) return true;
            }

            return false;
        }

        private void DrawPartLabels(DrawingContext dc, SchematicPart part, PartDefinition def, IReadOnlyDictionary<string, string> parameters, bool ghost, bool changed = false)
        {
            if (def.Symbol == SymbolKind.Ground || _scale < 6) return;
            if (def.Symbol == SymbolKind.PowerRail)
            {
                var net = parameters.TryGetValue("net", out var n) ? n : "VCC";
                var top = ToScreen(part.Transform(0, -0.9));
                var ftNet = Text(net, Math.Max(10, _scale * 0.8), LabelColor, true);
                dc.DrawText(ftNet, new Point(top.X - ftNet.Width / 2, top.Y - ftNet.Height));
                return;
            }

            string value = ValueText(def, parameters);
            var b = PartBounds(part);
            var r = new Rect(ToScreen(b.Left, b.Top), ToScreen(b.Right, b.Bottom));
            double size = Math.Max(10, Math.Min(14, _scale * 0.78));
            var refText = Text(part.RefDes, size, ghost ? MutedText : LabelColor, false);
            var valText = string.IsNullOrEmpty(value) ? null : Text(value, size * 0.92, changed ? Accent : MutedText);   // [variants] changed in the active alternative

            if (def.Symbol == SymbolKind.Board)
            {
                dc.DrawText(refText, new Point(r.Left + 4, r.Top - refText.Height - 2));
                if (!ghost) RememberLabelRects(part, new Rect(r.Left + 4, r.Top - refText.Height - 2, refText.Width, refText.Height), null);
                return;
            }

            bool horizontal = r.Width >= r.Height;
            if (horizontal)
            {
                double width = refText.Width + (valText != null ? valText.Width + 6 : 0);
                double x = r.Left + (r.Width - width) / 2;
                double y = r.Top - refText.Height - 1;
                dc.DrawText(refText, new Point(x, y));
                if (valText != null) dc.DrawText(valText, new Point(x + refText.Width + 6, y + (refText.Height - valText.Height) / 2));
                if (!ghost) RememberLabelRects(part, new Rect(x, y, refText.Width, refText.Height), valText == null ? null : new Rect(x + refText.Width + 6, y + (refText.Height - valText.Height) / 2, valText.Width, valText.Height));
            }
            else
            {
                double x = r.Right + 4;
                double y = r.Top + r.Height / 2 - refText.Height;
                dc.DrawText(refText, new Point(x, y));
                if (valText != null) dc.DrawText(valText, new Point(x, y + refText.Height));
                if (!ghost) RememberLabelRects(part, new Rect(x, y, refText.Width, refText.Height), valText == null ? null : new Rect(x, y + refText.Height, valText.Width, valText.Height));
            }
        }

        public static string ValueText(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            if (string.IsNullOrEmpty(def.ValueKey) || !parameters.TryGetValue(def.ValueKey, out var raw) || string.IsNullOrEmpty(raw)) return string.Empty;
            var unit = def.Parameters.FirstOrDefault(p => p.Key == def.ValueKey)?.Unit ?? string.Empty;
            if (def.ValueKey == "color")
            {
                return raw switch
                {
                    "red" => "Kırmızı", "green" => "Yeşil", "blue" => "Mavi", "yellow" => "Sarı", "white" => "Beyaz",
                    "orange" => "Turuncu", "ir" => "IR", "uv" => "UV", _ => raw
                };
            }

            if ((unit == "Ω" || unit == "F" || unit == "H" || unit == "V" || unit == "A") && SiValue.TryParse(raw, out double v))
            {
                return SiValue.Format(v, unit).Replace(" ", string.Empty);
            }

            return raw;
        }

        private void DrawLabel(DrawingContext dc, NetLabel label, string? net)
        {
            var r = LabelRect(label);
            bool left = LabelFacesLeft(label);
            bool selected = _selection.Contains(label);
            bool lit = !Simulating && _netHighlight != null && net != null && string.Equals(net, _netHighlight, StringComparison.OrdinalIgnoreCase);
            var color = selected ? Accent : lit ? HighlightColor : LabelColor;
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                if (left)
                {
                    s.BeginFigure(new Point(r.Right, r.Top + r.Height / 2), true, true);
                    s.LineTo(new Point(r.Right - 8, r.Top), true, true);
                    s.LineTo(new Point(r.Left, r.Top), true, true);
                    s.LineTo(new Point(r.Left, r.Bottom), true, true);
                    s.LineTo(new Point(r.Right - 8, r.Bottom), true, true);
                }
                else
                {
                    s.BeginFigure(new Point(r.Left, r.Top + r.Height / 2), true, true);
                    s.LineTo(new Point(r.Left + 8, r.Top), true, true);
                    s.LineTo(new Point(r.Right, r.Top), true, true);
                    s.LineTo(new Point(r.Right, r.Bottom), true, true);
                    s.LineTo(new Point(r.Left + 8, r.Bottom), true, true);
                }
            }

            g.Freeze();
            var border = selected ? Accent : lit ? HighlightColor : label.Global ? LabelColor : PanelBorder;
            dc.DrawGeometry(SymbolRenderer.Brush(label.Global ? PanelColor : CanvasColor), MakePen(border, label.Global ? 1.4 : 1), g);
            var ft = Text(label.Name, 11.5, color, false);
            dc.DrawText(ft, new Point(left ? r.Left + 6 : r.Left + 12, r.Top + 3));
            dc.DrawEllipse(SymbolRenderer.Brush(WireColor), null, ToScreen(label.Position), 2.2, 2.2);

            if (Simulating && net != null)
            {
                double v = Sim.NetVoltage(net);
                if (!double.IsNaN(v))
                {
                    var vt = Text(SiValue.Format(v, "V"), 10.5, TextColor, false, true);
                    DrawPill(dc, left ? new Point(r.Left - vt.Width - 16, r.Top + 1) : new Point(r.Right + 4, r.Top + 1), vt, PanelColor);
                }
            }
        }

        private void DrawPill(DrawingContext dc, Point at, FormattedText text, Color background)
        {
            var r = new Rect(at.X, at.Y, text.Width + 12, text.Height + 4);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(background), MakePen(PanelBorder, 1, false), r, 4, 4);
            dc.DrawText(text, new Point(at.X + 6, at.Y + 2));
        }

        // ================================================================ simulation overlay

        private PartVisual VisualFor(SchematicPart part, PartDefinition def)
        {
            var v = new PartVisual { HasState = true, Display = Sim.I2cDeviceOf(part.RefDes) };
            var devices = Sim.Compiled?.DevicesOf(part.RefDes);
            if (devices == null || devices.Count == 0) return v;
            var main = devices[0];
            v.Failed = devices.Any(d => d.IsFailed);
            v.Shorted = devices.Any(d => d.Failure == FailureMode.Short);
            if (v.Failed) v.FailedAge = Sim.BurnTimes.TryGetValue(part.RefDes, out double burnt) ? Sim.WallTime - burnt : 60.0;
            foreach (var d in devices)
            {
                var (t, max) = DeviceTemperature(d);
                if (double.IsNaN(t) || !(max > 60)) continue;
                v.TemperatureC = Math.Max(v.TemperatureC, t);
                v.Heat = Math.Max(v.Heat, Math.Max(0.0, Math.Min(1.0, (t - 60.0) / (max - 60.0))));
            }

            v.Current = SimulationSession.DeviceCurrent(main);
            switch (main)
            {
                case Led led: v.Brightness = led.Brightness; break;
                case RobotTwin.CoreSim.Firmware.Devices.Ws2812Load strip: v.Display = strip; break;   // [neopixel] its LEDs' colours
                case Lamp lamp: v.Brightness = lamp.IsFailed ? 0 : lamp.Brightness; break;
                case Switch sw: v.Closed = sw.Closed; break;
                case Relay relay: v.Energized = relay.Energized; break;
                case DcMotor m: v.AngleDeg = m.OutputAngle * 180 / Math.PI * Math.Min(20, m.GearRatio); break;
                case Servo s: v.AngleDeg = s.AngleDeg; break;
                case Buzzer bz: v.Loudness = bz.Loudness; break;
                case Multimeter dmm:
                {
                    var r = dmm.Reading;
                    v.MeterMode = MultimeterSpec.ModeName(dmm.Mode);
                    v.MeterText = !r.Valid && !r.Overload ? "----" : r.Overload ? "OL" : r.Digits;
                    v.MeterUnit = r.Valid ? r.Unit : dmm.Range.Unit;
                    v.MeterBeep = r.Beep;
                    v.FuseBlown = dmm.MilliAmpFuseBlown || dmm.AmpFuseBlown;
                    break;
                }

                case BenchSupply psu:
                    v.SupplyText = psu.IsAc
                        ? $"{psu.RmsVoltage,5:0.00}V~|{psu.RmsCurrent,5:0.000}A~".Replace(',', '.')   // [psu] true RMS on AC
                        : $"{psu.OutputVoltage,5:0.00}V|{Math.Max(0, psu.OutputCurrent),5:0.000}A".Replace(',', '.');
                    v.SupplyMode = psu.Regulation == SupplyRegulation.ConstantCurrent ? "CC" : psu.Regulation == SupplyRegulation.Off ? "OFF" : "CV";
                    break;
            }

            return v;
        }

        /// <summary>Body/junction temperature of a device and the temperature at which it fails or shuts down.</summary>
        public static (double T, double Max) DeviceTemperature(AnalogDevice d) => d switch
        {
            Lamp _ => (double.NaN, double.NaN),
            Resistor r when r.Thermal != null => (r.Thermal.TemperatureC, r.MaxTempC),
            Diode dd when dd.Thermal != null => (dd.Thermal.TemperatureC, dd.MaxJunctionTempC),
            Bjt q when q.Thermal != null => (q.Thermal.TemperatureC, q.MaxJunctionTempC),
            Mosfet m when m.Thermal != null => (m.Thermal.TemperatureC, m.MaxJunctionTempC),
            LinearRegulator reg when reg.Thermal != null => (reg.Thermal.TemperatureC, reg.ShutdownTempC),
            DcMotor m => (m.Thermal.TemperatureC, m.MaxWindingTempC),
            AveragedPwmDriver p => (p.Thermal.TemperatureC, 150.0),
            Battery b => (b.Thermal.TemperatureC, b.IsLithium ? b.RunawayTempC : 95.0),
            _ => (double.NaN, double.NaN)
        };

        // ================================================================ burn effects

        private GridPoint PartCenter(SchematicPart part)
        {
            var b = PartBounds(part);
            return new GridPoint((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2);
        }

        private void OnPartFailed(SimLogEntry entry)
        {
            if (entry.Device == "Multimetre")
            {
                // The lead in the current jack shorted a supply: sparks at the red probe tip.
                if (Sim.MeterRedPoint is { } tip) _effects.Burst(tip, 0.8, false);
                InvalidateVisual();
                return;
            }

            string? refDes = Sim.PartOfDevice(entry.Device);
            var part = refDes != null ? Doc.FindPart(refDes) : null;
            if (part == null) return;
            var def = Lib.Find(part.PartId);
            bool violent = def != null && (def.Symbol is SymbolKind.PolarCapacitor or SymbolKind.Battery or SymbolKind.Regulator or
                                                        SymbolKind.Npn or SymbolKind.Pnp or SymbolKind.Nmos or SymbolKind.Pmos or SymbolKind.Darlington);
            double size = def != null ? Math.Max(1.0, Math.Min(3.0, Math.Max(def.HalfWidth, def.HalfHeight))) : 1.5;
            _effects.Burst(PartCenter(part), size, violent);
            InvalidateVisual();
        }

        private void StepEffects(double? fixedDt = null)
        {
            double now = Sim.WallTime;
            double dt = fixedDt ?? (double.IsNaN(_lastEffectWall) ? 0.0 : Math.Max(0.0, Math.Min(0.1, now - _lastEffectWall)));
            _lastEffectWall = now;
            if (!Simulating || dt <= 0)
            {
                return;
            }

            foreach (var part in Doc.Parts)
            {
                var devices = Sim.Compiled?.DevicesOf(part.RefDes);
                if (devices == null || devices.Count == 0) continue;
                var def = Lib.Find(part.PartId);
                double size = def != null ? Math.Max(0.8, Math.Min(2.5, Math.Max(def.HalfWidth, def.HalfHeight) * 0.7)) : 1.0;
                bool failed = devices.Any(d => d.IsFailed);
                var center = PartCenter(part);
                if (failed)
                {
                    double age = Sim.BurnTimes.TryGetValue(part.RefDes, out double t0) ? now - t0 : 30.0;
                    double rate = age < 3 ? 16 : age < 12 ? 5 : age < 40 ? 1.2 : 0.3;
                    _effects.Emit(part.RefDes, new GridPoint(center.X, center.Y - 0.4), size, rate, dt, true);
                    if (age < 5) _effects.Embers(center, size * 2, 10, dt);
                    continue;
                }

                double heat = 0;
                foreach (var d in devices)
                {
                    var (t, max) = DeviceTemperature(d);
                    if (!double.IsNaN(t) && max > 60) heat = Math.Max(heat, (t - 60.0) / (max - 60.0));
                }

                if (heat > 0.4) _effects.Emit(part.RefDes, new GridPoint(center.X, center.Y - 0.3), size * 0.8, 1.0 + 8.0 * (heat - 0.4) / 0.6, dt, false);
            }

            _effects.Update(dt);
        }

        // ================================================================ instrument overlays

        private static readonly Color LeadRed = Color.FromRgb(0xD9, 0x3A, 0x3A);
        private static readonly Color LeadBlack = Color.FromRgb(0x30, 0x30, 0x34);

        private void DrawMeterLeads(DrawingContext dc)
        {
            var leads = new List<(GridPoint At, Color Color, string Label)>();
            if (Sim.MeterRedPoint is { } red) leads.Add((red, LeadRed, Sim.MeterJack == MultimeterJack.VoltOhm ? "VΩ" : Sim.MeterJack == MultimeterJack.MilliAmp ? "mA" : "10A"));
            if (Sim.MeterBlackPoint is { } black) leads.Add((black, LeadBlack, "COM"));
            int k = 0;
            foreach (var (at, color, label) in leads)
            {
                var tip = ToScreen(at);
                var dir = new Vector(0.62, 0.78);
                var handle0 = tip + dir * 9;
                var handle1 = tip + dir * 40;
                // Cable to the meter (bottom-right corner of the sheet view).
                var end = new Point(ActualWidth - 24 - 18 * k, ActualHeight + 30);
                var cable = new StreamGeometry();
                using (var g = cable.Open())
                {
                    g.BeginFigure(handle1, false, false);
                    g.BezierTo(handle1 + dir * 60, new Point(end.X, end.Y - 120), end, true, true);
                }

                cable.Freeze();
                var cablePen = new Pen(SymbolRenderer.Brush(Color.FromArgb(0xD0, color.R, color.G, color.B)), 3.2) { StartLineCap = PenLineCap.Round };
                cablePen.Freeze();
                dc.DrawGeometry(null, cablePen, cable);
                var body = new Pen(SymbolRenderer.Brush(color), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                body.Freeze();
                dc.DrawLine(body, handle0 + dir * 4, handle1);
                var guard = new Pen(SymbolRenderer.Brush(color), 11) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                guard.Freeze();
                dc.DrawLine(guard, handle0 + dir * 4, handle0 + dir * 7);
                dc.DrawLine(MakePen(Color.FromRgb(0xC8, 0xC8, 0xCE), 2), tip, handle0 + dir * 4);
                dc.DrawEllipse(SymbolRenderer.Brush(Color.FromRgb(0xE8, 0xE8, 0xEC)), null, tip, 2.2, 2.2);
                var ft = Text(label, 10, color == LeadBlack ? Color.FromRgb(0xC8, 0xC8, 0xCE) : color, true);
                dc.DrawText(ft, new Point(tip.X - ft.Width - 6, tip.Y - ft.Height - 2));
                k++;
            }

            if (_placingLead != null)
            {
                var pin = HitPin(_mouseGrid);
                var hint = Text(_placingLead == true ? "Kırmızı uç: tıklayın" : "Siyah uç (COM): tıklayın", 11, _placingLead == true ? LeadRed : TextColor, true);
                DrawPill(dc, new Point(_mouse.X + 14, _mouse.Y + 12), hint, PanelColor);
                if (pin != null) dc.DrawEllipse(null, MakePen(_placingLead == true ? LeadRed : TextColor, 1.6), ToScreen(pin.Value.Part.Transform(pin.Value.Pin.X, pin.Value.Pin.Y)), 6, 6);
            }
        }

        private void DrawScopeProbes(DrawingContext dc, Netlist netlist)
        {
            for (int i = 0; i < Sim.ScopeProbes.Length; i++)
            {
                var probe = Sim.ScopeProbes[i];
                if (probe == null) continue;
                GridPoint? at = Sim.ScopeProbePoints[i];
                if (at == null)
                {
                    if (probe.Kind == ProbeKind.Voltage)
                    {
                        int w = netlist.WireNets.FindIndex(n => n == probe.Name);
                        if (w >= 0 && w < Doc.Wires.Count && Doc.Wires[w].Points.Count > 0) at = Doc.Wires[w].Points[0];
                        else
                        {
                            int l = netlist.LabelNets.FindIndex(n => n == probe.Name);
                            if (l >= 0 && l < Doc.Labels.Count) at = Doc.Labels[l].Position;
                        }
                    }
                    else if (Doc.FindPart(probe.Target) is { } part)
                    {
                        at = PartCenter(part);
                    }
                }

                if (at == null) continue;
                var color = ScopeView.ColorOf(i);
                var tip = ToScreen(at.Value);
                var flag = new Point(tip.X + 7, tip.Y - 20 - 3 * i);
                dc.DrawLine(MakePen(color, 1.4), tip, new Point(flag.X, flag.Y + 8));
                dc.DrawEllipse(null, MakePen(color, 1.6), tip, 3.5, 3.5);
                var ft = Text((probe.Kind == ProbeKind.Current ? "A" : "") + (i + 1).ToString(CultureInfo.InvariantCulture), 10, CanvasColor, true);
                var box = new Rect(flag.X, flag.Y, ft.Width + 8, ft.Height + 2);
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(color), null, box, 3, 3);
                dc.DrawText(ft, new Point(box.Left + 4, box.Top + 1));
            }
        }

        private void DrawHighlight(DrawingContext dc)
        {
            if (_highlight.Count == 0) return;
            double left = _highlightUntil - Sim.WallTime;
            if (left <= 0)
            {
                _highlight.Clear();
                return;
            }

            double pulse = 0.5 + 0.5 * Math.Sin(Sim.WallTime * 9);
            foreach (var part in Doc.Parts)
            {
                if (!_highlight.Contains(part.RefDes)) continue;
                var b = PartBounds(part);
                var r = new Rect(ToScreen(b.Left, b.Top), ToScreen(b.Right, b.Bottom));
                r.Inflate(8 + 4 * pulse, 8 + 4 * pulse);
                dc.DrawRoundedRectangle(null, MakePen(Color.FromArgb((byte)(120 + 120 * pulse), 0xE5, 0x48, 0x4D), 2, false), r, 6, 6);
            }
        }

        private void DrawSimulationOverlay(DrawingContext dc, Netlist netlist)
        {
            // Current flow dots along two-terminal parts.
            foreach (var part in AppSettings.Current.ShowCurrentFlow ? Doc.Parts : new List<SchematicPart>())
            {
                var def = Lib.Find(part.PartId);
                if (def == null || def.Pins.Count != 2 || def.Symbol == SymbolKind.Ground) continue;
                var device = Sim.MainDevice(part.RefDes);
                if (device == null) continue;
                double i = SimulationSession.DeviceCurrent(device);
                if (double.IsNaN(i) || Math.Abs(i) < 50e-6) continue;
                var a = ToScreen(part.Transform(def.Pins[0].X, def.Pins[0].Y));
                var b = ToScreen(part.Transform(def.Pins[1].X, def.Pins[1].Y));
                // Conventional current flows from pin 0 to pin 1 when positive.
                if (device is VoltageSource || device is Battery) (a, b) = (b, a);
                if (i < 0) (a, b) = (b, a);
                double speed = Math.Min(3.0, 0.6 + Math.Log10(1 + Math.Abs(i) * 1000));
                double phase = (_flowPhase * speed) % 1.0;
                var brush = SymbolRenderer.Brush(Color.FromArgb(0xD0, Accent.R, Accent.G, Accent.B));
                for (int k = 0; k < 3; k++)
                {
                    double t = (phase + k / 3.0) % 1.0;
                    dc.DrawEllipse(brush, null, new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t), 1.7, 1.7);
                }
            }

            // Readout for the hovered item.
            string? info = HoverInfo(netlist);
            if (info != null)
            {
                var ft = Text(info, 11.5, TextColor, false, true);
                var at = new Point(_mouse.X + 16, _mouse.Y + 14);
                if (at.X + ft.Width + 24 > ActualWidth) at.X = _mouse.X - ft.Width - 28;
                var r = new Rect(at.X, at.Y, ft.Width + 20, ft.Height + 14);
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(PanelColor), MakePen(PanelBorder, 1, false), r, 6, 6);
                dc.DrawText(ft, new Point(at.X + 10, at.Y + 7));
            }
        }

        private string? HoverInfo(Netlist netlist)
        {
            if (_hover is SchematicWire wire)
            {
                int idx = Doc.Wires.IndexOf(wire);
                string? net = idx >= 0 && idx < netlist.WireNets.Count ? netlist.WireNets[idx] : null;
                if (net == null) return null;
                return $"{net}\n{SiValue.Format(Sim.NetVoltage(net), "V", 4)}\nTıkla: osiloskoba ekle";
            }

            if (_hover is SchematicPart part)
            {
                var devices = Sim.Compiled?.DevicesOf(part.RefDes);
                if (devices == null || devices.Count == 0) return null;
                var d = devices[0];
                var lines = new List<string> { part.RefDes + "  " + (Lib.Find(part.PartId)?.Name ?? string.Empty) };
                void Add(string label, double value, string unit)
                {
                    if (!double.IsNaN(value)) lines.Add($"{label,-3} {SiValue.Format(value, unit, 4)}");
                }

                switch (d)
                {
                    case Led led:
                        Add("V", led.Voltage, "V"); Add("I", led.Current, "A"); Add("P", led.Power, "W");
                        lines.Add($"Işık %{led.Brightness * 100:F0}");
                        if (led.Thermal != null) Add("T", led.Thermal.TemperatureC, "°C");
                        break;
                    case Resistor r:
                        Add("V", r.Voltage, "V"); Add("I", r.Current, "A"); Add("P", r.Power, "W");
                        if (r.Thermal != null) Add("T", r.Thermal.TemperatureC, "°C");
                        break;
                    case Diode dd: Add("V", dd.Voltage, "V"); Add("I", dd.Current, "A"); Add("P", dd.Power, "W"); break;
                    case Capacitor c: Add("V", c.Voltage, "V"); Add("I", c.Current, "A"); Add("E", c.Energy, "J"); break;
                    case Inductor l: Add("V", l.Voltage, "V"); Add("I", l.Current, "A"); break;
                    case Battery bat: Add("V", bat.Voltage, "V"); Add("I", bat.Current, "A"); lines.Add($"Şarj %{bat.StateOfCharge * 100:F1}"); break;
                    case Ammeter am: Add("I", am.Reading, "A"); break;
                    case VoltageSource vs: Add("V", vs.Voltage, "V"); Add("I", vs.Current, "A"); Add("P", vs.Voltage * vs.Current, "W"); break;
                    case DcMotor m: Add("V", m.Voltage, "V"); Add("I", m.Current, "A"); lines.Add($"Hız {m.OutputRpm:F0} rpm"); Add("τ", m.OutputTorque, "N·m"); break;
                    case Bjt q: Add("Ic", q.CollectorCurrent, "A"); Add("Ib", q.BaseCurrent, "A"); Add("Vce", q.Vce, "V"); Add("P", q.Power, "W"); break;
                    case Mosfet m: Add("Id", m.DrainCurrent, "A"); Add("Vgs", m.Vgs, "V"); Add("Vds", m.Vds, "V"); Add("P", m.Power, "W"); break;
                    case LinearRegulator reg: Add("Vin", reg.Vin, "V"); Add("Vout", reg.Vout, "V"); Add("I", reg.Current, "A"); Add("P", reg.Power, "W"); break;
                    case OpAmp op: Add("Vo", op.OutputVoltage, "V"); Add("Io", op.OutputCurrent, "A"); break;
                    case Servo s: lines.Add($"Açı {s.AngleDeg:F0}°"); Add("I", s.SupplyCurrent, "A"); break;
                    case Relay k: lines.Add(k.Energized ? "Çekili" : "Bırakık"); Add("Ib", k.CoilCurrent, "A"); break;
                    case Switch sw: lines.Add(sw.Closed ? "Kapalı" : "Açık"); Add("I", sw.Current, "A"); break;
                    case ControlledSource cs: lines.Add($"Seviye {cs.Level:G3}"); lines.Add("Tekerlek: değiştir"); break;
                    case Multimeter dmm:
                        lines.Add($"{MultimeterSpec.ModeName(dmm.Mode)}  {dmm.Reading.Text}  ({dmm.Range.Label})");
                        if (MultimeterSpec.IsCurrent(dmm.Mode)) Add("Uyük", dmm.BurdenVoltage, "V");
                        if (dmm.Reading.Warning != null) lines.Add("⚠ " + dmm.Reading.Warning);
                        lines.Add("Tıkla: multimetre panelinde aç");
                        break;
                    case BenchSupply psu:
                        if (psu.IsAc)
                        {
                            Add("Vrms", psu.RmsVoltage, "V"); Add("Irms", psu.RmsCurrent, "A"); Add("f", psu.Frequency, "Hz");
                        }
                        else
                        {
                            Add("V", psu.OutputVoltage, "V"); Add("I", psu.OutputCurrent, "A"); Add("P", psu.Power, "W");
                        }

                        lines.Add(psu.Regulation == SupplyRegulation.ConstantCurrent ? "CC (akım sınırında)" : psu.Regulation == SupplyRegulation.Off ? (psu.OvpTripped ? "OVP: çıkış kapalı" : "Çıkış kapalı") : "CV");
                        break;
                }

                var (temp, tmax) = DeviceTemperature(d);
                if (!double.IsNaN(temp) && temp > 45 && !(d is Resistor) && !(d is Led)) Add("T", temp, "°C");
                if (!double.IsNaN(temp) && tmax > 60 && temp > 60 && !d.IsFailed) lines.Add($"Isınma: sınırın %{Math.Max(0, (temp - 25) / (tmax - 25)) * 100:F0}'i");
                if (Sim.Compiled != null && Sim.Compiled.ToleranceDraws.TryGetValue(part.RefDes, out var draws))
                {
                    foreach (var draw in draws.Take(2)) lines.Add($"Gerçek {draw.Label.ToLowerInvariant()}: {SiValue.Format(draw.Actual, draw.Unit, 4)} ({draw.DeviationPercent:+0.0;-0.0} %)".Replace(',', '.'));
                }

                if (Lib.Find(part.PartId)?.Symbol == SymbolKind.Board)
                {
                    foreach (var pin in new[] { "5V", "3V3" }) Add(pin, Sim.PinVoltage(part.RefDes, pin), "V");
                }

                foreach (var dev in devices.Where(x => x.IsFailed)) lines.Add("⚠ " + dev.FailureReason);
                if (devices.Any(x => x.IsFailed)) lines.Add("Sağ tık: parçayı onar / değiştir");
                if (d is Potentiometer pot) lines.Add($"Konum %{pot.Position * 100:F0} (tekerlek)");
                if (d is Thermistor th) lines.Add($"{th.SensedTempC:F1} °C (tekerlek)");
                if (d is Photoresistor ph) lines.Add($"{ph.Lux:F0} lux (tekerlek)");
                return string.Join("\n", lines);
            }

            return null;
        }

        // ================================================================ input

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (Simulating && _hover is SchematicPart part && AdjustByWheel(part, e.Delta > 0 ? 1 : -1))
            {
                e.Handled = true;
                return;
            }

            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            double newScale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
            var p = e.GetPosition(this);
            var g = ToGrid(p);
            _scale = newScale;
            _offset = new Vector(p.X - g.X * _scale, p.Y - g.Y * _scale);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }

        public event EventHandler? ZoomChanged;

        private bool AdjustByWheel(SchematicPart part, int dir)
        {
            var device = Sim.MainDevice(part.RefDes);
            switch (device)
            {
                case Potentiometer pot:
                    Sim.SetPotPosition(part.RefDes, Math.Max(0, Math.Min(1, pot.Position + dir * 0.05)));
                    return true;
                case Thermistor th:
                    Sim.SetEnvironment(part.RefDes, th.SensedTempC + dir * 2);
                    return true;
                case Photoresistor ph:
                    Sim.SetEnvironment(part.RefDes, Math.Max(0, ph.Lux * (dir > 0 ? 1.4 : 1 / 1.4)));
                    return true;
                case ControlledSource cs:
                    double step = cs.Ratiometric ? 0.05 : (cs.Level < 1 ? 0.01 : 0.05);
                    Sim.SetSensorLevel(part.RefDes, Math.Max(0, cs.Level + dir * step));
                    return true;
                case DcMotor m:
                    Sim.SetEnvironment(part.RefDes, Math.Max(0, m.LoadTorque + dir * 0.02));
                    return true;
            }

            return false;
        }

        public event EventHandler<SchematicPart>? PartDoubleClicked;

        /// <summary>Net and exact sheet point under the mouse (pin tip, point on a wire, or a label).</summary>
        private (string? Net, GridPoint At) NetAt(GridPoint g)
        {
            var pin = HitPin(g);
            if (pin != null)
            {
                var tip = pin.Value.Part.Transform(pin.Value.Pin.X, pin.Value.Pin.Y);
                return (Netlist.NetOf(pin.Value.Part.RefDes, pin.Value.Pin.Name)?.Name, tip);
            }

            var hit = HitObject(g);
            if (hit is SchematicWire wire)
            {
                int idx = Doc.Wires.IndexOf(wire);
                string? net = idx >= 0 && idx < Netlist.WireNets.Count ? Netlist.WireNets[idx] : null;
                GridPoint best = wire.Points[0];
                double bestD = double.MaxValue;
                for (int i = 0; i + 1 < wire.Points.Count; i++)
                {
                    var a = wire.Points[i];
                    var b = wire.Points[i + 1];
                    double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                    double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((g.X - a.X) * dx + (g.Y - a.Y) * dy) / len2)) : 0;
                    var q = new GridPoint(a.X + dx * t, a.Y + dy * t);
                    double d = (q.X - g.X) * (q.X - g.X) + (q.Y - g.Y) * (q.Y - g.Y);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = q;
                    }
                }

                return (net, best);
            }

            if (hit is NetLabel label)
            {
                int idx = Doc.Labels.IndexOf(label);
                return (idx >= 0 && idx < Netlist.LabelNets.Count ? Netlist.LabelNets[idx] : null, label.Position);
            }

            return (null, g);
        }

        private void PlaceLead()
        {
            var (net, at) = NetAt(_mouseGrid);
            if (net == null)
            {
                W.Status = "Uç bir kabloya, pine veya etikete takılmalı.";
                return;
            }

            bool red = _placingLead == true;
            Sim.SetMeterLead(red, net, at);
            _placingLead = red && Sim.MeterBlackNet == null ? false : null;
            if (_placingLead == null)
            {
                Cursor = _tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
                W.Status = $"Multimetre: kırmızı {Sim.MeterRedNet ?? "—"}, siyah {Sim.MeterBlackNet ?? "—"}";
            }
            else
            {
                W.Status = "Siyah (COM) ucu referans noktasına tıklayın (genellikle GND) · Esc iptal";
            }

            LeadPlaced?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        private void HandleSimulationClick(MouseButtonEventArgs e)
        {
            var target = HitObject(_mouseGrid);
            if (target is SchematicPart part)
            {
                var def = Lib.Find(part.PartId);
                if (def?.Symbol == SymbolKind.Multimeter)
                {
                    MeterPartClicked?.Invoke(this, part);
                    _selection.Clear();
                    _selection.Add(part);
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (def?.Symbol == SymbolKind.PushButton)
                {
                    _pressedButton = part;
                    Sim.SetSwitch(part.RefDes, true);
                    _drag = DragMode.Interact;
                    return;
                }

                if (def?.Symbol == SymbolKind.Switch && Sim.MainDevice(part.RefDes) is Switch sw)
                {
                    Sim.SetSwitch(part.RefDes, !sw.Closed);
                    return;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && def != null && def.Pins.Count == 2)
                {
                    Sim.AddCurrentProbe(part.RefDes, null, PartCenter(part));
                    return;
                }

                _selection.Clear();
                _selection.Add(part);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (target is SchematicWire || target is NetLabel)
            {
                var (net, at) = NetAt(_mouseGrid);
                if (net != null) Sim.AddVoltageProbe(net, null, at);
            }
        }
    }
}
