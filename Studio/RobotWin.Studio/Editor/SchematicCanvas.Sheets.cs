using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// [sheets] KiCad's hierarchy in the editor: the sheet shown (the root or a sub-sheet), the way down (double-click a
    /// sheet symbol), up and back / forward through the sheets visited, the sheet symbols with their pins, and the tools
    /// that draw a sheet, put a pin on its edge and place a hierarchical label. Everything the editor draws, picks and
    /// adds is the shown sheet's; the netlist, the board and the simulation see every sheet at once.
    /// </summary>
    public sealed partial class SchematicCanvas
    {
        private readonly List<string?> _sheetBack = new List<string?>();
        private readonly List<string?> _sheetForward = new List<string?>();
        private GridPoint? _sheetCorner;   // the sheet tool's first corner

        /// <summary>[sheets] The sheet shown and edited (null: the root).</summary>
        public string? CurrentSheet { get; private set; }

        /// <summary>[sheets] Raised when another sheet is shown (the page's path bar follows).</summary>
        public event EventHandler? SheetChanged;

        public bool CanSheetBack => _sheetBack.Count > 0;
        public bool CanSheetForward => _sheetForward.Count > 0;
        public bool CanSheetUp => CurrentSheet != null;

        private bool Here(object o) => Schematic.SameSheet(Schematic.SheetOf(o), CurrentSheet);
        private IEnumerable<SchematicPart> SheetParts => Doc.Parts.Where(Here);
        private IEnumerable<SchematicWire> SheetWires => Doc.Wires.Where(Here);
        private IEnumerable<NetLabel> SheetLabels => Doc.Labels.Where(Here);
        private IEnumerable<NoConnectFlag> SheetNoConnects => Doc.NoConnects.Where(Here);
        private IEnumerable<SchematicNote> SheetNotes => Doc.Notes.Where(Here);
        private IEnumerable<SchematicShape> SheetShapes => Doc.Shapes.Where(Here);
        private IEnumerable<SchematicSheet> SheetSymbols => Doc.SheetsOn(CurrentSheet);

        /// <summary>[sheets] Shows a sheet (null: the root), remembering where it came from for Back.</summary>
        public void EnterSheet(string? uid, bool remember = true)
        {
            if (uid != null && Doc.FindSheet(uid) == null) uid = null;
            if (Schematic.SameSheet(uid, CurrentSheet)) return;
            if (remember)
            {
                _sheetBack.Add(CurrentSheet);
                _sheetForward.Clear();
            }

            ShowSheet(uid);
        }

        /// <summary>[sheets] KiCad's Leave Sheet / Navigate Up: to the parent sheet, its symbol selected.</summary>
        public void SheetUp()
        {
            var symbol = Doc.FindSheet(CurrentSheet);
            if (symbol == null) return;
            EnterSheet(symbol.Sheet);
            SelectItems(new object[] { symbol });
        }

        /// <summary>[sheets] KiCad's Navigate Back.</summary>
        public void SheetBack()
        {
            if (_sheetBack.Count == 0) return;
            var to = _sheetBack[_sheetBack.Count - 1];
            _sheetBack.RemoveAt(_sheetBack.Count - 1);
            _sheetForward.Add(CurrentSheet);
            ShowSheet(to);
        }

        /// <summary>[sheets] KiCad's Navigate Forward.</summary>
        public void SheetForward()
        {
            if (_sheetForward.Count == 0) return;
            var to = _sheetForward[_sheetForward.Count - 1];
            _sheetForward.RemoveAt(_sheetForward.Count - 1);
            _sheetBack.Add(CurrentSheet);
            ShowSheet(to);
        }

        private void ShowSheet(string? uid)
        {
            if (uid != null && Doc.FindSheet(uid) == null) uid = null;
            _floating = null;
            _wirePoints = null;
            _shapeDraft = null;
            _sheetCorner = null;
            CurrentSheet = uid;
            _selection.Clear();
            _netlist = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            FitToContent();
            SheetChanged?.Invoke(this, EventArgs.Empty);
            W.Status = Shell.Lang.T("Sayfa") + ": " + Doc.SheetPath(uid);
            InvalidateVisual();
        }

        /// <summary>[sheets] After an undo or a load: a sheet that is gone sends the view to the root.</summary>
        private void KeepSheetValid()
        {
            if (CurrentSheet == null || Doc.FindSheet(CurrentSheet) != null) return;
            _sheetBack.Clear();
            _sheetForward.Clear();
            ShowSheet(null);
        }

        // ================================================================ drawing

        private static Color SheetInk => Shell.CanvasColors.Label;

        private void DrawSheetSymbols(DrawingContext dc)
        {
            foreach (var h in SheetSymbols)
            {
                bool selected = _selection.Contains(h);
                bool hover = ReferenceEquals(_hover, h);
                var ink = selected ? Accent : SheetInk;
                var r = new Rect(ToScreen(h.Position), ToScreen(new GridPoint(h.Position.X + h.Width, h.Position.Y + h.Height)));
                dc.DrawRectangle(SymbolRenderer.Brush(Color.FromArgb(hover ? (byte)0x22 : (byte)0x12, SheetInk.R, SheetInk.G, SheetInk.B)), MakePen(ink, Math.Max(1.4, _scale * 0.12)), r);
                var name = Text(h.Name, 12, ink, true);
                dc.DrawText(name, new Point(r.Left, r.Top - name.Height - 2));
                int inside = Doc.Parts.Count(p => Schematic.SameSheet(p.Sheet, h.Uid));
                var file = Text(string.Format(CultureInfo.InvariantCulture, Shell.Lang.T("{0} parça · çift tık: aç"), inside), 10.5, MutedText, false);
                dc.DrawText(file, new Point(r.Left, r.Bottom + 2));
                foreach (var pin in h.Pins) DrawSheetPin(dc, h, pin, ink);
            }
        }

        /// <summary>A sheet pin: KiCad's shape on the edge (in, out, both ways) and its name inside the frame.</summary>
        private void DrawSheetPin(DrawingContext dc, SchematicSheet h, SheetPin pin, Color ink)
        {
            var at = ToScreen(pin.Position);
            int edge = h.EdgeOf(pin.Position);
            // the direction into the sheet
            var into = edge switch { 0 => new Vector(1, 0), 1 => new Vector(-1, 0), 2 => new Vector(0, 1), _ => new Vector(0, -1) };
            var across = new Vector(-into.Y, into.X);
            double s = Math.Max(4, _scale * 0.35);
            // an input points into the sheet, an output out of it, both ways a diamond, passive a square
            Point[] shape = pin.Shape switch
            {
                LabelShape.Input => new[] { at + into * s, at - across * s * 0.7, at + across * s * 0.7 },
                LabelShape.Output => new[] { at, at + into * s - across * s * 0.7, at + into * s + across * s * 0.7 },
                LabelShape.Passive => new[] { at - across * s * 0.6, at + into * s - across * s * 0.6, at + into * s + across * s * 0.6, at + across * s * 0.6 },
                _ => new[] { at, at + into * s * 0.5 - across * s * 0.7, at + into * s, at + into * s * 0.5 + across * s * 0.7 }
            };
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(shape[0], true, true);
                for (int i = 1; i < shape.Length; i++) c.LineTo(shape[i], true, true);
            }

            g.Freeze();
            dc.DrawGeometry(SymbolRenderer.Brush(CanvasColor), MakePen(ink, 1.3), g);
            var t = Text(pin.Name, 10.5, ink, false);
            var textAt = edge switch
            {
                0 => new Point(at.X + s + 4, at.Y - t.Height / 2),
                1 => new Point(at.X - s - 4 - t.Width, at.Y - t.Height / 2),
                2 => new Point(at.X - t.Width / 2, at.Y + s + 2),
                _ => new Point(at.X - t.Width / 2, at.Y - s - 2 - t.Height)
            };
            dc.DrawText(t, textAt);
        }

        /// <summary>The sheet tool's frame while its second corner is chosen.</summary>
        private void DrawSheetDraft(DrawingContext dc)
        {
            if (_tool != EditorTool.Sheet || _sheetCorner is not { } a) return;
            var b = Snap(_mouseGrid);
            var r = new Rect(ToScreen(a), ToScreen(b));
            var pen = MakePen(SheetInk, 1.2).Clone();
            pen.DashStyle = DashStyles.Dash;
            dc.DrawRectangle(null, pen, r);
        }

        // ================================================================ picking

        private SchematicSheet? HitSheet(GridPoint g)
        {
            foreach (var h in SheetSymbols.Reverse())
            {
                var b = h.Bounds();
                if (g.X >= b.MinX - 0.3 && g.X <= b.MaxX + 0.3 && g.Y >= b.MinY - 0.3 && g.Y <= b.MaxY + 0.3) return h;
            }

            return null;
        }

        private (SchematicSheet Sheet, SheetPin Pin)? HitSheetPin(GridPoint g)
        {
            double tol = Math.Max(0.45, 7 / _scale);
            foreach (var h in SheetSymbols)
                foreach (var pin in h.Pins)
                    if (Math.Abs(pin.Position.X - g.X) <= tol && Math.Abs(pin.Position.Y - g.Y) <= tol)
                        return (h, pin);
            return null;
        }

        // ================================================================ tools

        /// <summary>[sheets] The sheet tool: two corners, then its name typed in place.</summary>
        private void SheetToolClick()
        {
            var at = Snap(_mouseGrid);
            if (_sheetCorner is not { } a)
            {
                _sheetCorner = at;
                W.Status = Shell.Lang.T("Sayfa: karşı köşeye tıklayın");
                InvalidateVisual();
                return;
            }

            _sheetCorner = null;
            double x0 = Math.Min(a.X, at.X), y0 = Math.Min(a.Y, at.Y), w = Math.Abs(at.X - a.X), hgt = Math.Abs(at.Y - a.Y);
            if (w < 2 || hgt < 2)
            {
                W.Status = Shell.Lang.T("Sayfa çok küçük: en az iki adım genişlik ve yükseklik");
                InvalidateVisual();
                return;
            }

            int n = 1;
            while (Doc.Sheets.Any(s => s.Name == "Sayfa" + n.ToString(CultureInfo.InvariantCulture))) n++;
            var sheet = new SchematicSheet { Name = "Sayfa" + n.ToString(CultureInfo.InvariantCulture), Sheet = CurrentSheet, Position = new GridPoint(x0, y0), Width = w, Height = hgt };
            W.BeginChange();
            Doc.Sheets.Add(sheet);
            W.CommitChange(Shell.Lang.T("Hiyerarşik sayfa eklendi"));
            Tool = EditorTool.Select;
            SelectItems(new object[] { sheet });
            RequestCommand(SchematicCommand.EditSheet, sheet);
        }

        /// <summary>[sheets] The sheet pin tool: a click on (or near) a sheet's edge puts a pin there, its name typed in place.</summary>
        private void SheetPinToolClick()
        {
            var g = _mouseGrid;
            var sheet = SheetSymbols.Reverse().FirstOrDefault(h =>
            {
                var b = h.Bounds();
                return g.X >= b.MinX - 1 && g.X <= b.MaxX + 1 && g.Y >= b.MinY - 1 && g.Y <= b.MaxY + 1;
            });
            if (sheet == null)
            {
                W.Status = Shell.Lang.T("Sayfa pini: bir sayfa sembolünün kenarına tıklayın");
                return;
            }

            var at = sheet.EdgePoint(g);
            if (sheet.Pins.Any(p => p.Position.Equals(at)))
            {
                W.Status = Shell.Lang.T("Orada zaten bir pin var");
                return;
            }

            // the name of the first hierarchical label inside that has no pin yet, else a new one
            var free = Doc.HierarchicalLabels(sheet.Uid).FirstOrDefault(l => !sheet.Pins.Any(p => string.Equals(p.Name, l.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
            int n = 1;
            while (sheet.Pins.Any(p => p.Name == "PIN" + n.ToString(CultureInfo.InvariantCulture))) n++;
            var pin = new SheetPin { Name = free?.Name.Trim() ?? "PIN" + n.ToString(CultureInfo.InvariantCulture), Position = at, Shape = free?.Shape ?? LabelShape.Input };
            W.BeginChange();
            sheet.Pins.Add(pin);
            W.CommitChange(Shell.Lang.T("Sayfa pini eklendi") + ": " + pin.Name);
            SelectItems(new object[] { sheet });
            RequestCommand(SchematicCommand.EditSheetPin, (sheet, pin));
        }

        /// <summary>[sheets] A hierarchical label at a point (on a sub-sheet it joins its symbol's pin of the same name), its name opened for typing.</summary>
        public NetLabel? AddHierarchicalLabelAt(GridPoint at)
        {
            if (!GuardEditing()) return null;
            var symbol = Doc.FindSheet(CurrentSheet);
            // the name of a pin on this sheet's symbol that has no label yet, else a new one
            string? name = symbol?.Pins.Select(p => p.Name).FirstOrDefault(pn => !Doc.Labels.Any(l => l.Hierarchical && Schematic.SameSheet(l.Sheet, CurrentSheet) &&
                                                                                              string.Equals(l.Name.Trim(), pn.Trim(), StringComparison.OrdinalIgnoreCase)));
            int n = 1;
            while (Doc.Labels.Any(l => l.Name == "HL" + n.ToString(CultureInfo.InvariantCulture))) n++;
            var label = new NetLabel
            {
                Name = name ?? "HL" + n.ToString(CultureInfo.InvariantCulture), Position = at, Hierarchical = true, Sheet = CurrentSheet,
                Shape = symbol?.Pins.FirstOrDefault(p => p.Name == name)?.Shape ?? LabelShape.Input
            };
            W.BeginChange();
            Doc.Labels.Add(label);
            W.CommitChange(Shell.Lang.T("Hiyerarşik etiket eklendi"));
            SelectItems(new object[] { label });
            RequestCommand(SchematicCommand.EditValue, label);
            return label;
        }

        /// <summary>[sheets] Pins for every hierarchical label inside the sheet that has none (KiCad's Sync Sheet Pins).</summary>
        public int SyncSheetPins(SchematicSheet sheet)
        {
            W.BeginChange();
            int added = Doc.ImportSheetPins(sheet);
            if (added > 0) W.CommitChange(string.Format(CultureInfo.InvariantCulture, Shell.Lang.T("{0} sayfa pini eklendi"), added));
            else W.CancelChange();
            InvalidateVisual();
            return added;
        }
    }
}
