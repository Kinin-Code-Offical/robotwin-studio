using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[plot] One layer the plot can draw.</summary>
    public sealed class PlotLayer
    {
        public PlotLayer(string id, string title, string color, PlotLayerKind kind, CopperLayer side, GraphicLayer? graphic = null)
        {
            Id = id;
            Title = title;
            Color = color;
            Kind = kind;
            Side = side;
            Graphic = graphic;
        }

        /// <summary>KiCad's layer name ("F.Cu", "B.Silkscreen", "Edge.Cuts").</summary>
        public string Id { get; }

        public string Title { get; }

        /// <summary>The ink in a colour plot ("#RRGGBB"), dark enough for white paper.</summary>
        public string Color { get; }

        public PlotLayerKind Kind { get; }

        /// <summary>The copper layer (copper) or the side (mask, paste, silkscreen, fab).</summary>
        public CopperLayer Side { get; }

        public GraphicLayer? Graphic { get; }

        /// <summary>The name as a file name part ("F_Cu").</summary>
        public string FileName => Id.Replace('.', '_');

        public override string ToString() => Id;
    }

    public enum PlotLayerKind
    {
        Copper,
        Mask,
        Paste,
        Silkscreen,
        Graphics,
        Edge
    }

    /// <summary>[plot] KiCad's Plot options for SVG and PDF.</summary>
    public sealed class PlotOptions
    {
        /// <summary>Each layer in its own colour; off: everything black (for toner transfer and photo masks).</summary>
        public bool Color { get; set; } = true;

        /// <summary>Mirrored (KiCad's Mirrored plot): a bottom layer as seen from below, a top layer for toner transfer.</summary>
        public bool Mirror { get; set; }

        /// <summary>Holes drawn open at their real size on copper layers (KiCad's drill marks: actual size).</summary>
        public bool DrillHoles { get; set; } = true;

        /// <summary>The board's edge drawn on every layer too.</summary>
        public bool EdgeOnEveryLayer { get; set; } = true;

        /// <summary>The board's and the layer's name under the board.</summary>
        public bool Label { get; set; } = true;

        /// <summary>White paper around the board (mm).</summary>
        public double Margin { get; set; } = 5;

        /// <summary>Written in the label and the PDF's title.</summary>
        public string Title { get; set; } = string.Empty;
    }

    /// <summary>
    /// [plot] KiCad's Plot to SVG and PDF: each layer drawn 1:1 by the same code that writes its Gerber
    /// (<see cref="IPlotter"/>), so what is printed is what is fabricated: zones, teardrops, tracks, pads, vias, the mask's
    /// openings, the stencil, the legend and the edge. Holes are left open; a layer can be mirrored or plotted in black.
    /// SVG: one file per layer or all in one (a group per layer); PDF: one page per layer, written by our own writer.
    /// </summary>
    public static class BoardPlot
    {
        private const double LabelHeight = 2.0, LabelGap = 2.0;

        private static readonly string[] InnerColors = { "#B07A1E", "#7A3EB0", "#1E8A6E", "#B03E7A", "#5A7A1E", "#3E5AB0" };

        /// <summary>The layers this board can plot, from the top down (KiCad's order in its Plot window).</summary>
        public static IReadOnlyList<PlotLayer> Layers(BoardDocument board)
        {
            var list = new List<PlotLayer>();
            var stack = board.CopperLayers();
            int inner = 0;
            foreach (var l in stack)
            {
                string name = PcbLayers.KiCadName(l);
                string color = l == CopperLayer.Top ? "#C83434" : l == CopperLayer.Bottom ? "#2F5FB8" : InnerColors[inner++ % InnerColors.Length];
                list.Add(new PlotLayer(name, name, color, PlotLayerKind.Copper, l));
            }

            list.Add(new PlotLayer("F.Silkscreen", "Üst serigrafi", "#8A6A00", PlotLayerKind.Silkscreen, CopperLayer.Top, GraphicLayer.SilkTop));
            list.Add(new PlotLayer("B.Silkscreen", "Alt serigrafi", "#2E6B2E", PlotLayerKind.Silkscreen, CopperLayer.Bottom, GraphicLayer.SilkBottom));
            list.Add(new PlotLayer("F.Mask", "Üst lehim maskesi", "#7A2A8F", PlotLayerKind.Mask, CopperLayer.Top));
            list.Add(new PlotLayer("B.Mask", "Alt lehim maskesi", "#1F7A7A", PlotLayerKind.Mask, CopperLayer.Bottom));
            list.Add(new PlotLayer("F.Paste", "Üst lehim pastası", "#6B6B6B", PlotLayerKind.Paste, CopperLayer.Top));
            list.Add(new PlotLayer("B.Paste", "Alt lehim pastası", "#4A4A4A", PlotLayerKind.Paste, CopperLayer.Bottom));
            if (HasGraphics(board, GraphicLayer.FabTop)) list.Add(new PlotLayer("F.Fab", "Üst üretim", "#6B5A3E", PlotLayerKind.Graphics, CopperLayer.Top, GraphicLayer.FabTop));
            if (HasGraphics(board, GraphicLayer.FabBottom)) list.Add(new PlotLayer("B.Fab", "Alt üretim", "#3E5A6B", PlotLayerKind.Graphics, CopperLayer.Bottom, GraphicLayer.FabBottom));
            list.Add(new PlotLayer("User.Drawings", "Çizimler ve ölçüler", "#333333", PlotLayerKind.Graphics, CopperLayer.Top, GraphicLayer.Drawings));
            list.Add(new PlotLayer("Edge.Cuts", "Kart kenarı", "#000000", PlotLayerKind.Edge, CopperLayer.Top));
            return list;
        }

        private static bool HasGraphics(BoardDocument board, GraphicLayer layer) =>
            board.Graphics.Any(g => g.Layer == layer) || board.Texts.Any(t => t.Layer == layer) || board.Dimensions.Any(d => d.Layer == layer);

        /// <summary>Finds a layer by its KiCad name.</summary>
        public static PlotLayer? Layer(BoardDocument board, string id) =>
            Layers(board).FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

        // ================================================================ what a layer draws

        private static void Draw(IPlotter g, BoardDocument board, PlotLayer layer)
        {
            switch (layer.Kind)
            {
                case PlotLayerKind.Copper: GerberExporter.PlotCopper(g, board, layer.Side); break;
                case PlotLayerKind.Mask: GerberExporter.PlotMask(g, board, layer.Side); break;
                case PlotLayerKind.Paste: GerberExporter.PlotPaste(g, board, layer.Side); break;
                case PlotLayerKind.Silkscreen: GerberExporter.PlotSilk(g, board, layer.Side); break;
                case PlotLayerKind.Edge: GerberExporter.PlotOutline(g, board, 0.15); break;
                case PlotLayerKind.Graphics:
                    var gl = layer.Graphic ?? GraphicLayer.Drawings;
                    foreach (var gr in board.Graphics.Where(x => x.Layer == gl))
                        foreach (var s in Silk.GraphicStrokes(gr)) g.Line(s.X1, s.Y1, s.X2, s.Y2, gr.Width);
                    foreach (var t in board.Texts.Where(x => x.Layer == gl))
                        foreach (var s in Silk.TextStrokes(t)) g.Line(s.X1, s.Y1, s.X2, s.Y2, t.Thickness);
                    foreach (var d in board.Dimensions.Where(x => x.Layer == gl))
                        foreach (var s in Silk.DimensionStrokes(d)) g.Line(s.X1, s.Y1, s.X2, s.Y2, 0.15);
                    break;
            }
        }

        /// <summary>The holes as (x, y, diameter): pads, vias, mounting holes.</summary>
        public static List<(double X, double Y, double D)> Holes(BoardDocument board)
        {
            var holes = new List<(double, double, double)>();
            foreach (var p in board.Pads().Where(p => p.Pad.IsThroughHole)) holes.Add((p.X, p.Y, p.Pad.Drill));
            foreach (var v in board.Vias) holes.Add((v.X, v.Y, v.Drill));
            foreach (var h in board.MountingHoles) holes.Add((h.X, h.Y, h.Drill));
            return holes;
        }

        /// <summary>One layer's ink, its holes and the edge, into a plotter.</summary>
        private static void Plot(Plotter g, BoardDocument board, PlotLayer layer, PlotOptions o, IReadOnlyList<PlotLayer> all)
        {
            g.Ink = o.Color ? layer.Color : "#000000";
            Draw(g, board, layer);
            if (layer.Kind == PlotLayerKind.Copper && o.DrillHoles)
            {
                g.Ink = "#FFFFFF";
                foreach (var (x, y, d) in Holes(board)) g.Flash(PadShape.Circle, x, y, d, d);
            }

            if (o.EdgeOnEveryLayer && layer.Kind != PlotLayerKind.Edge && all.FirstOrDefault(l => l.Kind == PlotLayerKind.Edge) is { } edge)
            {
                g.Ink = o.Color ? edge.Color : "#000000";
                GerberExporter.PlotOutline(g, board, 0.15);
            }
        }

        private static void Label(Plotter g, BoardDocument board, PlotLayer layer, PlotOptions o, (double MinX, double MinY, double MaxX, double MaxY) box)
        {
            if (!o.Label) return;
            g.Ink = "#000000";
            g.Mirrored = false;   // the words read the right way round on a mirrored plot too
            string text = (string.IsNullOrWhiteSpace(o.Title) ? string.Empty : o.Title.Trim() + "  ") + layer.Id + (o.Mirror ? "  (AYNALI)" : string.Empty);
            text = text.ToUpperInvariant();
            double room = box.MaxX - box.MinX + o.Margin, height = LabelHeight, measured = StrokeFont.Measure(text, LabelHeight);
            if (measured > room && measured > 0) height = LabelHeight * room / measured;   // a long list of layers still fits the paper
            StrokeFont.Draw((x1, y1, x2, y2, w) => g.Line(x1, y1, x2, y2, w), text, box.MinX, box.MaxY + o.Margin * 0.5, height, 0.2 * height / LabelHeight);
            g.Mirrored = o.Mirror;
        }

        /// <summary>The board's extent (its edge; the drawn copper when it has none).</summary>
        public static (double MinX, double MinY, double MaxX, double MaxY) Extent(BoardDocument board)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            void Add(double x, double y)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            foreach (var e in board.OutlineElements())
            {
                Add(e.X1, e.Y1);
                Add(e.X2, e.Y2);
                if (e.IsArc) Add(e.MidX, e.MidY);
            }

            if (minX > maxX) return (0, 0, Math.Max(1, board.Width), Math.Max(1, board.Height));
            return (minX, minY, maxX, maxY);
        }

        // ================================================================ SVG

        /// <summary>One layer as an SVG file (1:1, millimetres).</summary>
        public static string Svg(BoardDocument board, PlotLayer layer, PlotOptions? options = null) => Svg(board, new[] { layer }, options);

        /// <summary>Several layers in one SVG, stacked as on the board: the bottom side first, the top's legend, drawings and edge last (a group per layer, named by its KiCad name).</summary>
        public static string Svg(BoardDocument board, IReadOnlyList<PlotLayer> layers, PlotOptions? options = null)
        {
            var o = options ?? new PlotOptions();
            ZoneFiller.RefillOutdated(board);
            var box = Extent(board);
            double w = box.MaxX - box.MinX + 2 * o.Margin, h = box.MaxY - box.MinY + 2 * o.Margin + (o.Label ? LabelHeight + LabelGap : 0);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(N(w)).Append("mm\" height=\"").Append(N(h)).Append("mm\" viewBox=\"0 0 ")
              .Append(N(w)).Append(' ').Append(N(h)).Append("\">\n");
            sb.Append("<title>").Append(Xml((string.IsNullOrWhiteSpace(o.Title) ? "RobotWin" : o.Title) + " — " + string.Join(", ", layers.Select(l => l.Id)))).Append("</title>\n");
            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(N(w)).Append("\" height=\"").Append(N(h)).Append("\" fill=\"#FFFFFF\"/>\n");
            var all = Layers(board);
            var drawn = layers.OrderBy(l => Height(board, l)).ToList();   // as they lie: what is on top is drawn last
            for (int i = 0; i < drawn.Count; i++)
            {
                var layer = drawn[i];
                var g = new SvgPlotter(box, o);
                // in one file the edge and the holes come once, with the last (top) layer
                var lo = drawn.Count > 1 && i < drawn.Count - 1 ? Copy(o, edge: false) : o;
                Plot(g, board, layer, lo, all);
                sb.Append("<g id=\"").Append(Xml(layer.Id)).Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\">\n").Append(g.Body).Append("</g>\n");
            }

            if (o.Label && drawn.Count > 0)
            {
                var g = new SvgPlotter(box, o);
                Label(g, board, drawn.Count == 1 ? drawn[0] : new PlotLayer(string.Join(" + ", layers.Select(l => l.Id)), string.Empty, "#000000", PlotLayerKind.Graphics, CopperLayer.Top), o, box);
                sb.Append("<g id=\"label\" stroke-linecap=\"round\">\n").Append(g.Body).Append("</g>\n");
            }

            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>Where a layer lies, from below the board up.</summary>
        private static int Height(BoardDocument board, PlotLayer l)
        {
            bool top = l.Side == CopperLayer.Top;
            switch (l.Kind)
            {
                case PlotLayerKind.Copper: return 10 + (PcbLayers.StackIndex(l.Side, board.LayerCount) is int i && i >= 0 ? board.LayerCount - i : 0);
                case PlotLayerKind.Mask: return top ? 60 : 3;
                case PlotLayerKind.Paste: return top ? 61 : 2;
                case PlotLayerKind.Silkscreen: return top ? 62 : 1;
                case PlotLayerKind.Graphics: return l.Graphic == GraphicLayer.Drawings ? 70 : top ? 63 : 0;
                default: return 80;   // the edge over everything
            }
        }

        private static PlotOptions Copy(PlotOptions o, bool edge) => new PlotOptions
        {
            Color = o.Color, Mirror = o.Mirror, DrillHoles = o.DrillHoles, EdgeOnEveryLayer = edge, Label = o.Label, Margin = o.Margin, Title = o.Title
        };

        private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        // ================================================================ PDF

        /// <summary>The layers as one PDF, a page each (1:1; KiCad's "one document").</summary>
        public static byte[] Pdf(BoardDocument board, IReadOnlyList<PlotLayer> layers, PlotOptions? options = null)
        {
            var o = options ?? new PlotOptions();
            ZoneFiller.RefillOutdated(board);
            var box = Extent(board);
            double w = box.MaxX - box.MinX + 2 * o.Margin, h = box.MaxY - box.MinY + 2 * o.Margin + (o.Label ? LabelHeight + LabelGap : 0);
            var all = Layers(board);
            var pages = new List<byte[]>();
            foreach (var layer in layers)
            {
                var g = new PdfPlotter(box, o, h);
                g.Begin();
                Plot(g, board, layer, o, all);
                Label(g, board, layer, o, box);
                pages.Add(Encoding.ASCII.GetBytes(g.Body.ToString()));
            }

            return PdfFile.Write(pages, w * PdfPlotter.Pt, h * PdfPlotter.Pt, string.IsNullOrWhiteSpace(o.Title) ? "RobotWin" : o.Title);
        }

        // ================================================================ plotters

        /// <summary>A plotter that maps the board frame onto the page (mirrored when asked) and draws paths.</summary>
        private abstract class Plotter : IPlotter
        {
            protected readonly (double MinX, double MinY, double MaxX, double MaxY) Box;
            protected readonly PlotOptions Options;

            protected Plotter((double, double, double, double) box, PlotOptions options)
            {
                Box = box;
                Options = options;
                Mirrored = options.Mirror;
            }

            public string Ink { get; set; } = "#000000";
            public bool Mirrored { get; set; }

            /// <summary>Board frame (mm, Y down) → page frame of the subclass.</summary>
            protected (double X, double Y) Map(double x, double y)
            {
                double mx = Mirrored ? Box.MinX + Box.MaxX - x : x;
                return Page(mx - Box.MinX + Options.Margin, y - Box.MinY + Options.Margin);
            }

            /// <summary>Paper millimetres (origin top-left, Y down) → the format's units.</summary>
            protected abstract (double X, double Y) Page(double x, double y);

            protected abstract void MoveTo((double X, double Y) p);
            protected abstract void LineTo((double X, double Y) p);
            protected abstract void CurveTo((double X, double Y) a, (double X, double Y) b, (double X, double Y) c);
            protected abstract void Close();
            protected abstract void Fill();
            protected abstract void Stroke(double width);

            public void Line(double x1, double y1, double x2, double y2, double width)
            {
                MoveTo(Map(x1, y1));
                LineTo(Map(x2, y2));
                Stroke(width);
            }

            public void Circle(double cx, double cy, double r, double width)
            {
                ArcPath(cx, cy, r, 0, 2 * Math.PI, true);
                Close();
                Stroke(width);
            }

            public void Arc(double x1, double y1, double x2, double y2, double cx, double cy, bool angleIncreasing, double width)
            {
                double r = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
                double a1 = Math.Atan2(y1 - cy, x1 - cx), a2 = Math.Atan2(y2 - cy, x2 - cx);
                double sweep = a2 - a1;
                if (angleIncreasing) { while (sweep <= 1e-12) sweep += 2 * Math.PI; }
                else { while (sweep >= -1e-12) sweep -= 2 * Math.PI; }
                ArcPath(cx, cy, r, a1, sweep, true);
                Stroke(width);
            }

            /// <summary>Cubic Béziers of at most 90° each, computed in the board frame and mapped point by point.</summary>
            private void ArcPath(double cx, double cy, double r, double start, double sweep, bool move)
            {
                int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
                double d = sweep / n, k = 4.0 / 3.0 * Math.Tan(d / 4);
                double t = start;
                if (move) MoveTo(Map(cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
                for (int i = 0; i < n; i++)
                {
                    double c0 = Math.Cos(t), s0 = Math.Sin(t), c1 = Math.Cos(t + d), s1 = Math.Sin(t + d);
                    CurveTo(Map(cx + r * (c0 - k * s0), cy + r * (s0 + k * c0)),
                            Map(cx + r * (c1 + k * s1), cy + r * (s1 - k * c1)),
                            Map(cx + r * c1, cy + r * s1));
                    t += d;
                }
            }

            public void Flash(PadShape shape, double x, double y, double w, double h, double expand = 0)
            {
                w += 2 * expand;
                h += 2 * expand;
                if (shape == PadShape.Circle || (shape == PadShape.Oval && Math.Abs(w - h) < 1e-9))
                {
                    ArcPath(x, y, Math.Max(w, h) / 2, 0, 2 * Math.PI, true);
                    Close();
                    Fill();
                    return;
                }

                if (shape == PadShape.Oval)
                {
                    double r = Math.Min(w, h) / 2;
                    if (w > h)
                    {
                        double a = x - w / 2 + r, b = x + w / 2 - r;
                        MoveTo(Map(a, y + r));
                        ArcPath(a, y, r, Math.PI / 2, Math.PI, false);
                        LineTo(Map(b, y - r));
                        ArcPath(b, y, r, -Math.PI / 2, Math.PI, false);
                    }
                    else
                    {
                        double a = y - h / 2 + r, b = y + h / 2 - r;
                        MoveTo(Map(x - r, a));
                        ArcPath(x, a, r, Math.PI, Math.PI, false);
                        LineTo(Map(x + r, b));
                        ArcPath(x, b, r, 0, Math.PI, false);
                    }

                    Close();
                    Fill();
                    return;
                }

                MoveTo(Map(x - w / 2, y - h / 2));
                LineTo(Map(x + w / 2, y - h / 2));
                LineTo(Map(x + w / 2, y + h / 2));
                LineTo(Map(x - w / 2, y + h / 2));
                Close();
                Fill();
            }

            public void Region(IReadOnlyList<PcbPoint> points)
            {
                if (points.Count < 3) return;
                MoveTo(Map(points[0].X, points[0].Y));
                for (int i = 1; i < points.Count; i++) LineTo(Map(points[i].X, points[i].Y));
                Close();
                Fill();
            }
        }

        private sealed class SvgPlotter : Plotter
        {
            private readonly StringBuilder _path = new StringBuilder();
            public readonly StringBuilder Body = new StringBuilder();

            public SvgPlotter((double, double, double, double) box, PlotOptions options) : base(box, options) { }

            protected override (double X, double Y) Page(double x, double y) => (x, y);

            private void P((double X, double Y) p) => _path.Append(N(p.X)).Append(' ').Append(N(p.Y)).Append(' ');

            protected override void MoveTo((double X, double Y) p) { _path.Append('M'); P(p); }
            protected override void LineTo((double X, double Y) p) { _path.Append('L'); P(p); }
            protected override void CurveTo((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) { _path.Append('C'); P(a); P(b); P(c); }
            protected override void Close() => _path.Append('Z');

            protected override void Fill()
            {
                Body.Append("<path d=\"").Append(_path.ToString().TrimEnd()).Append("\" fill=\"").Append(Ink).Append("\" stroke=\"none\"/>\n");
                _path.Clear();
            }

            protected override void Stroke(double width)
            {
                Body.Append("<path d=\"").Append(_path.ToString().TrimEnd()).Append("\" fill=\"none\" stroke=\"").Append(Ink).Append("\" stroke-width=\"").Append(N(width)).Append("\"/>\n");
                _path.Clear();
            }
        }

        private sealed class PdfPlotter : Plotter
        {
            /// <summary>Points per millimetre.</summary>
            public const double Pt = 72.0 / 25.4;

            private readonly double _paperHeight;
            private string _ink = string.Empty;
            public readonly StringBuilder Body = new StringBuilder();

            public PdfPlotter((double, double, double, double) box, PlotOptions options, double paperHeightMm) : base(box, options) => _paperHeight = paperHeightMm;

            public void Begin() => Body.Append("1 J 1 j\n");

            protected override (double X, double Y) Page(double x, double y) => (x * Pt, (_paperHeight - y) * Pt);

            private static string D(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

            // a path is gathered first: the colour and the pen width are set before it starts, as PDF wants
            private readonly StringBuilder _path = new StringBuilder();
            private double _width = double.NaN;

            private void P((double X, double Y) p) => _path.Append(D(p.X)).Append(' ').Append(D(p.Y)).Append(' ');

            private void Colour()
            {
                if (Ink == _ink) return;
                _ink = Ink;
                int rgb = int.Parse(Ink.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                string c = D(((rgb >> 16) & 255) / 255.0) + " " + D(((rgb >> 8) & 255) / 255.0) + " " + D((rgb & 255) / 255.0);
                Body.Append(c).Append(" rg ").Append(c).Append(" RG\n");
            }

            protected override void MoveTo((double X, double Y) p) { P(p); _path.Append("m\n"); }
            protected override void LineTo((double X, double Y) p) { P(p); _path.Append("l\n"); }
            protected override void CurveTo((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) { P(a); P(b); P(c); _path.Append("c\n"); }
            protected override void Close() => _path.Append("h\n");

            protected override void Fill()
            {
                Colour();
                Body.Append(_path).Append("f\n");
                _path.Clear();
            }

            protected override void Stroke(double width)
            {
                Colour();
                if (double.IsNaN(_width) || Math.Abs(width - _width) > 1e-12)
                {
                    _width = width;
                    Body.Append(D(width * Pt)).Append(" w\n");
                }

                Body.Append(_path).Append("S\n");
                _path.Clear();
            }
        }
    }

    /// <summary>[plot] A minimal PDF 1.4 writer: pages of vector content (Flate-compressed), a title, nothing else.</summary>
    public static class PdfFile
    {
        public static byte[] Write(IReadOnlyList<byte[]> pages, double widthPt, double heightPt, string title)
        {
            var objects = new List<byte[]>();
            int pagesId = 2, firstPage = 3;
            string D(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => (firstPage + 2 * i).ToString(CultureInfo.InvariantCulture) + " 0 R"));
            objects.Add(A("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(A("<< /Type /Pages /Kids [" + kids + "] /Count " + pages.Count.ToString(CultureInfo.InvariantCulture) + " >>"));
            for (int i = 0; i < pages.Count; i++)
            {
                int contents = firstPage + 2 * i + 1;
                objects.Add(A("<< /Type /Page /Parent " + pagesId + " 0 R /MediaBox [0 0 " + D(widthPt) + " " + D(heightPt) + "] /Resources << >> /Contents " +
                              contents.ToString(CultureInfo.InvariantCulture) + " 0 R >>"));
                var z = Zlib(pages[i]);
                var stream = new MemoryStream();
                var head = A("<< /Length " + z.Length.ToString(CultureInfo.InvariantCulture) + " /Filter /FlateDecode >>\nstream\n");
                stream.Write(head, 0, head.Length);
                stream.Write(z, 0, z.Length);
                var tail = A("\nendstream");
                stream.Write(tail, 0, tail.Length);
                objects.Add(stream.ToArray());
            }

            int infoId = objects.Count + 1;
            objects.Add(A("<< /Title " + Text(title) + " /Producer (RobotWin Studio) >>"));

            var file = new MemoryStream();
            void Put(byte[] b) => file.Write(b, 0, b.Length);
            Put(A("%PDF-1.4\n"));
            Put(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });   // binary marker
            var offsets = new long[objects.Count];
            for (int i = 0; i < objects.Count; i++)
            {
                offsets[i] = file.Position;
                Put(A((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
                Put(objects[i]);
                Put(A("\nendobj\n"));
            }

            long xref = file.Position;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
            foreach (long off in offsets) sb.Append(off.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append(" /Root 1 0 R /Info ")
              .Append(infoId.ToString(CultureInfo.InvariantCulture)).Append(" 0 R >>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
            Put(A(sb.ToString()));
            return file.ToArray();
        }

        private static byte[] A(string s) => Encoding.ASCII.GetBytes(s);

        /// <summary>A text string as UTF-16BE with its byte-order mark (any language in the title).</summary>
        private static string Text(string s)
        {
            var sb = new StringBuilder("<FEFF");
            foreach (byte b in Encoding.BigEndianUnicode.GetBytes(s)) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return sb.Append('>').ToString();
        }

        /// <summary>zlib (RFC 1950) around Deflate: what /FlateDecode reads.</summary>
        public static byte[] Zlib(byte[] data)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0x9C);
            using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }

            uint adler = (b << 16) | a;
            ms.WriteByte((byte)(adler >> 24));
            ms.WriteByte((byte)(adler >> 16));
            ms.WriteByte((byte)(adler >> 8));
            ms.WriteByte((byte)adler);
            return ms.ToArray();
        }
    }
}
