using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[gerber-check] A place where a file and the board disagree (board coordinates, mm).</summary>
    public sealed class GerberDifference
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Area of the disagreement, mm² (its core: a one-pixel rim is not counted).</summary>
        public double Area { get; set; }

        /// <summary>True: the board has it, the file does not. False: the file has something the board does not.</summary>
        public bool Missing { get; set; }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.00}, {2:0.00}) mm, {3:0.000} mm²", Missing ? "eksik" : "fazla", X, Y, Area);
    }

    /// <summary>[gerber-check] One Gerber file read back and laid over the board's own picture of that layer.</summary>
    public sealed class GerberLayerCheck
    {
        public string File { get; set; } = string.Empty;
        public string Function { get; set; } = string.Empty;
        public double ExpectedArea { get; set; }
        public double FileArea { get; set; }
        public List<GerberDifference> Differences { get; } = new List<GerberDifference>();
        public List<string> Warnings { get; } = new List<string>();
        public bool Ok => Differences.Count == 0 && Warnings.Count == 0;
    }

    /// <summary>[gerber-check] A drill file's holes against the board's.</summary>
    public sealed class GerberDrillCheck
    {
        public string File { get; set; } = string.Empty;
        public string Function { get; set; } = string.Empty;
        public int Expected { get; set; }
        public int Found { get; set; }
        public List<string> Problems { get; } = new List<string>();
        public bool Ok => Problems.Count == 0;
    }

    public sealed class GerberCheckReport
    {
        public List<GerberLayerCheck> Layers { get; } = new List<GerberLayerCheck>();
        public List<GerberDrillCheck> Drills { get; } = new List<GerberDrillCheck>();
        public double Pixel { get; set; }
        public bool Ok => Layers.All(l => l.Ok) && Drills.All(d => d.Ok);

        /// <summary>"12 Gerber / 2 delik dosyası kartla aynı" or the first disagreements.</summary>
        public string Summary()
        {
            int files = Layers.Count, drills = Drills.Count;
            if (Ok) return $"{files} Gerber ve {drills} delik dosyası kartla aynı (bağımsız okuyucuyla, {Pixel * 1000:0} µm çözünürlükte)";
            var bad = Layers.Where(l => !l.Ok).Select(l => l.File + ": " + (l.Differences.Count > 0 ? l.Differences.Count + " fark, ilki " + l.Differences[0] : l.Warnings[0]))
                .Concat(Drills.Where(d => !d.Ok).Select(d => d.File + ": " + d.Problems[0]));
            return "Üretim dosyalarında kartla uyuşmayan yerler: " + string.Join("; ", bad);
        }
    }

    /// <summary>
    /// [gerber-check] Reads the exported Gerber and Excellon files with <see cref="GerberReader"/>, an independent reader,
    /// paints each layer, and lays it over a picture of the same layer painted straight from the board's geometry (tracks,
    /// pads by their shapes, vias, zone fills, mask openings, paste, silk strokes, outline): what differs by more than a
    /// pixel's rim is reported where it is. Drill files are matched hole for hole, plating and diameter included. A wrong
    /// unit, a flipped axis, a wrong aperture, a lost arc direction or a missing object all show.
    /// </summary>
    public static class GerberCheck
    {
        /// <summary>The text files of a fabrication zip, by name (what was written to disk, to check that).</summary>
        public static Dictionary<string, string> ReadZip(string zipPath)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                using var reader = new System.IO.StreamReader(entry.Open());
                files[entry.FullName] = reader.ReadToEnd();
            }

            return files;
        }

        public static GerberCheckReport Check(BoardDocument board, IReadOnlyDictionary<string, string> files, double pixel = 0) => Check(board, files, null, pixel);

        /// <summary>
        /// [gerbv] The same check with another reader drawing the files: <paramref name="paint"/> (file name, its text, the
        /// frame in the files' coordinates) fills the frame and returns null, or what went wrong. A third-party renderer such
        /// as gerbv then stands in for <see cref="GerberReader"/>, which still names the layer from its file function.
        /// </summary>
        public static GerberCheckReport Check(BoardDocument board, IReadOnlyDictionary<string, string> files, Func<string, string, GerberRaster, string?>? paint, double pixel = 0)
        {
            const double margin = 2.0;
            double area = (board.Width + 2 * margin) * (board.Height + 2 * margin);
            if (pixel <= 0) pixel = Math.Max(0.02, Math.Sqrt(area / 12e6));
            var report = new GerberCheckReport { Pixel = pixel };
            double hgt = board.Height;
            var stack = board.CopperLayers();
            foreach (var kv in files.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string name = kv.Key;
                if (name.EndsWith(".drl", StringComparison.OrdinalIgnoreCase))
                {
                    report.Drills.Add(CheckDrill(board, name, kv.Value));
                    continue;
                }

                if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                var image = GerberReader.Parse(kv.Value);
                var check = new GerberLayerCheck { File = name, Function = image.FileFunction };
                check.Warnings.AddRange(image.Warnings);
                var expected = new GerberRaster(-margin, -margin, board.Width + 2 * margin, board.Height + 2 * margin, pixel);
                if (!PaintExpected(board, image.FileFunction, stack, expected, hgt))
                {
                    check.Warnings.Add("dosya işlevi tanınmadı: " + image.FileFunction);
                    report.Layers.Add(check);
                    continue;
                }

                // [origins] the files measure from the fabrication origin: the same pixels, shifted to it
                var fab = board.FabOrigin;
                var got = new GerberRaster(-margin - fab.X, -margin - (hgt - fab.Y), board.Width + 2 * margin, board.Height + 2 * margin, pixel);
                if (paint == null) got.Draw(image);
                else if (paint(name, kv.Value, got) is { } problem) check.Warnings.Add(problem);
                check.ExpectedArea = expected.Count() * pixel * pixel;
                check.FileArea = got.Count() * pixel * pixel;
                check.Differences.AddRange(Compare(expected, got, hgt));
                report.Layers.Add(check);
            }

            return report;
        }

        /// <summary>The board's own picture of the layer a file function names, in the files' frame (Y up from the board's bottom edge).</summary>
        private static bool PaintExpected(BoardDocument board, string function, IReadOnlyList<CopperLayer> stack, GerberRaster r, double hgt)
        {
            double Gy(double y) => hgt - y;
            var parts = function.Split(',');
            switch (parts[0])
            {
                case "Copper":
                {
                    if (parts.Length < 2 || !parts[1].StartsWith("L", StringComparison.Ordinal) || !int.TryParse(parts[1].Substring(1), out int index) || index < 1 || index > stack.Count) return false;
                    var layer = stack[index - 1];
                    int count = board.LayerCount;
                    foreach (var zone in board.Zones.Where(z => z.Layer == layer && z.IsFilled))
                    {
                        foreach (var region in zone.FilledRegions)
                        {
                            var contours = new List<IReadOnlyList<(double, double)>> { region.Outer.Select(p => (p.X, Gy(p.Y))).ToList() };
                            contours.AddRange(region.Holes.Select(h => (IReadOnlyList<(double, double)>)h.Select(p => (p.X, Gy(p.Y))).ToList()));
                            r.Polygon(contours);
                        }
                    }

                    foreach (var t in board.Tracks.Where(t => t.Layer == layer)) r.Capsule(t.X1, Gy(t.Y1), t.X2, Gy(t.Y2), t.Width / 2);
                    foreach (var p in board.Pads().Where(p => p.OnLayer(layer))) PaintPad(r, p.Pad.Shape, p.X, Gy(p.Y), p.Width, p.Height, p.Pad.CornerRatio, 0);
                    foreach (var v in board.Vias.Where(v => v.Spans(layer, count))) r.Disc(v.X, Gy(v.Y), v.Diameter / 2);
                    foreach (var h in board.MountingHoles.Where(h => h.Plated)) r.Disc(h.X, Gy(h.Y), h.PadDiameter / 2);
                    return true;
                }

                case "Soldermask":
                {
                    var side = parts.Length > 1 && parts[1] == "Bot" ? CopperLayer.Bottom : CopperLayer.Top;
                    double e = GerberExporter.MaskExpansion;
                    foreach (var o in GerberExporter.MaskOpenings(board, side))
                    {
                        bool roundRect = o.Pad is { } p && p.Pad.Shape == PadShape.RoundRect && !(p.Pad.IsThroughHole && p.Pad.NonPlated);
                        PaintPad(r, roundRect ? PadShape.RoundRect : o.Shape, o.X, Gy(o.Y), o.W, o.H, o.Pad?.Pad.CornerRatio ?? 0, e);
                    }

                    return true;
                }

                case "Paste":
                {
                    var side = parts.Length > 1 && parts[1] == "Bot" ? CopperLayer.Bottom : CopperLayer.Top;
                    foreach (var p in board.Pads().Where(p => !p.Pad.IsThroughHole && p.SmdLayer == side)) PaintPad(r, p.Pad.Shape, p.X, Gy(p.Y), p.Width, p.Height, p.Pad.CornerRatio, 0);
                    return true;
                }

                case "Legend":
                {
                    var side = parts.Length > 1 && parts[1] == "Bot" ? CopperLayer.Bottom : CopperLayer.Top;
                    foreach (var (s, width) in GerberExporter.SilkStrokes(board, side)) r.Capsule(s.X1, Gy(s.Y1), s.X2, Gy(s.Y2), width / 2);
                    return true;
                }

                case "Profile":
                {
                    foreach (var e in board.OutlineElements())
                    {
                        if (!e.IsArc)
                        {
                            r.Capsule(e.X1, Gy(e.Y1), e.X2, Gy(e.Y2), 0.05);
                            continue;
                        }

                        // the direction from the arc's own middle point, not from the writer's flag
                        bool cw = Through(e.X1, Gy(e.Y1), e.X2, Gy(e.Y2), e.CX, Gy(e.CY), e.MidX, Gy(e.MidY));
                        r.ArcStroke(e.X1, Gy(e.Y1), e.X2, Gy(e.Y2), e.CX, Gy(e.CY), cw, 0.05);
                    }

                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>Whether the clockwise way from start to end passes through <paramref name="mx"/>, <paramref name="my"/>.</summary>
        private static bool Through(double x1, double y1, double x2, double y2, double cx, double cy, double mx, double my)
        {
            double sweep = GerberReader.Sweep(x1, y1, x2, y2, cx, cy, true);
            double toMid = GerberReader.Sweep(x1, y1, mx, my, cx, cy, true, false);
            return toMid <= sweep;
        }

        /// <summary>A pad's shape grown by <paramref name="expand"/> all round, as fabrication means it.</summary>
        private static void PaintPad(GerberRaster r, PadShape shape, double x, double y, double w, double h, double cornerRatio, double expand)
        {
            switch (shape)
            {
                case PadShape.Circle:
                    r.Disc(x, y, Math.Max(w, h) / 2 + expand);
                    break;
                case PadShape.Oval:
                    r.RoundRect(x, y, w + 2 * expand, h + 2 * expand, Math.Min(w, h) / 2 + expand);
                    break;
                case PadShape.RoundRect:
                    r.RoundRect(x, y, w + 2 * expand, h + 2 * expand, cornerRatio * Math.Min(w, h) + expand);
                    break;
                default:
                    r.Rect(x, y, w + 2 * expand, h + 2 * expand);
                    break;
            }
        }

        /// <summary>Where two pictures differ by more than an edge pixel: the difference eroded once, then gathered into spots.</summary>
        private static List<GerberDifference> Compare(GerberRaster expected, GerberRaster got, double hgt)
        {
            int w = expected.W, h = expected.H;
            var a = expected.Bits;
            var b = got.Bits;
            var core = new bool[w * h];
            bool Xor(int i) => a[i] != b[i];
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (Xor(i) && Xor(i - 1) && Xor(i + 1) && Xor(i - w) && Xor(i + w)) core[i] = true;
                }
            }

            var spots = new List<GerberDifference>();
            var stack = new Stack<int>();
            for (int start = 0; start < core.Length; start++)
            {
                if (!core[start]) continue;
                int n = 0, missing = 0;
                double sx = 0, sy = 0;
                core[start] = false;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    int cx = i % w, cy = i / w;
                    n++;
                    if (a[i]) missing++;
                    sx += expected.Cx(cx);
                    sy += expected.Cy(cy);
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (!core[j]) continue;
                            core[j] = false;
                            stack.Push(j);
                        }
                    }
                }

                if (n < 2) continue;
                spots.Add(new GerberDifference { X = sx / n, Y = hgt - sy / n, Area = n * expected.Pixel * expected.Pixel, Missing = missing * 2 >= n });
            }

            return spots.OrderByDescending(s => s.Area).Take(50).ToList();
        }

        private static GerberDrillCheck CheckDrill(BoardDocument board, string name, string text)
        {
            var check = new GerberDrillCheck { File = name };
            int fn = text.IndexOf("TF.FileFunction,", StringComparison.Ordinal);
            if (fn >= 0)
            {
                int end = text.IndexOf('\n', fn);
                check.Function = text.Substring(fn + "TF.FileFunction,".Length, (end < 0 ? text.Length : end) - fn - "TF.FileFunction,".Length).Trim();
            }

            var (holes, warnings) = GerberReader.ParseExcellon(text);
            check.Problems.AddRange(warnings);
            check.Found = holes.Count;

            // The board's holes this file should carry: plated or not, and the layer span for blind/buried vias.
            var parts = check.Function.Split(',');
            bool plated = parts.Length > 0 && parts[0] == "Plated";
            int n = board.LayerCount;
            int from = parts.Length > 2 && int.TryParse(parts[1], out int f) ? f : 1, to = parts.Length > 2 && int.TryParse(parts[2], out int t) ? t : n;
            var expected = new List<(double D, double X, double Y)>();
            foreach (var p in board.Pads().Where(p => p.Pad.IsThroughHole))
                if (p.Pad.NonPlated != plated && from == 1 && to == n) expected.Add((p.Pad.Drill, p.X, p.Y));
            foreach (var v in board.Vias)
            {
                if (!plated) continue;
                var (a, z) = v.Span(n);
                int ia = PcbLayers.StackIndex(a, n) + 1, iz = PcbLayers.StackIndex(z, n) + 1;
                if (Math.Min(ia, iz) == from && Math.Max(ia, iz) == to) expected.Add((v.Drill, v.X, v.Y));
            }

            foreach (var hole in board.MountingHoles)
                if (hole.Plated == plated && from == 1 && to == n) expected.Add((hole.Drill, hole.X, hole.Y));
            check.Expected = expected.Count;

            // Match hole for hole (the file is in the Y-up frame; positions to 1 µm, diameters to 1 µm after the tool table's rounding).
            var fab = board.FabOrigin;   // [origins]
            var left = holes.Select(hh => (hh.D, X: hh.X + fab.X, Y: fab.Y - hh.Y)).ToList();
            foreach (var e in expected)
            {
                int k = left.FindIndex(hh => Math.Abs(hh.X - e.X) < 2e-3 && Math.Abs(hh.Y - e.Y) < 2e-3);
                if (k < 0)
                {
                    check.Problems.Add(string.Format(CultureInfo.InvariantCulture, "delik eksik: Ø{0:0.###} mm ({1:0.###}, {2:0.###})", e.D, e.X, e.Y));
                    continue;
                }

                if (Math.Abs(left[k].D - e.D) > 1.5e-3)
                    check.Problems.Add(string.Format(CultureInfo.InvariantCulture, "delik çapı farklı: ({0:0.###}, {1:0.###}) kartta Ø{2:0.###}, dosyada Ø{3:0.###} mm", e.X, e.Y, e.D, left[k].D));
                left.RemoveAt(k);
            }

            foreach (var extra in left) check.Problems.Add(string.Format(CultureInfo.InvariantCulture, "fazla delik: Ø{0:0.###} mm ({1:0.###}, {2:0.###})", extra.D, extra.X, extra.Y));
            return check;
        }
    }
}
