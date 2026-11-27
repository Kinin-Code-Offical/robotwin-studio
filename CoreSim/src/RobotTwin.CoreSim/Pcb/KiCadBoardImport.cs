using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Formats;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[kicad-in] Where a footprint stands on a KiCad board.</summary>
    public sealed class KiCadPlacement
    {
        public string RefDes { get; set; } = string.Empty;
        public string Footprint { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Degrees, as this board's components count them (the bottom side's extra half turn taken off).</summary>
        public double Rotation { get; set; }

        public CopperLayer Side { get; set; } = CopperLayer.Top;
    }

    /// <summary>[kicad-in] The layout of a <c>.kicad_pcb</c>: placements, copper, holes and the outline's extent, in this board's frame.</summary>
    public sealed class KiCadLayout
    {
        public List<KiCadPlacement> Components { get; } = new List<KiCadPlacement>();
        public List<Track> Tracks { get; } = new List<Track>();
        public List<Via> Vias { get; } = new List<Via>();
        public List<Zone> Zones { get; } = new List<Zone>();
        public List<MountingHole> Holes { get; } = new List<MountingHole>();
        public double? Thickness { get; set; }
        public int CopperLayers { get; set; } = 2;

        /// <summary>The Edge.Cuts extent (mm), after the shift that puts its corner at the origin; null without an outline.</summary>
        public (double Width, double Height)? Size { get; set; }

        /// <summary>What was moved to put the outline's corner at the origin (subtracted from every coordinate).</summary>
        public (double X, double Y) Shift { get; set; }

        /// <summary>[origins] The board's drill/place file origin and grid origin (after the shift); null where KiCad has none.</summary>
        public PcbPoint? AuxOrigin { get; set; }

        public PcbPoint? GridOrigin { get; set; }

        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// [kicad-in] Reads a KiCad 6–8 board (<c>.kicad_pcb</c>) for its layout: footprint placements by reference designator,
    /// tracks (arcs in short chords), vias, zones, mounting holes, the board thickness and outline. The footprints'
    /// own geometry is left to the library: this is how a board exported to KiCad, placed or routed there, comes back.
    /// Coordinates are shifted so the outline's corner is the origin, as this board's frame has it.
    /// </summary>
    public static class KiCadBoardImport
    {
        public static KiCadLayout Read(string text)
        {
            var root = SExpression.Parse(text ?? string.Empty);
            var layout = new KiCadLayout();
            if (root.Head != "kicad_pcb") layout.Warnings.Add("Bir KiCad kartı değil (kicad_pcb bekleniyordu).");

            layout.Thickness = root.Child("general")?.Child("thickness")?.Number(1, 0) is double th && th > 0 ? th : (double?)null;
            var layers = root.Child("layers");
            if (layers != null)
                layout.CopperLayers = Math.Max(2, layers.Items.Count(l => l.IsList && l.Count > 1 && (l.Items[1].Atom ?? string.Empty).EndsWith(".Cu", StringComparison.Ordinal)));

            var nets = new Dictionary<int, string>();
            foreach (var n in root.Children("net"))
                if (n.Count >= 3 && int.TryParse(n.Items[1].Atom, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code)) nets[code] = n.Items[2].Atom ?? string.Empty;
            string NetOf(SExpression node)
            {
                var net = node.Child("net");
                if (net == null || net.Count < 2) return string.Empty;
                if (net.Count >= 3 && !net.Items[1].IsQuoted && int.TryParse(net.Items[1].Atom, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return net.Items[2].Atom ?? string.Empty;
                if (int.TryParse(net.Items[1].Atom, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)) return nets.TryGetValue(c, out var name) ? name : string.Empty;
                return net.Items[1].Atom ?? string.Empty;   // KiCad 9 names the net itself
            }

            CopperLayer? Layer(SExpression node)
            {
                var l = node.Child("layer");
                return l != null && l.Count > 1 ? PcbLayers.FromKiCadName(l.Items[1].Atom ?? string.Empty) : null;
            }

            // ---- the outline's extent: everything drawn on Edge.Cuts
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            void Grow(double x, double y)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            foreach (var g in root.Children("gr_line", "gr_arc", "gr_rect", "gr_circle", "gr_poly"))
            {
                if ((g.Child("layer")?.Items.ElementAtOrDefault(1)?.Atom ?? string.Empty) != "Edge.Cuts") continue;
                foreach (var pt in Points(g)) Grow(pt.X, pt.Y);
                if (g.Head == "gr_circle" && g.Child("center") is { } c && g.Child("end") is { } e)
                {
                    double r = Math.Sqrt(Math.Pow(e.Number(1) - c.Number(1), 2) + Math.Pow(e.Number(2) - c.Number(2), 2));
                    Grow(c.Number(1) - r, c.Number(2) - r);
                    Grow(c.Number(1) + r, c.Number(2) + r);
                }
            }

            double ox = 0, oy = 0;
            if (minX <= maxX)
            {
                ox = minX;
                oy = minY;
                layout.Size = (maxX - minX, maxY - minY);
            }
            else
            {
                layout.Warnings.Add("Kartın dış hattı (Edge.Cuts) yok: koordinatlar olduğu gibi alındı.");
            }

            layout.Shift = (ox, oy);
            PcbPoint? Origin(string name)
            {
                var n = root.Child("setup")?.Child(name);
                if (n == null || n.Count < 3) return null;
                double x = n.Number(1), y = n.Number(2);
                return x == 0 && y == 0 ? (PcbPoint?)null : new PcbPoint(x - ox, y - oy);   // KiCad writes (0, 0) for none
            }

            layout.AuxOrigin = Origin("aux_axis_origin");
            layout.GridOrigin = Origin("grid_origin");

            // ---- footprints: placements, and the mounting holes this board writes as footprints
            foreach (var f in root.Children("footprint", "module"))
            {
                string name = f.Count > 1 ? f.Items[1].Atom ?? string.Empty : string.Empty;
                var at = f.Child("at");
                double x = (at?.Number(1, 0) ?? 0) - ox, y = (at?.Number(2, 0) ?? 0) - oy, angle = at?.Number(3, 0) ?? 0;
                bool bottom = (f.Child("layer")?.Items.ElementAtOrDefault(1)?.Atom ?? "F.Cu").StartsWith("B.", StringComparison.Ordinal);
                string reference = Property(f, "Reference"), value = Property(f, "Value");
                if (name.IndexOf("MountingHole", StringComparison.OrdinalIgnoreCase) >= 0 && f.Children("pad").Any())
                {
                    var pad = f.Children("pad").First();
                    double drill = pad.Child("drill")?.Number(1, 0) ?? 0, size = pad.Child("size")?.Number(1, 0) ?? 0;
                    bool plated = (pad.Items.ElementAtOrDefault(2)?.Atom ?? string.Empty) == "thru_hole";
                    layout.Holes.Add(new MountingHole { X = x, Y = y, Drill = drill > 0 ? drill : 3.2, PadDiameter = plated && size > drill ? size : 0, Net = NetOf(pad) });
                    continue;
                }

                if (reference.Length == 0)
                {
                    layout.Warnings.Add($"Referansı olmayan bir ayak izi atlandı ({name}).");
                    continue;
                }

                double rotation = bottom ? angle - 180 : angle;
                rotation = ((rotation % 360) + 360) % 360;
                layout.Components.Add(new KiCadPlacement { RefDes = reference, Footprint = name, Value = value, X = x, Y = y, Rotation = rotation, Side = bottom ? CopperLayer.Bottom : CopperLayer.Top });
            }

            // ---- copper
            foreach (var s in root.Children("segment"))
            {
                if (Layer(s) is not { } l || s.Child("start") is not { } a || s.Child("end") is not { } b) continue;
                layout.Tracks.Add(new Track { X1 = a.Number(1) - ox, Y1 = a.Number(2) - oy, X2 = b.Number(1) - ox, Y2 = b.Number(2) - oy, Width = s.Child("width")?.Number(1, 0.25) ?? 0.25, Layer = l, Net = NetOf(s) });
            }

            foreach (var s in root.Children("arc"))
            {
                if (Layer(s) is not { } l) continue;
                var pts = ArcChords(s).ToList();
                double w = s.Child("width")?.Number(1, 0.25) ?? 0.25;
                for (int i = 0; i + 1 < pts.Count; i++)
                    layout.Tracks.Add(new Track { X1 = pts[i].X - ox, Y1 = pts[i].Y - oy, X2 = pts[i + 1].X - ox, Y2 = pts[i + 1].Y - oy, Width = w, Layer = l, Net = NetOf(s) });
            }

            foreach (var v in root.Children("via"))
            {
                if (v.Child("at") is not { } at) continue;
                var spanNode = v.Child("layers");
                var from = spanNode != null && spanNode.Count > 1 ? PcbLayers.FromKiCadName(spanNode.Items[1].Atom ?? string.Empty) : null;
                var to = spanNode != null && spanNode.Count > 2 ? PcbLayers.FromKiCadName(spanNode.Items[2].Atom ?? string.Empty) : null;
                layout.Vias.Add(new Via
                {
                    X = at.Number(1) - ox, Y = at.Number(2) - oy,
                    Diameter = v.Child("size")?.Number(1, 0.9) ?? 0.9, Drill = v.Child("drill")?.Number(1, 0.45) ?? 0.45,
                    StartLayer = from ?? CopperLayer.Top, EndLayer = to ?? CopperLayer.Bottom, Net = NetOf(v),
                });
            }

            foreach (var z in root.Children("zone"))
            {
                var poly = z.Child("polygon")?.Child("pts");
                if (poly == null) continue;
                string net = z.Child("net_name")?.Items.ElementAtOrDefault(1)?.Atom ?? NetOf(z);
                var layerNames = new List<string>();
                if (z.Child("layer") is { } one && one.Count > 1) layerNames.Add(one.Items[1].Atom ?? string.Empty);
                if (z.Child("layers") is { } many) layerNames.AddRange(many.Items.Skip(1).Select(i => i.Atom ?? string.Empty));
                var outline = poly.Children("xy").Select(p => new PcbPoint(p.Number(1) - ox, p.Number(2) - oy)).ToList();
                foreach (var ln in layerNames)
                {
                    if (PcbLayers.FromKiCadName(ln) is not { } l) continue;
                    var zone = new Zone { Net = net, Layer = l, Outline = new List<PcbPoint>(outline) };
                    if (z.Child("connect_pads")?.Child("clearance")?.Number(1, 0) is double c && c > 0) zone.Clearance = c;
                    if (z.Child("min_thickness")?.Number(1, 0) is double mt && mt > 0) zone.MinWidth = mt;
                    layout.Zones.Add(zone);
                }
            }

            return layout;
        }

        private static string Property(SExpression footprint, string name)
        {
            foreach (var p in footprint.Children("property"))
                if (p.Count > 2 && p.Items[1].Atom == name) return p.Items[2].Atom ?? string.Empty;
            foreach (var t in footprint.Children("fp_text"))   // KiCad 6/7
                if (t.Count > 2 && string.Equals(t.Items[1].Atom, name, StringComparison.OrdinalIgnoreCase)) return t.Items[2].Atom ?? string.Empty;
            return string.Empty;
        }

        private static IEnumerable<(double X, double Y)> Points(SExpression g)
        {
            foreach (var key in new[] { "start", "mid", "end", "center" })
                if (g.Child(key) is { } p && p.Count >= 3) yield return (p.Number(1), p.Number(2));
            if (g.Child("pts") is { } pts)
                foreach (var xy in pts.Children("xy")) yield return (xy.Number(1), xy.Number(2));
        }

        /// <summary>An arc track (start, mid, end) as chords of at most 10°.</summary>
        private static IEnumerable<(double X, double Y)> ArcChords(SExpression s)
        {
            var a = s.Child("start");
            var m = s.Child("mid");
            var b = s.Child("end");
            if (a == null || b == null) yield break;
            double x1 = a.Number(1), y1 = a.Number(2), x3 = b.Number(1), y3 = b.Number(2);
            if (m == null)
            {
                yield return (x1, y1);
                yield return (x3, y3);
                yield break;
            }

            double x2 = m.Number(1), y2 = m.Number(2);
            double d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));
            if (Math.Abs(d) < 1e-12)
            {
                yield return (x1, y1);
                yield return (x3, y3);
                yield break;
            }

            double s1 = x1 * x1 + y1 * y1, s2 = x2 * x2 + y2 * y2, s3 = x3 * x3 + y3 * y3;
            double cx = (s1 * (y2 - y3) + s2 * (y3 - y1) + s3 * (y1 - y2)) / d, cy = (s1 * (x3 - x2) + s2 * (x1 - x3) + s3 * (x2 - x1)) / d;
            double r = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
            double a1 = Math.Atan2(y1 - cy, x1 - cx), a2 = Math.Atan2(y2 - cy, x2 - cx), a3 = Math.Atan2(y3 - cy, x3 - cx);
            double sweep = a3 - a1, toMid = a2 - a1;
            while (sweep <= -Math.PI * 2) sweep += Math.PI * 2;
            while (toMid < 0) toMid += Math.PI * 2;
            while (sweep < 0) sweep += Math.PI * 2;
            if (toMid > sweep) sweep -= Math.PI * 2;   // the middle point says which way round
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 18)));
            for (int i = 0; i <= n; i++)
            {
                double ang = a1 + sweep * i / n;
                yield return (cx + r * Math.Cos(ang), cy + r * Math.Sin(ang));
            }
        }
    }

    /// <summary>[kicad-in] One difference between this board and a KiCad layout of it.</summary>
    public sealed class BoardChange
    {
        public string What { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Applying the layout carries it over (false: only reported, e.g. a part the schematic does not have).</summary>
        public bool Applies { get; set; }

        public override string ToString() => Text;
    }

    /// <summary>
    /// [kicad-in] What a KiCad layout changes on this board, and carrying it over: placements are matched by reference
    /// designator (moved, turned, flipped); tracks, vias, zones and mounting holes are compared net by net and replaced
    /// whole. Footprints and the schematic stay this project's: a part KiCad has and the schematic does not is reported,
    /// not added.
    /// </summary>
    public static class BoardDiff
    {
        private const double Tolerance = 0.005;   // mm: below the file's precision after a round trip

        public static List<BoardChange> Compare(BoardDocument board, KiCadLayout layout)
        {
            var changes = new List<BoardChange>();
            var placed = layout.Components.GroupBy(c => c.RefDes, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var comp in board.Components)
            {
                if (!placed.TryGetValue(comp.RefDes, out var k))
                {
                    changes.Add(new BoardChange { What = "missing", Text = $"{comp.RefDes} KiCad kartında yok", X = comp.X, Y = comp.Y });
                    continue;
                }

                double rot = QuarterTurn(k.Rotation);
                if (Math.Abs(k.Rotation - rot) > 0.01 && Math.Abs(k.Rotation - rot - 360) > 0.01)
                    changes.Add(new BoardChange { What = "angle", Text = $"{comp.RefDes} KiCad'de {k.Rotation:0.#}° döndürülmüş: en yakın çeyrek tur ({rot:0}°) alınır", X = k.X, Y = k.Y, Applies = true });
                if (Math.Abs(k.X - comp.X) > Tolerance || Math.Abs(k.Y - comp.Y) > Tolerance)
                    changes.Add(new BoardChange { What = "moved", Text = string.Format(CultureInfo.InvariantCulture, "{0} taşındı: ({1:0.###}, {2:0.###}) → ({3:0.###}, {4:0.###}) mm", comp.RefDes, comp.X, comp.Y, k.X, k.Y), X = k.X, Y = k.Y, Applies = true });
                if (((int)rot - comp.Rotation % 360 + 360) % 360 != 0)
                    changes.Add(new BoardChange { What = "turned", Text = $"{comp.RefDes} döndürüldü: {(comp.Rotation % 360 + 360) % 360}° → {rot:0}°", X = k.X, Y = k.Y, Applies = true });
                if (k.Side != comp.Side)
                    changes.Add(new BoardChange { What = "flipped", Text = $"{comp.RefDes} öbür yüze alındı: {(k.Side == CopperLayer.Bottom ? "alt" : "üst")}", X = k.X, Y = k.Y, Applies = true });
                string fpName = k.Footprint.Contains(':') ? k.Footprint.Substring(k.Footprint.IndexOf(':') + 1) : k.Footprint;
                if (fpName.Length > 0 && !string.Equals(fpName, comp.FootprintId, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new BoardChange { What = "footprint", Text = $"{comp.RefDes} KiCad'de başka ayak iziyle: {fpName} (bu projede {comp.FootprintId}; ayak izi şemadan gelir, değişmez)", X = k.X, Y = k.Y });
            }

            foreach (var k in layout.Components)
                if (!board.Components.Any(c => string.Equals(c.RefDes, k.RefDes, StringComparison.OrdinalIgnoreCase)))
                    changes.Add(new BoardChange { What = "extra", Text = $"{k.RefDes} ({k.Footprint}) yalnız KiCad'de: şemaya eklenmeden alınmaz", X = k.X, Y = k.Y });

            // copper, net by net
            var nets = board.Tracks.Select(t => t.Net).Concat(layout.Tracks.Select(t => t.Net)).Concat(board.Vias.Select(v => v.Net)).Concat(layout.Vias.Select(v => v.Net))
                .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            foreach (var net in nets)
            {
                var a = board.Tracks.Where(t => string.Equals(t.Net, net, StringComparison.OrdinalIgnoreCase)).ToList();
                var b = layout.Tracks.Where(t => string.Equals(t.Net, net, StringComparison.OrdinalIgnoreCase)).ToList();
                double la = a.Sum(Length), lb = b.Sum(Length);
                int va = board.Vias.Count(v => string.Equals(v.Net, net, StringComparison.OrdinalIgnoreCase)), vb = layout.Vias.Count(v => string.Equals(v.Net, net, StringComparison.OrdinalIgnoreCase));
                if (a.Count != b.Count || Math.Abs(la - lb) > 0.01 || va != vb || !SameSegments(a, b))
                {
                    var at = b.FirstOrDefault() ?? a.FirstOrDefault();
                    changes.Add(new BoardChange
                    {
                        What = "routed",
                        Text = string.Format(CultureInfo.InvariantCulture, "{0}: {1} yol {2:0.0} mm, {3} via → {4} yol {5:0.0} mm, {6} via", net, a.Count, la, va, b.Count, lb, vb),
                        X = at?.X1 ?? 0, Y = at?.Y1 ?? 0, Applies = true,
                    });
                }
            }

            if (board.Zones.Count != layout.Zones.Count)
                changes.Add(new BoardChange { What = "zones", Text = $"Bakır dolgu: {board.Zones.Count} → {layout.Zones.Count}", Applies = true });
            if (board.MountingHoles.Count != layout.Holes.Count || board.MountingHoles.Zip(layout.Holes, (h, k) => Math.Abs(h.X - k.X) > Tolerance || Math.Abs(h.Y - k.Y) > Tolerance).Any(d => d))
                changes.Add(new BoardChange { What = "holes", Text = $"Montaj delikleri: {board.MountingHoles.Count} → {layout.Holes.Count}", Applies = true });
            if (layout.Size is { } size && board.Outline.Count == 0 && (Math.Abs(size.Width - board.Width) > 0.05 || Math.Abs(size.Height - board.Height) > 0.05))
                changes.Add(new BoardChange { What = "size", Text = string.Format(CultureInfo.InvariantCulture, "Kart ölçüsü: {0:0.##} × {1:0.##} → {2:0.##} × {3:0.##} mm", board.Width, board.Height, size.Width, size.Height), Applies = true });
            return changes;
        }

        /// <summary>Carries the layout over: placements of the parts both have, and the copper, holes and (a rectangular board's) size whole. Returns what it changed.</summary>
        public static int Apply(BoardDocument board, KiCadLayout layout)
        {
            var changes = Compare(board, layout).Where(c => c.Applies).ToList();
            var placed = layout.Components.GroupBy(c => c.RefDes, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var comp in board.Components)
            {
                if (!placed.TryGetValue(comp.RefDes, out var k)) continue;
                comp.X = k.X;
                comp.Y = k.Y;
                comp.Rotation = (int)QuarterTurn(k.Rotation);
                comp.Side = k.Side;
            }

            if (changes.Any(c => c.What == "routed"))
            {
                board.Tracks.Clear();
                board.Tracks.AddRange(layout.Tracks);
                board.Vias.Clear();
                board.Vias.AddRange(layout.Vias);
            }

            if (changes.Any(c => c.What == "zones") || layout.Zones.Count > 0)
            {
                board.Zones.Clear();
                board.Zones.AddRange(layout.Zones);
                if (board.Zones.Count > 0) ZoneFiller.FillAll(board);
            }

            if (changes.Any(c => c.What == "holes"))
            {
                board.MountingHoles.Clear();
                board.MountingHoles.AddRange(layout.Holes);
            }

            if (layout.AuxOrigin != null) board.AuxOrigin = layout.AuxOrigin;   // [origins]
            if (layout.GridOrigin != null) board.GridOrigin = layout.GridOrigin;
            if (layout.Size is { } size && board.Outline.Count == 0)
            {
                board.Width = Math.Round(size.Width, 4);
                board.Height = Math.Round(size.Height, 4);
            }

            if (layout.Thickness is double t) board.Stackup.Thickness = t;
            return changes.Count;
        }

        private static double QuarterTurn(double degrees) => ((Math.Round(degrees / 90) * 90) % 360 + 360) % 360;

        private static double Length(Track t) => Math.Sqrt((t.X2 - t.X1) * (t.X2 - t.X1) + (t.Y2 - t.Y1) * (t.Y2 - t.Y1));

        /// <summary>The same segments, in either direction and any order (to the file's precision).</summary>
        private static bool SameSegments(List<Track> a, List<Track> b)
        {
            if (a.Count != b.Count) return false;
            var left = new List<Track>(b);
            foreach (var t in a)
            {
                int i = left.FindIndex(u => u.Layer == t.Layer && Math.Abs(u.Width - t.Width) < 1e-3 &&
                    ((Near(u.X1, t.X1) && Near(u.Y1, t.Y1) && Near(u.X2, t.X2) && Near(u.Y2, t.Y2)) || (Near(u.X1, t.X2) && Near(u.Y1, t.Y2) && Near(u.X2, t.X1) && Near(u.Y2, t.Y1))));
                if (i < 0) return false;
                left.RemoveAt(i);
            }

            return true;
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) <= Tolerance;
    }
}
