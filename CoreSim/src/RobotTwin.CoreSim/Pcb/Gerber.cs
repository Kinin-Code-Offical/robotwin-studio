using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// Fabrication output: RS-274X Gerber layers with X2 file attributes for every copper
    /// layer of the stackup (zone fills as fractured regions), solder mask, paste,
    /// silkscreen on both sides (clipped at mask openings) and the outline (lines and arcs);
    /// Excellon drill files for plated through holes, non-plated holes and each blind/buried
    /// via span; bill of materials and pick-and-place. Packed as a zip board houses accept.
    /// Outdated zone fills are refilled first.
    /// </summary>
    public static class GerberExporter
    {
        /// <param name="fitting">[variants] The alternative being built: its parts not fitted leave the bill of materials and the
        /// pick-and-place (their pads stay), its values replace the drawn ones there. Null: the design as drawn.</param>
        public static Dictionary<string, string> Generate(BoardDocument board, string projectName = "robotwin", Design.Fitting? fitting = null)
        {
            ZoneFiller.RefillOutdated(board);
            string Name(string suffix) => projectName + "-" + suffix;
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var stack = board.CopperLayers();
            for (int i = 0; i < stack.Count; i++)
            {
                var l = stack[i];
                string ext = l == CopperLayer.Top ? "gtl" : l == CopperLayer.Bottom ? "gbl" : "g" + i.ToString(CultureInfo.InvariantCulture);
                files[Name(PcbLayers.FileName(l) + "." + ext)] = Copper(board, l, i + 1, stack.Count);
            }

            files[Name("F_Mask.gts")] = Mask(board, CopperLayer.Top);
            files[Name("B_Mask.gbs")] = Mask(board, CopperLayer.Bottom);
            files[Name("F_Paste.gtp")] = Paste(board, CopperLayer.Top);
            files[Name("B_Paste.gbp")] = Paste(board, CopperLayer.Bottom);
            files[Name("F_Silkscreen.gto")] = Silkscreen(board, CopperLayer.Top);
            files[Name("B_Silkscreen.gbo")] = Silkscreen(board, CopperLayer.Bottom);
            files[Name("Edge_Cuts.gm1")] = Outline(board);
            foreach (var kv in Drills(board, projectName)) files[kv.Key] = kv.Value;
            files[Name("BOM.csv")] = Bom(board, fitting);
            files[Name("CPL.csv")] = PickAndPlace(board, fitting);
            return files;
        }

        public static void WriteZip(BoardDocument board, string zipPath, string projectName = "robotwin", Design.Fitting? fitting = null)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var kv in Generate(board, projectName, fitting))
            {
                var entry = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(kv.Value);
            }
        }

        // ------------------------------------------------------------- Gerber writer

        private sealed class GerberWriter
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly Dictionary<string, int> _apertures = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly StringBuilder _body = new StringBuilder();
            private readonly double _height;
            private int _current = -1;

            public GerberWriter(double boardHeight, string function, string polarity = "Positive")
            {
                _height = boardHeight;
                _sb.Append("%TF.GenerationSoftware,RobotWin,Studio,2.0*%\n");
                _sb.Append("%TF.FileFunction,").Append(function).Append("*%\n");
                _sb.Append("%TF.FilePolarity,").Append(polarity).Append("*%\n");
                _sb.Append("%FSLAX46Y46*%\n%MOMM*%\n%LPD*%\n");
            }

            private static string F(double mm) => ((long)Math.Round(mm * 1e6)).ToString(CultureInfo.InvariantCulture);

            private static string D(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

            private string X(double x) => "X" + F(x);

            private string Y(double y) => "Y" + F(_height - y);

            private void Select(string template)
            {
                if (!_apertures.TryGetValue(template, out int code))
                {
                    code = 10 + _apertures.Count;
                    _apertures[template] = code;
                }

                if (code != _current)
                {
                    _body.Append('D').Append(code.ToString(CultureInfo.InvariantCulture)).Append("*\n");
                    _current = code;
                }
            }

            public void Line(double x1, double y1, double x2, double y2, double width)
            {
                Select("C," + D(width));
                _body.Append(X(x1)).Append(Y(y1)).Append("D02*\n");
                _body.Append(X(x2)).Append(Y(y2)).Append("D01*\n");
            }

            public void Circle(double cx, double cy, double r, double width)
            {
                Select("C," + D(width));
                _body.Append("G75*\n");
                _body.Append(X(cx + r)).Append(Y(cy)).Append("D02*\n");
                _body.Append("G03").Append(X(cx + r)).Append(Y(cy)).Append("I").Append(F(-r)).Append("J0D01*\n");
                _body.Append("G01*\n");
            }

            /// <summary>Arc in board coordinates; <paramref name="angleIncreasing"/> is the direction in the Y-down board frame.</summary>
            public void Arc(double x1, double y1, double x2, double y2, double cx, double cy, bool angleIncreasing, double width)
            {
                Select("C," + D(width));
                _body.Append("G75*\n");
                _body.Append(X(x1)).Append(Y(y1)).Append("D02*\n");
                // Y flips, so an arc that turns with increasing board angle is clockwise in Gerber.
                _body.Append(angleIncreasing ? "G02" : "G03").Append(X(x2)).Append(Y(y2))
                     .Append("I").Append(F(cx - x1)).Append("J").Append(F(y1 - cy)).Append("D01*\n");
                _body.Append("G01*\n");
            }

            public void Flash(PadShape shape, double x, double y, double w, double h, double expand = 0)
            {
                w += 2 * expand;
                h += 2 * expand;
                string template;
                switch (shape)
                {
                    case PadShape.Circle: template = "C," + D(Math.Max(w, h)); break;
                    case PadShape.Oval: template = "O," + D(w) + "X" + D(h); break;
                    default: template = "R," + D(w) + "X" + D(h); break;
                }

                Select(template);
                _body.Append(X(x)).Append(Y(y)).Append("D03*\n");
            }

            public void Region(IReadOnlyList<PcbPoint> points)
            {
                if (points.Count < 3) return;
                _body.Append("G36*\n");
                _body.Append(X(points[0].X)).Append(Y(points[0].Y)).Append("D02*\n");
                for (int i = 1; i < points.Count; i++) _body.Append(X(points[i].X)).Append(Y(points[i].Y)).Append("D01*\n");
                _body.Append(X(points[0].X)).Append(Y(points[0].Y)).Append("D01*\n");
                _body.Append("G37*\n");
            }

            public override string ToString()
            {
                var result = new StringBuilder(_sb.ToString());
                foreach (var kv in _apertures.OrderBy(k => k.Value))
                {
                    result.Append("%ADD").Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append(kv.Key).Append("*%\n");
                }

                result.Append("G01*\n").Append(_body).Append("M02*\n");
                return result.ToString();
            }
        }

        /// <summary>Pad copper: flashed apertures; round-rect pads as exact regions.</summary>
        private static void PadCopper(GerberWriter g, PlacedPad p, double expand = 0)
        {
            if (p.Pad.Shape == PadShape.RoundRect)
            {
                g.Region(Shapes.Pad(PadShape.RoundRect, p.X, p.Y, p.Width, p.Height, p.Pad.CornerRatio, expand, 0.001));
            }
            else
            {
                g.Flash(p.Pad.Shape, p.X, p.Y, p.Width, p.Height, expand);
            }
        }

        private static string Copper(BoardDocument board, CopperLayer layer, int index, int count)
        {
            string function = layer == CopperLayer.Top ? "Copper,L1,Top"
                : layer == CopperLayer.Bottom ? "Copper,L" + count.ToString(CultureInfo.InvariantCulture) + ",Bot"
                : "Copper,L" + index.ToString(CultureInfo.InvariantCulture) + ",Inr";
            var g = new GerberWriter(board.Height, function);
            int layers = board.LayerCount;

            foreach (var zone in board.Zones.Where(z => z.Layer == layer && z.IsFilled))
            {
                foreach (var region in zone.FilledRegions) g.Region(Polygon.Fracture(region));
            }

            foreach (var td in Teardrops.Of(board).Where(td => td.Layer == layer)) g.Region(td.Outline);   // [teardrop]
            foreach (var t in board.Tracks.Where(t => t.Layer == layer)) g.Line(t.X1, t.Y1, t.X2, t.Y2, t.Width);
            foreach (var p in board.Pads().Where(p => p.OnLayer(layer))) PadCopper(g, p);
            foreach (var v in board.Vias.Where(v => v.Spans(layer, layers))) g.Flash(PadShape.Circle, v.X, v.Y, v.Diameter, v.Diameter);
            foreach (var h in board.MountingHoles.Where(h => h.Plated)) g.Flash(PadShape.Circle, h.X, h.Y, h.PadDiameter, h.PadDiameter);
            return g.ToString();
        }

        /// <summary>Solder mask openings grow this much all round past the copper they open (mm).</summary>
        public const double MaskExpansion = 0.05;

        /// <summary>Mask openings on one side: pads with copper there, non-plated holes and mounting holes (vias are tented).</summary>
        public static IEnumerable<(PlacedPad? Pad, double X, double Y, double W, double H, PadShape Shape)> MaskOpenings(BoardDocument board, CopperLayer side)
        {
            foreach (var p in board.Pads())
            {
                if (p.Pad.IsThroughHole && p.Pad.NonPlated)
                {
                    yield return (p, p.X, p.Y, p.Pad.Drill, p.Pad.Drill, PadShape.Circle);
                }
                else if (p.OnLayer(side))
                {
                    yield return (p, p.X, p.Y, p.Width, p.Height, p.Pad.Shape);
                }
            }

            foreach (var h in board.MountingHoles)
            {
                double d = h.Plated ? h.PadDiameter : h.Drill;
                yield return (null, h.X, h.Y, d, d, PadShape.Circle);
            }
        }

        private static string Mask(BoardDocument board, CopperLayer side)
        {
            var g = new GerberWriter(board.Height, side == CopperLayer.Top ? "Soldermask,Top" : "Soldermask,Bot", "Negative");
            foreach (var o in MaskOpenings(board, side))
            {
                if (o.Pad is { } p && p.Pad.Shape == PadShape.RoundRect && !(p.Pad.IsThroughHole && p.Pad.NonPlated)) PadCopper(g, p, MaskExpansion);
                else g.Flash(o.Shape, o.X, o.Y, o.W, o.H, MaskExpansion);
            }

            return g.ToString();
        }

        private static string Paste(BoardDocument board, CopperLayer side)
        {
            var g = new GerberWriter(board.Height, side == CopperLayer.Top ? "Paste,Top" : "Paste,Bot");
            foreach (var p in board.Pads().Where(p => !p.Pad.IsThroughHole && p.SmdLayer == side)) PadCopper(g, p);
            return g.ToString();
        }

        private static string Silkscreen(BoardDocument board, CopperLayer side)
        {
            var g = new GerberWriter(board.Height, side == CopperLayer.Top ? "Legend,Top" : "Legend,Bot");
            foreach (var (s, width) in SilkStrokes(board, side)) g.Line(s.X1, s.Y1, s.X2, s.Y2, width);
            return g.ToString();
        }

        /// <summary>
        /// The legend on one side as pen strokes (board frame) with their widths: footprints' outlines, reference designators
        /// and texts, clipped where the mask opens (silk never prints on exposed copper).
        /// </summary>
        public static List<(Segment2 Stroke, double Width)> SilkStrokes(BoardDocument board, CopperLayer side)
        {
            var strokes = new List<(Segment2, double)>();
            double w = board.Rules.SilkWidth;
            var keepOut = MaskOpenings(board, side).Select(o => (o.X, o.Y, o.W / 2 + MaskExpansion + w / 2, o.H / 2 + MaskExpansion + w / 2)).ToList();
            void Emit(Segment2 s, double width)
            {
                foreach (var piece in Silk.Clip(s, keepOut)) strokes.Add((piece, width));
            }

            foreach (var comp in board.Components.Where(c => c.Side == side))
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                foreach (var s in Silk.FootprintStrokes(comp, fp)) Emit(s, w);
                foreach (var s in Silk.RefDesStrokes(comp, fp)) Emit(s, w);
            }

            var layer = side == CopperLayer.Top ? GraphicLayer.SilkTop : GraphicLayer.SilkBottom;
            foreach (var t in board.Texts.Where(t => t.Layer == layer))
            {
                foreach (var s in Silk.TextStrokes(t)) Emit(s, t.Thickness);
            }

            // [graphics] drawn shapes on this side's legend; a filled one is hatched with strokes of its own width
            foreach (var g in board.Graphics.Where(g => g.Layer == layer))
            {
                foreach (var s in Silk.GraphicStrokes(g)) Emit(s, g.Width);
            }

            return strokes;
        }

        private static string Outline(BoardDocument board)
        {
            var g = new GerberWriter(board.Height, "Profile,NP");
            foreach (var e in board.OutlineElements())
            {
                if (e.IsArc) g.Arc(e.X1, e.Y1, e.X2, e.Y2, e.CX, e.CY, e.CounterClockwise, 0.1);
                else g.Line(e.X1, e.Y1, e.X2, e.Y2, 0.1);
            }

            return g.ToString();
        }

        /// <summary>Excellon files: PTH (through), NPTH (if any), and one per blind/buried span.</summary>
        private static Dictionary<string, string> Drills(BoardDocument board, string projectName)
        {
            int n = board.LayerCount;
            var groups = new Dictionary<string, (string Function, List<(double D, double X, double Y)> Holes)>(StringComparer.Ordinal);
            void Add(string key, string function, double d, double x, double y)
            {
                if (!groups.TryGetValue(key, out var gr)) groups[key] = gr = (function, new List<(double, double, double)>());
                gr.Holes.Add((d, x, y));
            }

            string pth = projectName + "-PTH.drl";
            groups[pth] = ("Plated,1," + n.ToString(CultureInfo.InvariantCulture) + ",PTH", new List<(double, double, double)>());
            foreach (var p in board.Pads())
            {
                if (!p.Pad.IsThroughHole) continue;
                if (p.Pad.NonPlated) Add(projectName + "-NPTH.drl", "NonPlated,1," + n.ToString(CultureInfo.InvariantCulture) + ",NPTH", p.Pad.Drill, p.X, p.Y);
                else Add(pth, groups[pth].Function, p.Pad.Drill, p.X, p.Y);
            }

            foreach (var v in board.Vias)
            {
                if (v.IsThrough(n))
                {
                    Add(pth, groups[pth].Function, v.Drill, v.X, v.Y);
                    continue;
                }

                var (from, to) = v.Span(n);
                int a = PcbLayers.StackIndex(from, n) + 1, b = PcbLayers.StackIndex(to, n) + 1;
                string kind = a == 1 || b == n ? "Blind" : "Buried";
                Add(projectName + "-" + PcbLayers.FileName(from) + "-" + PcbLayers.FileName(to) + ".drl",
                    "Plated," + a.ToString(CultureInfo.InvariantCulture) + "," + b.ToString(CultureInfo.InvariantCulture) + "," + kind, v.Drill, v.X, v.Y);
            }

            foreach (var h in board.MountingHoles)
            {
                if (h.Plated) Add(pth, groups[pth].Function, h.Drill, h.X, h.Y);
                else Add(projectName + "-NPTH.drl", "NonPlated,1," + n.ToString(CultureInfo.InvariantCulture) + ",NPTH", h.Drill, h.X, h.Y);
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in groups) result[kv.Key] = Excellon(board, kv.Value.Function, kv.Value.Holes);
            return result;
        }

        private static string Excellon(BoardDocument board, string function, List<(double D, double X, double Y)> holes)
        {
            var tools = holes.Select(h => Math.Round(h.D, 3)).Distinct().OrderBy(d => d).ToList();
            var sb = new StringBuilder();
            sb.Append("M48\n; DRILL file - RobotWin Studio\n; #@! TF.FileFunction,").Append(function).Append('\n');
            sb.Append("FMAT,2\nMETRIC,TZ\n");
            for (int i = 0; i < tools.Count; i++)
            {
                sb.Append('T').Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append('C')
                  .Append(tools[i].ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
            }

            sb.Append("%\nG90\nG05\n");
            for (int i = 0; i < tools.Count; i++)
            {
                sb.Append('T').Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (var h in holes.Where(h => Math.Round(h.D, 3) == tools[i]))
                {
                    sb.Append('X').Append(h.X.ToString("0.000", CultureInfo.InvariantCulture))
                      .Append('Y').Append((board.Height - h.Y).ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
                }
            }

            sb.Append("T0\nM30\n");
            return sb.ToString();
        }

        private static string Csv(string s) => "\"" + (s ?? string.Empty).Replace("\"", "\"\"") + "\"";

        // [variants] the components the alternative fits, and the value it gives each
        private static IEnumerable<BoardComponent> Fitted(BoardDocument board, Design.Fitting? fitting) =>
            fitting == null ? board.Components : board.Components.Where(c => !fitting.NotFitted.Contains(c.RefDes));

        private static string ValueOf(BoardComponent c, Design.Fitting? fitting) =>
            fitting != null && fitting.Values.TryGetValue(c.RefDes, out var v) ? v : c.Value;

        /// <summary>Bill of materials (JLCPCB columns): one line per value + footprint, with designators and side.</summary>
        public static string Bom(BoardDocument board, Design.Fitting? fitting = null)
        {
            var sb = new StringBuilder("Comment,Designator,Footprint,Quantity,Side\n");
            foreach (var g in Fitted(board, fitting).GroupBy(c => (Value: ValueOf(c, fitting), c.FootprintId)).OrderBy(g => g.Key.FootprintId, StringComparer.Ordinal).ThenBy(g => g.Key.Value, StringComparer.Ordinal))
            {
                var refs = string.Join(" ", g.Select(c => c.RefDes).OrderBy(r => r, StringComparer.Ordinal));
                var sides = g.Select(c => c.Side == CopperLayer.Top ? "Top" : "Bottom").Distinct().OrderByDescending(s => s);
                sb.Append(Csv(g.Key.Value)).Append(',').Append(Csv(refs)).Append(',').Append(Csv(g.Key.FootprintId)).Append(',')
                  .Append(g.Count().ToString(CultureInfo.InvariantCulture)).Append(',').Append(string.Join("+", sides)).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>Pick-and-place (JLCPCB CPL): part centre in mm from the lower-left corner, side and rotation.</summary>
        public static string PickAndPlace(BoardDocument board, Design.Fitting? fitting = null)
        {
            var sb = new StringBuilder("Designator,Val,Package,Mid X,Mid Y,Layer,Rotation\n");
            foreach (var c in Fitted(board, fitting).OrderBy(c => c.RefDes, StringComparer.Ordinal))
            {
                var fp = FootprintLibrary.Get(c.FootprintId);
                if (fp == null) continue;
                var box = c.Courtyard(fp);
                int rot = ((c.Rotation % 360) + 360) % 360;
                if (c.Side == CopperLayer.Bottom) rot = (360 - rot) % 360;
                sb.Append(c.RefDes).Append(',').Append(Csv(ValueOf(c, fitting))).Append(',').Append(Csv(c.FootprintId)).Append(',')
                  .Append(((box.MinX + box.MaxX) / 2).ToString("0.###", CultureInfo.InvariantCulture)).Append("mm,")
                  .Append((board.Height - (box.MinY + box.MaxY) / 2).ToString("0.###", CultureInfo.InvariantCulture)).Append("mm,")
                  .Append(c.Side == CopperLayer.Top ? "Top" : "Bottom").Append(',')
                  .Append(rot.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            return sb.ToString();
        }
    }

    /// <summary>Silkscreen geometry in board coordinates (shared by the Gerber writer, DRC and the editor).</summary>
    public static class Silk
    {
        /// <summary>Footprint silkscreen lines, circles (as chords) and silk texts placed on the board.</summary>
        public static List<Segment2> FootprintStrokes(BoardComponent comp, Footprint fp)
        {
            var list = new List<Segment2>();
            foreach (var s in fp.Silkscreen)
            {
                var a = comp.ToBoard(s.X1, s.Y1);
                var b = comp.ToBoard(s.X2, s.Y2);
                list.Add(new Segment2(a.X, a.Y, b.X, b.Y));
            }

            foreach (var (x, y, r) in fp.SilkCircles)
            {
                var c = comp.ToBoard(x, y);
                int n = Math.Max(16, (int)Math.Ceiling(2 * Math.PI / Shapes.StepAngle(r, 0.01)));
                for (int i = 0; i < n; i++)
                {
                    double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                    list.Add(new Segment2(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0), c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)));
                }
            }

            foreach (var t in fp.Texts.Where(t => t.Layer == GraphicLayer.SilkTop))
            {
                var p = comp.ToBoard(t.X, t.Y);
                StrokeFont.DrawCentered((x1, y1, x2, y2, _) => list.Add(new Segment2(x1, y1, x2, y2)), t.Text, p.X, p.Y, t.Size, 0.15, 0, comp.Side == CopperLayer.Bottom);
            }

            return list;
        }

        /// <summary>Reference designator above the courtyard (mirrored for bottom-side parts).</summary>
        public static List<Segment2> RefDesStrokes(BoardComponent comp, Footprint fp, double height = 1.0)
        {
            var list = new List<Segment2>();
            var box = comp.Courtyard(fp);
            double w = StrokeFont.Measure(comp.RefDes, height);
            StrokeFont.DrawCentered((x1, y1, x2, y2, _) => list.Add(new Segment2(x1, y1, x2, y2)), comp.RefDes,
                box.MinX + w / 2, box.MinY - 0.4 - height / 2, height, 0.15, 0, comp.Side == CopperLayer.Bottom);
            return list;
        }

        /// <summary>[graphics] A drawn shape as legend strokes: its outline, and for a filled one parallel strokes a line width apart across it.</summary>
        public static List<Segment2> GraphicStrokes(BoardGraphic g)
        {
            var list = g.Strokes();
            if (!g.Filled) return list;
            var area = g.Area();
            if (area.Count < 3) return list;
            double step = Math.Max(0.05, g.Width * 0.8);
            double y0 = area.Min(p => p.Y), y1 = area.Max(p => p.Y);
            for (double y = y0 + step / 2; y < y1; y += step)
            {
                var xs = new List<double>();
                for (int i = 0, j = area.Count - 1; i < area.Count; j = i++)
                {
                    var a = area[i];
                    var b = area[j];
                    if ((a.Y > y) != (b.Y > y)) xs.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }

                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2) list.Add(new Segment2(xs[k], y, xs[k + 1], y));
            }

            return list;
        }

        public static List<Segment2> TextStrokes(BoardText t)
        {
            var list = new List<Segment2>();
            StrokeFont.DrawCentered((x1, y1, x2, y2, _) => list.Add(new Segment2(x1, y1, x2, y2)), t.Text, t.X, t.Y, t.Size, t.Thickness, t.Rotation, t.Mirrored);
            return list;
        }

        public static List<Segment2> DimensionStrokes(BoardDimension d, bool mil = false)
        {
            var list = new List<Segment2>();
            var geo = d.Geometry();
            foreach (var l in geo.Lines) list.Add(new Segment2(l.X1, l.Y1, l.X2, l.Y2));
            double angle = geo.Angle;
            if (angle > 90.0001 || angle <= -90) angle += 180;
            StrokeFont.DrawCentered((x1, y1, x2, y2, _) => list.Add(new Segment2(x1, y1, x2, y2)), d.Label(mil), geo.TextX, geo.TextY, d.TextSize, 0.12, angle, false);
            return list;
        }

        /// <summary>The parts of a segment outside every rectangle (centre, half sizes): Liang-Barsky interval removal.</summary>
        public static IEnumerable<Segment2> Clip(Segment2 s, IReadOnlyList<(double X, double Y, double HX, double HY)> rects)
        {
            var keep = new List<(double A, double B)> { (0, 1) };
            double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
            foreach (var r in rects)
            {
                if (Math.Max(s.X1, s.X2) < r.X - r.HX || Math.Min(s.X1, s.X2) > r.X + r.HX ||
                    Math.Max(s.Y1, s.Y2) < r.Y - r.HY || Math.Min(s.Y1, s.Y2) > r.Y + r.HY) continue;
                double t0 = 0, t1 = 1;
                bool Edge(double p, double q)
                {
                    if (Math.Abs(p) < 1e-15) return q >= 0;
                    double t = q / p;
                    if (p < 0)
                    {
                        if (t > t1) return false;
                        if (t > t0) t0 = t;
                    }
                    else
                    {
                        if (t < t0) return false;
                        if (t < t1) t1 = t;
                    }

                    return true;
                }

                if (!(Edge(-dx, s.X1 - (r.X - r.HX)) && Edge(dx, r.X + r.HX - s.X1) && Edge(-dy, s.Y1 - (r.Y - r.HY)) && Edge(dy, r.Y + r.HY - s.Y1))) continue;
                if (t1 <= t0) continue;
                var next = new List<(double, double)>();
                foreach (var (a, b) in keep)
                {
                    if (t1 <= a || t0 >= b)
                    {
                        next.Add((a, b));
                        continue;
                    }

                    if (t0 > a) next.Add((a, t0));
                    if (t1 < b) next.Add((t1, b));
                }

                keep = next;
                if (keep.Count == 0) yield break;
            }

            foreach (var (a, b) in keep)
            {
                if (b - a < 1e-6) continue;
                yield return new Segment2(s.X1 + dx * a, s.Y1 + dy * a, s.X1 + dx * b, s.Y1 + dy * b);
            }
        }
    }

    /// <summary>Minimal single-stroke font for silkscreen text (A-Z, 0-9, common symbols; Turkish letters map to their base glyph).</summary>
    public static class StrokeFont
    {
        // Each glyph: strokes as polylines on a 4x6 grid ("x,y x,y|x,y x,y").
        private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['0'] = "0,0 4,0 4,6 0,6 0,0 4,6", ['1'] = "1,1 2,0 2,6|1,6 3,6", ['2'] = "0,1 1,0 3,0 4,1 4,2 0,6 4,6",
            ['3'] = "0,0 4,0 2,2 4,3 4,5 3,6 0,6", ['4'] = "3,6 3,0 0,4 4,4", ['5'] = "4,0 0,0 0,3 3,3 4,4 4,5 3,6 0,6",
            ['6'] = "4,0 1,0 0,1 0,6 4,6 4,3 0,3", ['7'] = "0,0 4,0 1,6", ['8'] = "0,0 4,0 4,6 0,6 0,0|0,3 4,3",
            ['9'] = "4,3 0,3 0,0 4,0 4,6 0,6",
            ['A'] = "0,6 0,2 2,0 4,2 4,6|0,3 4,3", ['B'] = "0,0 3,0 4,1 3,3 0,3|3,3 4,4 4,5 3,6 0,6 0,0",
            ['C'] = "4,0 0,0 0,6 4,6", ['D'] = "0,0 3,0 4,1 4,5 3,6 0,6 0,0", ['E'] = "4,0 0,0 0,6 4,6|0,3 3,3",
            ['F'] = "4,0 0,0 0,6|0,3 3,3", ['G'] = "4,0 0,0 0,6 4,6 4,3 2,3", ['H'] = "0,0 0,6|4,0 4,6|0,3 4,3",
            ['I'] = "1,0 3,0|2,0 2,6|1,6 3,6", ['J'] = "4,0 4,6 1,6 0,5", ['K'] = "0,0 0,6|4,0 0,3 4,6",
            ['L'] = "0,0 0,6 4,6", ['M'] = "0,6 0,0 2,3 4,0 4,6", ['N'] = "0,6 0,0 4,6 4,0", ['O'] = "0,0 4,0 4,6 0,6 0,0",
            ['P'] = "0,6 0,0 4,0 4,3 0,3", ['Q'] = "0,0 4,0 4,6 0,6 0,0|2,4 4,6", ['R'] = "0,6 0,0 4,0 4,3 0,3 4,6",
            ['S'] = "4,0 0,0 0,3 4,3 4,6 0,6", ['T'] = "0,0 4,0|2,0 2,6", ['U'] = "0,0 0,6 4,6 4,0", ['V'] = "0,0 2,6 4,0",
            ['W'] = "0,0 1,6 2,3 3,6 4,0", ['X'] = "0,0 4,6|4,0 0,6", ['Y'] = "0,0 2,3 4,0|2,3 2,6", ['Z'] = "0,0 4,0 0,6 4,6",
            ['-'] = "1,3 3,3", ['+'] = "1,3 3,3|2,2 2,4", ['.'] = "2,6 2,5.6", ['_'] = "0,6 4,6", ['$'] = "4,1 0,1 0,3 4,3 4,5 0,5|2,0 2,6",
            ['/'] = "0,6 4,0", [':'] = "2,1.5 2,2|2,4.5 2,5", ['='] = "0,2 4,2|0,4 4,4", ['('] = "3,0 2,1 2,5 3,6", [')'] = "1,0 2,1 2,5 1,6",
            [','] = "2,5.5 1.5,6.5", ['%'] = "0,6 4,0|0,0 1,0 1,1 0,1 0,0|3,5 4,5 4,6 3,6 3,5", ['#'] = "1,0 1,6|3,0 3,6|0,2 4,2|0,4 4,4",
            ['*'] = "0,1 4,5|4,1 0,5|2,0 2,6", ['<'] = "4,0 0,3 4,6", ['>'] = "0,0 4,3 0,6", ['!'] = "2,0 2,4|2,5.6 2,6",
            ['?'] = "0,1 1,0 3,0 4,1 4,2 2,3 2,4|2,5.6 2,6", ['\''] = "2,0 2,1.5", ['"'] = "1,0 1,1.5|3,0 3,1.5", ['&'] = "4,6 0,2 1,0 2,0 2,2 0,4 0,6 3,6 4,4",
            ['Ø'] = "0,0 4,0 4,6 0,6 0,0|0,6 4,0", ['°'] = "1,0 2,0 2,1 1,1 1,0", ['µ'] = "0,6 0,2|0,5 4,5 4,2"
        };

        private static readonly Dictionary<char, char> Fold = new Dictionary<char, char>
        {
            ['Ç'] = 'C', ['Ğ'] = 'G', ['İ'] = 'I', ['I'] = 'I', ['Ö'] = 'O', ['Ş'] = 'S', ['Ü'] = 'U', ['Â'] = 'A', ['Î'] = 'I', ['Û'] = 'U'
        };

        public delegate void LineSink(double x1, double y1, double x2, double y2, double width);

        private static char Glyph(char raw)
        {
            char c = char.ToUpper(raw, CultureInfo.GetCultureInfo("tr-TR"));
            if (c == 'ı') c = 'I';
            if (Fold.TryGetValue(c, out var f)) c = f;
            if (!Glyphs.ContainsKey(c)) c = char.ToUpperInvariant(raw);
            return c;
        }

        /// <summary>Width of a text line in mm.</summary>
        public static double Measure(string text, double height) =>
            string.IsNullOrEmpty(text) ? 0 : (text.Length * 5.5 - 1.5) * height / 6.0;

        /// <summary>Draws text with its top-left corner at (x, y - height); height in mm.</summary>
        public static void Draw(LineSink sink, string text, double x, double y, double height, double width)
        {
            double s = height / 6.0;
            double top = y - height;
            foreach (char raw in text)
            {
                if (Glyphs.TryGetValue(Glyph(raw), out var glyph))
                {
                    foreach (var stroke in glyph.Split('|'))
                    {
                        var pts = stroke.Split(' ').Select(p =>
                        {
                            var xy = p.Split(',');
                            return (double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture));
                        }).ToList();
                        for (int i = 0; i + 1 < pts.Count; i++)
                        {
                            sink(x + pts[i].Item1 * s, top + pts[i].Item2 * s, x + pts[i + 1].Item1 * s, top + pts[i + 1].Item2 * s, width);
                        }
                    }
                }

                x += 5.5 * s;
            }
        }

        /// <summary>Draws text centred on (cx, cy), rotated by <paramref name="angleDeg"/> (board frame) and optionally mirrored (bottom side).</summary>
        public static void DrawCentered(LineSink sink, string text, double cx, double cy, double height, double width, double angleDeg, bool mirror)
        {
            if (string.IsNullOrEmpty(text)) return;
            double w = Measure(text, height);
            double a = angleDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            (double, double) T(double x, double y)
            {
                double lx = x - w / 2, ly = y - height / 2;
                if (mirror) lx = -lx;
                return (cx + lx * ca - ly * sa, cy + lx * sa + ly * ca);
            }

            Draw((x1, y1, x2, y2, wd) =>
            {
                var p = T(x1, y1);
                var q = T(x2, y2);
                sink(p.Item1, p.Item2, q.Item1, q.Item2, wd);
            }, text, 0, height, height, width);
        }
    }
}
