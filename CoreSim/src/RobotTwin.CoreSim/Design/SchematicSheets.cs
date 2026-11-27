using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[sheets] Which way a hierarchical label or a sheet pin carries its signal (KiCad's label shapes).</summary>
    public enum LabelShape
    {
        Input,
        Output,
        Bidirectional,
        TriState,
        Passive
    }

    /// <summary>
    /// [sheets] A sheet pin (KiCad's hierarchical pin): a point on a sheet symbol's edge, on the sheet the symbol is drawn
    /// on, that joins the hierarchical label of the same name inside the sub-sheet.
    /// </summary>
    public sealed class SheetPin
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Where it sits on the parent sheet (on the symbol's edge), in grid units.</summary>
        public GridPoint Position { get; set; }

        public LabelShape Shape { get; set; } = LabelShape.Input;
    }

    /// <summary>
    /// [sheets] A sheet symbol (KiCad's hierarchical sheet): a framed block on its parent sheet that stands for a sub-sheet.
    /// The sub-sheet's parts, wires and labels carry its <see cref="Uid"/> in their Sheet; its hierarchical labels join the
    /// block's pins of the same name; its local labels are its own (named /Name/label in the netlist), while global labels
    /// and power symbols join across every sheet. All parts stay in the one schematic, so the board, the simulation and the
    /// bill of materials see the whole design.
    /// </summary>
    public sealed class SchematicSheet
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        /// <summary>Its identity: the sub-sheet's items carry it.</summary>
        public string Uid { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = "Sayfa";

        /// <summary>The sheet it is drawn on; null: the root sheet.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sheet { get; set; }

        /// <summary>Its top left corner, in grid units.</summary>
        public GridPoint Position { get; set; }

        public double Width { get; set; } = 12;
        public double Height { get; set; } = 8;

        public List<SheetPin> Pins { get; set; } = new List<SheetPin>();

        public GridRect Bounds() => new GridRect(Position.X, Position.Y, Position.X + Width, Position.Y + Height);

        /// <summary>The nearest point on its edge to <paramref name="p"/> (where a pin dropped there goes).</summary>
        public GridPoint EdgePoint(GridPoint p)
        {
            double x0 = Position.X, y0 = Position.Y, x1 = x0 + Width, y1 = y0 + Height;
            double cx = Math.Max(x0, Math.Min(x1, p.X)), cy = Math.Max(y0, Math.Min(y1, p.Y));
            double dl = Math.Abs(p.X - x0), dr = Math.Abs(p.X - x1), dt = Math.Abs(p.Y - y0), db = Math.Abs(p.Y - y1);
            double m = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
            if (m == dl) return new GridPoint(x0, Math.Round(cy));
            if (m == dr) return new GridPoint(x1, Math.Round(cy));
            if (m == dt) return new GridPoint(Math.Round(cx), y0);
            return new GridPoint(Math.Round(cx), y1);
        }

        /// <summary>Which edge a pin is on: 0 left, 1 right, 2 top, 3 bottom (its label is drawn inside, away from it).</summary>
        public int EdgeOf(GridPoint p)
        {
            double x0 = Position.X, y0 = Position.Y, x1 = x0 + Width, y1 = y0 + Height;
            double dl = Math.Abs(p.X - x0), dr = Math.Abs(p.X - x1), dt = Math.Abs(p.Y - y0), db = Math.Abs(p.Y - y1);
            double m = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
            return m == dl ? 0 : m == dr ? 1 : m == dt ? 2 : 3;
        }
    }

    public sealed partial class Schematic
    {
        /// <summary>[sheets] The sheet symbols, on whichever sheet each is drawn (KiCad's hierarchy).</summary>
        public List<SchematicSheet> Sheets { get; set; } = new List<SchematicSheet>();

        /// <summary>[sheets] Whether two sheet references are the same sheet (null and "" both mean the root).</summary>
        public static bool SameSheet(string? a, string? b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);

        public SchematicSheet? FindSheet(string? uid) =>
            string.IsNullOrEmpty(uid) ? null : Sheets.FirstOrDefault(s => string.Equals(s.Uid, uid, StringComparison.Ordinal));

        /// <summary>[sheets] The sheet symbols drawn on a sheet.</summary>
        public IEnumerable<SchematicSheet> SheetsOn(string? sheet) => Sheets.Where(s => SameSheet(s.Sheet, sheet));

        /// <summary>[sheets] The symbols from the root down to a sheet (empty for the root).</summary>
        public List<SchematicSheet> SheetChain(string? uid)
        {
            var chain = new List<SchematicSheet>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var s = FindSheet(uid); s != null && seen.Add(s.Uid); s = FindSheet(s.Sheet)) chain.Insert(0, s);
            return chain;
        }

        /// <summary>[sheets] KiCad's sheet path: "/" for the root, "/Motor/" and "/Motor/Driver/" below it.</summary>
        public string SheetPath(string? uid) => "/" + string.Concat(SheetChain(uid).Select(s => s.Name + "/"));

        /// <summary>[sheets] How deep a sheet is: 0 the root, 1 a sheet on it …</summary>
        public int SheetDepth(string? uid) => SheetChain(uid).Count;

        /// <summary>[sheets] The sheet an item is on (null: the root).</summary>
        public static string? SheetOf(object item) => item switch
        {
            SchematicPart p => p.Sheet,
            SchematicWire w => w.Sheet,
            NetLabel l => l.Sheet,
            NoConnectFlag n => n.Sheet,
            SchematicNote t => t.Sheet,
            SchematicShape s => s.Sheet,
            SchematicSheet h => h.Sheet,
            _ => null
        };

        /// <summary>[sheets] Puts an item on a sheet.</summary>
        public static void SetSheet(object item, string? sheet)
        {
            sheet = string.IsNullOrEmpty(sheet) ? null : sheet;
            switch (item)
            {
                case SchematicPart p: p.Sheet = sheet; break;
                case SchematicWire w: w.Sheet = sheet; break;
                case NetLabel l: l.Sheet = sheet; break;
                case NoConnectFlag n: n.Sheet = sheet; break;
                case SchematicNote t: t.Sheet = sheet; break;
                case SchematicShape s: s.Sheet = sheet; break;
                case SchematicSheet h: h.Sheet = sheet; break;
            }
        }

        /// <summary>[sheets] A sheet and every sheet below it.</summary>
        public HashSet<string> SheetSubtree(string uid)
        {
            var all = new HashSet<string>(StringComparer.Ordinal) { uid };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var s in Sheets)
                    if (s.Sheet != null && all.Contains(s.Sheet) && all.Add(s.Uid))
                        grew = true;
            }

            return all;
        }

        /// <summary>[sheets] Removes a sheet symbol with its sub-sheet: the sheets below it and everything on them.</summary>
        public int RemoveSheet(SchematicSheet sheet)
        {
            var gone = SheetSubtree(sheet.Uid);
            bool On(string? s) => s != null && gone.Contains(s);
            int n = Parts.RemoveAll(p => On(p.Sheet)) + Wires.RemoveAll(w => On(w.Sheet)) + Labels.RemoveAll(l => On(l.Sheet)) +
                    NoConnects.RemoveAll(x => On(x.Sheet)) + Notes.RemoveAll(x => On(x.Sheet)) + Shapes.RemoveAll(x => On(x.Sheet));
            n += Sheets.RemoveAll(s => gone.Contains(s.Uid) || ReferenceEquals(s, sheet));
            return n;
        }

        /// <summary>[sheets] The hierarchical labels inside a sub-sheet (one per name, in the order they were placed).</summary>
        public List<NetLabel> HierarchicalLabels(string uid) =>
            Labels.Where(l => l.Hierarchical && SameSheet(l.Sheet, uid) && !string.IsNullOrWhiteSpace(l.Name))
                  .GroupBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        /// <summary>
        /// [sheets] KiCad's "import sheet pins": a pin on the symbol for every hierarchical label inside that has none yet,
        /// down its left edge (inputs) or right edge (the rest), a grid step apart. Returns how many were added.
        /// </summary>
        public int ImportSheetPins(SchematicSheet sheet)
        {
            int added = 0;
            foreach (var label in HierarchicalLabels(sheet.Uid))
            {
                if (sheet.Pins.Any(p => string.Equals(p.Name, label.Name.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                bool left = label.Shape == LabelShape.Input;
                double x = left ? sheet.Position.X : sheet.Position.X + sheet.Width;
                double y = sheet.Position.Y + 1;
                while (sheet.Pins.Any(p => Math.Abs(p.Position.X - x) < 1e-6 && Math.Abs(p.Position.Y - y) < 1e-6)) y += 1;
                if (y > sheet.Position.Y + sheet.Height - 1) sheet.Height = y - sheet.Position.Y + 1;
                sheet.Pins.Add(new SheetPin { Name = label.Name.Trim(), Position = new GridPoint(x, y), Shape = label.Shape });
                added++;
            }

            return added;
        }
    }
}
